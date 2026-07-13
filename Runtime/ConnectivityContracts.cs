using System;
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
