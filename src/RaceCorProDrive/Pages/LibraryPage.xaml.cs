using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Recording;
using RaceCorProDrive.Sharing;

namespace RaceCorProDrive.Pages
{
    public sealed partial class LibraryPage : Page
    {
        private readonly LibraryService _service = new();
        private readonly ObservableCollection<LibraryItemViewModel> _items = new();

        public LibraryPage()
        {
            this.InitializeComponent();
            LibraryRepeater.ItemsSource = _items;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await Refresh();
        }

        private async void OnRefresh(object sender, RoutedEventArgs e)
        {
            await Refresh();
        }

        private async Task Refresh()
        {
            LoadingRing.IsActive = true;
            ErrorText.Text = "";
            EmptyState.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = false;

            try
            {
                var results = await _service.RefreshAsync();
                _items.Clear();
                foreach (var entry in results)
                {
                    _items.Add(LibraryItemViewModel.From(entry));
                }
                CountText.Text = _items.Count == 1
                    ? "1 recorded session"
                    : $"{_items.Count} recorded sessions";
                EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Couldn't scan library: {ex.Message}";
            }
            finally
            {
                LoadingRing.IsActive = false;
                RefreshButton.IsEnabled = true;
            }
        }

        private async void OnTileClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var path = btn.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            // Stub editor — show metadata in a content dialog. Real editor lands later.
            var item = FindItem(path);
            if (item == null) return;

            var dialog = new ContentDialog
            {
                Title = item.Title,
                Content = BuildDetailContent(item),
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot,
            };
            await dialog.ShowAsync();
        }

        private LibraryItemViewModel? FindItem(string path)
        {
            foreach (var i in _items)
            {
                if (string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return null;
        }

        private async void OnSendToMac(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item) return;
            var path = item.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            // Re-read the entry from the cache rather than the view-model
            // so we get the full LibraryEntry (header included) — the
            // dialog needs that to derive the bundleId for HMAC signing.
            var header = BundleReader.ReadHeader(path);
            if (header == null) return;
            var info = new FileInfo(path);
            var entry = new LibraryEntry(path, header, info.Length, info.LastWriteTime);

            var dialog = new SendBundleDialog(entry) { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
        }

        private void OnShowInExplorer(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item) return;
            var path = item.Tag as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            // /select, opens Explorer with the file pre-selected.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }

        private static StackPanel BuildDetailContent(LibraryItemViewModel item)
        {
            var stack = new StackPanel { Spacing = 6 };
            void Row(string label, string value)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var l = new TextBlock
                {
                    Text = label.ToUpperInvariant(),
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                };
                var v = new TextBlock
                {
                    Text = value,
                    FontSize = 12,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                };
                Grid.SetColumn(l, 0);
                Grid.SetColumn(v, 1);
                grid.Children.Add(l);
                grid.Children.Add(v);
                stack.Children.Add(grid);
            }

            Row("Path", item.Path);
            Row("Bundle ID", item.BundleId);
            Row("Track", item.Track);
            Row("Car", item.Car);
            Row("Duration", item.Duration);
            Row("Resolution", item.Resolution);
            Row("File size", item.FileSize);
            Row("Frames", item.FrameCount);
            Row("Recorded by", item.RecordedBy);
            Row("Created", item.CreatedAt);
            return stack;
        }
    }

    /// <summary>
    /// Display-shaped projection of a <see cref="LibraryEntry"/>. Strings only —
    /// the data template binds to these without further conversion.
    /// </summary>
    public sealed class LibraryItemViewModel
    {
        public string Path { get; init; } = "";
        public string BundleId { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string Duration { get; init; } = "";
        public string FileSize { get; init; } = "";
        public string Resolution { get; init; } = "";
        public string FrameCount { get; init; } = "";
        public string RecordedBy { get; init; } = "";
        public string CreatedAt { get; init; } = "";
        public string Track { get; init; } = "";
        public string Car { get; init; } = "";
        public Visibility DemoChipVisibility { get; init; } = Visibility.Collapsed;

        public static LibraryItemViewModel From(LibraryEntry e) => new()
        {
            Path = e.Path,
            BundleId = e.Header.BundleId,
            Title = e.DisplayTitle,
            Subtitle = $"{e.DisplayCreatedAt}  ·  {e.DisplayFileSize}",
            Duration = e.DisplayDuration,
            FileSize = e.DisplayFileSize,
            Resolution = $"{e.Header.Video.Width} × {e.Header.Video.Height}",
            FrameCount = e.Header.Telemetry.FrameCount.ToString("N0"),
            RecordedBy = $"{e.Header.CreatedBy.App} {e.Header.CreatedBy.Version ?? ""}".Trim(),
            CreatedAt = e.DisplayCreatedAt,
            Track = e.Header.Race.Track ?? "—",
            Car = e.Header.Race.Car ?? "—",
            DemoChipVisibility = (e.Header.Race.IsDemo == true) ? Visibility.Visible : Visibility.Collapsed,
        };
    }
}
