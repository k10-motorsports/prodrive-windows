using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// One field row inside a <see cref="SettingsCard"/>. Layout is
    /// label-left (with optional caption underneath) and control-right.
    /// Replaces the inline <c>Header=</c> on ToggleSwitch/ComboBox/etc.
    /// which gave inconsistent vertical heights and label positioning
    /// per control type — this gives every row a uniform shape.
    /// </summary>
    public sealed class SettingsRow : ContentControl
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(SettingsRow),
            new PropertyMetadata("", (d, _) => ((SettingsRow)d).Apply()));

        public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(SettingsRow),
            new PropertyMetadata("", (d, _) => ((SettingsRow)d).Apply()));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string Caption
        {
            get => (string)GetValue(CaptionProperty);
            set => SetValue(CaptionProperty, value);
        }

        private readonly Grid _layout;
        private readonly TextBlock _labelText;
        private readonly TextBlock _captionText;
        private readonly ContentPresenter _controlHost;

        public SettingsRow()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;

            _labelText = new TextBlock
            {
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            _captionText = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 2, 0, 0),
            };
            _controlHost = new ContentPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var leftStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 0,
            };
            leftStack.Children.Add(_labelText);
            leftStack.Children.Add(_captionText);

            _layout = new Grid { Padding = new Thickness(0, 6, 0, 6), ColumnSpacing = 12 };
            _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(leftStack, 0);
            Grid.SetColumn(_controlHost, 1);
            _layout.Children.Add(leftStack);
            _layout.Children.Add(_controlHost);

            base.Content = _layout;
            Apply();
        }

        // Hide ContentControl.Content so XAML <c:SettingsRow><Toggle/></c:SettingsRow>
        // assigns the inner control instead of replacing the row layout.
        public new object? Content
        {
            get => _controlHost.Content;
            set => _controlHost.Content = value;
        }

        private void Apply()
        {
            _labelText.Text = Label ?? string.Empty;
            _labelText.Visibility = string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
            _captionText.Text = Caption ?? string.Empty;
            _captionText.Visibility = string.IsNullOrEmpty(Caption) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
