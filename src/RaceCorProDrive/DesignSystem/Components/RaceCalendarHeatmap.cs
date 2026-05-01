using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using RaceCorProDrive.Api;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Monthly calendar heatmap. Mirrors the SwiftUI
    /// <c>RaceCalendarHeatmap</c> 1:1 — rows = weeks, columns =
    /// weekdays (M…S, Monday-first), cells colored by session count
    /// (saturated red ramp). Sourced from <c>recentSessions</c> so
    /// the specific-date axis is preserved. Cells stretch to fill
    /// the panel width — wide rectangles, not squares — so the
    /// calendar reads "shorter but way wider" same as the Mac.
    /// </summary>
    public sealed class RaceCalendarHeatmap : UserControl
    {
        public static readonly DependencyProperty SessionsProperty = DependencyProperty.Register(
            nameof(Sessions), typeof(IReadOnlyList<RecentSession>), typeof(RaceCalendarHeatmap),
            new PropertyMetadata(null, (d, _) => ((RaceCalendarHeatmap)d).Rebuild()));

        public IReadOnlyList<RecentSession>? Sessions
        {
            get => (IReadOnlyList<RecentSession>?)GetValue(SessionsProperty);
            set => SetValue(SessionsProperty, value);
        }

        // 6 week rows + Mon-first column order — matches the Swift
        // RaceCalendarHeatmap.weekRows + dayLabels exactly.
        private const int WeekRows = 6;
        private static readonly string[] DayLabels = { "M", "T", "W", "T", "F", "S", "S" };

        // 28pt cell height with 4pt spacing — sized for the 3-up
        // dashboard layout where the calendar lives in a 33%-width
        // column. The Mac build uses 20pt cells in a much narrower
        // window; on Windows at 1/3 of the main column each cell
        // ends up roughly square at 28pt, balancing visual presence
        // with the schedule + DNA peers next to it.
        private const double CellHeight = 28;
        private const double CellSpacing = 4;

        public RaceCalendarHeatmap()
        {
            Rebuild();
        }

        private Dictionary<DateTime, int> ComputeSessionsByDay()
        {
            var result = new Dictionary<DateTime, int>();
            if (Sessions != null)
            {
                foreach (var s in Sessions)
                {
                    var date = s.StartTime.ToLocalTime().Date;
                    result.TryGetValue(date, out var count);
                    result[date] = count + 1;
                }
            }
            return result;
        }

        private void Rebuild()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };

            // Header: icon + title (left) + month label (right). Mac
            // build doesn't render month nav controls — just a static
            // "MMM yyyy" label.
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "", // Calendar glyph
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 0);
            headerRow.Children.Add(icon);

            var title = new TextBlock
            {
                Text = "Race Calendar",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(title, 1);
            headerRow.Children.Add(title);

            var monthLabel = new TextBlock
            {
                Text = DateTime.Now.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(monthLabel, 3);
            headerRow.Children.Add(monthLabel);
            stack.Children.Add(headerRow);

            stack.Children.Add(BuildGrid());

            // No outer Padding on Panel — that's the ContentControl
            // Padding which insets the visible card edge from the
            // UserControl bounds and breaks left-edge alignment with
            // the ShouldYouRace / Strengths panels above. Inner
            // Border in Panel already pads to 16pt.
            Content = new Panel { Content = stack };
        }

        private FrameworkElement BuildGrid()
        {
            var sessionsByDay = ComputeSessionsByDay();
            var gridStart = ComputeGridStartDate();
            var today = DateTime.Now.Date;

            var outer = new StackPanel { Orientation = Orientation.Vertical, Spacing = CellSpacing };

            // Weekday header row — 7 stretched columns matching the
            // grid below. No left gutter (Mac build doesn't show
            // week labels).
            var labelRow = new Grid { ColumnSpacing = CellSpacing };
            for (int i = 0; i < 7; i++)
            {
                labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var lbl = new TextBlock
                {
                    Text = DayLabels[i],
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    CharacterSpacing = 60,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                Grid.SetColumn(lbl, i);
                labelRow.Children.Add(lbl);
            }
            outer.Children.Add(labelRow);

            for (int week = 0; week < WeekRows; week++)
            {
                var row = new Grid { ColumnSpacing = CellSpacing };
                for (int day = 0; day < 7; day++)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var cellDate = gridStart.AddDays(week * 7 + day);
                    var count = sessionsByDay.TryGetValue(cellDate, out var n) ? n : 0;
                    var isFuture = cellDate > today;
                    var cell = BuildDayCell(count, isFuture);
                    Grid.SetColumn(cell, day);
                    row.Children.Add(cell);
                }
                outer.Children.Add(row);
            }
            return outer;
        }

        private static FrameworkElement BuildDayCell(int count, bool isFuture)
        {
            var intensity = Math.Min(1.0, count / 3.0);  // 3+ saturates
            var border = new Border
            {
                Height = CellHeight,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(ComputeCellFill(intensity, isFuture)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            if (count > 0)
            {
                border.Child = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            return border;
        }

        /// <summary>
        /// Mac build's red ramp: <c>#b02020</c> with alpha 0.2..0.9
        /// scaled by session-count intensity (0..3+). Empty days
        /// fade into the background; future days are nearly clear.
        /// </summary>
        private static Color ComputeCellFill(double intensity, bool isFuture)
        {
            if (isFuture) return Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
            if (intensity == 0) return Color.FromArgb(0x66, 0x0A, 0x0A, 0x14);
            var alpha = (byte)Math.Round((0.2 + 0.7 * intensity) * 255);
            return Color.FromArgb(alpha, 0xB0, 0x20, 0x20);
        }

        /// <summary>
        /// Start of the week, N weeks back from today, aligned to
        /// Monday so the column layout matches the Mac build's
        /// Mon-first ordering.
        /// </summary>
        private static DateTime ComputeGridStartDate()
        {
            var today = DateTime.Now.Date;
            var dow = (int)today.DayOfWeek;
            // C#: Sunday = 0; we want Monday = 0.
            var fromMonday = (dow + 6) % 7;
            var weekStart = today.AddDays(-fromMonday);
            return weekStart.AddDays(-(WeekRows - 1) * 7);
        }
    }
}
