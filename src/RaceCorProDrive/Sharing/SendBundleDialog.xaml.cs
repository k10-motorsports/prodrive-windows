using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Auth;
using RaceCorProDrive.Recording;

namespace RaceCorProDrive.Sharing
{
    /// <summary>
    /// "Send to Mac" picker. Opens, runs Bonjour discovery, lists peers,
    /// then streams the bundle to the chosen peer with a progress bar.
    /// </summary>
    public sealed partial class SendBundleDialog : ContentDialog
    {
        private readonly string _bundlePath;
        private readonly LibraryEntry _entry;
        private readonly DispatcherQueue _dispatcher;
        private readonly ObservableCollection<DiscoveredPeerVm> _peers = new();
        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _sendCts;
        private DiscoveredPeer? _selected;

        public SendBundleDialog(LibraryEntry entry)
        {
            this.InitializeComponent();
            _entry = entry;
            _bundlePath = entry.Path;
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            BundleNameText.Text = entry.DisplayTitle;
            StatusText.Text = "Searching for Macs on your network…";
            PeerList.ItemsSource = _peers;

            this.Opened += async (_, _) => await ScanAsync();
            this.Closing += OnClosing;
        }

        private async System.Threading.Tasks.Task ScanAsync()
        {
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            ErrorText.Text = "";

            try
            {
                var auth = new AuthService();
                var userId = auth.CurrentUser != null
                    ? $"discord:{auth.CurrentUser.DiscordId}"
                    : null;

                var found = await BonjourBrowser
                    .DiscoverAsync(userIdFilter: userId, cancellationToken: _scanCts.Token)
                    .ConfigureAwait(true);

                _peers.Clear();
                foreach (var peer in found)
                {
                    _peers.Add(new DiscoveredPeerVm(peer));
                }

                if (_peers.Count == 0)
                {
                    StatusText.Text = "No Macs found. Make sure your Mac is awake, signed in, and on the same network.";
                }
                else
                {
                    StatusText.Text = _peers.Count == 1
                        ? "Found 1 Mac."
                        : $"Found {_peers.Count} Macs.";
                    PeerList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                ErrorText.Text = $"Scan failed: {ex.Message}";
            }
        }

        private void OnPeerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = PeerList.SelectedItem as DiscoveredPeerVm;
            _selected = vm?.Source;
            IsPrimaryButtonEnabled = _selected != null;
        }

        private async void OnSendClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_selected == null)
            {
                args.Cancel = true;
                return;
            }

            // Defer the dialog dismissal until the upload finishes (or fails).
            var deferral = args.GetDeferral();

            try
            {
                // TokenStore is the canonical source of truth for the bearer
                // token — same path APIClient + CalcClient use.
                var token = new TokenStore().LoadTokens()?.AccessToken;
                if (string.IsNullOrEmpty(token))
                {
                    ErrorText.Text = "Sign in to share bundles.";
                    args.Cancel = true;
                    return;
                }

                IsPrimaryButtonEnabled = false;
                SendProgress.Visibility = Visibility.Visible;
                SendProgress.IsIndeterminate = false;
                SendProgress.Minimum = 0;
                SendProgress.Maximum = Math.Max(1, _entry.FileSize);
                SendProgress.Value = 0;
                StatusText.Text = $"Sending to {_selected.DisplayName}…";
                ErrorText.Text = "";

                _sendCts = new CancellationTokenSource();
                var sender2 = new LanSender(token);
                var progress = new Progress<long>(bytes =>
                {
                    _dispatcher.TryEnqueue(() => SendProgress.Value = bytes);
                });

                var result = await sender2
                    .SendAsync(_selected, _bundlePath, progress, _sendCts.Token)
                    .ConfigureAwait(true);

                if (result.Success)
                {
                    StatusText.Text = $"Sent — saved to {result.RemoteSavedPath ?? "the Mac"}.";
                    SendProgress.Value = SendProgress.Maximum;
                    // Auto-close after a short pause so the user sees the success state.
                    await System.Threading.Tasks.Task.Delay(900).ConfigureAwait(true);
                }
                else
                {
                    args.Cancel = true;
                    SendProgress.Visibility = Visibility.Collapsed;
                    IsPrimaryButtonEnabled = true;
                    ErrorText.Text = result.ErrorMessage ?? $"Send failed (HTTP {result.StatusCode}).";
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                SendProgress.Visibility = Visibility.Collapsed;
                IsPrimaryButtonEnabled = true;
                ErrorText.Text = $"Send failed: {ex.Message}";
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            _scanCts?.Cancel();
            _sendCts?.Cancel();
        }
    }

    /// <summary>
    /// View-model wrapping a <see cref="DiscoveredPeer"/> so XAML's
    /// <c>x:DataType</c> can bind without going through reflection.
    /// </summary>
    public sealed class DiscoveredPeerVm
    {
        public DiscoveredPeer Source { get; }
        public string DisplayName => Source.DisplayName;
        public string Subtitle => $"{Source.IPAddress}:{Source.Port}  ·  v{Source.Version ?? "?"}";

        public DiscoveredPeerVm(DiscoveredPeer source) { Source = source; }
    }
}
