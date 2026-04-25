using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Tab picker matching the Swift <c>DashboardTabs</c>: three text
    /// labels with a brand-red underline marking the active tab. Lives
    /// at the top of the dashboard's main column.
    /// </summary>
    public sealed class DashboardTabs : UserControl
    {
        public enum Tab { Performance, NextRace, Previous }

        public event EventHandler<Tab>? SelectionChanged;

        public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
            nameof(Selected), typeof(Tab), typeof(DashboardTabs),
            new PropertyMetadata(Tab.Performance, (d, _) => ((DashboardTabs)d).Refresh()));

        public Tab Selected
        {
            get => (Tab)GetValue(SelectedProperty);
            set => SetValue(SelectedProperty, value);
        }

        private readonly StackPanel _row;
        private readonly TabButton _perf;
        private readonly TabButton _next;
        private readonly TabButton _prev;

        public DashboardTabs()
        {
            _perf = MakeButton("Performance", Tab.Performance);
            _next = MakeButton("Next Race", Tab.NextRace);
            _prev = MakeButton("Previous Races", Tab.Previous);

            _row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _row.Children.Add(_perf);
            _row.Children.Add(_next);
            _row.Children.Add(_prev);

            Content = _row;
            Refresh();
        }

        private TabButton MakeButton(string label, Tab tab)
        {
            var btn = new TabButton(label);
            btn.Tapped += (_, __) =>
            {
                Selected = tab;
                SelectionChanged?.Invoke(this, tab);
            };
            return btn;
        }

        private void Refresh()
        {
            _perf.IsActive = Selected == Tab.Performance;
            _next.IsActive = Selected == Tab.NextRace;
            _prev.IsActive = Selected == Tab.Previous;
        }

        private sealed class TabButton : Border
        {
            private readonly TextBlock _label;
            private readonly Microsoft.UI.Xaml.Shapes.Rectangle _underline;
            private bool _isActive;

            public bool IsActive
            {
                get => _isActive;
                set { _isActive = value; UpdateState(); }
            }

            public TabButton(string text)
            {
                _label = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                };
                _underline = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Height = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0x39, 0x35)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };

                var grid = new Grid();
                grid.Children.Add(_label);
                grid.Children.Add(_underline);
                _label.HorizontalAlignment = HorizontalAlignment.Center;
                _label.VerticalAlignment = VerticalAlignment.Center;

                Padding = new Thickness(12, 8, 12, 8);
                Child = grid;
                UpdateState();
            }

            private void UpdateState()
            {
                _label.Foreground = _isActive
                    ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextDimBrush"];
                _underline.Visibility = _isActive ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
