using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace RaceCorProDrive.DesignSystem.Components
{
    public enum Verdict { Good, Marginal, Bad, Unknown }

    /// <summary>
    /// "Should you race right now?" advisory panel. Mirrors the Swift
    /// <c>ShouldYouRacePanel</c>: small caps header, big verdict,
    /// reason. Detail body (peak/worst windows + current-time
    /// annotation) appears when <see cref="Detail"/> is non-null;
    /// rendered statically (no expand/collapse for v1).
    /// </summary>
    public sealed class ShouldYouRacePanel : UserControl
    {
        public sealed class DetailModel
        {
            public string? PeakWindow { get; set; }
            public string? PeakDelta { get; set; }
            public string? WorstWindow { get; set; }
            public string? WorstDelta { get; set; }
            public string? NowDescription { get; set; }
        }

        public static readonly DependencyProperty VerdictProperty = DependencyProperty.Register(
            nameof(Verdict), typeof(Verdict), typeof(ShouldYouRacePanel),
            new PropertyMetadata(Verdict.Unknown, (d, _) => ((ShouldYouRacePanel)d).Rebuild()));

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(ShouldYouRacePanel),
            new PropertyMetadata("", (d, _) => ((ShouldYouRacePanel)d).Rebuild()));

        public static readonly DependencyProperty ReasonProperty = DependencyProperty.Register(
            nameof(Reason), typeof(string), typeof(ShouldYouRacePanel),
            new PropertyMetadata("", (d, _) => ((ShouldYouRacePanel)d).Rebuild()));

        public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
            nameof(Detail), typeof(DetailModel), typeof(ShouldYouRacePanel),
            new PropertyMetadata(null, (d, _) => ((ShouldYouRacePanel)d).Rebuild()));

        public Verdict Verdict
        {
            get => (Verdict)GetValue(VerdictProperty);
            set => SetValue(VerdictProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Reason
        {
            get => (string)GetValue(ReasonProperty);
            set => SetValue(ReasonProperty, value);
        }

        public DetailModel? Detail
        {
            get => (DetailModel?)GetValue(DetailProperty);
            set => SetValue(DetailProperty, value);
        }

        private readonly Panel _shell;
        private readonly StackPanel _content;

        public ShouldYouRacePanel()
        {
            _content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            _shell = new Panel { Content = _content, Padding = new Thickness(14) };
            Content = _shell;
            Rebuild();
        }

        private void Rebuild()
        {
            _content.Children.Clear();

            var tint = TintFor(Verdict);
            _shell.BorderTint = new SolidColorBrush(WithAlpha(tint, 0x59));

            // Top row: header + verdict + reason
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            labelStack.Children.Add(new TextBlock
            {
                Text = "SHOULD YOU RACE RIGHT NOW?",
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 120,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
            });

            var verdictRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            verdictRow.Children.Add(new TextBlock
            {
                Text = Title,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(tint),
            });
            verdictRow.Children.Add(new TextBlock
            {
                Text = Reason,
                FontSize = 12,
                MaxLines = 2,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            });
            labelStack.Children.Add(verdictRow);
            Grid.SetColumn(labelStack, 1);
            top.Children.Add(labelStack);
            _content.Children.Add(top);

            if (Detail is { } d)
            {
                _content.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 12, 0, 10),
                    Background = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                });

                AddDetail(d.PeakWindow, d.PeakDelta,
                    Color.FromArgb(0xFF, 0x34, 0xC7, 0x59));
                AddDetail(d.WorstWindow, d.WorstDelta,
                    Color.FromArgb(0xFF, 0xE5, 0x39, 0x35));
                AddDetail(d.NowDescription, null,
                    Color.FromArgb(0xFF, 0xB0, 0xB0, 0xB0));
            }
        }

        private void AddDetail(string? headline, string? sub, Color iconTint)
        {
            if (string.IsNullOrEmpty(headline)) return;
            var row = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                Margin = new Thickness(0, 0, 0, 8),
            };
            row.Children.Add(new TextBlock
            {
                Text = headline,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            });
            if (!string.IsNullOrEmpty(sub))
            {
                row.Children.Add(new TextBlock
                {
                    Text = sub,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                });
            }
            _content.Children.Add(row);
        }

        private static Color TintFor(Verdict v) => v switch
        {
            Verdict.Good     => Color.FromArgb(0xFF, 0x34, 0xC7, 0x59),
            Verdict.Marginal => Color.FromArgb(0xFF, 0xFF, 0xCF, 0x4A),
            Verdict.Bad      => Color.FromArgb(0xFF, 0xE5, 0x39, 0x35),
            _                => Color.FromArgb(0xFF, 0xB0, 0xB0, 0xB0),
        };

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    }
}
