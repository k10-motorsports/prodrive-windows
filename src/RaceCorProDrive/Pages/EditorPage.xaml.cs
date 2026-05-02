using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using RaceCorProDrive.Recording;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Bundle editor / playback view. Loads a <c>.rcpdv</c> file via
    /// <see cref="BundleReader"/>, plays the embedded MP4 with a WinUI
    /// MediaPlayerElement, and overlays bundle metadata + the live
    /// playback timestamp.
    ///
    /// MVP — matches the Mac editor's surface: track / car / position
    /// time / frame counter. Per-frame telemetry overlay (speed, lap,
    /// pit chip, etc.) lands in a follow-up PR once we wire the JSONL
    /// parse + timestamp index. The Mac shipped without those too.
    /// </summary>
    public sealed partial class EditorPage : Page
    {
        private string? _bundlePath;
        private BundleHeader? _header;
        private DispatcherTimer? _tickTimer;

        public EditorPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _bundlePath = e.Parameter as string;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Sub-page of Library — clear the titlebar slots so the
            // (currently empty) page chrome matches.
            App.MainWindow?.SetTitleBarTabs(null);
            App.MainWindow?.SetTitleBarFilters(null);

            if (string.IsNullOrEmpty(_bundlePath) || !File.Exists(_bundlePath))
            {
                ShowError("Couldn't open the bundle — file missing or path empty.");
                return;
            }

            try
            {
                _header = BundleReader.ReadHeader(_bundlePath);
            }
            catch (Exception ex)
            {
                ShowError($"Couldn't read bundle header: {ex.Message}");
                return;
            }

            if (_header == null)
            {
                ShowError("Bundle is missing the prodrive-telemetry-v3 uuid box. Is this an RCPDV file?");
                return;
            }

            ApplyHeaderToOverlay(_header);

            // Point the MediaPlayer at the same .rcpdv file. Media
            // Foundation reads MP4 boxes natively and ignores the
            // trailing uuid telemetry box (it isn't a valid sample
            // table entry — just sidecar data). H.264 + AAC pass
            // through without any codec install.
            var source = MediaSource.CreateFromUri(new Uri(_bundlePath));
            Player.Source = source;

            // 10Hz tick that re-stamps the overlay from the player
            // state. PositionChanged exists on PlaybackSession but
            // fires erratically (every frame submitted, not on a
            // wall-clock cadence) — a manual DispatcherTimer is more
            // predictable for "just keep the timestamp/frame label
            // ticking" UX.
            _tickTimer?.Stop();
            _tickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100),
            };
            _tickTimer.Tick += OnPlaybackTick;
            _tickTimer.Start();

            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _tickTimer?.Stop();
            _tickTimer = null;
            // Disposing the MediaPlayer releases the file handle so the
            // user can immediately Send-to-Mac or rename the bundle
            // without an "in use" error.
            var src = Player.Source as MediaSource;
            Player.Source = null;
            src?.Dispose();
            Player.MediaPlayer?.Dispose();
        }

        private void OnBack(object sender, RoutedEventArgs e)
        {
            App.MainWindow?.NavigateToPage("library");
        }

        // ── Overlay rendering ──────────────────────────────────────

        private void ApplyHeaderToOverlay(BundleHeader header)
        {
            var track = header.Race?.Track ?? "—";
            var car = header.Race?.Car ?? "—";
            var demoTag = (header.Race?.IsDemo == true) ? " · DEMO" : "";

            TitleText.Text = $"{track}{demoTag}";
            SubtitleText.Text = string.IsNullOrEmpty(header.BundleId)
                ? Path.GetFileName(_bundlePath ?? "")
                : header.BundleId;

            OverlayTrack.Text = track;
            OverlayCar.Text = car;
            OverlayPosition.Text = "00:00.0 / —";
            OverlayFrame.Text = $"frame 0 / {header.Telemetry?.FrameCount ?? 0:N0}";
        }

        private void OnPlaybackTick(object? sender, object e)
        {
            var session = Player.MediaPlayer?.PlaybackSession;
            if (session == null || _header == null) return;

            var pos = session.Position;
            var dur = session.NaturalDuration;
            var posStr = pos.ToString(@"mm\:ss\.f");
            var durStr = dur > TimeSpan.Zero ? dur.ToString(@"mm\:ss") : "—";
            OverlayPosition.Text = $"{posStr} / {durStr}";

            // Estimate the current telemetry frame from playback
            // position. The bundle's telemetry sample rate isn't
            // stamped in the header, so derive it from
            // FrameCount / Duration. Good enough for a counter
            // display; replace with a real timestamp index when the
            // per-frame overlay lands.
            var frames = _header.Telemetry?.FrameCount ?? 0;
            if (frames > 0 && dur > TimeSpan.Zero)
            {
                var fraction = pos.TotalSeconds / dur.TotalSeconds;
                var current = (long)Math.Clamp(fraction * frames, 0, frames);
                OverlayFrame.Text = $"frame {current:N0} / {frames:N0}";
            }
        }

        // ── Error surface ──────────────────────────────────────────

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            Player.Visibility = Visibility.Collapsed;
            OverlayPanel.Visibility = Visibility.Collapsed;
        }
    }
}
