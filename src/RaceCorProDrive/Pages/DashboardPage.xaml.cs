using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Api;
using RaceCorProDrive.DesignSystem;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Support;
using Panel = RaceCorProDrive.DesignSystem.Components.Panel;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Signed-in dashboard. Reads live data from
    /// <see cref="DashboardPoller.Shared"/>; rebuilds the Performance
    /// tab's panels (Should-You-Race, Strengths/Watch-Out, plus
    /// stubbed visualization placeholders pending the Win2D port) on
    /// every <see cref="INotifyPropertyChanged"/> tick. NextRace and
    /// Previous tab content is stubbed for v1 and will follow.
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

        public DashboardPage()
        {
            this.InitializeComponent();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            DashboardPoller.Shared.PropertyChanged += OnPollerChanged;
            DashboardPoller.Shared.Start();
            await ThemeStore.Shared.RefreshAsync();
            UpdateForCurrentSnapshot();
            // Sync the filter buttons' Content to whatever's persisted
            // — first paint shouldn't blink-default to "Races" if the
            // user previously chose Practice.
            UpdateFilterButtonLabels();
        }

        // ── Dashboard filter buttons (Race/Practice + License) ─────
        // The XAML side has DropDownButton + MenuFlyoutItem rows. Each
        // click sets the corresponding pref via DashboardFilterPrefs,
        // updates the button labels, and restarts the poller so the
        // dashboard refetches immediately rather than waiting up to
        // 60s for the next tick.

        private void UpdateFilterButtonLabels()
        {
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

        private void OnContextRaceClick(object sender, RoutedEventArgs e)     => ApplyContext(DashboardContext.Race);
        private void OnContextPracticeClick(object sender, RoutedEventArgs e) => ApplyContext(DashboardContext.Practice);

        private void OnLicenseAllClick(object sender, RoutedEventArgs e)      => ApplyLicense(DashboardLicense.All);
        private void OnLicenseRoadClick(object sender, RoutedEventArgs e)     => ApplyLicense(DashboardLicense.Road);
        private void OnLicenseOvalClick(object sender, RoutedEventArgs e)     => ApplyLicense(DashboardLicense.Oval);
        private void OnLicenseDirtRoadClick(object sender, RoutedEventArgs e) => ApplyLicense(DashboardLicense.DirtRoad);
        private void OnLicenseDirtOvalClick(object sender, RoutedEventArgs e) => ApplyLicense(DashboardLicense.DirtOval);
        private void OnLicenseFormulaClick(object sender, RoutedEventArgs e)  => ApplyLicense(DashboardLicense.Formula);

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DashboardPoller.Shared.PropertyChanged -= OnPollerChanged;
            // Keep polling alive even when this Page is unloaded so
            // notifications + cached state survive nav transitions.
            // Sign-out flows call Stop() explicitly.
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

            switch (TabsBar.Selected)
            {
                case DashboardTabs.Tab.Performance:
                    PopulatePerformance(dash);
                    break;
                case DashboardTabs.Tab.NextRace:
                    PopulatePlaceholder("Next Race", "Race-suggestion ideas land here once the Performance tab feels complete.");
                    break;
                case DashboardTabs.Tab.Previous:
                    PopulatePlaceholder("Previous Races", "Recent race rows will populate here from the dashboard payload's recentSessions field.");
                    break;
            }
        }

        // MARK: - Performance tab

        private void PopulatePerformance(Dashboard? dash)
        {
            MainContent.Children.Add(BuildShouldYouRace(dash));
            MainContent.Children.Add(BuildStrengthsWatchOut(dash));
            MainContent.Children.Add(BuildVizPlaceholder("Race Calendar"));
            MainContent.Children.Add(BuildVizPlaceholder("Race Schedule"));
            MainContent.Children.Add(BuildVizPlaceholder("Driver DNA"));
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

        /// <summary>
        /// Until the Win2D port lands, the three viz cards (calendar /
        /// schedule / DNA) render as panel placeholders so the layout
        /// rhythm matches the macOS dashboard top-to-bottom and we
        /// can swap each one in independently.
        /// </summary>
        private static Panel BuildVizPlaceholder(string title)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextPrimaryBrush"],
            });
            stack.Children.Add(new Border
            {
                Height = 96,
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BgBaseBrush"],
                Opacity = 0.3,
                Child = new TextBlock
                {
                    Text = "Coming soon",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMutedBrush"],
                },
            });
            return new Panel { Content = stack, Padding = new Thickness(14) };
        }

        // MARK: - Other tabs (placeholders for v1)

        private void PopulatePlaceholder(string title, string sub)
        {
            MainContent.Children.Add(new Panel
            {
                Padding = new Thickness(40),
                Content = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title.ToUpperInvariant(),
                            FontSize = 11,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            CharacterSpacing = 200,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextDimBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = sub,
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextSecondaryBrush"],
                        },
                    },
                },
            });
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
