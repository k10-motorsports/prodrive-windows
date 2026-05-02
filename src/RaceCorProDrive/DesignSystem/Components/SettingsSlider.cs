using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Slider with a live value badge next to its label and an optional
    /// caption underneath. Drop into a <see cref="SettingsCard"/>; it
    /// renders self-contained as label+value above, slider below — wider
    /// than a standard <see cref="SettingsRow"/> child because the slider
    /// itself needs horizontal room.
    /// </summary>
    public sealed class SettingsSlider : UserControl
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(SettingsSlider),
            new PropertyMetadata("", (d, _) => ((SettingsSlider)d).Apply()));

        public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(SettingsSlider),
            new PropertyMetadata("", (d, _) => ((SettingsSlider)d).Apply()));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(SettingsSlider),
            new PropertyMetadata(0.0, (d, _) => ((SettingsSlider)d).Apply()));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(SettingsSlider),
            new PropertyMetadata(100.0, (d, _) => ((SettingsSlider)d).Apply()));

        public static readonly DependencyProperty StepFrequencyProperty = DependencyProperty.Register(
            nameof(StepFrequency), typeof(double), typeof(SettingsSlider),
            new PropertyMetadata(1.0, (d, _) => ((SettingsSlider)d).Apply()));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(SettingsSlider),
            new PropertyMetadata(0.0, (d, _) => ((SettingsSlider)d).Apply()));

        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(SettingsSlider),
            new PropertyMetadata("0.##", (d, _) => ((SettingsSlider)d).Apply()));

        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(SettingsSlider),
            new PropertyMetadata("", (d, _) => ((SettingsSlider)d).Apply()));

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

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double StepFrequency
        {
            get => (double)GetValue(StepFrequencyProperty);
            set => SetValue(StepFrequencyProperty, value);
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Numeric format string for the live value badge. Default "0.##".</summary>
        public string ValueFormat
        {
            get => (string)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        /// <summary>Optional unit appended to the value badge (e.g. "%", "s", "px").</summary>
        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public event EventHandler<double>? ValueCommitted;

        private readonly TextBlock _labelText;
        private readonly TextBlock _valueText;
        private readonly TextBlock _captionText;
        private readonly Slider _slider;
        private bool _suppressEvent;

        public SettingsSlider()
        {
            _labelText = new TextBlock
            {
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            _valueText = new TextBlock
            {
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            _captionText = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 2, 0, 4),
            };

            _slider = new Slider
            {
                MinWidth = 280,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _slider.ValueChanged += OnSliderValueChanged;

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_labelText, 0);
            Grid.SetColumn(_valueText, 1);
            headerRow.Children.Add(_labelText);
            headerRow.Children.Add(_valueText);

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Padding = new Thickness(0, 6, 0, 6),
            };
            stack.Children.Add(headerRow);
            stack.Children.Add(_captionText);
            stack.Children.Add(_slider);

            Content = stack;
            Apply();
        }

        private void OnSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressEvent) return;
            // Two-way: the dependency property is the source of truth so
            // Value bindings outside this control stay in sync.
            Value = e.NewValue;
            UpdateValueText();
            ValueCommitted?.Invoke(this, e.NewValue);
        }

        private void Apply()
        {
            _labelText.Text = Label ?? string.Empty;
            _captionText.Text = Caption ?? string.Empty;
            _captionText.Visibility = string.IsNullOrEmpty(Caption) ? Visibility.Collapsed : Visibility.Visible;

            _suppressEvent = true;
            try
            {
                _slider.Minimum = Minimum;
                _slider.Maximum = Maximum;
                _slider.StepFrequency = StepFrequency;
                _slider.Value = Value;
            }
            finally { _suppressEvent = false; }

            UpdateValueText();
        }

        private void UpdateValueText()
        {
            var formatted = Value.ToString(ValueFormat ?? "0.##");
            _valueText.Text = string.IsNullOrEmpty(Unit) ? formatted : $"{formatted}{Unit}";
        }
    }
}
