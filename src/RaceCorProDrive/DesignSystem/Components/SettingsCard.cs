using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Boxed card for grouping related settings on the SettingsPage.
    /// Mirrors the web's <c>SectionCard.tsx</c>: a small-caps title +
    /// optional caption above a body of <see cref="SettingsRow"/>s.
    /// Renders inside a <see cref="Panel"/> chrome so it picks up the
    /// rest of the app's elevated-surface look automatically.
    /// </summary>
    public sealed class SettingsCard : ContentControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(SettingsCard),
            new PropertyMetadata("", (d, _) => ((SettingsCard)d).Apply()));

        public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(SettingsCard),
            new PropertyMetadata("", (d, _) => ((SettingsCard)d).Apply()));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Caption
        {
            get => (string)GetValue(CaptionProperty);
            set => SetValue(CaptionProperty, value);
        }

        private readonly TextBlock _titleText;
        private readonly TextBlock _captionText;
        private readonly StackPanel _bodyHost;
        private readonly Panel _shell;

        public SettingsCard()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;

            _titleText = new TextBlock
            {
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 160,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 0, 0, 4),
            };
            _captionText = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                Margin = new Thickness(0, 0, 0, 12),
            };
            _bodyHost = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };

            var inner = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            inner.Children.Add(_titleText);
            inner.Children.Add(_captionText);
            inner.Children.Add(_bodyHost);

            _shell = new Panel { Content = inner };
            base.Content = _shell;
            Apply();
        }

        // Hide ContentControl.Content so XAML <c:SettingsCard><X/></c:SettingsCard>
        // assigns to the inner body host instead of replacing the whole shell.
        public new object? Content
        {
            get => _bodyHost.Children.Count == 1 ? _bodyHost.Children[0] : _bodyHost.Children;
            set
            {
                _bodyHost.Children.Clear();
                if (value is UIElement single)
                {
                    _bodyHost.Children.Add(single);
                }
            }
        }

        /// <summary>Add a row directly without wrapping it in a fresh container.</summary>
        public void AddRow(UIElement row) => _bodyHost.Children.Add(row);

        public void ClearRows() => _bodyHost.Children.Clear();

        /// <summary>
        /// Number of rows currently stacked in the body. Used by the
        /// SettingsPage's two-column layout to estimate a card's height
        /// before render so the columns can be balanced vertically.
        /// </summary>
        public int RowCount => _bodyHost.Children.Count;

        private void Apply()
        {
            _titleText.Text = (Title ?? string.Empty).ToUpperInvariant();
            _titleText.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
            _captionText.Text = Caption ?? string.Empty;
            _captionText.Visibility = string.IsNullOrEmpty(Caption) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
