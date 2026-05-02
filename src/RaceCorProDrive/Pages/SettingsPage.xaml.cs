using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Services;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// HUD settings page. Reads from
    /// <see cref="OverlaySettingsService.Shared"/> on load, writes back
    /// on every control change, watches for external writes (the
    /// in-game move/resize hotkey, or another app instance editing
    /// the same JSON) and rebuilds when they arrive.
    ///
    /// New architecture (PR-A) — categories:
    ///   Visual      — display/layout/components/leaderboard (stub for
    ///                 now; PR-B replaces with drag/drop editor)
    ///   Commentary  — combined commentary + coach (driver name,
    ///                 categories, tone, depth, hardware/feel/etc.)
    ///   Recording   — extended (devices, facecam, format, encoder,
    ///                 replay buffer)
    ///   System      — HUD launch + login + branding + API + SimHub URL
    ///                 + read-only hotkeys
    ///
    /// Race Rules removed entirely (incident counts come from iRacing,
    /// force flag was dev-only, drive/rally modes never wired).
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private OverlaySettings _settings = new();
        private string _category = "visual";
        private bool _suppressSave;
        private DispatcherTimer? _saveStatusTimer;

        // Category tab strip — lives in MainWindow's titlebar slot
        // while this page is loaded. Built in OnLoaded, cleared from
        // the slot in OnUnloaded.
        private HorizontalTabs? _categoryTabs;

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        // ── Lifecycle ────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _settings = OverlaySettingsService.Shared.Load();
            OverlaySettingsService.Shared.Changed += OnSettingsFileChanged;
            OverlaySettingsService.Shared.StartWatching();

            // Build the horizontal tab strip and push it into the
            // window-level titlebar slot. Same key set the old
            // NavigationView used so BuildCategory's switch statement
            // keeps working unchanged.
            _categoryTabs = new HorizontalTabs
            {
                Tabs = new[]
                {
                    new HorizontalTabs.Tab { Key = "visual",     Label = "Visual" },
                    new HorizontalTabs.Tab { Key = "commentary", Label = "Commentary" },
                    new HorizontalTabs.Tab { Key = "recording",  Label = "Recording" },
                    new HorizontalTabs.Tab { Key = "system",     Label = "System" },
                },
                SelectedKey = _category,
            };
            _categoryTabs.SelectionChanged += OnCategoryTabChanged;
            App.MainWindow?.SetTitleBarTabs(_categoryTabs);
            // No filter widgets on this page — clear the slot in case
            // the previous page (e.g. dashboard) left filters there.
            App.MainWindow?.SetTitleBarFilters(null);

            BuildCategory();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            OverlaySettingsService.Shared.Changed -= OnSettingsFileChanged;
            App.MainWindow?.SetTitleBarTabs(null);
            // Don't StopWatching — other pages (e.g. dashboard's HUD
            // status indicator) want to know about external changes too.
        }

        private void OnSettingsFileChanged(object? sender, OverlaySettings refreshed)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _settings = refreshed;
                BuildCategory();
            });
        }

        // ── Category routing ─────────────────────────────────────────

        private void OnCategoryTabChanged(object? sender, string key)
        {
            _category = key;
            BuildCategory();
        }

        // Cards are accumulated here during a Build*Section pass so
        // BuildCategory can balance them across the two columns at the
        // end (rather than placing each card immediately and ending up
        // with a long left column + short right column).
        private readonly List<SettingsCard> _pendingCards = new();

        private void BuildCategory()
        {
            LeftColumn.Children.Clear();
            RightColumn.Children.Clear();
            _pendingCards.Clear();
            _suppressSave = true;
            try
            {
                switch (_category)
                {
                    case "visual":
                        SetHeader("Visual",
                            "Layout, theme, ambient lighting, and which HUD modules appear in-game.");
                        BuildVisualSection();
                        break;
                    case "commentary":
                        SetHeader("Commentary",
                            "Tune what the in-race coach surfaces and how it speaks.");
                        BuildCommentarySection();
                        break;
                    case "recording":
                        SetHeader("Recording",
                            "Capture, microphones, facecam, output, and the rolling replay buffer.");
                        BuildRecordingSection();
                        break;
                    case "system":
                        SetHeader("System",
                            "Launch behavior, plugin connection, branding, and API access.");
                        BuildSystemSection();
                        break;
                }
                FlushColumns();
            }
            finally { _suppressSave = false; }
        }

        private void SetHeader(string heading, string caption)
        {
            PageHeadingText.Text = heading;
            PageCaptionText.Text = caption;
        }

        // ── Visual (PR-A stub; PR-B replaces with drag/drop editor) ─

        private void BuildVisualSection()
        {
            // Note to PR-B: the contents below are the *current* flat
            // toggles preserved verbatim so nothing's lost in this PR.
            // PR-B replaces the layout-position picker with a 6-zone
            // drag/drop editor; the Display/Effects/Modules/Branding
            // cards stay alongside it.

            var display = new SettingsCard
            {
                Title = "Display",
                Caption = "Where the HUD lives on screen and how it's drawn.",
            };
            display.AddRow(MakeRow("Layout position",
                "Single-anchor mode. Drag/drop editor in the next release.",
                MakePicker(
                    new[] { "top-left", "top-center", "top-right", "bottom-left", "bottom-center", "bottom-right", "centered" },
                    () => _settings.LayoutPosition ?? "top-right",
                    v => _settings.LayoutPosition = v)));
            display.AddRow(MakeSlider("Zoom", null, 0.5, 2.0, 0.05,
                () => _settings.Zoom ?? 1.0,
                v => _settings.Zoom = v,
                "0.00", "x"));
            display.AddRow(MakeSlider("Bottom Y offset", "Push the HUD up from the bottom edge.",
                0, 240, 1,
                () => _settings.BottomYOffset ?? 0,
                v => _settings.BottomYOffset = (int)Math.Round(v),
                "0", "px"));
            display.AddRow(MakeRow("Ambient lighting", null,
                MakeSegmented(
                    new[] { ("off", "Off"), ("auto", "Auto"), ("screen", "Screen"), ("wled", "WLED") },
                    () => _settings.AmbientMode ?? "auto",
                    v => _settings.AmbientMode = v)));
            display.AddRow(MakeRow("Theme", null,
                MakePicker(
                    new[] { "default", "carbon", "obsidian", "highvis" },
                    () => _settings.Theme ?? "default",
                    v => _settings.Theme = v)));
            display.AddRow(MakeRow("Visual preset", "Density / richness of the rendered HUD.",
                MakeSegmented(
                    new[] { ("minimal", "Minimal"), ("minimal+", "Minimal+"), ("standard", "Standard"), ("rich", "Rich") },
                    () => _settings.VisualPreset ?? "standard",
                    v => _settings.VisualPreset = v)));
            AddCard(display);

            var effects = new SettingsCard
            {
                Title = "Effects",
                Caption = "GPU effects + accessibility toggles.",
            };
            effects.AddRow(MakeToggleRow("WebGL effects",
                "Glare, bloom, glow, g-force vignette via fragment shaders.",
                () => _settings.ShowWebGL ?? true,
                v => _settings.ShowWebGL = v));
            effects.AddRow(MakeToggleRow("Show panel borders",
                "Outlines every component for layout debugging.",
                () => _settings.ShowBorders ?? false,
                v => _settings.ShowBorders = v));
            effects.AddRow(MakeToggleRow("Green-screen mode",
                "Replace the background with pure green for OBS chroma keying.",
                () => _settings.GreenScreen ?? false,
                v => _settings.GreenScreen = v));
            AddCard(effects);

            var modules = new SettingsCard
            {
                Title = "Modules",
                Caption = "Toggle individual HUD modules. Off-screen modules don't render.",
            };
            void M(string title, Func<bool> g, Action<bool> s)
                => modules.AddRow(MakeToggleRow(title, null, g, s));
            M("Fuel",        () => _settings.ShowFuel        ?? true, v => _settings.ShowFuel = v);
            M("Tyres",       () => _settings.ShowTyres       ?? true, v => _settings.ShowTyres = v);
            M("Controls",    () => _settings.ShowControls    ?? true, v => _settings.ShowControls = v);
            M("Pedals",      () => _settings.ShowPedals      ?? true, v => _settings.ShowPedals = v);
            M("Position",    () => _settings.ShowPosition    ?? true, v => _settings.ShowPosition = v);
            M("Tachometer",  () => _settings.ShowTacho       ?? true, v => _settings.ShowTacho = v);
            M("Commentary",  () => _settings.ShowCommentary  ?? true, v => _settings.ShowCommentary = v);
            M("Leaderboard", () => _settings.ShowLeaderboard ?? true, v => _settings.ShowLeaderboard = v);
            M("Datastream",  () => _settings.ShowDatastream  ?? true, v => _settings.ShowDatastream = v);
            M("Pit box",     () => _settings.ShowPitBox      ?? true, v => _settings.ShowPitBox = v);
            M("Incidents",   () => _settings.ShowIncidents   ?? true, v => _settings.ShowIncidents = v);
            M("Spotter",     () => _settings.ShowSpotter     ?? true, v => _settings.ShowSpotter = v);
            AddCard(modules);

            var leaderboard = new SettingsCard
            {
                Title = "Leaderboard",
                Caption = "Visible only when the Leaderboard module is enabled above.",
            };
            leaderboard.AddRow(MakeRow("Focus", "Which driver the leaderboard centers on.",
                MakeSegmented(
                    new[] { ("self", "Me"), ("leader", "Leader"), ("class", "Class") },
                    () => _settings.LbFocus ?? "self",
                    v => _settings.LbFocus = v)));
            leaderboard.AddRow(MakeSlider("Max visible rows", null, 3, 24, 1,
                () => _settings.LbMaxRows ?? 6,
                v => _settings.LbMaxRows = (int)Math.Round(v),
                "0", " rows"));
            leaderboard.AddRow(MakeToggleRow("Expand to fill",
                "Stretch rows vertically to fill available space.",
                () => _settings.LbExpandToFill ?? false,
                v => _settings.LbExpandToFill = v));
            AddCard(leaderboard);

            var branding = new SettingsCard
            {
                Title = "Branding",
                Caption = "Logos shown in-HUD.",
            };
            branding.AddRow(MakeToggleRow("K10 logo", null,
                () => _settings.ShowK10Logo ?? true, v => _settings.ShowK10Logo = v));
            branding.AddRow(MakeToggleRow("Car logo", null,
                () => _settings.ShowCarLogo ?? true, v => _settings.ShowCarLogo = v));
            branding.AddRow(MakeToggleRow("Game logo", null,
                () => _settings.ShowGameLogo ?? true, v => _settings.ShowGameLogo = v));
            branding.AddRow(MakeRow("Logo subtitle", "Optional small line under the K10 logo.",
                MakeTextBox(
                    () => _settings.LogoSubtitle ?? "",
                    v => _settings.LogoSubtitle = v)));
            AddCard(branding);
        }

        // ── Commentary (combined commentary + coach) ─────────────────

        private void BuildCommentarySection()
        {
            // Driver name card removed — first/last name come from the
            // signed-in user's profile (see App.MainWindow.CurrentUser
            // DisplayName) rather than being a settings input. The
            // CommentaryDriverFirstName/LastName properties remain on
            // the OverlaySettings model so existing JSON keys round-
            // trip; the host writes them from the auth profile if the
            // overlay still reads them.

            var prompts = new SettingsCard
            {
                Title = "Prompts",
                Caption = "Pacing and surface behavior.",
            };
            prompts.AddRow(MakeSlider("Prompt duration", "How long each prompt stays on screen.",
                4, 20, 1,
                () => _settings.CommentaryPromptDuration ?? 8,
                v => _settings.CommentaryPromptDuration = (int)Math.Round(v),
                "0", "s"));
            prompts.AddRow(MakeToggleRow("Show topic title", null,
                () => _settings.CommentaryShowTopicTitle ?? true,
                v => _settings.CommentaryShowTopicTitle = v));
            prompts.AddRow(MakeToggleRow("Event-only mode",
                "Skip ambient color, only speak when something notable happens.",
                () => _settings.CommentaryEventOnlyMode ?? false,
                v => _settings.CommentaryEventOnlyMode = v));
            prompts.AddRow(MakeToggleRow("Demo mode",
                "Cycle synthetic prompts so you can preview the engine without driving.",
                () => _settings.CommentaryDemoMode ?? false,
                v => _settings.CommentaryDemoMode = v));
            AddCard(prompts);

            var topics = new SettingsCard
            {
                Title = "Topic categories",
                Caption = "Coach speaks about whichever you keep on.",
            };
            topics.AddRow(MakeToggleRow("Strategy", null,
                () => _settings.CommentaryCatStrategy ?? true,
                v => _settings.CommentaryCatStrategy = v));
            topics.AddRow(MakeToggleRow("Track", null,
                () => _settings.CommentaryCatTrack ?? true,
                v => _settings.CommentaryCatTrack = v));
            topics.AddRow(MakeToggleRow("Rivals", null,
                () => _settings.CommentaryCatRivals ?? true,
                v => _settings.CommentaryCatRivals = v));
            topics.AddRow(MakeToggleRow("Behavior", null,
                () => _settings.CommentaryCatBehavior ?? true,
                v => _settings.CommentaryCatBehavior = v));
            AddCard(topics);

            var profile = new SettingsCard
            {
                Title = "Driver profile",
                Caption = "Coach incorporates these to tailor commentary depth.",
            };
            profile.AddRow(MakeToggleRow("Hardware feedback",
                "Coach can comment on FFB, pedals, button bindings.",
                () => _settings.CommentaryCatHardware ?? true,
                v => _settings.CommentaryCatHardware = v));
            profile.AddRow(MakeToggleRow("Game feel",
                "Coach can comment on tire model, weather, sim physics.",
                () => _settings.CommentaryCatGameFeel ?? true,
                v => _settings.CommentaryCatGameFeel = v));
            profile.AddRow(MakeToggleRow("Car response",
                "Coach can comment on differential, throttle map, brake bias.",
                () => _settings.CommentaryCatCarResponse ?? true,
                v => _settings.CommentaryCatCarResponse = v));
            profile.AddRow(MakeToggleRow("Racing experience",
                "Coach can call back to past sessions and trends.",
                () => _settings.CommentaryCatRacingExperience ?? true,
                v => _settings.CommentaryCatRacingExperience = v));
            AddCard(profile);

            var voice = new SettingsCard
            {
                Title = "Coach voice",
                Caption = "How the coach addresses you.",
            };
            voice.AddRow(MakeRow("Tone", null,
                MakeSegmented(
                    new[]
                    {
                        ("neutral", "Neutral"),
                        ("warm", "Warm"),
                        ("drill-sergeant", "Drill"),
                        ("british-broadcaster", "Broadcast"),
                    },
                    () => _settings.CoachTone ?? "neutral",
                    v => _settings.CoachTone = v)));
            voice.AddRow(MakeRow("Depth", null,
                MakeSegmented(
                    new[] { ("terse", "Terse"), ("balanced", "Balanced"), ("rich", "Rich") },
                    () => _settings.CoachDepth ?? "balanced",
                    v => _settings.CoachDepth = v)));
            AddCard(voice);
        }

        // ── Recording ────────────────────────────────────────────────

        private void BuildRecordingSection()
        {
            var capture = new SettingsCard { Title = "Capture", Caption = "Output quality + auto-record behavior." };
            capture.AddRow(MakeRow("Quality", null,
                MakeSegmented(
                    new[] { ("low", "Low"), ("medium", "Medium"), ("high", "High"), ("ultra", "Ultra") },
                    () => _settings.RecordingQuality ?? "high",
                    v => _settings.RecordingQuality = v)));
            capture.AddRow(MakeToggleRow("Auto-record",
                "Start recording when a session begins, stop at the end.",
                () => _settings.RecordingAutoRecord ?? false,
                v => _settings.RecordingAutoRecord = v));
            capture.AddRow(MakeToggleRow("Auto-stop on pit",
                "End the recording when you enter the pit lane.",
                () => _settings.RecordingAutoStopOnPit ?? false,
                v => _settings.RecordingAutoStopOnPit = v));
            AddCard(capture);

            var audio = new SettingsCard { Title = "Audio", Caption = "Microphone + system audio sources." };
            audio.AddRow(MakeToggleRow("Record microphone", null,
                () => _settings.RecordingMicEnabled ?? false,
                v => _settings.RecordingMicEnabled = v));
            audio.AddRow(MakeRow("Mic device", null,
                MakeTextBox(
                    () => _settings.RecordingMicDevice ?? "",
                    v => _settings.RecordingMicDevice = v)));
            audio.AddRow(MakeSlider("Mic volume", null, 0, 100, 1,
                () => (_settings.RecordingMicVolume ?? 0.8) * 100,
                v => _settings.RecordingMicVolume = v / 100,
                "0", "%"));
            audio.AddRow(MakeRow("System audio device", null,
                MakeTextBox(
                    () => _settings.RecordingSystemAudioDevice ?? "",
                    v => _settings.RecordingSystemAudioDevice = v)));
            audio.AddRow(MakeSlider("System volume", null, 0, 100, 1,
                () => (_settings.RecordingSystemVolume ?? 1.0) * 100,
                v => _settings.RecordingSystemVolume = v / 100,
                "0", "%"));
            AddCard(audio);

            var facecam = new SettingsCard { Title = "Facecam", Caption = "Optional in-frame webcam." };
            facecam.AddRow(MakeRow("Camera", null,
                MakeTextBox(
                    () => _settings.RecordingWebcamDevice ?? "",
                    v => _settings.RecordingWebcamDevice = v)));
            facecam.AddRow(MakeRow("Size", null,
                MakeSegmented(
                    new[] { ("small", "Small"), ("medium", "Medium"), ("large", "Large") },
                    () => _settings.RecordingFacecamSize ?? "small",
                    v => _settings.RecordingFacecamSize = v)));
            facecam.AddRow(MakeRow("Position", null,
                MakeSegmented(
                    new[]
                    {
                        ("top-left", "TL"), ("top-right", "TR"),
                        ("bottom-left", "BL"), ("bottom-right", "BR"),
                    },
                    () => _settings.RecordingFacecamPos ?? "bottom-right",
                    v => _settings.RecordingFacecamPos = v)));
            AddCard(facecam);

            var output = new SettingsCard { Title = "Output", Caption = "File format + encoder selection." };
            output.AddRow(MakeRow("Format", null,
                MakeSegmented(
                    new[] { ("mp4", "MP4"), ("mkv", "MKV"), ("mov", "MOV") },
                    () => _settings.RecordingOutputFormat ?? "mp4",
                    v => _settings.RecordingOutputFormat = v)));
            output.AddRow(MakeRow("Encoder", null,
                MakeSegmented(
                    new[] { ("auto", "Auto"), ("h264", "H.264"), ("h265", "H.265"), ("av1", "AV1") },
                    () => _settings.RecordingEncoder ?? "auto",
                    v => _settings.RecordingEncoder = v)));
            output.AddRow(MakeToggleRow("Delete source after transcode",
                "Save space by removing the raw capture once the encoded file lands.",
                () => _settings.RecordingDeleteSource ?? true,
                v => _settings.RecordingDeleteSource = v));
            AddCard(output);

            var replay = new SettingsCard { Title = "Replay buffer", Caption = "Always-on rolling capture for save-after-the-fact clips." };
            replay.AddRow(MakeToggleRow("Enable replay buffer", null,
                () => _settings.ReplayBufferEnabled ?? false,
                v => _settings.ReplayBufferEnabled = v));
            replay.AddRow(MakeSlider("Buffer length", null, 15, 120, 5,
                () => _settings.ReplayBufferSeconds ?? 30,
                v =>
                {
                    var i = (int)Math.Round(v);
                    _settings.ReplayBufferSeconds = i;
                    _settings.ReplayBufferDuration = i.ToString(); // mirror to web schema
                },
                "0", "s"));
            AddCard(replay);
        }

        // ── System ───────────────────────────────────────────────────

        private void BuildSystemSection()
        {
            var launch = new SettingsCard { Title = "Launch", Caption = "When the host and HUD start." };
            launch.AddRow(MakeToggleRow("Open this app when I sign in to Windows",
                "Adds a startup entry so the dashboard is ready before you start a session.",
                () => OverlayLauncher.Shared.IsLaunchOnLoginEnabled,
                v => OverlayLauncher.Shared.SetLaunchOnLogin(v),
                persistsToFile: false));
            launch.AddRow(MakeToggleRow("Auto-launch HUD when this app opens",
                "Spawns the in-game HUD process the moment this dashboard window comes up.",
                () => _settings.WinUIAutoLaunchHud ?? false,
                v => _settings.WinUIAutoLaunchHud = v));
            launch.AddRow(MakeToggleRow("Logo-only on first appearance",
                "Show only the K10 logo until telemetry connects.",
                () => _settings.LogoOnlyStart ?? true,
                v => _settings.LogoOnlyStart = v));
            AddCard(launch);

            var iracing = new SettingsCard { Title = "iRacing", Caption = "Sim-specific behavior." };
            iracing.AddRow(MakeToggleRow("iRacing data sync", null,
                () => _settings.IracingDataSync ?? true,
                v => _settings.IracingDataSync = v));
            AddCard(iracing);

            var hotkeys = new SettingsCard
            {
                Title = "Hotkeys",
                Caption = "Global shortcuts the HUD listens for. Customization coming in a later release.",
            };
            hotkeys.AddRow(MakeReadonlyHotkeyRow("Toggle HUD visibility", "Ctrl+Shift+H"));
            hotkeys.AddRow(MakeReadonlyHotkeyRow("Toggle HUD settings mode", "Ctrl+Shift+S"));
            hotkeys.AddRow(MakeReadonlyHotkeyRow("Save replay clip", "Ctrl+Shift+V"));
            AddCard(hotkeys);

            var connection = new SettingsCard { Title = "Connection", Caption = "Where the HUD talks to SimHub + the cloud." };
            connection.AddRow(MakeRow("SimHub plugin URL",
                "Where the HUD talks to the local SimHub plugin (loopback).",
                MakeTextBox(
                    () => _settings.SimHubUrl ?? "http://localhost:8889",
                    v => _settings.SimHubUrl = v)));
            connection.AddRow(MakeRow("API base URL", null,
                MakeTextBox(
                    () => _settings.ApiBase ?? "https://prodrive.racecor.io",
                    v => _settings.ApiBase = v)));
            connection.AddRow(MakeToggleRow("Use remote tokens",
                "Authenticate against the production token endpoint instead of local SimHub credentials.",
                () => _settings.UseRemoteTokens ?? false,
                v => _settings.UseRemoteTokens = v));
            connection.AddRow(MakeRow("Agent key", null,
                MakeTextBox(
                    () => _settings.AgentKey ?? "",
                    v => _settings.AgentKey = v)));
            AddCard(connection);
        }

        // ── Two-column card layout ───────────────────────────────────
        //
        // Cards accumulate in _pendingCards as each Build*Section runs.
        // FlushColumns() then walks the list in source order and drops
        // each card into whichever column is currently shorter (height
        // estimated from the card's row count). Source order is
        // preserved within each column, but the two columns end up
        // roughly balanced vertically — no long-left / stub-right
        // imbalance the old row-major flow produced.
        private void AddCard(SettingsCard card)
        {
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Top;
            _pendingCards.Add(card);
        }

        private void FlushColumns()
        {
            // Estimated heights: header band (~70pt for title + caption +
            // top/bottom padding) + per-row band (~52pt for a typical
            // row with control + caption). These don't have to be exact
            // — they only need to rank cards by relative height for the
            // greedy balancer.
            const double HeaderEstimate = 70.0;
            const double RowEstimate = 52.0;

            double leftHeight = 0, rightHeight = 0;
            foreach (var card in _pendingCards)
            {
                var estimate = HeaderEstimate + card.RowCount * RowEstimate;
                if (leftHeight <= rightHeight)
                {
                    LeftColumn.Children.Add(card);
                    leftHeight += estimate;
                }
                else
                {
                    RightColumn.Children.Add(card);
                    rightHeight += estimate;
                }
            }
        }

        // ── Builders (return SettingsRow / SettingsSlider / etc.) ────

        private SettingsRow MakeRow(string label, string? caption, UIElement control)
        {
            return new SettingsRow
            {
                Label = label,
                Caption = caption ?? string.Empty,
                Content = control,
            };
        }

        private SettingsRow MakeToggleRow(
            string title,
            string? caption,
            Func<bool> getter,
            Action<bool> setter,
            bool persistsToFile = true)
        {
            var sw = new ToggleSwitch
            {
                IsOn = getter(),
                OnContent = "On",
                OffContent = "Off",
                MinWidth = 96,
            };
            sw.Toggled += async (_, __) =>
            {
                if (_suppressSave) return;
                setter(sw.IsOn);
                if (persistsToFile) await SaveAsync();
            };
            return MakeRow(title, caption, sw);
        }

        private SettingsRow MakeReadonlyHotkeyRow(string label, string combo)
        {
            return new SettingsRow
            {
                Label = label,
                Content = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3),
                    BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                    Child = new TextBlock
                    {
                        Text = combo,
                        FontSize = 11,
                        FontFamily = new FontFamily("Consolas"),
                        Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    },
                },
            };
        }

        private ComboBox MakePicker(IList<string> options, Func<string> current, Action<string> setter)
        {
            var combo = new ComboBox
            {
                ItemsSource = options,
                SelectedItem = current(),
                MinWidth = 200,
            };
            combo.SelectionChanged += async (_, __) =>
            {
                if (_suppressSave) return;
                if (combo.SelectedItem is string v)
                {
                    setter(v);
                    await SaveAsync();
                }
            };
            return combo;
        }

        private SegmentedPicker MakeSegmented(
            (string value, string label)[] options,
            Func<string> current,
            Action<string> setter)
        {
            var picker = new SegmentedPicker
            {
                Options = Array.ConvertAll(options,
                    p => new SegmentedPicker.Option { Value = p.value, Label = p.label }),
                SelectedValue = current(),
            };
            picker.SelectionChanged += async (_, value) =>
            {
                if (_suppressSave) return;
                setter(value);
                await SaveAsync();
            };
            return picker;
        }

        private SettingsSlider MakeSlider(
            string label, string? caption,
            double min, double max, double step,
            Func<double> getter, Action<double> setter,
            string format = "0.##", string unit = "")
        {
            var slider = new SettingsSlider
            {
                Label = label,
                Caption = caption ?? string.Empty,
                Minimum = min,
                Maximum = max,
                StepFrequency = step,
                Value = getter(),
                ValueFormat = format,
                Unit = unit,
            };
            slider.ValueCommitted += async (_, v) =>
            {
                if (_suppressSave) return;
                setter(v);
                await SaveAsync();
            };
            return slider;
        }

        private TextBox MakeTextBox(Func<string> getter, Action<string> setter)
        {
            var box = new TextBox
            {
                Text = getter(),
                MinWidth = 280,
            };
            // Save on focus loss rather than every keystroke.
            box.LostFocus += async (_, __) =>
            {
                if (_suppressSave) return;
                setter(box.Text);
                await SaveAsync();
            };
            return box;
        }

        // ── Save + status ────────────────────────────────────────────

        private async Task SaveAsync()
        {
            SaveStatusText.Text = "Saving…";
            await OverlaySettingsService.Shared.SaveAsync(_settings);
            SaveStatusText.Text = "Saved ✓";
            ResetSaveStatusTimer();
        }

        private void ResetSaveStatusTimer()
        {
            _saveStatusTimer?.Stop();
            _saveStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _saveStatusTimer.Tick += (_, __) =>
            {
                SaveStatusText.Text = string.Empty;
                _saveStatusTimer?.Stop();
            };
            _saveStatusTimer.Start();
        }
    }
}
