using System;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RaceCorProDrive.Services;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Thin always-on status strip that surfaces UpdateService state at
    /// the top of the app shell. Idle state shows just the current
    /// version (so the user can always read "I'm on vX.Y.Z"); active
    /// states (checking / available / downloading / failed) expand the
    /// strip with progress + an action button. Floats just under the
    /// title bar — see MainWindow.xaml for placement.
    /// </summary>
    public sealed class UpdateBanner : ContentControl
    {
        private readonly TextBlock _versionText;
        private readonly TextBlock _statusText;
        private readonly ProgressBar _progress;
        private readonly Button _actionBtn;

        public UpdateBanner()
        {
            _versionText = new TextBlock
            {
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ResolveBrush("TextSecondaryBrush", Colors.Silver),
            };

            _statusText = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = ResolveBrush("TextDimBrush", Colors.DimGray),
            };

            _progress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Width = 90,
                Height = 4,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };

            _actionBtn = new Button
            {
                FontSize = 11,
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            _actionBtn.Click += async (_, __) => await UpdateService.Shared.InstallAsync();

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            row.Children.Add(_versionText);
            row.Children.Add(_statusText);
            row.Children.Add(_progress);
            row.Children.Add(_actionBtn);

            Content = row;
            IsTabStop = false;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Center;
            Padding = new Thickness(12, 4, 12, 4);

            Loaded += (_, __) =>
            {
                UpdateService.Shared.PropertyChanged += OnUpdateChanged;
                Apply();
            };
            Unloaded += (_, __) =>
            {
                UpdateService.Shared.PropertyChanged -= OnUpdateChanged;
            };
        }

        private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
            => DispatcherQueue.TryEnqueue(Apply);

        private void Apply()
        {
            var svc = UpdateService.Shared;
            _versionText.Text = "v" + svc.CurrentVersion;

            // Status line + action button visibility track UpdateService
            // state. Order matters: error wins over downloading wins
            // over checking, so a stale "checking…" doesn't flash over
            // a fresh failure.
            if (!string.IsNullOrEmpty(svc.ErrorMessage))
            {
                _statusText.Text = svc.ErrorMessage!;
                _statusText.Foreground = ResolveBrush("BrandRedBrush", Colors.OrangeRed);
                _progress.Visibility = Visibility.Collapsed;
                _actionBtn.Visibility = Visibility.Collapsed;
            }
            else if (svc.IsDownloading)
            {
                _statusText.Text = $"Updating to v{svc.LatestVersion}… {svc.DownloadPercent}%";
                _statusText.Foreground = ResolveBrush("TextSecondaryBrush", Colors.Silver);
                _progress.Value = svc.DownloadPercent;
                _progress.Visibility = Visibility.Visible;
                _actionBtn.Visibility = Visibility.Collapsed;
            }
            else if (svc.UpdateAvailable && !string.IsNullOrEmpty(svc.LatestVersion))
            {
                _statusText.Text = $"→ v{svc.LatestVersion} available";
                _statusText.Foreground = ResolveBrush("TextSecondaryBrush", Colors.Silver);
                _progress.Visibility = Visibility.Collapsed;
                _actionBtn.Content = "Update now";
                _actionBtn.Visibility = Visibility.Visible;
            }
            else if (svc.IsChecking)
            {
                _statusText.Text = "Checking for updates…";
                _statusText.Foreground = ResolveBrush("TextDimBrush", Colors.DimGray);
                _progress.Visibility = Visibility.Collapsed;
                _actionBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                _statusText.Text = string.Empty;
                _progress.Visibility = Visibility.Collapsed;
                _actionBtn.Visibility = Visibility.Collapsed;
            }
        }

        private static Brush ResolveBrush(string key, Windows.UI.Color fallback)
        {
            if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b)
                return b;
            return new SolidColorBrush(fallback);
        }
    }
}
