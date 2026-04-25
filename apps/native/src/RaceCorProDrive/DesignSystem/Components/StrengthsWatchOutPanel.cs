using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Two-column "Strengths / Watch Out" panel rendered from
    /// pre-computed copy on the dashboard payload. Mirrors the
    /// SwiftUI <c>StrengthsWatchOutPanel</c>: each side shows a label,
    /// a multiline window header, an optional iR-delta footnote, a
    /// prose summary paragraph, and a list of bullet insights.
    /// </summary>
    public sealed class StrengthsWatchOutPanel : UserControl
    {
        public sealed class SideModel
        {
            public string Label { get; set; } = "";
            public Color Tint { get; set; }
            public string Window { get; set; } = "";
            public string? Footnote { get; set; }
            public string Summary { get; set; } = "";
            public IReadOnlyList<string> Bullets { get; set; } = System.Array.Empty<string>();
        }

        public static readonly DependencyProperty StrengthsProperty = DependencyProperty.Register(
            nameof(Strengths), typeof(SideModel), typeof(StrengthsWatchOutPanel),
            new PropertyMetadata(null, (d, _) => ((StrengthsWatchOutPanel)d).Rebuild()));

        public static readonly DependencyProperty WatchOutProperty = DependencyProperty.Register(
            nameof(WatchOut), typeof(SideModel), typeof(StrengthsWatchOutPanel),
            new PropertyMetadata(null, (d, _) => ((StrengthsWatchOutPanel)d).Rebuild()));

        public SideModel? Strengths
        {
            get => (SideModel?)GetValue(StrengthsProperty);
            set => SetValue(StrengthsProperty, value);
        }

        public SideModel? WatchOut
        {
            get => (SideModel?)GetValue(WatchOutProperty);
            set => SetValue(WatchOutProperty, value);
        }

        private readonly Grid _columns;
        private readonly Panel _shell;

        public StrengthsWatchOutPanel()
        {
            _columns = new Grid { ColumnSpacing = 24 };
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _shell = new Panel { Content = _columns };
            Content = _shell;
            Rebuild();
        }

        private void Rebuild()
        {
            _columns.Children.Clear();

            if (Strengths is { } s)
            {
                var col = BuildColumn(s);
                Grid.SetColumn(col, 0);
                _columns.Children.Add(col);
            }
            if (WatchOut is { } w)
            {
                var col = BuildColumn(w);
                Grid.SetColumn(col, 1);
                _columns.Children.Add(col);
            }
        }

        private static StackPanel BuildColumn(SideModel m)
        {
            var col = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12 };

            col.Children.Add(new TextBlock
            {
                Text = m.Label.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 120,
                Foreground = new SolidColorBrush(m.Tint),
            });

            var headerRow = new Grid { ColumnSpacing = 16 };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 110 });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var windowStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            windowStack.Children.Add(new TextBlock
            {
                Text = m.Window,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            });
            if (!string.IsNullOrEmpty(m.Footnote))
            {
                windowStack.Children.Add(new TextBlock
                {
                    Text = m.Footnote,
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                });
            }
            Grid.SetColumn(windowStack, 0);
            headerRow.Children.Add(windowStack);

            var summary = new TextBlock
            {
                Text = m.Summary,
                FontSize = 12,
                MaxLines = 4,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            };
            Grid.SetColumn(summary, 1);
            headerRow.Children.Add(summary);

            col.Children.Add(headerRow);

            if (m.Bullets.Count > 0)
            {
                col.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 4, 0, 0),
                    Background = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                });
                var bullets = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
                foreach (var b in m.Bullets)
                {
                    bullets.Children.Add(new TextBlock
                    {
                        Text = "—  " + b,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    });
                }
                col.Children.Add(bullets);
            }

            return col;
        }
    }
}
