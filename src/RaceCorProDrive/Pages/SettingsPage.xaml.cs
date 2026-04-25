using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RaceCorProDrive.Services;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// HUD settings page. Reads from
    /// <see cref="OverlaySettingsService.Shared"/> on load, writes back
    /// on every control change, watches for external writes (the
    /// in-game move/resize hotkey) and rebuilds the form when they
    /// arrive so the UI stays in sync without explicit refresh.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private OverlaySettings _settings = new();
        private string _category = "hud";
        private bool _suppressSave;   // set during programmatic Update

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        // ── Lifecycle ──

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _settings = OverlaySettingsService.Shared.Load();
            OverlaySettingsService.Shared.Changed += OnSettingsFileChanged;
            OverlaySettingsService.Shared.StartWatching();

            CategoryNav.SelectedItem = CategoryNav.MenuItems[0];
            BuildForm();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            OverlaySettingsService.Shared.Changed -= OnSettingsFileChanged;
            // Don't stop watching globally — other pages (e.g. the
            // dashboard's overlay-status indicator) may want to know
            // about HUD-side changes too.
        }

        private void OnSettingsFileChanged(object? sender, OverlaySettings refreshed)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _settings = refreshed;
                BuildForm();
            });
        }

        // ── Category routing ──

        private void OnCategoryChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                _category = tag;
                BuildForm();
            }
        }

        // ── Form construction ──

        private void BuildForm()
        {
            FormHost.Children.Clear();
            _suppressSave = true;
            try
            {
                switch (_category)
                {
                    case "hud":         BuildHudSection(); break;
                    case "display":     BuildDisplaySection(); break;
                    case "components":  BuildComponentsSection(); break;
                    case "commentary":  BuildCommentarySection(); break;
                    case "recording":   BuildRecordingSection(); break;
                    case "advanced":    BuildAdvancedSection(); break;
                }
            }
            finally { _suppressSave = false; }
        }

        private void BuildHudSection()
        {
            AddHeader("HUD");
            AddCaption("Manage when the in-game HUD launches and how it boots up.");

            AddToggle(
                title: "Auto-launch HUD when this app opens",
                caption: "Spawns the in-game HUD process the moment this dashboard window comes up.",
                getter: () => _settings.WinUIAutoLaunchHud ?? false,
                setter: v => _settings.WinUIAutoLaunchHud = v);

            AddToggle(
                title: "Open this app when I sign in to Windows",
                caption: "Adds a startup entry so the dashboard is ready before you start a session.",
                getter: () => OverlayLauncher.Shared.IsLaunchOnLoginEnabled,
                setter: v => OverlayLauncher.Shared.SetLaunchOnLogin(v),
                persistsToFile: false);

            AddToggle(
                title: "Logo-only on first appearance",
                caption: "Show only the K10 logo until telemetry connects — keeps the screen clean while the game loads.",
                getter: () => _settings.LogoOnlyStart ?? true,
                setter: v => _settings.LogoOnlyStart = v);

            AddPicker(
                title: "Visual preset",
                options: new[] { "minimal", "minimal+", "standard", "rich" },
                current: () => _settings.VisualPreset ?? "standard",
                setter: v => _settings.VisualPreset = v);
        }

        private void BuildDisplaySection()
        {
            AddHeader("Display");
            AddCaption("Where the HUD lives on screen and how it's drawn.");

            AddPicker(
                title: "Layout position",
                options: new[] { "top-left", "top-right", "bottom-left", "bottom-right", "centered" },
                current: () => _settings.LayoutPosition ?? "top-right",
                setter: v => _settings.LayoutPosition = v);

            AddSlider(
                title: "Zoom",
                caption: "Scales the entire HUD up or down.",
                min: 0.5, max: 2.0, step: 0.05,
                getter: () => _settings.Zoom ?? 1.0,
                setter: v => _settings.Zoom = v);

            AddPicker(
                title: "Ambient lighting",
                options: new[] { "off", "auto", "screen", "wled" },
                current: () => _settings.AmbientMode ?? "auto",
                setter: v => _settings.AmbientMode = v);

            AddToggle("Show panel borders",
                "Outlines every component for layout debugging.",
                () => _settings.ShowBorders ?? false,
                v => _settings.ShowBorders = v);

            AddToggle("WebGL effects",
                "Glare, bloom, glow, and g-force vignette via fragment shaders.",
                () => _settings.ShowWebGL ?? true,
                v => _settings.ShowWebGL = v);

            AddToggle("Green-screen mode",
                "Replace the background with pure green for OBS chroma keying.",
                () => _settings.GreenScreen ?? false,
                v => _settings.GreenScreen = v);
        }

        private void BuildComponentsSection()
        {
            AddHeader("Components");
            AddCaption("Toggle individual HUD modules. Off-screen modules don't render or eat any frame budget.");

            void Toggle(string title, Func<bool> get, Action<bool> set)
                => AddToggle(title, null, get, set);

            Toggle("Fuel",          () => _settings.ShowFuel        ?? true, v => _settings.ShowFuel = v);
            Toggle("Tyres",         () => _settings.ShowTyres       ?? true, v => _settings.ShowTyres = v);
            Toggle("Controls",      () => _settings.ShowControls    ?? true, v => _settings.ShowControls = v);
            Toggle("Pedals",        () => _settings.ShowPedals      ?? true, v => _settings.ShowPedals = v);
            Toggle("Position",      () => _settings.ShowPosition    ?? true, v => _settings.ShowPosition = v);
            Toggle("Tachometer",    () => _settings.ShowTacho       ?? true, v => _settings.ShowTacho = v);
            Toggle("Commentary",    () => _settings.ShowCommentary  ?? true, v => _settings.ShowCommentary = v);
            Toggle("Leaderboard",   () => _settings.ShowLeaderboard ?? true, v => _settings.ShowLeaderboard = v);
            Toggle("Datastream",    () => _settings.ShowDatastream  ?? true, v => _settings.ShowDatastream = v);
            Toggle("Pit box",       () => _settings.ShowPitBox      ?? true, v => _settings.ShowPitBox = v);
            Toggle("Incidents",     () => _settings.ShowIncidents   ?? true, v => _settings.ShowIncidents = v);
            Toggle("Spotter",       () => _settings.ShowSpotter     ?? true, v => _settings.ShowSpotter = v);
            Toggle("K10 logo",      () => _settings.ShowK10Logo     ?? true, v => _settings.ShowK10Logo = v);
            Toggle("Car logo",      () => _settings.ShowCarLogo     ?? true, v => _settings.ShowCarLogo = v);
            Toggle("Game logo",     () => _settings.ShowGameLogo    ?? true, v => _settings.ShowGameLogo = v);
        }

        private void BuildCommentarySection()
        {
            AddHeader("Commentary");
            AddCaption("Tune what the in-race coach surfaces and how it speaks.");

            AddSlider("Prompt duration (s)", null, 4, 20, 1,
                () => _settings.CommentaryPromptDuration ?? 8,
                v => _settings.CommentaryPromptDuration = (int)System.Math.Round(v));

            AddToggle("Show topic title", null,
                () => _settings.CommentaryShowTopicTitle ?? true,
                v => _settings.CommentaryShowTopicTitle = v);

            AddToggle("Event-only mode",
                "Skip ambient color, only speak when something notable happens.",
                () => _settings.CommentaryEventOnlyMode ?? false,
                v => _settings.CommentaryEventOnlyMode = v);

            AddToggle("Demo mode",
                "Cycle synthetic prompts so you can preview the engine without driving.",
                () => _settings.CommentaryDemoMode ?? false,
                v => _settings.CommentaryDemoMode = v);

            AddHeader("Categories", small: true);
            AddToggle("Strategy", null, () => _settings.CommentaryCatStrategy ?? true, v => _settings.CommentaryCatStrategy = v);
            AddToggle("Track",    null, () => _settings.CommentaryCatTrack    ?? true, v => _settings.CommentaryCatTrack = v);
            AddToggle("Rivals",   null, () => _settings.CommentaryCatRivals   ?? true, v => _settings.CommentaryCatRivals = v);
            AddToggle("Behavior", null, () => _settings.CommentaryCatBehavior ?? true, v => _settings.CommentaryCatBehavior = v);

            AddHeader("Coach voice", small: true);
            AddPicker("Tone",
                new[] { "neutral", "warm", "drill-sergeant", "british-broadcaster" },
                () => _settings.CoachTone ?? "neutral",
                v => _settings.CoachTone = v);
            AddPicker("Depth",
                new[] { "terse", "balanced", "rich" },
                () => _settings.CoachDepth ?? "balanced",
                v => _settings.CoachDepth = v);
        }

        private void BuildRecordingSection()
        {
            AddHeader("Recording");
            AddCaption("Replay buffer + microphone setup for the rolling-window recorder.");

            AddPicker("Quality",
                new[] { "low", "medium", "high", "ultra" },
                () => _settings.RecordingQuality ?? "high",
                v => _settings.RecordingQuality = v);

            AddToggle("Microphone enabled", null,
                () => _settings.RecordingMicEnabled ?? false,
                v => _settings.RecordingMicEnabled = v);

            AddToggle("Replay buffer enabled",
                "Continuously buffers the last N seconds so you can save a clip after a moment passes.",
                () => _settings.ReplayBufferEnabled ?? false,
                v => _settings.ReplayBufferEnabled = v);

            AddSlider("Buffer length (s)", null, 15, 120, 5,
                () => _settings.ReplayBufferSeconds ?? 30,
                v => _settings.ReplayBufferSeconds = (int)System.Math.Round(v));
        }

        private void BuildAdvancedSection()
        {
            AddHeader("Advanced");
            AddCaption("Power-user toggles. Most users never need to touch these.");

            AddToggle("Penalty incidents counted", null,
                () => _settings.IncPenalty ?? true, v => _settings.IncPenalty = v);
            AddToggle("DQ incidents counted", null,
                () => _settings.IncDQ ?? true, v => _settings.IncDQ = v);
            AddToggle("Rally mode",
                "Stage-by-stage layout instead of per-lap.",
                () => _settings.RallyMode ?? false, v => _settings.RallyMode = v);
            AddPicker("Drive mode",
                new[] { "circuit", "rally", "drift", "endurance" },
                () => _settings.DriveMode ?? "circuit",
                v => _settings.DriveMode = v);
            AddPicker("Force flag",
                new[] { "auto", "green", "yellow", "double-yellow", "red", "white", "checkered" },
                () => _settings.ForceFlag ?? "auto",
                v => _settings.ForceFlag = v);
            AddToggle("iRacing data sync", null,
                () => _settings.IracingDataSync ?? true, v => _settings.IracingDataSync = v);
            AddToggle("Use remote tokens",
                "Authenticate against the production token endpoint instead of local SimHub credentials.",
                () => _settings.UseRemoteTokens ?? false, v => _settings.UseRemoteTokens = v);
            AddTextField("API base URL",
                () => _settings.ApiBase ?? "https://prodrive.racecor.io",
                v => _settings.ApiBase = v);
            AddTextField("Agent key",
                () => _settings.AgentKey ?? "",
                v => _settings.AgentKey = v);
        }

        // ── Builders ──

        private void AddHeader(string text, bool small = false)
        {
            FormHost.Children.Add(new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = small ? 11 : 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = small ? 100 : 160,
                Margin = new Thickness(0, small ? 14 : 0, 0, 4),
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
            });
        }

        private void AddCaption(string text)
        {
            FormHost.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            });
        }

        private void AddToggle(
            string title,
            string? caption,
            Func<bool> getter,
            Action<bool> setter,
            bool persistsToFile = true)
        {
            var sw = new ToggleSwitch
            {
                Header = title,
                IsOn = getter(),
                OnContent = "On",
                OffContent = "Off",
            };
            if (!string.IsNullOrEmpty(caption))
            {
                sw.Header = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = title },
                        new TextBlock
                        {
                            Text = caption,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                        },
                    },
                };
            }
            sw.Toggled += async (_, __) =>
            {
                if (_suppressSave) return;
                setter(sw.IsOn);
                if (persistsToFile) await SaveAsync();
            };
            FormHost.Children.Add(sw);
        }

        private void AddPicker(
            string title,
            IList<string> options,
            Func<string> current,
            Action<string> setter)
        {
            var combo = new ComboBox
            {
                Header = title,
                ItemsSource = options,
                SelectedItem = current(),
                MinWidth = 240,
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
            FormHost.Children.Add(combo);
        }

        private void AddSlider(
            string title,
            string? caption,
            double min,
            double max,
            double step,
            Func<double> getter,
            Action<double> setter)
        {
            var slider = new Slider
            {
                Header = title,
                Minimum = min,
                Maximum = max,
                StepFrequency = step,
                Value = getter(),
                MinWidth = 320,
            };
            slider.ValueChanged += async (_, __) =>
            {
                if (_suppressSave) return;
                setter(slider.Value);
                await SaveAsync();
            };
            FormHost.Children.Add(slider);
            if (!string.IsNullOrEmpty(caption))
            {
                FormHost.Children.Add(new TextBlock
                {
                    Text = caption,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                });
            }
        }

        private void AddTextField(string title, Func<string> getter, Action<string> setter)
        {
            var box = new TextBox
            {
                Header = title,
                Text = getter(),
                MinWidth = 360,
            };
            // Save on focus loss rather than every keystroke — typing
            // a URL would otherwise hammer the watcher.
            box.LostFocus += async (_, __) =>
            {
                if (_suppressSave) return;
                setter(box.Text);
                await SaveAsync();
            };
            FormHost.Children.Add(box);
        }

        private Task SaveAsync()
            => OverlaySettingsService.Shared.SaveAsync(_settings);
    }
}
