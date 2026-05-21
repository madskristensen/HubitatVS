using System.Threading;

namespace HubitatVS
{
    internal sealed class HubitatRefreshCoordinator : IDisposable
    {
        private readonly Func<int, CancellationToken, Task> _worker;
        private readonly Action<Exception> _onError;

        private CancellationTokenSource _cts = new CancellationTokenSource();
        private int _version;
        private int _refreshInProgress;
        private int _refreshQueued;
        private int _disposed;

        public HubitatRefreshCoordinator(Func<int, CancellationToken, Task> worker, Action<Exception>? onError = null)
        {
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));
            _onError = onError ?? (ex => ex.Log());
        }

        public int CurrentVersion => Volatile.Read(ref _version);

        public void RequestRefresh(bool cancelRunning = true)
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;

            if (!cancelRunning && Volatile.Read(ref _refreshInProgress) == 1)
            {
                Interlocked.Exchange(ref _refreshQueued, 1);
                return;
            }

            var newVersion = Interlocked.Increment(ref _version);
            var newCts = new CancellationTokenSource();
            var old = Interlocked.Exchange(ref _cts, newCts);
            old.Cancel();
            old.Dispose();

            Interlocked.Exchange(ref _refreshInProgress, 1);
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(() => RunAsync(newVersion, newCts.Token));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            var cts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
            cts.Cancel();
            cts.Dispose();
        }

        private async Task RunAsync(int refreshVersion, CancellationToken ct)
        {
            try
            {
                await _worker(refreshVersion, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInProgress, 0);
                if (Interlocked.Exchange(ref _refreshQueued, 0) == 1 && Volatile.Read(ref _disposed) == 0)
                {
                    RequestRefresh(cancelRunning: false);
                }
            }
        }
    }
}
