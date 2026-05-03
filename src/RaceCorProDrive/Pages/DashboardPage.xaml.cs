using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RaceCorProDrive.Api;
using RaceCorProDrive.DesignSystem;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Services;
using RaceCorProDrive.Support;
using Windows.UI;
using Panel = RaceCorProDrive.DesignSystem.Components.Panel;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Signed-in dashboard. Reads live data from
    /// <see cref="DashboardPoller.Shared"/>; rebuilds the active tab's
    /// content on every <see cref="INotifyPropertyChanged"/> tick.
    /// Performance, Next Race, and Previous Races tabs all render
    /// real content mirroring the SwiftUI <c>DashboardView</c>.
    /// </summary>
    public sealed partial class DashboardPage : Page
    {
        // ── Server-canonical Race-Now state ────────────────────────
        // Cached evaluation from /api/v1/calc/race-now. Re-fetched
        // whenever the When-profile shifts (new poll, hour rollover,
        // sign-in change). The view renders evaluation.Headline +
        // evaluation.Detail verbatim — same numbers as the web's
        // ShouldIRaceNow chip, no local synthesis.
        private RaceNowEvaluation? _raceNowEvaluation;
        private List<RaceNowAlternative> _raceNowAlternatives = new();
        private string? _raceNowError;
        // peakHourStart of the last profile we fetched for; used to
        // dedupe redundant fetches when the dashboard re-pumps but
        // the When-profile hasn't shifted.
        private int? _lastFetchedProfileKey;

        // Title-bar widgets owned by this page — built in OnLoaded,
        // pushed into MainWindow's titlebar slots so they sit in the
        // same strip as the system caption buttons. Held here so
        // UpdateForCurrentSnapshot can read TabsBar.Selected and
        // UpdateFilterButtonLabels can mutate the buttons' Content.
        private DashboardTabs? TabsBar;
        private DropDownButton? ContextFilterButton;
        private DropDownButton? LicenseFilterButton;

        public DashboardPage()
        {
            this.InitializeComponent();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildTitleBarWidgets();

            DashboardPoller.Shared.PropertyChanged += OnPollerChanged;
            DashboardPoller.Shared.Start();
            await ThemeStore.Shared.RefreshAsync();
            UpdateForCurrentSnapshot();
            // Sync the filter buttons' Content to whatever's persisted
            // — first paint shouldn't blink-default to "Races" if the
            // user previously chose Practice.
            UpdateFilterButtonLabels();
        }

        // ── Title-bar widget construction ──────────────────────────
        //
        // DashboardTabs + the two filter DropDownButtons used to live
        // in the page XAML in a 76pt row. They moved into MainWindow's
        // 44pt titlebar so the chrome reads as one continuous strip.
        // The page still owns the controls (events + state belong to
        // the page); MainWindow just provides the parent ContentControl
        // slots.
        private void BuildTitleBarWidgets()
        {
            TabsBar = new DashboardTabs();
            TabsBar.SelectionChanged += OnTabChanged;

            var filtersPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Per-state brush overrides for the themed
            // AppDropDownButtonStyle. These used to sit in
            // DashboardPage.Page.Resources but the buttons live in
            // MainWindow chrome now — putting the overrides on the
            // filter container's Resources keeps them inheriting via
            // the same StaticResource lookup the style template
            // expects without polluting global app resources.
            filtersPanel.Resources["ButtonBackgroundPointerOver"]  = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            filtersPanel.Resources["ButtonBackgroundPressed"]      = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
            filtersPanel.Resources["ButtonBackgroundDisabled"]     = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
            filtersPanel.Resources["ButtonForegroundPointerOver"]  = (Brush)Application.Current.Resources["TextPrimaryBrush"];
            filtersPanel.Resources["ButtonForegroundPressed"]      = (Brush)Application.Current.Resources["TextPrimaryBrush"];
            filtersPanel.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));
            filtersPanel.Resources["ButtonBorderBrushPressed"]     = (Brush)Application.Current.Resources["BrandRedBrush"];

            ContextFilterButton = MakeDropDown(new[]
            {
                ("Races",     (Action)(() => ApplyContext(DashboardContext.Race))),
                ("Practices", (Action)(() => ApplyContext(DashboardContext.Practice))),
            });
            LicenseFilterButton = MakeDropDown(new[]
            {
                ("All Licenses", (Action)(() => ApplyLicense(DashboardLicense.All))),
                ("Sports Car",   (Action)(() => ApplyLicense(DashboardLicense.Road))),
                ("Oval",         (Action)(() => ApplyLicense(DashboardLicense.Oval))),
                ("Dirt Road",    (Action)(() => ApplyLicense(DashboardLicense.DirtRoad))),
                ("Dirt Oval",    (Action)(() => ApplyLicense(DashboardLicense.DirtOval))),
                ("Formula",      (Action)(() => ApplyLicense(DashboardLicense.Formula))),
            });

            filtersPanel.Children.Add(ContextFilterButton);
            filtersPanel.Children.Add(LicenseFilterButton);

            App.MainWindow?.SetTitleBarTabs(TabsBar);
            App.MainWindow?.SetTitleBarFilters(filtersPanel);
        }

        private static DropDownButton MakeDropDown(IEnumerable<(string label, Action onClick)> items)
        {
            var btn = new DropDownButton
            {
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["AppDropDownButtonStyle"],
            };
            var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
            foreach (var (label, onClick) in items)
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, __) => onClick();
                flyout.Items.Add(item);
            }
            btn.Flyout = flyout;
            return btn;
        }

        // ── Dashboard filter buttons (Race/Practice + License) ─────
        // The XAML side has DropDownButton + MenuFlyoutItem rows. Each
        // click sets the corresponding pref via DashboardFilterPrefs,
        // updates the button labels, and restarts the poller so the
        // dashboard refetches immediately rather than waiting up to
        // 60s for the next tick.

        private void UpdateFilterButtonLabels()
        {
            if (ContextFilterButton is null || LicenseFilterButton is null) return;
            var (ctx, lic) = DashboardFilterPrefs.Current();
            ContextFilterButton.Content = ctx.DisplayName();
            LicenseFilterButton.Content = lic.DisplayName();
        }

        private void ApplyContext(DashboardContext ctx)
        {
            DashboardFilterPrefs.SetContext(ctx);
            UpdateFilterButtonLabels();
            // Sync the strip's count label immediately so the label
            // doesn't lag behind the dropdown selection by a network
            // round-trip. The next poller tick refreshes the
            // numbers; the label is just presentation.
            StatStrip.Context = ctx;
            // Restart poller so it ticks now with the new prefs.
            DashboardPoller.Shared.Start();
        }

        private void ApplyLicense(DashboardLicense lic)
        {
            DashboardFilterPrefs.SetLicense(lic);
            UpdateFilterButtonLabels();
            DashboardPoller.Shared.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DashboardPoller.Shared.PropertyChanged -= OnPollerChanged;
            // Don't clear the titlebar slots here — Frame nav fires the
            // next page's OnLoaded BEFORE this OnUnloaded, so a clear
            // would wipe whatever the new page just set. Each page is
            // responsible for setting both slots in its own OnLoaded
            // (its widgets, or null). Keep polling alive across nav so
            // notifications + cached state survive transitions.
        }

        private void OnPollerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardPoller.Latest))
            {
                DispatcherQueue.TryEnqueue(UpdateForCurrentSnapshot);
            }
        }

        private void OnTabChanged(object? sender, DashboardTabs.Tab tab)
            => UpdateForCurrentSnapshot();

        private void UpdateForCurrentSnapshot()
        {
            var dash = DashboardPoller.Shared.Latest;
            StatStrip.Dashboard = dash;
            // Keep the strip's count label in sync with the active
            // context — flips between "RACES" and "PRACTICES" based
            // on whatever the user picked in the filter dropdown.
            StatStrip.Context = DashboardFilterPrefs.Current().Context;
            Sidebar.Dashboard = dash;

            LoadingRing.IsActive = dash == null;
            MainContent.Children.Clear();

            // Trigger a canonical Race-Now refetch whenever the When
            // profile shifts. Keying on PeakHourStart (an int) is enough
            // — every new poll where the user's data has actually changed
            // will move that value, and hour rollover shifts it on its own.
            // Fire-and-forget; the response handler re-enters this method
            // via DispatcherQueue.TryEnqueue so the UI re-renders with
            // the fresh evaluation.
            var newProfileKey = dash?.When?.PeakHourStart;
            if (newProfileKey != _lastFetchedProfileKey)
            {
                _lastFetchedProfileKey = newProfileKey;
                _ = RefreshRaceNowAsync(dash);
            }

            switch (TabsBar?.Selected ?? DashboardTabs.Tab.Performance)
            {
                case DashboardTabs.Tab.Performance:
                    PopulatePerformance(dash);
                    break;
                case DashboardTabs.Tab.NextRace:
                    PopulateNextRace(dash);
                    break;
                case DashboardTabs.Tab.Previous:
                    PopulatePrevious(dash);
                    break;
            }
        }

        // MARK: - Performance tab

        private void PopulatePerformance(Dashboard? dash)
        {
            MainContent.Children.Add(BuildShouldYouRace(dash));
            MainContent.Children.Add(BuildStrengthsWatchOut(dash));
            MainContent.Children.Add(BuildVisualizationsRow(dash));
        }

        /// <summary>
        /// Calendar / Schedule / DNA in a 3-up grid (33% each, 14pt
        /// gaps). Mac stacks these vertically full-width; the Windows
        /// dashboard has more horizontal real estate so a row of three
        /// equal cards reads better.
        /// </summary>
        private static Microsoft.UI.Xaml.Controls.Grid BuildVisualizationsRow(Dashboard? dash)
        {
            var row = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 14 };
            row.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
            {
                Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star),
            });
            row.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
            {
                Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star),
            });
            row.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
            {
                Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star),
            });

            var calendar = new RaceCalendarHeatmap
            {
                Sessions = DashboardPoller.Shared.Sessions,
            };
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(calendar, 0);
            row.Children.Add(calendar);

            var schedule = new RaceScheduleScatter { Buckets = dash?.ScatterBuckets };
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(schedule, 1);
            row.Children.Add(schedule);

            var dna = new DriverDNARadar { Dashboard = dash };
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(dna, 2);
            row.Children.Add(dna);

            return row;
        }

        // MARK: - Next Race tab

        private void PopulateNextRace(Dashboard? dash)
        {
            MainContent.Children.Add(new NextRaceTabContent
            {
                Ideas = dash?.NextRaceIdeas,
                Lookups = dash?.Lookups,
            });
        }

        // MARK: - Previous Races tab

        private void PopulatePrevious(Dashboard? dash)
        {
            MainContent.Children.Add(new PreviousRacesTabContent
            {
                Cards = DashboardPoller.Shared.PreviousRaces,
                Lookups = dash?.Lookups,
            });
        }

        // BuildShouldYouRace is now an instance method (not static) because it
        // reads cached server state from this page. The verdict + headline +
        // detail come straight from /api/v1/calc/race-now via CalcClient —
        // see RefreshRaceNowAsync below.
        private ShouldYouRacePanel BuildShouldYouRace(Dashboard? dash)
        {
            var panel = new ShouldYouRacePanel();

            if (dash?.When is { } when)
            {
                // Detail body still derived from dash.WhenPanel (server's
                // pre-rendered Strengths/Watch-Out copy), plus a "now"
                // annotation that uses the local clock for context.
                var hour = DateTime.Now.Hour;
                var inPeak = IsInWindow(hour, when.PeakHourStart, when.WindowSize);
                var inWorst = IsInWindow(hour, when.WorstHourStart, when.WindowSize);

                ShouldYouRacePanel.DetailModel? detail = null;
                if (dash.WhenPanel is { } wp)
                {
                    detail = new ShouldYouRacePanel.DetailModel
                    {
                        PeakWindow = wp.Strengths is { } s ? $"{s.Days}, {s.Hours}" : null,
                        PeakDelta = wp.Strengths?.AvgIRatingDelta is double sd ? FormatDelta(sd) : null,
                        WorstWindow = wp.WatchOut is { } w ? $"{w.Days}, {w.Hours}" : null,
                        WorstDelta = wp.WatchOut?.AvgIRatingDelta is double wd ? FormatDelta(wd) : null,
                        NowDescription = NowAnnotation(hour, inPeak, inWorst),
                    };
                }

                if (_raceNowEvaluation is { } evaluation)
                {
                    // Server values rendered verbatim — same headline + detail
                    // strings the web's ShouldIRaceNow component shows.
                    panel.Verdict = MapServerVerdict(evaluation.Verdict);
                    panel.Title = evaluation.Headline;
                    panel.Reason = evaluation.Detail;
                }
                else if (_raceNowError is { } err)
                {
                    // Fetch failed — surface the message instead of sitting
                    // on "Calculating…" forever.
                    panel.Verdict = Verdict.Marginal;
                    panel.Title = "Verdict unavailable";
                    panel.Reason = err;
                }
                else
                {
                    // In-flight fetch.
                    panel.Verdict = Verdict.Unknown;
                    panel.Title = "Calculating…";
                    panel.Reason = "Loading current race-now verdict from server.";
                }

                if (detail is { }) panel.Detail = detail;
            }
            else
            {
                panel.Verdict = Verdict.Unknown;
                panel.Title = "Not enough data";
                panel.Reason = "Add a few more races to get a race-now advisory.";
            }
            return panel;
        }

        // Server's six-tier vocabulary (mirrors SlotTier) collapsed into
        // the existing four-case Verdict enum:
        //   best, good                  → Good
        //   clean-side, messy-side      → Marginal
        //   risky                       → Bad
        //   insufficient (or unknown)   → Unknown
        // Headline string still renders the canonical verdict text — this
        // mapping only drives the icon/color on the chip.
        private static Verdict MapServerVerdict(string verdict) => verdict switch
        {
            "best" => Verdict.Good,
            "good" => Verdict.Good,
            "clean-side" => Verdict.Marginal,
            "messy-side" => Verdict.Marginal,
            "risky" => Verdict.Bad,
            _ => Verdict.Unknown, // "insufficient" or unrecognised
        };

        // Refetches the canonical race-now verdict from web-api. Called
        // from UpdateForCurrentSnapshot whenever the When-profile shifts.
        // Bridges the typed Dashboard.WhenProfile to the calc API's opaque
        // JsonElement profile via JSON round-trip — the shapes are
        // compatible (both come from the same server-side computeWhenProfile).
        private async Task RefreshRaceNowAsync(Dashboard? dash)
        {
            if (dash?.When is not { } when)
            {
                _raceNowEvaluation = null;
                _raceNowAlternatives = new();
                _raceNowError = null;
                DispatcherQueue.TryEnqueue(UpdateForCurrentSnapshot);
                return;
            }

            try
            {
                // Typed → opaque bridge. Same JsonSerializerOptions as the
                // rest of the API surface so casing (camelCase) matches.
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var profileJson = JsonSerializer.Serialize(when, options);
                using var doc = JsonDocument.Parse(profileJson);
                var profile = doc.RootElement.Clone();

                // Send the user's IANA timezone so the verdict targets THEIR
                // local hour, not UTC. .NET on Windows surfaces tzdb names
                // in TimeZoneInfo.Local.HasIanaId; otherwise convert from
                // the Windows zone-name registry.
                var tz = TimeZoneInfo.Local.HasIanaId
                    ? TimeZoneInfo.Local.Id
                    : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)
                        ? iana
                        : "UTC";

                var response = await CalcClient.Shared.FetchRaceNowAsync(
                    profile: profile,
                    timeZone: tz);

                _raceNowEvaluation = response.Evaluation;
                _raceNowAlternatives = response.Alternatives;
                _raceNowError = response.Evaluation == null
                    ? "Server returned no evaluation (insufficient data)"
                    : null;

                Debug.WriteLine($"[CalcClient] race-now ok — verdict={response.Evaluation?.Verdict ?? "null"}, tz={tz}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalcClient] race-now fetch failed: {ex}");
                _raceNowEvaluation = null;
                _raceNowAlternatives = new();
                _raceNowError = ex.Message;
            }

            // Re-render with whatever just landed.
            DispatcherQueue.TryEnqueue(UpdateForCurrentSnapshot);
        }

        private static StrengthsWatchOutPanel BuildStrengthsWatchOut(Dashboard? dash)
        {
            var panel = new StrengthsWatchOutPanel();
            var success = Microsoft.UI.Colors.LimeGreen;
            var brand = Windows.UI.Color.FromArgb(0xFF, 0xE5, 0x39, 0x35);

            if (dash?.WhenPanel is { Strengths: { } s, WatchOut: { } w })
            {
                panel.Strengths = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "STRENGTHS",
                    Tint = Windows.UI.Color.FromArgb(0xFF, 0x34, 0xC7, 0x59),
                    Window = $"{s.Days}\n{s.Hours}",
                    Footnote = s.AvgIRatingDelta is double d ? FormatDelta(d) : null,
                    Summary = s.Paragraph,
                    Bullets = s.Bullets.ConvertAll(b => b.Text),
                };
                panel.WatchOut = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "WATCH OUT",
                    Tint = brand,
                    Window = $"{w.Days}\n{w.Hours}",
                    Footnote = w.AvgIRatingDelta is double dw ? FormatDelta(dw) : null,
                    Summary = w.Paragraph,
                    Bullets = w.Bullets.ConvertAll(b => b.Text),
                };
            }
            else
            {
                panel.Strengths = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "STRENGTHS",
                    Tint = Windows.UI.Color.FromArgb(0xFF, 0x34, 0xC7, 0x59),
                    Window = "Collecting data",
                    Summary = "Run 5+ sessions to see your strongest time windows.",
                };
                panel.WatchOut = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "WATCH OUT",
                    Tint = brand,
                    Window = "Collecting data",
                    Summary = "Incident hotspots surface once more sessions are logged.",
                };
            }
            return panel;
        }

        // MARK: - Helpers

        private static bool IsInWindow(int hour, int start, int size)
        {
            for (int i = 0; i < size; i++)
            {
                var h = (start + i) % 24;
                if (h == hour) return true;
            }
            return false;
        }

        private static string FormatDelta(double delta)
        {
            var sign = delta > 0 ? "+" : "";
            return $"avg {sign}{(int)System.Math.Round(delta)} iR";
        }

        private static string NowAnnotation(int hour, bool inPeak, bool inWorst)
        {
            var time = DateTime.Now.ToString("h:mm tt 'on' dddd", CultureInfo.InvariantCulture);
            if (inPeak) return $"Currently {time} — inside your peak window.";
            if (inWorst) return $"Currently {time} — inside your weakest window.";
            return $"Currently {time} — outside both windows.";
        }
    }
}
