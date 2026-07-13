using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ActionFit.Connectivity.Tests
{
    public class ConnectivityServiceTests
    {
        [Test]
        public void NewService_StartsUnknownAndIdle()
        {
            ConnectivityService service = CreateService(new QueueProbe(true));

            Assert.That(service.State, Is.EqualTo(ConnectivityState.Unknown));
            Assert.That(service.IsPaused, Is.False);
            Assert.That(service.IsMonitoring, Is.False);
        }

        [Test]
        public async Task CheckNow_NotReachableSkipsProbeAndPublishesOffline()
        {
            var reachability = new FakeReachability(ConnectivityReachability.NotReachable);
            var probe = new QueueProbe(true);
            ConnectivityService service = CreateService(probe, reachability: reachability);
            var states = new List<ConnectivityState>();
            service.StateChanged += states.Add;

            bool online = await service.CheckNowAsync();

            Assert.That(online, Is.False);
            Assert.That(probe.CallCount, Is.Zero);
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Offline));
            Assert.That(states, Is.EqualTo(new[] { ConnectivityState.Checking, ConnectivityState.Offline }));
        }

        [TestCase(ConnectivityReachability.Unknown)]
        [TestCase(ConnectivityReachability.Reachable)]
        public async Task CheckNow_ReachableOrUnknownUsesProbe(ConnectivityReachability reachabilityValue)
        {
            var reachability = new FakeReachability(reachabilityValue);
            var probe = new QueueProbe(true);
            ConnectivityService service = CreateService(probe, reachability: reachability);

            bool online = await service.CheckNowAsync();

            Assert.That(online, Is.True);
            Assert.That(probe.CallCount, Is.EqualTo(1));
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Online));
            Assert.That(probe.LastEndpoint.AbsoluteUri, Is.EqualTo("https://example.com/connectivity"));
            Assert.That(probe.LastTimeout, Is.EqualTo(TimeSpan.FromSeconds(2f)));
        }

        [Test]
        public async Task CheckWithRetry_ExhaustsConfiguredRetriesBeforeOffline()
        {
            var probe = new QueueProbe(false, false, false);
            var delay = new FakeDelay();
            ConnectivityService service = CreateService(probe, delay: delay, maxRetryCount: 2);

            bool online = await service.CheckWithRetryAsync();

            Assert.That(online, Is.False);
            Assert.That(probe.CallCount, Is.EqualTo(3));
            Assert.That(delay.CallCount, Is.EqualTo(2));
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Offline));
        }

        [Test]
        public async Task CheckWithRetry_StopsWhenLaterAttemptRecovers()
        {
            var probe = new QueueProbe(false, true, false);
            var delay = new FakeDelay();
            ConnectivityService service = CreateService(probe, delay: delay, maxRetryCount: 2);

            bool online = await service.CheckWithRetryAsync();

            Assert.That(online, Is.True);
            Assert.That(probe.CallCount, Is.EqualTo(2));
            Assert.That(delay.CallCount, Is.EqualTo(1));
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Online));
        }

        [Test]
        public async Task WaitForOnline_CompletesAfterLaterSuccessfulCheck()
        {
            var probe = new QueueProbe(false, true);
            ConnectivityService service = CreateService(probe);
            Assert.That(await service.CheckNowAsync(), Is.False);

            Task wait = service.WaitForOnlineAsync();
            Assert.That(wait.IsCompleted, Is.False);

            Assert.That(await service.CheckNowAsync(), Is.True);
            await wait;
            Assert.That(wait.IsCompleted, Is.True);
            Assert.That(wait.IsFaulted, Is.False);
        }

        [Test]
        public async Task PauseAndResume_PreserveStateThenRunImmediateCheck()
        {
            var probe = new QueueProbe(false, true);
            ConnectivityService service = CreateService(probe);
            Assert.That(await service.CheckNowAsync(), Is.False);

            service.Pause();
            Assert.That(service.IsPaused, Is.True);
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Offline));

            bool online = await service.ResumeAsync();

            Assert.That(online, Is.True);
            Assert.That(service.IsPaused, Is.False);
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Online));
        }

        [Test]
        public async Task Monitoring_RunsImmediately_PausesTicks_AndResumesWithImmediateCheck()
        {
            var probe = new QueueProbe(true, true);
            var delay = new ControlledDelay();
            ConnectivityService service = CreateService(probe, delay: delay);

            service.StartMonitoring();
            await WaitUntilAsync(() => probe.CallCount == 1 && delay.CallCount == 1);

            Assert.That(service.IsMonitoring, Is.True);
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Online));

            service.Pause();
            delay.ReleaseNext();
            await WaitUntilAsync(() => delay.CallCount == 2);
            Assert.That(probe.CallCount, Is.EqualTo(1));

            Assert.That(await service.ResumeAsync(), Is.True);
            Assert.That(probe.CallCount, Is.EqualTo(2));

            service.StopMonitoring();
            Assert.That(service.IsMonitoring, Is.False);
        }

        [Test]
        public async Task CancelledInFlightCheck_RestoresLastStableState()
        {
            var probe = new CancellableAfterFirstProbe();
            ConnectivityService service = CreateService(probe);
            Assert.That(await service.CheckNowAsync(), Is.True);
            using var cancellation = new CancellationTokenSource();

            Task<bool> check = service.CheckNowAsync(cancellation.Token);
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Checking));

            cancellation.Cancel();
            OperationCanceledException cancellationException = null;
            try
            {
                await check;
            }
            catch (OperationCanceledException exception)
            {
                cancellationException = exception;
            }

            Assert.That(cancellationException, Is.Not.Null);
            Assert.That(service.State, Is.EqualTo(ConnectivityState.Online));
        }

        [Test]
        public async Task FallbackProbe_StopsAfterFirstSuccessfulProbe()
        {
            var first = new QueueProbe(false);
            var second = new QueueProbe(true);
            var third = new QueueProbe(true);
            var fallback = new FallbackConnectivityProbe(first, second, third);

            bool online = await fallback.ProbeAsync(
                new Uri("https://example.com/connectivity"),
                TimeSpan.FromSeconds(2f),
                CancellationToken.None);

            Assert.That(online, Is.True);
            Assert.That(first.CallCount, Is.EqualTo(1));
            Assert.That(second.CallCount, Is.EqualTo(1));
            Assert.That(third.CallCount, Is.Zero);
        }

        [TestCase("")]
        [TestCase("relative/path")]
        [TestCase("ftp://example.com")]
        public void Options_RejectInvalidProbeUrl(string probeUrl)
        {
            Assert.Throws<ArgumentException>(() => new ConnectivityOptions(probeUrl, 2f, 10f, 1f, 1));
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            for (int i = 0; i < 100 && !predicate(); i++)
            {
                await Task.Yield();
            }
            Assert.That(predicate(), Is.True, "Timed out waiting for the asynchronous test condition.");
        }

        private static ConnectivityService CreateService(
            IConnectivityProbe probe,
            FakeReachability reachability = null,
            IConnectivityDelay delay = null,
            int maxRetryCount = 1)
        {
            return new ConnectivityService(
                reachability ?? new FakeReachability(ConnectivityReachability.Reachable),
                probe,
                new ConnectivityOptions(
                    "https://example.com/connectivity",
                    2f,
                    10f,
                    1f,
                    maxRetryCount),
                delay ?? new FakeDelay());
        }

        private sealed class FakeReachability : IConnectivityReachabilityProvider
        {
            public FakeReachability(ConnectivityReachability current) => Current = current;
            public ConnectivityReachability Current { get; set; }
        }

        private sealed class QueueProbe : IConnectivityProbe
        {
            private readonly Queue<bool> _results;

            public QueueProbe(params bool[] results)
            {
                _results = new Queue<bool>(results);
            }

            public int CallCount { get; private set; }
            public Uri LastEndpoint { get; private set; }
            public TimeSpan LastTimeout { get; private set; }

            public Task<bool> ProbeAsync(
                Uri endpoint,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                LastEndpoint = endpoint;
                LastTimeout = timeout;
                return Task.FromResult(_results.Count > 0 && _results.Dequeue());
            }
        }

        private sealed class CancellableAfterFirstProbe : IConnectivityProbe
        {
            private int _callCount;

            public Task<bool> ProbeAsync(
                Uri endpoint,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                _callCount++;
                if (_callCount == 1) return Task.FromResult(true);

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => completion.TrySetCanceled());
                return completion.Task;
            }
        }

        private sealed class FakeDelay : IConnectivityDelay
        {
            public int CallCount { get; private set; }

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class ControlledDelay : IConnectivityDelay
        {
            private readonly Queue<TaskCompletionSource<bool>> _pending =
                new Queue<TaskCompletionSource<bool>>();

            public int CallCount { get; private set; }

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => completion.TrySetCanceled());
                _pending.Enqueue(completion);
                return completion.Task;
            }

            public void ReleaseNext()
            {
                Assert.That(_pending.Count, Is.GreaterThan(0));
                _pending.Dequeue().TrySetResult(true);
            }
        }
    }
}
