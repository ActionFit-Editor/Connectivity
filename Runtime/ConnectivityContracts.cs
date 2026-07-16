using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ActionFit.Connectivity
{
    public enum ConnectivityState
    {
        Unknown,
        Checking,
        Online,
        Offline
    }

    public enum ConnectivityReachability
    {
        Unknown,
        Reachable,
        NotReachable
    }

    public interface IConnectivityReachabilityProvider
    {
        ConnectivityReachability Current { get; }
    }

    public interface IConnectivityProbe
    {
        Task<bool> ProbeAsync(Uri endpoint, TimeSpan timeout, CancellationToken cancellationToken);
    }

    /// <summary>Performs one HTTPS observation without exposing arbitrary response headers or bodies.</summary>
    public interface IConnectivityObservationProbe
    {
        /// <summary>Returns one bounded response observation and optionally requests cache bypass.</summary>
        Task<ConnectivityObservation> ObserveAsync(
            Uri endpoint,
            TimeSpan timeout,
            bool bypassCache,
            CancellationToken cancellationToken);
    }

    /// <summary>Contains the bounded response metadata required by connectivity and server-time consumers.</summary>
    public readonly struct ConnectivityObservation
    {
        internal ConnectivityObservation(
            bool isConnected,
            long responseCode,
            DateTimeOffset? serverDateUtc,
            long? ageSeconds,
            bool hasValidAge,
            TimeSpan roundTripDuration)
        {
            IsConnected = isConnected;
            ResponseCode = responseCode;
            ServerDateUtc = serverDateUtc;
            AgeSeconds = ageSeconds;
            HasValidAge = hasValidAge;
            RoundTripDuration = roundTripDuration;
        }

        public bool IsConnected { get; }
        public long ResponseCode { get; }
        public DateTimeOffset? ServerDateUtc { get; }
        public long? AgeSeconds { get; }
        public bool HasValidAge { get; }
        public TimeSpan RoundTripDuration { get; }

        public bool HasFreshServerDate =>
            IsConnected
            && ServerDateUtc.HasValue
            && HasValidAge
            && (!AgeSeconds.HasValue || AgeSeconds.Value == 0L);
    }

    /// <summary>Parses the standard response metadata used by an HTTPS observation.</summary>
    public static class ConnectivityObservationParser
    {
        /// <summary>Parses standard Date and Age values into a bounded observation.</summary>
        public static ConnectivityObservation Parse(
            bool requestSucceeded,
            long responseCode,
            string dateHeader,
            string ageHeader,
            TimeSpan roundTripDuration)
        {
            bool isConnected = requestSucceeded && responseCode >= 200L && responseCode < 400L;

            DateTimeOffset? serverDateUtc = null;
            if (DateTimeOffset.TryParse(
                    dateHeader,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsedDate))
            {
                serverDateUtc = parsedDate.ToUniversalTime();
            }

            bool hasValidAge = string.IsNullOrWhiteSpace(ageHeader);
            long? ageSeconds = null;
            if (!hasValidAge
                && long.TryParse(ageHeader, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedAge)
                && parsedAge >= 0L)
            {
                hasValidAge = true;
                ageSeconds = parsedAge;
            }

            return new ConnectivityObservation(
                isConnected,
                responseCode,
                serverDateUtc,
                ageSeconds,
                hasValidAge,
                roundTripDuration);
        }
    }

    public interface IConnectivityDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public interface IConnectivityService
    {
        ConnectivityState State { get; }
        bool IsPaused { get; }
        bool IsMonitoring { get; }

        event Action<ConnectivityState> StateChanged;

        Task<bool> CheckNowAsync(CancellationToken cancellationToken = default);
        Task<bool> CheckWithRetryAsync(CancellationToken cancellationToken = default);
        Task WaitForOnlineAsync(CancellationToken cancellationToken = default);
        void StartMonitoring();
        void StopMonitoring();
        void Pause();
        Task<bool> ResumeAsync(CancellationToken cancellationToken = default);
    }

    public sealed class ConnectivityOptions
    {
        public ConnectivityOptions(
            string probeUrl,
            float probeTimeoutSeconds,
            float checkIntervalSeconds,
            float retryIntervalSeconds,
            int maxRetryCount)
        {
            if (!Uri.TryCreate(probeUrl, UriKind.Absolute, out Uri endpoint)
                || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Probe URL must be an absolute HTTP or HTTPS URL.", nameof(probeUrl));
            }
            if (probeTimeoutSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(probeTimeoutSeconds));
            if (checkIntervalSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(checkIntervalSeconds));
            if (retryIntervalSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(retryIntervalSeconds));
            if (maxRetryCount < 0)
                throw new ArgumentOutOfRangeException(nameof(maxRetryCount));

            ProbeEndpoint = endpoint;
            ProbeTimeout = TimeSpan.FromSeconds(probeTimeoutSeconds);
            CheckInterval = TimeSpan.FromSeconds(checkIntervalSeconds);
            RetryInterval = TimeSpan.FromSeconds(retryIntervalSeconds);
            MaxRetryCount = maxRetryCount;
        }

        public Uri ProbeEndpoint { get; }
        public TimeSpan ProbeTimeout { get; }
        public TimeSpan CheckInterval { get; }
        public TimeSpan RetryInterval { get; }
        public int MaxRetryCount { get; }
    }
}
