using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Services.Moza;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Hardware settings tab — direct Moza serial control. The plugin
    /// used to own this; it now lives in-process so the host can drive
    /// the wheelbase / pedals / handbrake / shifter / dashboard / wheel
    /// without round-tripping through SimHub HTTP on :8889.
    ///
    /// MVP layout (per kev: "light and unopinionated, mostly copy
    /// Pit House but simpler"): one card per connected device, every
    /// known setting laid out as a labeled row, no per-car switching
    /// (per-car concept will come later as a wrapping Profile pickle).
    /// Pedal curves are read-only line graphs for now; full editor in
    /// a follow-up PR.
    ///
    /// State is read directly from MozaService.Shared.Manager. Writes
    /// are queued on the same poll thread; the page rebuilds only on
    /// explicit Refresh so we don't snap a slider out from under the
    /// user mid-drag.
    /// </summary>
    public sealed partial class SettingsPage
    {
        private void BuildHardwareSection()
        {
            AddCard(BuildConnectionCard());

            var mgr = MozaService.Shared.Manager;
            if (mgr.Wheelbase != null) AddCard(BuildWheelbaseCard(mgr.Wheelbase));
            if (mgr.Pedals != null)    AddCard(BuildPedalsCard(mgr.Pedals));
            if (mgr.Handbrake != null) AddCard(BuildHandbrakeCard(mgr.Handbrake));
            if (mgr.Shifter != null)   AddCard(BuildShifterCard(mgr.Shifter));
            if (mgr.Dashboard != null) AddCard(BuildDashboardCard(mgr.Dashboard));
            if (mgr.SteeringWheel != null) AddCard(BuildWheelCard(mgr.SteeringWheel));
        }

        // ── Connection card ─────────────────────────────────────────

        private SettingsCard BuildConnectionCard()
        {
            var mgr = MozaService.Shared.Manager;
            var card = new SettingsCard
            {
                Title = "Connection",
                Caption = mgr.IsConnected
                    ? $"{mgr.DeviceCount} Moza device{(mgr.DeviceCount == 1 ? "" : "s")} connected over serial."
                    : "No Moza hardware detected. Plug in a wheelbase or pedal set, then hit Reconnect.",
            };

            if (!string.IsNullOrEmpty(mgr.PitHouseWarning))
            {
                // Pit House (or a sibling Moza service) is holding the
                // serial port. Surface the warning string the poll
                // thread captured — no remediation, just the message.
                card.AddRow(MakeStatusRow("Pit House conflict", mgr.PitHouseWarning, isWarning: true));
            }

            foreach (var d in mgr.Devices)
            {
                var label = d.DeviceType.ToString();
                var detail = d.IsConnected
                    ? $"{d.PortName} • {d.DisplayName}"
                    : "Disconnected";
                card.AddRow(MakeStatusRow(label, detail, isWarning: false));
            }

            // Refresh + Reconnect buttons. Refresh re-polls every
            // connected device on the next loop tick; Reconnect blows
            // away the discovery cache and re-scans COM ports.
            var refreshBtn = new Button
            {
                Content = "Refresh values",
                Margin = new Thickness(0, 8, 8, 0),
            };
            refreshBtn.Click += (_, __) => { mgr.ForceRefresh(); BuildCategory(); };

            var reconnectBtn = new Button
            {
                Content = "Reconnect devices",
                Margin = new Thickness(0, 8, 0, 0),
            };
            reconnectBtn.Click += (_, __) => { mgr.ForceRediscovery(); BuildCategory(); };

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
            };
            btnRow.Children.Add(refreshBtn);
            btnRow.Children.Add(reconnectBtn);
            card.AddRow(btnRow);

            return card;
        }

        // ── Wheelbase ───────────────────────────────────────────────

        private SettingsCard BuildWheelbaseCard(MozaDevice device)
        {
            var s = device.WheelbaseSettings;
            var card = new SettingsCard
            {
                Title = "Wheelbase",
                Caption = string.IsNullOrEmpty(s.Model) ? device.DisplayName : $"{device.DisplayName} ({s.Model})",
            };

            if (!s.HasData)
            {
                card.AddRow(MakeStatusRow("Reading…", "Waiting for first poll response.", isWarning: false));
                return card;
            }

            // Force feedback — the most-used knobs sit at the top.
            card.AddRow(MakeMozaSlider("FFB strength", "%", 0, 100, s.FfbStrength,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.FfbStrength, v)));
            card.AddRow(MakeMozaSlider("Max torque", "%", 0, 200, s.MaxTorque,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.MaxTorque, v)));
            card.AddRow(MakeMozaSlider("Road sensitivity", "%", 0, 100, s.RoadSensitivity,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.RoadSensitivity, v)));
            card.AddRow(MakeMozaSlider("Speed damping", "%", 0, 100, s.SpeedDamping,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.SpeedDamping, v)));

            // Rotation
            card.AddRow(MakeStatusRow("Rotation range", $"{s.RotationRange}° ({s.MinRotationAngle}° / +{s.MaxRotationAngle}°)", isWarning: false));
            card.AddRow(MakeMozaToggle("Soft lock", s.SoftLock != 0,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.SoftLock, v ? 1 : 0)));

            // Damping & feel
            card.AddRow(MakeMozaSlider("Friction", "%", 0, 100, s.Friction,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.Friction, v)));
            card.AddRow(MakeMozaSlider("Spring", "%", 0, 100, s.Spring,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.Spring, v)));
            card.AddRow(MakeMozaSlider("Damper", "%", 0, 100, s.Damper,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.Damper, v)));
            card.AddRow(MakeMozaSlider("Inertia", "%", 0, 100, s.Inertia,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.Inertia, v)));

            // EQ — center is 100, range 0–200.
            for (int i = 0; i < s.EqBands.Length; i++)
            {
                int band = i + 1;
                int idx = i;
                byte cmd = (byte)(MozaDeviceRegistry.WheelbaseCmd.EqBand1 + i);
                card.AddRow(MakeMozaSlider($"EQ band {band}", "", 0, 200, s.EqBands[idx],
                    v => WriteWb(cmd, v)));
            }

            // Protections
            card.AddRow(MakeMozaToggle("Temperature protection", s.TempProtection != 0,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.TempProtection, v ? 1 : 0)));
            card.AddRow(MakeMozaToggle("Hands-off protection", s.HandsOffProtection != 0,
                v => WriteWb(MozaDeviceRegistry.WheelbaseCmd.HandsOffProtection, v ? 1 : 0)));

            return card;
        }

        // ── Pedals ──────────────────────────────────────────────────

        private SettingsCard BuildPedalsCard(MozaDevice device)
        {
            var s = device.PedalSettings;
            var card = new SettingsCard
            {
                Title = "Pedals",
                Caption = device.DisplayName,
            };

            if (!s.HasData)
            {
                card.AddRow(MakeStatusRow("Reading…", "Waiting for first poll response.", isWarning: false));
                return card;
            }

            AddPedalAxisRows(card, "Throttle", s.Throttle, MozaDeviceRegistry.PedalAxis.Throttle);
            AddPedalAxisRows(card, "Brake",    s.Brake,    MozaDeviceRegistry.PedalAxis.Brake);
            AddPedalAxisRows(card, "Clutch",   s.Clutch,   MozaDeviceRegistry.PedalAxis.Clutch);

            return card;
        }

        private void AddPedalAxisRows(SettingsCard card, string axisName, MozaPedalAxisSettings axis, MozaDeviceRegistry.PedalAxis axisEnum)
        {
            // Axis sub-header — small caps red label so the three
            // axes read as their own groupings inside one card.
            var header = new TextBlock
            {
                Text = axisName.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 160,
                Foreground = (Brush)Application.Current.Resources["BrandRedBrush"],
                Margin = new Thickness(0, 8, 0, 4),
            };
            card.AddRow(header);

            if (!axis.HasData)
            {
                card.AddRow(MakeStatusRow("Not detected", "Pedal axis offline.", isWarning: false));
                return;
            }

            card.AddRow(MakeStatusRow("Calibration",
                $"min {axis.CalibrationMin}, max {axis.CalibrationMax}",
                isWarning: false));
            card.AddRow(MakeMozaSlider("Deadzone", "%", 0, 100, axis.Deadzone,
                v => WritePedal(axisEnum, MozaDeviceRegistry.PedalCmd.Deadzone, v)));
            card.AddRow(MakeMozaSlider("HID source", "", 0, 16, axis.HidSource,
                v => WritePedal(axisEnum, MozaDeviceRegistry.PedalCmd.HidSource, v)));

            // Curve view + tip about edit deferral. The 5 numeric
            // curve points map to fixed 20% input intervals.
            var curve = new PedalCurveView { Points = (IReadOnlyList<int>)axis.Curve };
            var curveLabel = new TextBlock
            {
                Text = $"Response curve: {string.Join(", ", axis.Curve)}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 4, 0, 6),
            };
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            stack.Children.Add(curveLabel);
            stack.Children.Add(curve);
            card.AddRow(stack);
        }

        // ── Handbrake ───────────────────────────────────────────────

        private SettingsCard BuildHandbrakeCard(MozaDevice device)
        {
            var s = device.HandbrakeSettings;
            var card = new SettingsCard
            {
                Title = "Handbrake",
                Caption = device.DisplayName,
            };

            if (!s.HasData)
            {
                card.AddRow(MakeStatusRow("Reading…", "Waiting for first poll response.", isWarning: false));
                return card;
            }

            card.AddRow(MakeStatusRow("Calibration",
                $"min {s.CalibrationMin}, max {s.CalibrationMax}",
                isWarning: false));
            card.AddRow(MakeMozaSlider("Deadzone", "%", 0, 100, s.Deadzone,
                v => WriteHandbrake(MozaDeviceRegistry.HandbrakeCmd.Deadzone, v)));
            card.AddRow(MakeMozaSlider("Button threshold", "%", 0, 100, s.ButtonThreshold,
                v => WriteHandbrake(MozaDeviceRegistry.HandbrakeCmd.ButtonThreshold, v)));
            card.AddRow(MakeRow("Output mode", null,
                MakePicker(
                    new[] { "analog", "button", "dual" },
                    () => OutputModeToString(s.OutputMode),
                    v => WriteHandbrake(MozaDeviceRegistry.HandbrakeCmd.OutputMode, OutputModeFromString(v)))));

            var curve = new PedalCurveView { Points = (IReadOnlyList<int>)s.Curve };
            var curveLabel = new TextBlock
            {
                Text = $"Response curve: {string.Join(", ", s.Curve)}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 4, 0, 6),
            };
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            stack.Children.Add(curveLabel);
            stack.Children.Add(curve);
            card.AddRow(stack);

            return card;
        }

        private static string OutputModeToString(int v) => v switch
        {
            1 => "button",
            2 => "dual",
            _ => "analog",
        };
        private static int OutputModeFromString(string v) => v switch
        {
            "button" => 1,
            "dual" => 2,
            _ => 0,
        };

        // ── Shifter ─────────────────────────────────────────────────

        private SettingsCard BuildShifterCard(MozaDevice device)
        {
            var s = device.ShifterSettings;
            var card = new SettingsCard
            {
                Title = "Shifter",
                Caption = string.IsNullOrEmpty(device.SubType) ? device.DisplayName : $"{device.DisplayName} ({device.SubType})",
            };

            if (!s.HasData)
            {
                card.AddRow(MakeStatusRow("Reading…", "Waiting for first poll response.", isWarning: false));
                return card;
            }

            card.AddRow(MakeStatusRow("Type", s.ShifterType ?? "unknown", isWarning: false));
            card.AddRow(MakeMozaSlider("Direction", "", 0, 1, s.Direction,
                v => WriteShifter(MozaDeviceRegistry.ShifterCmd.Direction, v)));
            card.AddRow(MakeRow("HID mode", null,
                MakePicker(
                    new[] { "joypad", "keyboard" },
                    () => s.HidMode == 1 ? "keyboard" : "joypad",
                    v => WriteShifter(MozaDeviceRegistry.ShifterCmd.HidMode, v == "keyboard" ? 1 : 0))));
            // Brightness only applies to SGP — show the row anyway,
            // it's harmless on H-pattern (firmware ignores).
            card.AddRow(MakeMozaSlider("Brightness", "%", 0, 100, s.Brightness,
                v => WriteShifter(MozaDeviceRegistry.ShifterCmd.Brightness, v)));

            return card;
        }

        // ── Dashboard ───────────────────────────────────────────────

        private SettingsCard BuildDashboardCard(MozaDevice device)
        {
            var s = device.DashboardSettings;
            var card = new SettingsCard
            {
                Title = "Dashboard",
                Caption = device.DisplayName,
            };

            if (!s.HasData)
            {
                card.AddRow(MakeStatusRow("Reading…", "Waiting for first poll response.", isWarning: false));
                return card;
            }

            card.AddRow(MakeRow("RPM display", null,
                MakePicker(
                    new[] { "analog", "led-bar", "digital" },
                    () => RpmModeToString(s.RpmDisplayMode),
                    v => WriteDashboard(MozaDeviceRegistry.DashboardCmd.RpmDisplayMode, RpmModeFromString(v)))));
            card.AddRow(MakeMozaSlider("Brightness", "%", 0, 100, s.Brightness,
                v => WriteDashboard(MozaDeviceRegistry.DashboardCmd.Brightness, v)));
            card.AddRow(MakeMozaSlider("Update interval", "ms", 0, 200, s.UpdateInterval,
                v => WriteDashboard(MozaDeviceRegistry.DashboardCmd.UpdateInterval, v)));
            card.AddRow(MakeMozaToggle("Telemetry", s.TelemetryEnable != 0,
                v => WriteDashboard(MozaDeviceRegistry.DashboardCmd.TelemetryEnable, v ? 1 : 0)));

            return card;
        }

        private static string RpmModeToString(int v) => v switch
        {
            1 => "led-bar",
            2 => "digital",
            _ => "analog",
        };
        private static int RpmModeFromString(string v) => v switch
        {
            "led-bar" => 1,
            "digital" => 2,
            _ => 0,
        };

        // ── Steering wheel ──────────────────────────────────────────

        private SettingsCard BuildWheelCard(MozaDevice device)
        {
            var s = device.WheelSettings;
            var card = new SettingsCard
            {
                Title = "Steering wheel",
                Caption = device.DisplayName,
            };

            if (!s.HasData)
            {
                card.AddRow(MakeStatusRow("Reading…", "Waiting for first poll response.", isWarning: false));
                return card;
            }

            card.AddRow(MakeMozaSlider("Paddle mode", "", 0, 4, s.PaddleMode,
                v => WriteWheel(MozaDeviceRegistry.DeviceSteeringWheelPrimary, MozaDeviceRegistry.WheelCmd.PaddleMode, v)));
            card.AddRow(MakeMozaSlider("Clutch bite point", "%", 0, 100, s.ClutchBitePoint,
                v => WriteWheel(MozaDeviceRegistry.DeviceSteeringWheelPrimary, MozaDeviceRegistry.WheelCmd.ClutchBitePoint, v)));
            card.AddRow(MakeMozaToggle("Adaptive paddles", s.AdaptivePaddles != 0,
                v => WriteWheel(MozaDeviceRegistry.DeviceSteeringWheelPrimary, MozaDeviceRegistry.WheelCmd.AdaptivePaddles, v ? 1 : 0)));
            card.AddRow(MakeMozaToggle("Idle animation", s.IdleAnimation != 0,
                v => WriteWheel(MozaDeviceRegistry.DeviceSteeringWheelExtended, MozaDeviceRegistry.WheelCmd.IdleAnimation, v ? 1 : 0)));
            card.AddRow(MakeMozaSlider("Brightness", "%", 0, 100, s.Brightness,
                v => WriteWheel(MozaDeviceRegistry.DeviceSteeringWheelExtended, MozaDeviceRegistry.WheelCmd.Brightness, v)));
            card.AddRow(MakeMozaSlider("Telemetry mode", "", 0, 4, s.TelemetryMode,
                v => WriteWheel(MozaDeviceRegistry.DeviceSteeringWheelExtended, MozaDeviceRegistry.WheelCmd.TelemetryMode, v)));

            return card;
        }

        // ── Helper builders ─────────────────────────────────────────

        // Status-only row (no editable control). Used for diagnostic
        // strings like "Calibration min/max" or the Pit House warning.
        private SettingsRow MakeStatusRow(string label, string value, bool isWarning)
        {
            var fg = isWarning
                ? (Brush)Application.Current.Resources["BrandRedBrush"]
                : (Brush)Application.Current.Resources["TextSecondaryBrush"];

            var text = new TextBlock
            {
                Text = value,
                FontSize = 12,
                Foreground = fg,
                TextWrapping = TextWrapping.Wrap,
            };
            return MakeRow(label, null, text);
        }

        // Moza-specific slider — sidesteps the OverlaySettings
        // SaveAsync pipeline that the regular MakeSlider triggers.
        // The "save" for a Moza value is queueing a serial write;
        // the poll loop confirms it on the next tick.
        // Returns the SettingsSlider directly (no MakeRow wrap) — the
        // slider widget already paints its own label, matching what
        // MakeSlider does for the OverlaySettings rows.
        private SettingsSlider MakeMozaSlider(string label, string suffix, double min, double max, int currentValue,
            Action<int> setter)
        {
            // Negative sentinel from the cache means "not yet read";
            // clamp to min so the slider doesn't sit off-screen at -1.
            double display = currentValue < min ? min : currentValue;
            var slider = new SettingsSlider
            {
                Label = label,
                Minimum = min,
                Maximum = max,
                StepFrequency = 1,
                Value = display,
                ValueFormat = "0",
                Unit = suffix,
            };
            slider.ValueCommitted += (_, v) =>
            {
                if (_suppressSave) return;
                setter((int)Math.Round(v));
            };
            return slider;
        }

        // Moza-specific toggle — same reason as MakeMozaSlider.
        private SettingsRow MakeMozaToggle(string label, bool current, Action<bool> setter)
        {
            var toggle = new ToggleSwitch
            {
                IsOn = current,
                OnContent = "On",
                OffContent = "Off",
            };
            toggle.Toggled += (_, __) =>
            {
                if (_suppressSave) return;
                setter(toggle.IsOn);
            };
            return MakeRow(label, null, toggle);
        }

        // ── Write helpers ───────────────────────────────────────────

        private static void WriteWb(byte cmd, int value)
        {
            MozaService.Shared.Manager.WriteSetting(
                MozaDeviceRegistry.DeviceWheelbase,
                cmd,
                (byte)Math.Clamp(value, 0, 255));
        }

        private static void WritePedal(MozaDeviceRegistry.PedalAxis axis, byte offset, int value)
        {
            byte cmd = MozaDeviceRegistry.PedalCmd.GetCommand(axis, offset);
            MozaService.Shared.Manager.WriteSetting(
                MozaDeviceRegistry.DevicePedals,
                cmd,
                (byte)Math.Clamp(value, 0, 255));
        }

        private static void WriteHandbrake(byte cmd, int value)
        {
            MozaService.Shared.Manager.WriteSetting(
                MozaDeviceRegistry.DeviceHandbrake,
                cmd,
                (byte)Math.Clamp(value, 0, 255));
        }

        private static void WriteShifter(byte cmd, int value)
        {
            MozaService.Shared.Manager.WriteSetting(
                MozaDeviceRegistry.DeviceShifter,
                cmd,
                (byte)Math.Clamp(value, 0, 255));
        }

        private static void WriteDashboard(byte cmd, int value)
        {
            MozaService.Shared.Manager.WriteSetting(
                MozaDeviceRegistry.DeviceDashboard,
                cmd,
                (byte)Math.Clamp(value, 0, 255));
        }

        private static void WriteWheel(byte deviceId, byte cmd, int value)
        {
            MozaService.Shared.Manager.WriteSetting(
                deviceId,
                cmd,
                (byte)Math.Clamp(value, 0, 255));
        }
    }
}
