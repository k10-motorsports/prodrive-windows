using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using RaceCorProDrive.Api;
using RaceCorProDrive.Support;

// `Path` is ambiguous between Microsoft.UI.Xaml.Shapes.Path (the
// vector shape we use for the gauge arcs) and System.IO.Path (pulled
// in by ImplicitUsings). Alias to the shape so BuildArc compiles.
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Pit Wall — racing-paddock at-a-glance dashboard. Surfaces the
    /// four load-bearing platform metrics (Composure, Heat, Streaks,
    /// Discipline Mix) defined in <c>agents/prodrive-context/glossary.md</c>.
    /// Mirrors the web app at <c>/drive/pit-wall</c> visually and copy-wise
    /// so the experience is consistent across surfaces.
    /// </summary>
    /// <remarks>
    /// Vocabulary rule: every user-visible string and every member name
    /// uses racing-native terms. No finance vocabulary anywhere.
    /// Data source is <see cref="DashboardPoller.Shared"/> — the same
    /// poller every other page consumes so the values always match.
    /// </remarks>
    public sealed partial class PitWallPage : Page
    {
        private const double GaugeArcStartDeg = 135;
        private const double GaugeArcSweepDeg = 270;
        private const double GaugeRadius = 96;
        private const double GaugeStrokeWidth = 16;
        private const double GaugeCenterXY = 120;

        private Storyboard? _gaugeBreathe;
        private Storyboard? _heatPulse;

        public PitWallPage()
        {
            InitializeComponent();
        }

        // ── Lifecycle ───────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            App.MainWindow?.SetTitleBarTabs(null);
            App.MainWindow?.SetTitleBarFilters(null);

            DashboardPoller.Shared.PropertyChanged += OnPollerChanged;
            DashboardPoller.Shared.Start();

            DrawGaugeTrack();
            Render(DashboardPoller.Shared.Latest);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DashboardPoller.Shared.PropertyChanged -= OnPollerChanged;
            _gaugeBreathe?.Stop();
            _heatPulse?.Stop();
        }

        private void OnPollerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardPoller.Latest))
            {
                DispatcherQueue.TryEnqueue(() => Render(DashboardPoller.Shared.Latest));
            }
        }

        // ── Render ─────────────────────────────────────────────────

        private void Render(Dashboard? dash)
        {
            var composure = dash?.Composure;
            var heat = dash?.Heat;
            var streaks = dash?.Streaks;
            var mix = dash?.DisciplineMix;

            // Hero
            HeadlineText.Text = BuildHeadline(composure, heat, streaks);

            // Gauge
            RenderGauge(composure, heat?.Direction);

            // Heat
            RenderHeat(heat);

            // Current streak
            RenderCurrentStreak(streaks);

            // Streak history
            RenderStreakHistory(streaks);

            // Discipline mix
            RenderDisciplineMix(mix);

            // Footnote
            if ((composure?.SampleSize ?? 0) > 0)
            {
                FootnoteText.Text = $"Composure rolls over your last {composure!.SampleSize} races. Heat watches the most recent {heat?.SampleSize ?? 0}.";
                FootnoteText.Visibility = Visibility.Visible;
            }
            else
            {
                FootnoteText.Visibility = Visibility.Collapsed;
            }
        }

        // ── Gauge ──────────────────────────────────────────────────

        private void DrawGaugeTrack()
        {
            // Background track arc — drawn once.
            var track = BuildArc(GaugeArcStartDeg, GaugeArcStartDeg + GaugeArcSweepDeg);
            track.Stroke = BrushFromHex("#2A2A36");
            track.StrokeThickness = GaugeStrokeWidth;
            track.StrokeStartLineCap = PenLineCap.Round;
            track.StrokeEndLineCap = PenLineCap.Round;
            GaugeCanvas.Children.Add(track);
        }

        private void RenderGauge(ComposureResult? composure, string? heatDirection)
        {
            var canvas = GaugeCanvas;
            // Strip any previously drawn fill arc; keep the track (first child).
            for (int i = canvas.Children.Count - 1; i >= 1; i--)
            {
                canvas.Children.RemoveAt(i);
            }

            int score = composure?.Score ?? 0;
            string band = composure?.Band ?? "steady";
            bool empty = (composure?.SampleSize ?? 0) == 0;

            var (stroke, _glow, label) = BandStyling(band);

            GaugeScoreText.Text = empty ? "—" : score.ToString(CultureInfo.InvariantCulture);
            GaugeScoreText.Foreground = empty ? BrushFromHex("#888899") : BrushFromHex("#F2F2F8");
            GaugeBandText.Text = empty ? "No sample" : label;
            GaugeBandText.Foreground = empty ? BrushFromHex("#888899") : BrushFromHex(stroke);
            WindowCaption.Text = empty
                ? "Awaiting first race"
                : $"Rolling {composure!.SampleSize}-race window";

            // Trend pill
            var (trendGlyph, trendColor, trendLabel) = TrendStyling(composure?.Trend ?? "new");
            TrendGlyph.Text = trendGlyph;
            TrendGlyph.Foreground = BrushFromHex(trendColor);
            TrendLabel.Text = trendLabel.ToUpperInvariant();
            TrendLabel.Foreground = BrushFromHex(trendColor);

            // Glow color
            GaugeGlow.Fill = empty
                ? BrushFromHex("#00000000")
                : BrushFromHex(stroke + "33");
            GaugeGlow.Opacity = empty ? 0 : 0.55;

            if (!empty)
            {
                double sweep = GaugeArcSweepDeg * Math.Clamp(score, 0, 100) / 100.0;
                var fill = BuildArc(GaugeArcStartDeg, GaugeArcStartDeg + sweep);
                fill.Stroke = BrushFromHex(stroke);
                fill.StrokeThickness = GaugeStrokeWidth;
                fill.StrokeStartLineCap = PenLineCap.Round;
                fill.StrokeEndLineCap = PenLineCap.Round;
                canvas.Children.Add(fill);

                StartGaugeBreathe(heatDirection);
            }
            else
            {
                _gaugeBreathe?.Stop();
            }
        }

        private void StartGaugeBreathe(string? heatDirection)
        {
            _gaugeBreathe?.Stop();

            var fast = heatDirection == "hot" || heatDirection == "cold";
            var duration = TimeSpan.FromSeconds(fast ? 2.4 : 4.2);

            var fade = new DoubleAnimation
            {
                From = 0.55,
                To = 0.85,
                Duration = duration,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(fade, GaugeGlow);
            Storyboard.SetTargetProperty(fade, "Opacity");

            _gaugeBreathe = new Storyboard();
            _gaugeBreathe.Children.Add(fade);
            _gaugeBreathe.Begin();
        }

        private XamlPath BuildArc(double startDeg, double endDeg)
        {
            var startPt = PolarToXY(startDeg);
            var endPt = PolarToXY(endDeg);
            var sweep = Math.Abs(endDeg - startDeg);
            bool largeArc = sweep > 180;

            var fig = new PathFigure { StartPoint = startPt };
            fig.Segments.Add(new ArcSegment
            {
                Point = endPt,
                Size = new Size(GaugeRadius, GaugeRadius),
                IsLargeArc = largeArc,
                SweepDirection = SweepDirection.Clockwise,
                RotationAngle = 0,
            });

            var geom = new PathGeometry();
            geom.Figures.Add(fig);

            return new XamlPath { Data = geom };
        }

        private Point PolarToXY(double deg)
        {
            double rad = deg * Math.PI / 180;
            return new Point(
                GaugeCenterXY + GaugeRadius * Math.Cos(rad),
                GaugeCenterXY + GaugeRadius * Math.Sin(rad));
        }

        // ── Heat ───────────────────────────────────────────────────

        private void RenderHeat(HeatResult? heat)
        {
            string direction = heat?.Direction ?? "flat";
            var (color, glyph, label, fastMs) = HeatStyling(direction);
            bool empty = (heat?.SampleSize ?? 0) == 0;

            HeatLabel.Text = empty ? "—" : label;
            HeatLabel.Foreground = empty ? BrushFromHex("#888899") : BrushFromHex(color);
            HeatGlyph.Text = empty ? "" : glyph;
            HeatGlyph.Foreground = BrushFromHex(color);
            HeatPulseDot.Fill = empty ? BrushFromHex("#888899") : BrushFromHex(color);
            HeatPulseRing.Fill = empty ? BrushFromHex("#888899") : BrushFromHex(color);
            HeatTile.BorderBrush = empty ? BrushFromHex("#2A2A36") : BrushFromHex(color + "88");

            if (empty)
            {
                HeatCaption.Text = "Awaiting first race";
                _heatPulse?.Stop();
                HeatPulseRing.Opacity = 0.3;
                return;
            }

            double avg = heat!.IRatingDeltaAvg;
            string sign = avg >= 0 ? "+" : "";
            HeatCaption.Text = $"{sign}{avg.ToString("0.0", CultureInfo.InvariantCulture)} iR / race · last {heat.SampleSize}";

            StartHeatPulse(fastMs);
        }

        private void StartHeatPulse(int durationMs)
        {
            _heatPulse?.Stop();

            var grow = new DoubleAnimation
            {
                From = 1.0,
                To = 1.7,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                AutoReverse = true,
            };
            Storyboard.SetTarget(grow, HeatPulseRing);
            Storyboard.SetTargetProperty(grow, "(UIElement.Opacity)");
            grow.From = 0.7;
            grow.To = 0.0;

            _heatPulse = new Storyboard();
            _heatPulse.Children.Add(grow);
            _heatPulse.Begin();
        }

        // ── Current streak ─────────────────────────────────────────

        private void RenderCurrentStreak(StreaksResult? streaks)
        {
            var current = streaks?.Current;
            if (current == null)
            {
                StreakKindGlyph.Text = "·";
                StreakKindLabel.Text = "Holding";
                StreakKindGlyph.Foreground = BrushFromHex("#94A3B8");
                StreakKindLabel.Foreground = BrushFromHex("#94A3B8");
                StreakLengthText.Text = "—";
                StreakDetailText.Text = "no streak";
                CurrentStreakTile.BorderBrush = BrushFromHex("#2A2A36");
                return;
            }

            var (label, color, glyph) = StreakKindStyling(current.Kind);
            StreakKindGlyph.Text = glyph;
            StreakKindGlyph.Foreground = BrushFromHex(color);
            StreakKindLabel.Text = label;
            StreakKindLabel.Foreground = BrushFromHex(color);
            StreakLengthText.Text = current.Span.Length.ToString(CultureInfo.InvariantCulture);
            string sign = current.Span.IRatingDeltaSum >= 0 ? "+" : "";
            string races = current.Span.Length == 1 ? "race" : "races";
            StreakDetailText.Text = $"{races} · {sign}{current.Span.IRatingDeltaSum} iR · {current.Span.IncidentSum}x";
            CurrentStreakTile.BorderBrush = BrushFromHex(color + "88");
        }

        // ── Streak history ─────────────────────────────────────────

        private void RenderStreakHistory(StreaksResult? streaks)
        {
            RenderSpan(
                streaks?.LongestSlump,
                emptyText: SlumpEmptyText,
                detailGrid: SlumpDetailGrid,
                lengthText: SlumpLengthText,
                racesLabel: SlumpRacesLabel,
                deltaText: SlumpDeltaText,
                dateText: SlumpDateText,
                glyph: SlumpGlyph,
                color: "#FF5A47");
            RenderSpan(
                streaks?.LongestSurge,
                emptyText: SurgeEmptyText,
                detailGrid: SurgeDetailGrid,
                lengthText: SurgeLengthText,
                racesLabel: SurgeRacesLabel,
                deltaText: SurgeDeltaText,
                dateText: SurgeDateText,
                glyph: SurgeGlyph,
                color: "#4ADE80");
        }

        private void RenderSpan(
            StreakSpan? span,
            TextBlock emptyText,
            Grid detailGrid,
            TextBlock lengthText,
            TextBlock racesLabel,
            TextBlock deltaText,
            TextBlock dateText,
            TextBlock glyph,
            string color)
        {
            if (span == null)
            {
                emptyText.Visibility = Visibility.Visible;
                detailGrid.Visibility = Visibility.Collapsed;
                dateText.Text = "";
                glyph.Foreground = BrushFromHex("#888899");
                return;
            }
            emptyText.Visibility = Visibility.Collapsed;
            detailGrid.Visibility = Visibility.Visible;
            glyph.Foreground = BrushFromHex(color);
            lengthText.Text = span.Length.ToString(CultureInfo.InvariantCulture);
            racesLabel.Text = span.Length == 1 ? "race" : "races";
            string sign = span.IRatingDeltaSum >= 0 ? "+" : "";
            deltaText.Text = $"{sign}{span.IRatingDeltaSum} iR";
            deltaText.Foreground = BrushFromHex(color);
            dateText.Text = FormatDateRange(span.StartDate, span.EndDate);
        }

        private string FormatDateRange(string startIso, string endIso)
        {
            if (!DateTime.TryParse(startIso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start)
                || !DateTime.TryParse(endIso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end))
            {
                return "";
            }
            string s = start.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
            string e = end.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
            return s == e ? s : $"{s} → {e}";
        }

        // ── Discipline mix ────────────────────────────────────────

        private void RenderDisciplineMix(DisciplineMixResult? mix)
        {
            DisciplineBarStack.Children.Clear();
            DisciplineLegendList.Items?.Clear();

            int total = mix?.Total ?? 0;
            MixTotalText.Text = total > 0 ? $"{total} race{(total == 1 ? "" : "s")}" : "—";

            if (mix == null || mix.ByCategory.Count == 0)
            {
                var placeholder = new TextBlock
                {
                    Text = "Run a race to populate",
                    FontSize = 12,
                    Foreground = BrushFromHex("#9090A2"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(placeholder, 0);
                DisciplineBarHost.Children.Add(placeholder);
                MixHeadlineText.Visibility = Visibility.Collapsed;
                return;
            }

            // Bar — segments proportional to each discipline's share, tinted by its
            // own Composure band.
            double availableWidth = Math.Max(800, DisciplineBarHost.ActualWidth);
            foreach (var slice in mix.ByCategory)
            {
                var (stroke, _, _) = BandStyling(slice.Composure.Band);
                double widthPct = Math.Max(slice.Proportion * 100, 5);
                double pixelW = availableWidth * (widthPct / 100.0);

                var segment = new Grid
                {
                    Background = BrushFromHex(stroke + "1A"),
                    Width = pixelW,
                    BorderBrush = BrushFromHex("#2A2A36"),
                    BorderThickness = new Thickness(0, 0, 1, 0),
                };
                segment.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                segment.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var labelStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(labelStack, 0);
                labelStack.Children.Add(new TextBlock
                {
                    Text = TitleCase(slice.Category),
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = BrushFromHex("#F2F2F8"),
                });
                labelStack.Children.Add(new TextBlock
                {
                    Text = slice.Composure.Score.ToString(CultureInfo.InvariantCulture),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = BrushFromHex(stroke),
                });
                segment.Children.Add(labelStack);

                var rail = new Border
                {
                    Height = 3,
                    Background = BrushFromHex(stroke),
                    Opacity = 0.85,
                };
                Grid.SetRow(rail, 1);
                segment.Children.Add(rail);

                DisciplineBarStack.Children.Add(segment);
            }

            // Legend
            foreach (var slice in mix.ByCategory)
            {
                var (stroke, _, label) = BandStyling(slice.Composure.Band);
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(0, 0, 14, 0),
                };
                row.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = BrushFromHex(stroke),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{TitleCase(slice.Category)} {Math.Round(slice.Proportion * 100)}%",
                    FontSize = 11,
                    Foreground = BrushFromHex("#C8C8D6"),
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"· {label}",
                    FontSize = 11,
                    Foreground = BrushFromHex("#888899"),
                });
                DisciplineLegendList.Items?.Add(row);
            }

            // Headline
            string? headline = BuildMixHeadline(mix);
            if (!string.IsNullOrEmpty(headline))
            {
                MixHeadlineText.Text = headline;
                MixHeadlineText.Visibility = Visibility.Visible;
            }
            else
            {
                MixHeadlineText.Visibility = Visibility.Collapsed;
            }
        }

        // ── Headlines ─────────────────────────────────────────────

        private string BuildHeadline(ComposureResult? composure, HeatResult? heat, StreaksResult? streaks)
        {
            if (composure == null || composure.SampleSize == 0)
            {
                return "Race once to light up the wall.";
            }

            string bandPhrase = composure.Band switch
            {
                "untouchable" => "Untouchable form",
                "sharp"       => "Sharp form",
                "gritty"      => "Gritty form",
                _             => "Steady form",
            };

            string heatPhrase = (heat?.Direction ?? "flat") switch
            {
                "hot"  => "and you're running hot",
                "warm" => "and the rating is climbing",
                "cold" => "but the last few have bitten back",
                _      => "with the needle flat",
            };

            string streakPhrase = "";
            var current = streaks?.Current;
            if (current != null && current.Span.Length >= 2)
            {
                streakPhrase = current.Kind switch
                {
                    "losing"  => $" — {current.Span.Length} races into a slump.",
                    "gaining" => $" — {current.Span.Length}-race surge in progress.",
                    _ => "",
                };
            }

            return $"{bandPhrase} {heatPhrase}{streakPhrase}";
        }

        private string? BuildMixHeadline(DisciplineMixResult mix)
        {
            if (mix.Total == 0 || string.IsNullOrEmpty(mix.Primary) || string.IsNullOrEmpty(mix.BestForRating))
            {
                return null;
            }
            if (mix.Primary == mix.BestForRating)
            {
                var primary = mix.ByCategory.Find(s => s.Category == mix.Primary);
                if (primary != null)
                {
                    var (_, _, label) = BandStyling(primary.Composure.Band);
                    return $"{label} in {TitleCase(mix.Primary)} — your home discipline is your best.";
                }
                return null;
            }
            var p = mix.ByCategory.Find(s => s.Category == mix.Primary);
            var b = mix.ByCategory.Find(s => s.Category == mix.BestForRating);
            if (p == null || b == null) return null;
            double pAvg = p.Count > 0 ? (double)p.IRatingDeltaSum / p.Count : 0;
            double bAvg = b.Count > 0 ? (double)b.IRatingDeltaSum / b.Count : 0;
            return string.Format(
                CultureInfo.CurrentCulture,
                "You race {0} most ({1}%), but {2} earns {3:+#0;-#0;0} iR / race vs {4:+#0;-#0;0} in your primary.",
                TitleCase(mix.Primary),
                Math.Round(p.Proportion * 100),
                TitleCase(mix.BestForRating),
                bAvg,
                pAvg);
        }

        // ── Styling helpers ───────────────────────────────────────

        private static (string Stroke, string Glow, string Label) BandStyling(string band) => band switch
        {
            "untouchable" => ("#7DD3FC", "#67E8F9", "Untouchable"),
            "sharp"       => ("#4ADE80", "#86EFAC", "Sharp"),
            "steady"      => ("#F5A524", "#FCD34D", "Steady"),
            "gritty"      => ("#FF5A47", "#FCA5A5", "Gritty"),
            _             => ("#94A3B8", "#CBD5E1", "Steady"),
        };

        private static (string Color, string Glyph, string Label, int DurationMs) HeatStyling(string direction) => direction switch
        {
            "hot"  => ("#FF7A1A", "▲▲", "Hot",  900),
            "warm" => ("#F5A524", "▲",  "Warm", 1600),
            "cold" => ("#5FA8FF", "▼",  "Cold", 2400),
            _      => ("#94A3B8", "·",  "Flat", 4000),
        };

        private static (string Glyph, string Color, string Label) TrendStyling(string trend) => trend switch
        {
            "improving" => ("↗", "#4ADE80", "Improving"),
            "declining" => ("↘", "#FF5A47", "Declining"),
            "stable"    => ("→", "#94A3B8", "Stable"),
            _           => ("·", "#94A3B8", "New"),
        };

        private static (string Label, string Color, string Glyph) StreakKindStyling(string kind) => kind switch
        {
            "gaining" => ("Surge",   "#4ADE80", "▲"),
            "losing"  => ("Slump",   "#FF5A47", "▼"),
            _         => ("Holding", "#94A3B8", "·"),
        };

        private static SolidColorBrush BrushFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            string h = hex.Substring(1);
            byte a = 0xFF, r, g, b;
            if (h.Length == 8)
            {
                a = byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                r = byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                g = byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                b = byte.Parse(h.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            else if (h.Length == 6)
            {
                r = byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                g = byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                b = byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            else
            {
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }

        private static string TitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var parts = s.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                parts[i] = char.ToUpper(parts[i][0], CultureInfo.InvariantCulture) + parts[i].Substring(1).ToLower(CultureInfo.InvariantCulture);
            }
            return string.Join(' ', parts);
        }
    }
}
