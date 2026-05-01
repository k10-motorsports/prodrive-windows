using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Stylized panel shell used by every named card (Should-You-Race,
    /// Strengths/Watch-Out, viz cards). Mirrors the SwiftUI
    /// <c>.panel()</c> modifier — rounded background tinted with the
    /// dark base color, subtle 1pt border. Use as a container by
    /// setting <see cref="ContentControl.Content"/> from XAML or code.
    /// </summary>
    public sealed class Panel : ContentControl
    {
        public static readonly DependencyProperty TintBrushProperty = DependencyProperty.Register(
            nameof(TintBrush), typeof(Brush), typeof(Panel),
            new PropertyMetadata(null, (d, _) => ((Panel)d).Apply()));

        public static readonly DependencyProperty BorderTintProperty = DependencyProperty.Register(
            nameof(BorderTint), typeof(Brush), typeof(Panel),
            new PropertyMetadata(null, (d, _) => ((Panel)d).Apply()));

        public Brush? TintBrush
        {
            get => (Brush?)GetValue(TintBrushProperty);
            set => SetValue(TintBrushProperty, value);
        }

        public Brush? BorderTint
        {
            get => (Brush?)GetValue(BorderTintProperty);
            set => SetValue(BorderTintProperty, value);
        }

        private readonly Border _border;

        public Panel()
        {
            // Stretch the inner Border to fill the templated
            // ContentPresenter slot. ContentControl defaults its
            // HorizontalContentAlignment to Left, which sizes _border
            // to its child's intrinsic width — fine for a card that's
            // measured in isolation, broken when the panel sits in a
            // Grid column that's supposed to fill 1/3 of the row.
            // Without this, the 3-up calendar/schedule/DNA cards
            // collapse to their content width and stop short of the
            // column edge, misaligning with the full-width panels
            // above them.
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;

            _border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            // ContentControl forwards Content to Border via Template;
            // we sidestep that with an explicit nested ContentPresenter
            // so Content always lives inside the styled border.
            base.Content = _border;
            Apply();
        }

        // Hide the inherited Content setter so callers' XAML
        // <local:Panel><Grid/></local:Panel> assigns to the inner
        // border instead of replacing the whole Border.
        public new object? Content
        {
            get => _border.Child;
            set => _border.Child = value as UIElement;
        }

        private void Apply()
        {
            _border.Background = TintBrush
                ?? (Brush)Application.Current.Resources["BgPanelBrush"];
            _border.BorderBrush = BorderTint
                ?? (Brush)Application.Current.Resources["BorderSubtleBrush"];
        }
    }
}
