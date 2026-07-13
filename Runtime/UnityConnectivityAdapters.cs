using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ActionFit.Connectivity
{
    public sealed class UnityReachabilityProvider : IConnectivityReachabilityProvider
    {
        public ConnectivityReachability Current
        {
            get
            {
                return Application.internetReachability == NetworkReachability.NotReachable
                    ? ConnectivityReachability.NotReachable
                    : ConnectivityReachability.Reachable;
            }
        }
    }

    public sealed class FallbackConnectivityProbe : IConnectivityProbe
    {
        private readonly List<IConnectivityProbe> _probes = new List<IConnectivityProbe>();

        public FallbackConnectivityProbe(params IConnectivityProbe[] probes)
        {
            if (probes == null) throw new ArgumentNullException(nameof(probes));
            foreach (IConnectivityProbe probe in probes)
            {
                if (probe != null) _probes.Add(probe);
            }
            if (_probes.Count == 0)
                throw new ArgumentException("At least one connectivity probe is required.", nameof(probes));
        }

        public async Task<bool> ProbeAsync(
            Uri endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            foreach (IConnectivityProbe probe in _probes)
            {
                if (await probe.ProbeAsync(endpoint, timeout, cancellationToken)) return true;
            }
            return false;
        }
    }

    public sealed class UnityPingConnectivityProbe : IConnectivityProbe
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.1f);
        private readonly string _target;

        public UnityPingConnectivityProbe(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("Ping target must not be empty.", nameof(target));
            _target = target;
        }

        public async Task<bool> ProbeAsync(
            Uri endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Ping ping = null;
            try
            {
                ping = new Ping(_target);
                DateTime deadline = DateTime.UtcNow + timeout;
                while (!ping.isDone && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(PollInterval, cancellationToken);
                }
                return ping.isDone && ping.time >= 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
            finally
            {
                ping?.DestroyPing();
            }
        }
    }

    public sealed class UnityWebRequestConnectivityProbe : IConnectivityProbe
    {
        public Task<bool> ProbeAsync(
            Uri endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            cancellationToken.ThrowIfCancellationRequested();

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var request = UnityWebRequest.Head(endpoint.AbsoluteUri);
            request.timeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            CancellationTokenRegistration registration = default;
            int completed = 0;

            void Complete(bool cancelled)
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;

                if (cancelled) request.Abort();
                bool online = !cancelled
                              && request.result == UnityWebRequest.Result.Success
                              && request.responseCode >= 200
                              && request.responseCode < 400;
                registration.Dispose();
                request.Dispose();
                if (cancelled) completion.TrySetCanceled();
                else completion.TrySetResult(online);
            }

            operation.completed += _ => Complete(false);
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() => Complete(true));
                if (Volatile.Read(ref completed) != 0) registration.Dispose();
            }
            return completion.Task;
        }
    }
}
