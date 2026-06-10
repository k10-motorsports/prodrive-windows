using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Services;
using Windows.System;
using Windows.UI;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Visual tab's layout editor — the "move modules by proxy" surface
    /// from docs/overlay-layout-v2.md §3. A scaled live capture of the
    /// primary display sits under draggable proxy boxes (one per layout
    /// group, module name chips inside). All geometry/gesture logic
    /// lives in <see cref="LayoutEditorModel"/> (pure BCL, unit-tested);
    /// this partial only renders proxies, routes pointer events, and
    /// writes the result through the normal settings pipeline. The real
    /// overlay is never touched directly — it follows the settings file
    /// via its own watcher, live, when running.
    /// </summary>
    public sealed partial class SettingsPage
    {
        private const double CanvasWidth = 860;          // canvas px; height follows screen aspect
        private const double CaptureRefreshMs = 1500;    // live-backdrop cadence

        private LayoutEditorModel? _layoutEditor;
        private Canvas? _proxyCanvas;
        private Canvas? _anchorCanvas;
        private Image? _backdropImage;
        private StackPanel? _railHost;
        private DispatcherTimer? _captureTimer;
        private bool _captureInFlight;
        private double _canvasScale;                     // canvas px per screen px
        private double _screenW, _screenH;
        private string? _selectedGroupId;
        private readonly List<string> _groupSelection = new(); // ctrl+click order, for merge

        // ── Lifecycle ────────────────────────────────────────────────

        private void BuildLayoutEditor()
        {
            var registry = ModuleRegistry.LoadBundled(OverlayLauncher.Shared.BinaryPath);
            var bounds = DisplayArea.Primary.OuterBounds;
            _screenW = bounds.Width;
            _screenH = bounds.Height;
            _canvasScale = CanvasWidth / _screenW;

            _layoutEditor = new LayoutEditorModel(
                _settings.Layout, registry, _screenW, _screenH, _settings.Zoom ?? 165.0)
            {
                Metrics = OverlayMetricsService.Shared.Load(),
                IsModuleVisible = m => m.ShowKey == null || GetShowKey(m.ShowKey),
            };

            var canvasH = Math.Round(CanvasWidth * _screenH / _screenW);

            _backdropImage = new Image
            {
                Stretch = Stretch.Fill,
                Width = CanvasWidth,
                Height = canvasH,
            };
            var scrim = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x00, 0x00)),
            };
            _anchorCanvas = new Canvas { IsHitTestVisible = false };
            _proxyCanvas = new Canvas
            {
                Background = new SolidColorBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            };

            var layers = new Grid { Width = CanvasWidth, Height = canvasH };
            layers.Children.Add(_backdropImage);
            layers.Children.Add(scrim);
            layers.Children.Add(_anchorCanvas);
            layers.Children.Add(_proxyCanvas);

            var canvasShell = new Border
            {
                Child = layers,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x10, 0x10, 0x12)),
            };

            _railHost = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                MinWidth = 300,
            };
            var railScroll = new ScrollViewer
            {
                Content = _railHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = canvasH,
            };

            var grid = new Grid { ColumnSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(canvasShell, 0);
            Grid.SetColumn(railScroll, 1);
            grid.Children.Add(canvasShell);
            grid.Children.Add(railScroll);

            // Ctrl+Z / Ctrl+Y on the editor surface.
            var undoAccel = new KeyboardAccelerator { Key = VirtualKey.Z, Modifiers = VirtualKeyModifiers.Control };
            undoAccel.Invoked += (_, e) => { e.Handled = true; if (_layoutEditor?.Undo() == true) CommitLayoutMutation(); };
            var redoAccel = new KeyboardAccelerator { Key = VirtualKey.Y, Modifiers = VirtualKeyModifiers.Control };
            redoAccel.Invoked += (_, e) => { e.Handled = true; if (_layoutEditor?.Redo() == true) CommitLayoutMutation(); };
            grid.KeyboardAccelerators.Add(undoAccel);
            grid.KeyboardAccelerators.Add(redoAccel);

            var headline = new TextBlock
            {
                Text = "LAYOUT",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 160,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 0, 0, 4),
            };
            var caption = new TextBlock
            {
                Text = "Live preview of your screen. Drag a group to move it — drop on a corner dot "
                     + "to lock it there, anywhere else for free placement. Drag a module name out of "
                     + "its group to split it off, or onto another group to join. Ctrl+click selects "
                     + "several groups for merging. The running overlay follows every change live.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                Margin = new Thickness(0, 0, 0, 12),
            };

            var inner = new StackPanel { Orientation = Orientation.Vertical };
            inner.Children.Add(headline);
            inner.Children.Add(caption);
            inner.Children.Add(grid);

            FullWidthHost.Children.Add(new DesignSystem.Components.Panel { Content = inner });

            RenderAnchorDots(null);
            RenderProxies();
            RebuildRail();

            // Live backdrop + pixel-exact metrics.
            _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CaptureRefreshMs) };
            _captureTimer.Tick += OnCaptureTick;
            _captureTimer.Start();
            OnCaptureTick(null, null!); // first frame immediately

            OverlayMetricsService.Shared.Changed += OnMetricsChanged;
            OverlayMetricsService.Shared.StartWatching();
        }

        private void TeardownLayoutEditor()
        {
            if (_captureTimer != null)
            {
                _captureTimer.Stop();
                _captureTimer.Tick -= OnCaptureTick;
                _captureTimer = null;
            }
            OverlayMetricsService.Shared.Changed -= OnMetricsChanged;
            _layoutEditor = null;
            _proxyCanvas = null;
            _anchorCanvas = null;
            _backdropImage = null;
            _railHost = null;
            _selectedGroupId = null;
            _groupSelection.Clear();
        }

        private async void OnCaptureTick(object? sender, object e)
        {
            if (_captureInFlight || _backdropImage == null) return;
            _captureInFlight = true;
            try
            {
                // 2× canvas width so the backdrop stays crisp on HiDPI;
                // still a fraction of a full-res move per tick.
                var bmp = await ScreenCapture.CaptureAsync(
                    (int)_screenW, (int)_screenH, scaledWidth: (int)(CanvasWidth * 2));
                if (_backdropImage != null) _backdropImage.Source = bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LayoutEditor] capture failed: {ex.Message}");
                _captureTimer?.Stop(); // don't hammer a failing capture path
            }
            finally { _captureInFlight = false; }
        }

        private void OnMetricsChanged(object? sender, OverlayMetrics metrics)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_layoutEditor == null) return;
                _layoutEditor.Metrics = metrics;
                RenderProxies();
            });
        }

        /// <summary>Every model mutation funnels through here: write the
        /// layout (+ legacy dual-write) into settings, save, re-render.
        /// Pass <paramref name="rebuildRail"/> false for continuous
        /// rail-originated gestures (the gap slider fires per tick — a
        /// rebuild would replace the control under the pointer).</summary>
        private void CommitLayoutMutation(bool rebuildRail = true)
        {
            if (_layoutEditor == null) return;
            _settings.Layout = _layoutEditor.ToLayout();
            _settings.LayoutPosition = _layoutEditor.DeriveLegacyPosition();
            _ = SaveAsync();
            RenderProxies();
            // Deferred — mutations can also originate from a discrete
            // rail control the rebuild would tear down mid-callback.
            if (rebuildRail) DispatcherQueue.TryEnqueue(RebuildRail);
        }

        // ── Canvas rendering ─────────────────────────────────────────

        private void RenderAnchorDots(string? highlightAnchor)
        {
            if (_anchorCanvas == null || _layoutEditor == null) return;
            _anchorCanvas.Children.Clear();
            foreach (var (anchor, x, y) in _layoutEditor.AnchorPoints())
            {
                var hot = anchor == highlightAnchor;
                var dot = new Ellipse
                {
                    Width = hot ? 14 : 8,
                    Height = hot ? 14 : 8,
                    Fill = new SolidColorBrush(hot
                        ? Color.FromArgb(0xFF, 0xFF, 0x3B, 0x30)
                        : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                };
                Canvas.SetLeft(dot, x * _canvasScale - dot.Width / 2);
                Canvas.SetTop(dot, y * _canvasScale - dot.Height / 2);
                _anchorCanvas.Children.Add(dot);
            }
        }

        private void RenderProxies()
        {
            if (_proxyCanvas == null || _layoutEditor == null) return;
            _proxyCanvas.Children.Clear();

            foreach (var proxy in _layoutEditor.ComputeProxies())
            {
                var groupId = proxy.Group.Id!;
                var selected = groupId == _selectedGroupId;
                var multi = _groupSelection.Contains(groupId);

                var box = new Border
                {
                    Width = Math.Max(26, proxy.Rect.W * _canvasScale),
                    Height = Math.Max(18, proxy.Rect.H * _canvasScale),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(selected || multi ? 2 : 1),
                    BorderBrush = new SolidColorBrush(
                        multi ? Color.FromArgb(0xFF, 0xFF, 0x9F, 0x0A)
                        : selected ? Color.FromArgb(0xFF, 0xFF, 0x3B, 0x30)
                        : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                    Background = new SolidColorBrush(Color.FromArgb(0x33, 0x40, 0x90, 0xFF)),
                    Tag = groupId,
                };
                Canvas.SetLeft(box, proxy.Rect.X * _canvasScale);
                Canvas.SetTop(box, proxy.Rect.Y * _canvasScale);
                WireGroupPointer(box, groupId);

                // Module chips, positioned relative to the group box.
                var chipHost = new Canvas();
                foreach (var module in proxy.Modules.Where(m => m.Visible))
                {
                    var chip = new Border
                    {
                        Width = Math.Max(22, module.Rect.W * _canvasScale - 2),
                        Height = Math.Max(14, module.Rect.H * _canvasScale - 2),
                        CornerRadius = new CornerRadius(2),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                        Background = new SolidColorBrush(Color.FromArgb(0x2E, 0x00, 0x00, 0x00)),
                        Child = new TextBlock
                        {
                            Text = module.Label,
                            FontSize = 9.5,
                            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextWrapping = TextWrapping.NoWrap,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(2, 0, 2, 0),
                        },
                        Tag = module.Id,
                    };
                    ToolTipService.SetToolTip(chip, $"{module.Label} — drag out to split, onto another group to join");
                    Canvas.SetLeft(chip, (module.Rect.X - proxy.Rect.X) * _canvasScale + 1);
                    Canvas.SetTop(chip, (module.Rect.Y - proxy.Rect.Y) * _canvasScale + 1);
                    WireModulePointer(chip, module.Id, groupId);
                    chipHost.Children.Add(chip);
                }
                box.Child = chipHost;
                _proxyCanvas.Children.Add(box);

                // Name tag above the box (lock glyph when pinned).
                var label = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(5, 1, 5, 2),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = (proxy.Group.Locked == true ? "  " : "") + (proxy.Group.Label ?? groupId),
                        FontSize = 10,
                        FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI"),
                        Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                    },
                };
                Canvas.SetLeft(label, proxy.Rect.X * _canvasScale);
                Canvas.SetTop(label, Math.Max(0, proxy.Rect.Y * _canvasScale - 18));
                _proxyCanvas.Children.Add(label);
            }
        }

        // ── Pointer gestures ─────────────────────────────────────────

        private void WireGroupPointer(Border box, string groupId)
        {
            var dragging = false;
            var moved = false;
            double grabDx = 0, grabDy = 0;   // pointer − box origin, canvas px
            double startX = 0, startY = 0;

            box.PointerPressed += (s, e) =>
            {
                var p = e.GetCurrentPoint(_proxyCanvas).Position;
                var ctrl = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
                if (ctrl)
                {
                    if (!_groupSelection.Remove(groupId)) _groupSelection.Add(groupId);
                    _selectedGroupId = groupId;
                    RenderProxies();
                    DispatcherQueue.TryEnqueue(RebuildRail);
                    e.Handled = true;
                    return;
                }
                dragging = true;
                moved = false;
                startX = p.X; startY = p.Y;
                grabDx = p.X - Canvas.GetLeft(box);
                grabDy = p.Y - Canvas.GetTop(box);
                box.CapturePointer(e.Pointer);
                e.Handled = true;
            };

            box.PointerMoved += (s, e) =>
            {
                if (!dragging || _layoutEditor == null) return;
                var p = e.GetCurrentPoint(_proxyCanvas).Position;
                if (!moved && Math.Abs(p.X - startX) < 4 && Math.Abs(p.Y - startY) < 4) return;
                moved = true;
                Canvas.SetLeft(box, p.X - grabDx);
                Canvas.SetTop(box, p.Y - grabDy);
                RenderAnchorDots(SnapAnchorAtPivot(groupId, box)?.Anchor);
            };

            box.PointerReleased += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                box.ReleasePointerCapture(e.Pointer);
                RenderAnchorDots(null);
                _selectedGroupId = groupId;
                if (moved && _layoutEditor != null)
                {
                    var (px, py) = PivotScreenPoint(groupId, box);
                    _layoutEditor.CommitGroupDrag(groupId, px, py);
                    CommitLayoutMutation();
                }
                else
                {
                    _groupSelection.Clear();
                    RenderProxies();
                    DispatcherQueue.TryEnqueue(RebuildRail);
                }
            };
        }

        /// <summary>The dragged box's anchor-pivot point, screen px.</summary>
        private (double X, double Y) PivotScreenPoint(string groupId, Border box)
        {
            var group = _layoutEditor!.Layout.Groups!.First(g => g.Id == groupId);
            var (pax, pay) = LayoutAnchor.Pivot(
                LayoutAnchor.IsValid(group.Anchor) ? group.Anchor! : LayoutAnchor.TopLeft);
            var x = (Canvas.GetLeft(box) + pax * box.Width) / _canvasScale;
            var y = (Canvas.GetTop(box) + pay * box.Height) / _canvasScale;
            return (x, y);
        }

        private LayoutEditorModel.SnapResult? SnapAnchorAtPivot(string groupId, Border box)
        {
            if (_layoutEditor == null) return null;
            var (px, py) = PivotScreenPoint(groupId, box);
            return _layoutEditor.HitTestSnap(px, py);
        }

        private void WireModulePointer(Border chip, string moduleId, string sourceGroupId)
        {
            var dragging = false;
            var moved = false;
            double startX = 0, startY = 0;
            Border? ghost = null;

            chip.PointerPressed += (s, e) =>
            {
                var p = e.GetCurrentPoint(_proxyCanvas).Position;
                dragging = true;
                moved = false;
                startX = p.X; startY = p.Y;
                chip.CapturePointer(e.Pointer);
                e.Handled = true; // don't start a group drag underneath
            };

            chip.PointerMoved += (s, e) =>
            {
                if (!dragging || _proxyCanvas == null) return;
                var p = e.GetCurrentPoint(_proxyCanvas).Position;
                if (!moved && Math.Abs(p.X - startX) < 5 && Math.Abs(p.Y - startY) < 5) return;
                if (!moved)
                {
                    moved = true;
                    ghost = new Border
                    {
                        Width = chip.Width,
                        Height = chip.Height,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x3B, 0x30)),
                        IsHitTestVisible = false,
                        Child = new TextBlock
                        {
                            Text = moduleId,
                            FontSize = 9.5,
                            Foreground = new SolidColorBrush(Colors.White),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    };
                    _proxyCanvas.Children.Add(ghost);
                    chip.Opacity = 0.35;
                }
                if (ghost != null)
                {
                    Canvas.SetLeft(ghost, p.X - ghost.Width / 2);
                    Canvas.SetTop(ghost, p.Y - ghost.Height / 2);
                }
            };

            chip.PointerReleased += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                chip.ReleasePointerCapture(e.Pointer);
                chip.Opacity = 1;
                if (ghost != null) { _proxyCanvas?.Children.Remove(ghost); ghost = null; }
                if (!moved || _layoutEditor == null) return;

                var p = e.GetCurrentPoint(_proxyCanvas).Position;
                var sx = p.X / _canvasScale;
                var sy = p.Y / _canvasScale;

                var target = _layoutEditor.ComputeProxies()
                    .FirstOrDefault(g => g.Rect.Contains(sx, sy));
                if (target != null && target.Group.Id == sourceGroupId) return; // dropped back home
                if (target != null)
                    _layoutEditor.MoveModuleToGroup(moduleId, target.Group.Id!);
                else
                    _layoutEditor.ExtractModule(moduleId, sx, sy);
                _selectedGroupId = target?.Group.Id;
                CommitLayoutMutation();
            };
        }

        // ── Right rail ───────────────────────────────────────────────

        private void RebuildRail()
        {
            if (_railHost == null || _layoutEditor == null) return;
            _railHost.Children.Clear();

            // Action row: undo / redo / merge / reset.
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var undoBtn = new Button { Content = "Undo", IsEnabled = _layoutEditor.CanUndo, FontSize = 12 };
            undoBtn.Click += (_, __) => { if (_layoutEditor?.Undo() == true) CommitLayoutMutation(); };
            var redoBtn = new Button { Content = "Redo", IsEnabled = _layoutEditor.CanRedo, FontSize = 12 };
            redoBtn.Click += (_, __) => { if (_layoutEditor?.Redo() == true) CommitLayoutMutation(); };
            actions.Children.Add(undoBtn);
            actions.Children.Add(redoBtn);
            if (_groupSelection.Count >= 2)
            {
                var mergeBtn = new Button { Content = $"Group {_groupSelection.Count} selected", FontSize = 12 };
                mergeBtn.Click += (_, __) =>
                {
                    if (_layoutEditor == null) return;
                    _selectedGroupId = _layoutEditor.MergeGroups(_groupSelection.ToList());
                    _groupSelection.Clear();
                    CommitLayoutMutation();
                };
                actions.Children.Add(mergeBtn);
            }
            var resetBtn = new Button { Content = "Reset", FontSize = 12 };
            ToolTipService.SetToolTip(resetBtn, "Restore the default layout (undoable)");
            resetBtn.Click += (_, __) =>
            {
                if (_layoutEditor == null) return;
                _layoutEditor.ResetToDefault();
                _selectedGroupId = null;
                _groupSelection.Clear();
                CommitLayoutMutation();
            };
            actions.Children.Add(resetBtn);
            _railHost.Children.Add(actions);

            if (_layoutEditor.Metrics == null)
            {
                _railHost.Children.Add(new TextBlock
                {
                    Text = "Sizes are estimates until the overlay runs once with this layout.",
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                });
            }

            // Selected-group properties.
            var selected = _selectedGroupId == null
                ? null
                : _layoutEditor.Layout.Groups!.FirstOrDefault(g => g.Id == _selectedGroupId);
            if (selected?.Id != null) _railHost.Children.Add(BuildGroupProps(selected));

            // Module list with visibility eyes.
            _railHost.Children.Add(BuildModuleList());
        }

        private UIElement BuildGroupProps(LayoutGroup group)
        {
            var id = group.Id!;
            var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            panel.Children.Add(MakeRailHeading($"GROUP — {(group.Label ?? id).ToUpperInvariant()}"));

            var nameBox = new TextBox { Text = group.Label ?? id, FontSize = 12 };
            nameBox.LostFocus += (_, __) =>
            {
                if (_layoutEditor == null || nameBox.Text == (group.Label ?? id)) return;
                _layoutEditor.Rename(id, nameBox.Text);
                CommitLayoutMutation();
            };
            panel.Children.Add(nameBox);

            // 9-dot anchor picker.
            var dots = new Grid { ColumnSpacing = 4, RowSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
            for (var i = 0; i < 3; i++)
            {
                dots.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                dots.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var anchorsByCell = new[]
            {
                LayoutAnchor.TopLeft, LayoutAnchor.TopCenter, LayoutAnchor.TopRight,
                LayoutAnchor.MiddleLeft, LayoutAnchor.Center, LayoutAnchor.MiddleRight,
                LayoutAnchor.BottomLeft, LayoutAnchor.BottomCenter, LayoutAnchor.BottomRight,
            };
            for (var i = 0; i < anchorsByCell.Length; i++)
            {
                var anchor = anchorsByCell[i];
                var isCurrent = group.Anchor == anchor;
                var dotBtn = new Button
                {
                    Width = 26,
                    Height = 26,
                    Padding = new Thickness(0),
                    Content = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = new SolidColorBrush(isCurrent
                            ? Color.FromArgb(0xFF, 0xFF, 0x3B, 0x30)
                            : Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    },
                };
                ToolTipService.SetToolTip(dotBtn, anchor);
                dotBtn.Click += (_, __) =>
                {
                    if (_layoutEditor == null) return;
                    _layoutEditor.SetAnchor(id, anchor);
                    CommitLayoutMutation();
                };
                Grid.SetColumn(dotBtn, i % 3);
                Grid.SetRow(dotBtn, i / 3);
                dots.Children.Add(dotBtn);
            }
            var anchorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            anchorRow.Children.Add(dots);

            var lockToggle = new ToggleSwitch
            {
                Header = "Lock to anchor",
                IsOn = group.Locked == true,
                FontSize = 12,
            };
            lockToggle.Toggled += (_, __) =>
            {
                if (_layoutEditor == null || lockToggle.IsOn == (group.Locked == true)) return;
                _layoutEditor.SetLocked(id, lockToggle.IsOn);
                CommitLayoutMutation();
            };
            anchorRow.Children.Add(lockToggle);
            panel.Children.Add(anchorRow);

            var flowPicker = MakeSegmented(
                new[] { ("row", "Row"), ("column", "Column") },
                () => group.Flow ?? "row",
                v => { _layoutEditor?.SetFlow(id, v); CommitLayoutMutation(); });
            panel.Children.Add(flowPicker);

            var gapSlider = MakeSlider("Gap", null, 0, 24, 1,
                () => group.Gap ?? 4,
                v => { _layoutEditor?.SetGap(id, Math.Round(v)); CommitLayoutMutation(rebuildRail: false); },
                "0", "px");
            panel.Children.Add(gapSlider);

            var dissolve = new Button { Content = "Dissolve group", FontSize = 12 };
            ToolTipService.SetToolTip(dissolve, "Members return to their default groups");
            dissolve.Click += (_, __) =>
            {
                if (_layoutEditor == null) return;
                _layoutEditor.DeleteGroup(id);
                _selectedGroupId = null;
                _groupSelection.Clear();
                CommitLayoutMutation();
            };
            panel.Children.Add(dissolve);

            return panel;
        }

        private UIElement BuildModuleList()
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            panel.Children.Add(MakeRailHeading("MODULES"));

            foreach (var module in _layoutEditor!.Registry.Modules!.Where(m => m.Placeable == true))
            {
                var visible = module.ShowKey == null || GetShowKey(module.ShowKey);
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var eye = new Button
                {
                    Padding = new Thickness(4, 2, 4, 2),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Content = new FontIcon
                    {
                        Glyph = "", // eye
                        FontSize = 13,
                        Opacity = visible ? 1.0 : 0.3,
                    },
                };
                ToolTipService.SetToolTip(eye, visible ? "Hide module" : "Show module");
                if (module.ShowKey is string key)
                {
                    eye.Click += (_, __) =>
                    {
                        SetShowKey(key, !GetShowKey(key));
                        _ = SaveAsync();
                        RenderProxies();
                        DispatcherQueue.TryEnqueue(RebuildRail);
                    };
                }
                Grid.SetColumn(eye, 0);
                row.Children.Add(eye);

                var name = new TextBlock
                {
                    Text = module.Label ?? module.Id,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources[
                        visible ? "TextPrimaryBrush" : "TextDimBrush"],
                };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                var groupName = new TextBlock
                {
                    Text = GroupLabelFor(module.Id!),
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                };
                Grid.SetColumn(groupName, 2);
                row.Children.Add(groupName);

                panel.Children.Add(row);
            }
            return panel;
        }

        private string GroupLabelFor(string moduleId)
        {
            var group = _layoutEditor!.Layout.Groups!
                .FirstOrDefault(g => g.Members != null
                    && g.Members.Any(s => s.ModuleIds.Contains(moduleId)));
            return group?.Label ?? group?.Id ?? "—";
        }

        private static TextBlock MakeRailHeading(string text) => new()
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CharacterSpacing = 140,
            Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
            Margin = new Thickness(0, 8, 0, 2),
        };

        // ── show* key plumbing ───────────────────────────────────────
        // The registry names visibility keys as strings; map them onto
        // the typed OverlaySettings properties. A registry key this
        // switch doesn't know yet (newer overlay than host) is treated
        // as always-visible rather than throwing.

        private bool GetShowKey(string key) => key switch
        {
            "showTacho"       => _settings.ShowTacho ?? true,
            "showK10Logo"     => _settings.ShowK10Logo ?? true,
            "showCarLogo"     => _settings.ShowCarLogo ?? true,
            "showGameLogo"    => _settings.ShowGameLogo ?? true,
            "showControls"    => _settings.ShowControls ?? true,
            "showPedals"      => _settings.ShowPedals ?? true,
            "showMaps"        => _settings.ShowMaps ?? true,
            "showTimer"       => _settings.ShowTimer ?? true,
            "showPosition"    => _settings.ShowPosition ?? true,
            "showLeaderboard" => _settings.ShowLeaderboard ?? true,
            "showDatastream"  => _settings.ShowDatastream ?? true,
            "showPitBox"      => _settings.ShowPitBox ?? true,
            "showIncidents"   => _settings.ShowIncidents ?? true,
            "showCommentary"  => _settings.ShowCommentary ?? true,
            "showSpotter"     => _settings.ShowSpotter ?? true,
            _ => true,
        };

        private void SetShowKey(string key, bool value)
        {
            switch (key)
            {
                case "showTacho":       _settings.ShowTacho = value; break;
                case "showK10Logo":     _settings.ShowK10Logo = value; break;
                case "showCarLogo":     _settings.ShowCarLogo = value; break;
                case "showGameLogo":    _settings.ShowGameLogo = value; break;
                case "showControls":    _settings.ShowControls = value; break;
                case "showPedals":      _settings.ShowPedals = value; break;
                case "showMaps":        _settings.ShowMaps = value; break;
                case "showTimer":       _settings.ShowTimer = value; break;
                case "showPosition":    _settings.ShowPosition = value; break;
                case "showLeaderboard": _settings.ShowLeaderboard = value; break;
                case "showDatastream":  _settings.ShowDatastream = value; break;
                case "showPitBox":      _settings.ShowPitBox = value; break;
                case "showIncidents":   _settings.ShowIncidents = value; break;
                case "showCommentary":  _settings.ShowCommentary = value; break;
                case "showSpotter":     _settings.ShowSpotter = value; break;
            }
        }
    }
}
