using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using RaceCorProDrive.Api;

namespace RaceCorProDrive.Support
{
    /// <summary>
    /// Periodically fetches <c>/api/v1/dashboard</c> while the user is
    /// signed in and raises <see cref="INotifyPropertyChanged"/> events
    /// so XAML-bound views re-render when fresh data arrives. Mirrors
    /// the Swift <c>DashboardPoller</c> in shape (singleton, 60s tick,
    /// start/stop lifecycle) so every platform behaves the same.
    /// </summary>
    /// <remarks>
    /// Why not <c>BackgroundService</c>: WinUI apps don't host a generic
    /// host — there's no DI container running by default. A simple
    /// async loop pinned to a <see cref="DispatcherQueue"/> gets us the
    /// same "on the UI thread, cancellable, auto-retries" story with
    /// less ceremony.
    /// </remarks>
    public sealed class DashboardPoller : INotifyPropertyChanged
    {
        public static DashboardPoller Shared { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Seconds between polls. Mirrors Swift's 60s cadence.</summary>
        public const int PollIntervalSeconds = 60;

        private Dashboard? _latest;
        public Dashboard? Latest
        {
            get => _latest;
            private set { _latest = value; Raise(); Raise(nameof(Sessions)); Raise(nameof(PreviousRaces)); }
        }

        private DateTime? _lastUpdated;
        public DateTime? LastUpdated
        {
            get => _lastUpdated;
            private set { _lastUpdated = value; Raise(); }
        }

        private bool _isPolling;
        public bool IsPolling
        {
            get => _isPolling;
            private set { _isPolling = value; Raise(); }
        }

        /// <summary>
        /// Convenience accessor — recent sessions ship INSIDE the
        /// dashboard payload. Matches the Swift poller's
        /// <c>poller.sessions</c> property.
        /// </summary>
        public IReadOnlyList<RecentSession> Sessions =>
            Latest?.RecentSessions ?? (IReadOnlyList<RecentSession>)Array.Empty<RecentSession>();

        /// <summary>
        /// Server-baked Previous Races cards. Mirrors the Swift
        /// poller's <c>previousRaces</c> accessor — view-models come
        /// fully formatted from <c>/api/v1/dashboard</c>.
        /// </summary>
        public IReadOnlyList<PreviousRaceCard> PreviousRaces =>
            Latest?.PreviousRaces ?? (IReadOnlyList<PreviousRaceCard>)Array.Empty<PreviousRaceCard>();

        private CancellationTokenSource? _cts;
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? DispatcherQueue.GetForCurrentThread();

        /// <summary>
        /// Starts or restarts the polling loop. Safe to call multiple
        /// times — existing loop is cancelled first. Call from the
        /// signed-in root so it doesn't fire against a logged-out
        /// session.
        /// </summary>
        public void Start()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await TickAsync();
                    try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), token); }
                    catch (TaskCanceledException) { return; }
                }
            }, token);
        }

        /// <summary>Stops the polling loop. Called on sign-out.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task TickAsync()
        {
            OnUi(() => IsPolling = true);
            try
            {
                // Read user's current filter prefs. Poller fires
                // outside the view tree so we read LocalSettings
                // directly. The dashboard page triggers an immediate
                // re-tick via PollNowAsync() on filter change.
                var (ctx, lic) = DashboardFilterPrefs.Current();
                var snapshot = await ApiClient.Shared.DashboardAsync(ctx, lic);
                if (snapshot != null)
                {
                    OnUi(() =>
                    {
                        Latest = snapshot;
                        LastUpdated = DateTime.UtcNow;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[POLL] dashboard fetch failed: {ex.Message}");
            }
            finally
            {
                OnUi(() => IsPolling = false);
            }
        }

        /// <summary>
        /// Marshal property changes onto the UI thread. XAML bindings
        /// always fire on the dispatcher thread — mutating from a
        /// background task without this would throw.
        /// </summary>
        private void OnUi(Action action)
        {
            if (_dispatcher == null || _dispatcher.HasThreadAccess)
            {
                action();
            }
            else
            {
                _dispatcher.TryEnqueue(() => action());
            }
        }

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
