using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Horizontal pill picker for short option lists (2-4 items). Better
    /// glanceability than a ComboBox dropdown — every option is visible
    /// at all times, the active one shows brand-tinted. Drop into a
    /// <see cref="SettingsRow"/>'s control slot for label-left,
    /// segmented-right layouts; or use standalone for cases where the
    /// picker's the whole row.
    /// </summary>
    public sealed class SegmentedPicker : UserControl
    {
        public sealed class Option
        {
            public string Value { get; init; } = "";
            public string Label { get; init; } = "";
        }

        public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
            nameof(Options), typeof(IReadOnlyList<Option>), typeof(SegmentedPicker),
            new PropertyMetadata(null, (d, _) => ((SegmentedPicker)d).Rebuild()));

        public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
            nameof(SelectedValue), typeof(string), typeof(SegmentedPicker),
            new PropertyMetadata("", (d, _) => ((SegmentedPicker)d).RefreshActiveStates()));

        public IReadOnlyList<Option>? Options
        {
            get => (IReadOnlyList<Option>?)GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
        }

        public string SelectedValue
        {
            get => (string)GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        /// <summary>Fires when the user picks a different segment.</summary>
        public event EventHandler<string>? SelectionChanged;

        private readonly StackPanel _strip;
        private readonly Border _shell;

        public SegmentedPicker()
        {
            _strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
            };
            _shell = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                Child = _strip,
            };
            Content = _shell;
        }

        private void Rebuild()
        {
            _strip.Children.Clear();
            var opts = Options;
            if (opts == null || opts.Count == 0) return;

            for (int i = 0; i < opts.Count; i++)
            {
                var opt = opts[i];
                var isFirst = i == 0;
                var isLast = i == opts.Count - 1;

                var label = new TextBlock
                {
                    Text = opt.Label,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var cell = new Border
                {
                    Padding = new Thickness(14, 6, 14, 6),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    CornerRadius = new CornerRadius(
                        isFirst ? 7 : 0,
                        isLast ? 7 : 0,
                        isLast ? 7 : 0,
                        isFirst ? 7 : 0),
                    Child = label,
                    Tag = opt.Value,
                };
                cell.Tapped += OnCellTapped;
                if (!isLast)
                {
                    var divider = new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        Width = 1,
                        Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                        VerticalAlignment = VerticalAlignment.Stretch,
                    };
                    _strip.Children.Add(cell);
                    _strip.Children.Add(divider);
                }
                else
                {
                    _strip.Children.Add(cell);
                }
            }
            RefreshActiveStates();
        }

        private void OnCellTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Border cell && cell.Tag is string value)
            {
                if (value == SelectedValue) return;
                SelectedValue = value;
                SelectionChanged?.Invoke(this, value);
            }
        }

        private void RefreshActiveStates()
        {
            foreach (var child in _strip.Children)
            {
                if (child is Border cell && cell.Tag is string value)
                {
                    var isActive = value == SelectedValue;
                    cell.Background = isActive
                        ? new SolidColorBrush(Color.FromArgb(0x59, 0xE5, 0x39, 0x35))
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    if (cell.Child is TextBlock tb)
                    {
                        tb.Foreground = isActive
                            ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                            : (Brush)Application.Current.Resources["TextDimBrush"];
                    }
                }
            }
        }
    }
}
