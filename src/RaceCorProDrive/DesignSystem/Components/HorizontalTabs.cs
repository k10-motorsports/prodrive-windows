using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Generic horizontal tab strip mirroring the dashboard's
    /// <see cref="DashboardTabs"/> visual: text labels with a brand-red
    /// underline marking the active tab. Unlike DashboardTabs (which
    /// hardcodes a 3-tab enum), this one takes a string-keyed option
    /// list so the same chrome can drive any page's tab switching.
    /// </summary>
    public sealed class HorizontalTabs : UserControl
    {
        public sealed class Tab
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
        }

        public static readonly DependencyProperty TabsProperty = DependencyProperty.Register(
            nameof(Tabs), typeof(IReadOnlyList<Tab>), typeof(HorizontalTabs),
            new PropertyMetadata(null, (d, _) => ((HorizontalTabs)d).Rebuild()));

        public static readonly DependencyProperty SelectedKeyProperty = DependencyProperty.Register(
            nameof(SelectedKey), typeof(string), typeof(HorizontalTabs),
            new PropertyMetadata("", (d, _) => ((HorizontalTabs)d).RefreshActiveStates()));

        public IReadOnlyList<Tab>? Tabs
        {
            get => (IReadOnlyList<Tab>?)GetValue(TabsProperty);
            set => SetValue(TabsProperty, value);
        }

        public string SelectedKey
        {
            get => (string)GetValue(SelectedKeyProperty);
            set => SetValue(SelectedKeyProperty, value);
        }

        public event EventHandler<string>? SelectionChanged;

        private readonly StackPanel _row;

        public HorizontalTabs()
        {
            _row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Content = _row;
        }

        private void Rebuild()
        {
            _row.Children.Clear();
            var tabs = Tabs;
            if (tabs == null) return;
            foreach (var tab in tabs)
            {
                _row.Children.Add(new TabButton(tab.Label, tab.Key, OnTabTapped));
            }
            RefreshActiveStates();
        }

        private void OnTabTapped(string key)
        {
            if (key == SelectedKey) return;
            SelectedKey = key;
            SelectionChanged?.Invoke(this, key);
        }

        private void RefreshActiveStates()
        {
            foreach (var child in _row.Children)
            {
                if (child is TabButton tb)
                {
                    tb.IsActive = tb.TabKey == SelectedKey;
                }
            }
        }

        private sealed class TabButton : UserControl
        {
            public string TabKey { get; }

            private readonly TextBlock _label;
            private readonly Microsoft.UI.Xaml.Shapes.Rectangle _underline;
            private bool _isActive;

            public bool IsActive
            {
                get => _isActive;
                set { _isActive = value; UpdateState(); }
            }

            public TabButton(string text, string key, Action<string> onTap)
            {
                TabKey = key;

                _label = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
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

                Content = new Border
                {
                    Padding = new Thickness(12, 8, 12, 8),
                    Child = grid,
                };
                Tapped += (_, __) => onTap(TabKey);
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
