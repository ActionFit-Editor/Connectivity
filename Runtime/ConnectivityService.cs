using System;
using System.Threading;
using System.Threading.Tasks;

namespace ActionFit.Connectivity
{
    public sealed class ConnectivityService : IConnectivityService
    {
        private readonly IConnectivityReachabilityProvider _reachability;
        private readonly IConnectivityProbe _probe;
        private readonly IConnectivityDelay _delay;
        private readonly ConnectivityOptions _options;
        private readonly SemaphoreSlim _checkGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _monitorCancellation;
        private Task _monitorTask;
        private ConnectivityState _state = ConnectivityState.Unknown;
        private ConnectivityState _lastStableState = ConnectivityState.Unknown;
        private bool _isPaused;

        public ConnectivityService(
            IConnectivityReachabilityProvider reachability,
            IConnectivityProbe probe,
            ConnectivityOptions options,
            IConnectivityDelay delay = null)
        {
            _reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _delay = delay ?? new SystemConnectivityDelay();
        }

        public ConnectivityState State => _state;
        public bool IsPaused => _isPaused;
        public bool IsMonitoring => _monitorTask != null;

        public event Action<ConnectivityState> StateChanged;

        /// <summary>Runs one reachability and probe attempt and publishes the resulting stable state.</summary>
        public Task<bool> CheckNowAsync(CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(0, cancellationToken);
        }

        /// <summary>Runs an immediate attempt followed by the configured retry count before publishing Offline.</summary>
        public Task<bool> CheckWithRetryAsync(CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(_options.MaxRetryCount, cancellationToken);
        }

        /// <summary>Waits until a later check publishes Online without starting an implicit monitor.</summary>
        public async Task WaitForOnlineAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_state == ConnectivityState.Online) return;

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void HandleStateChanged(ConnectivityState state)
            {
                if (state == ConnectivityState.Online) completion.TrySetResult(true);
            }

            StateChanged += HandleStateChanged;
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => completion.TrySetCanceled());
            try
            {
                if (_state == ConnectivityState.Online) return;
                await completion.Task;
            }
            finally
            {
                StateChanged -= HandleStateChanged;
            }
        }

        /// <summary>Starts one immediate check and then repeats checks at the configured interval.</summary>
        public void StartMonitoring()
        {
            if (_monitorCancellation != null) return;

            _monitorCancellation = new CancellationTokenSource();
            _monitorTask = MonitorAsync(_monitorCancellation.Token);
        }

        /// <summary>Stops automatic checks without removing state subscribers.</summary>
        public void StopMonitoring()
        {
            if (_monitorCancellation == null) return;

            _monitorCancellation.Cancel();
            _monitorCancellation.Dispose();
            _monitorCancellation = null;
            _monitorTask = null;
        }

        /// <summary>Suspends automatic checks while preserving the last published state.</summary>
        public void Pause()
        {
            _isPaused = true;
        }

        /// <summary>Resumes automatic checks and performs one immediate check.</summary>
        public Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
        {
            _isPaused = false;
            return CheckNowAsync(cancellationToken);
        }

        private async Task<bool> CheckCoreAsync(int retryCount, CancellationToken cancellationToken)
        {
            await _checkGate.WaitAsync(cancellationToken);
            ConnectivityState stateBeforeCheck = _lastStableState;
            try
            {
                SetState(ConnectivityState.Checking);
                int attemptCount = retryCount + 1;
                for (int attempt = 0; attempt < attemptCount; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool online = await ProbeOnceAsync(cancellationToken);
                    if (online)
                    {
                        SetState(ConnectivityState.Online);
                        return true;
                    }

                    if (attempt + 1 < attemptCount)
                        await _delay.DelayAsync(_options.RetryInterval, cancellationToken);
                }

                SetState(ConnectivityState.Offline);
                return false;
            }
            catch (OperationCanceledException)
            {
                SetState(stateBeforeCheck);
                throw;
            }
            catch
            {
                SetState(stateBeforeCheck);
                throw;
            }
            finally
            {
                _checkGate.Release();
            }
        }

        private async Task<bool> ProbeOnceAsync(CancellationToken cancellationToken)
        {
            if (_reachability.Current == ConnectivityReachability.NotReachable) return false;
            return await _probe.ProbeAsync(
                _options.ProbeEndpoint,
                _options.ProbeTimeout,
                cancellationToken);
        }

        private async Task MonitorAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Avoid running an entire monitor iteration synchronously from StartMonitoring.
                await Task.Yield();
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_isPaused)
                        await CheckWithRetryAsync(cancellationToken);

                    TimeSpan delay = _isPaused ? _options.RetryInterval : _options.CheckInterval;
                    await _delay.DelayAsync(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void SetState(ConnectivityState state)
        {
            if (_state == state) return;

            _state = state;
            if (state == ConnectivityState.Online || state == ConnectivityState.Offline)
                _lastStableState = state;
            StateChanged?.Invoke(state);
        }
    }

    public sealed class SystemConnectivityDelay : IConnectivityDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}
