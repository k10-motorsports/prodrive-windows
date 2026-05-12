using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using RaceCorProDrive.Services;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// A 16:9 mock-up of the recorded video frame. Stage = "game
    /// capture" (full-bleed). Two overlays sit on top:
    ///   • Facecam — corner + size from RecordingFacecamPos /
    ///     RecordingFacecamSize. Ghosted when no webcam device is
    ///     selected so the user still sees where it WOULD land.
    ///   • HUD badge — corner from LayoutPosition. The in-game HUD is
    ///     captured into the recording, so its anchor affects the
    ///     composed frame; surfacing it here makes corner collisions
    ///     with the facecam visible at a glance.
    /// A status line under the stage echoes Quality · Encoder ·
    /// Format so users can sanity-check the output knobs.
    ///
    /// Stateless wrt settings — callers push the latest snapshot via
    /// <see cref="Apply"/>. No event subscriptions, no save side
    /// effects.
    /// </summary>
    public sealed class VideoPreviewView : Grid
    {
        private const double StageHeight = 220;
        private const double StageAspect = 16.0 / 9.0;
        private const double EdgePadding = 8.0;

        private readonly Border _stage;
        private readonly Border _facecam;
        private readonly TextBlock _facecamLabel;
        private readonly Border _hudBadge;
        private readonly TextBlock _formatLine;

        public VideoPreviewView()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            HorizontalAlignment = HorizontalAlignment.Stretch;

            var stageWidth = StageHeight * StageAspect;

            var bg = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
            };
            bg.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(0xFF, 0x1C, 0x21, 0x2A),
                Offset = 0,
            });
            bg.GradientStops.Add(new GradientStop
            {
                Color = Color.FromArgb(0xFF, 0x0B, 0x0E, 0x14),
                Offset = 1,
            });

            _stage = new Border
            {
                Height = StageHeight,
                Width = stageWidth,
                CornerRadius = new CornerRadius(6),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Background = bg,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var composition = new Grid { Margin = new Thickness(EdgePadding) };

            var gameStub = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4,
            };
            gameStub.Children.Add(new TextBlock
            {
                Text = "GAME CAPTURE",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 180,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            gameStub.Children.Add(new TextBlock
            {
                Text = "Your sim, recorded edge-to-edge.",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            composition.Children.Add(gameStub);

            _hudBadge = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xE5, 0x35, 0x35)),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _hudBadge.Child = new TextBlock
            {
                Text = "HUD",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 120,
                Foreground = new SolidColorBrush(Colors.White),
            };
            composition.Children.Add(_hudBadge);

            _facecam = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2E, 0x36)),
            };
            _facecamLabel = new TextBlock
            {
                Text = "FACECAM",
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 120,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _facecam.Child = _facecamLabel;
            composition.Children.Add(_facecam);

            _stage.Child = composition;
            SetRow(_stage, 0);
            Children.Add(_stage);

            _formatLine = new TextBlock
            {
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 8, 0, 0),
            };
            SetRow(_formatLine, 1);
            Children.Add(_formatLine);
        }

        public void Apply(OverlaySettings s)
        {
            var stageWidth = StageHeight * StageAspect;

            // Facecam dimensions. Three discrete sizes mapped to a
            // fraction of the stage width; height is 16:9 from the
            // chosen width to match a standard webcam aspect.
            var sizeKey = (s.RecordingFacecamSize ?? "small").ToLowerInvariant();
            var sizeFrac = sizeKey switch
            {
                "large" => 0.32,
                "medium" => 0.22,
                _ => 0.15,
            };
            var fcWidth = stageWidth * sizeFrac;
            var fcHeight = fcWidth * 9.0 / 16.0;
            _facecam.Width = fcWidth;
            _facecam.Height = fcHeight;

            // No camera picked? Show where it WOULD land but ghost it
            // so the user understands it isn't active yet.
            var noDevice = string.IsNullOrEmpty(s.RecordingWebcamDevice);
            _facecam.Opacity = noDevice ? 0.35 : 1.0;
            _facecamLabel.Text = noDevice ? "FACECAM (off)" : "FACECAM";

            ApplyAnchor(_facecam, s.RecordingFacecamPos ?? "bottom-right");
            ApplyAnchor(_hudBadge, s.LayoutPosition ?? "top-right");

            var quality = TitleCase(s.RecordingQuality ?? "high");
            var encoder = (s.RecordingEncoder ?? "auto") switch
            {
                "auto" => "Auto encoder",
                "h264" => "H.264",
                "h265" => "H.265",
                "av1" => "AV1",
                var x => x.ToUpperInvariant(),
            };
            var format = (s.RecordingOutputFormat ?? "mp4").ToLowerInvariant();
            _formatLine.Text = $"{quality} · {encoder} · .{format}";
        }

        // Composition Grid carries a uniform 8pt inset, so anchoring an
        // overlay to a corner via Horizontal/Vertical alignment lands it
        // 8pt from the stage edge.
        private static void ApplyAnchor(FrameworkElement el, string pos)
        {
            switch (pos.ToLowerInvariant())
            {
                case "top-left":
                    el.HorizontalAlignment = HorizontalAlignment.Left;
                    el.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case "top-right":
                    el.HorizontalAlignment = HorizontalAlignment.Right;
                    el.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case "bottom-left":
                    el.HorizontalAlignment = HorizontalAlignment.Left;
                    el.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case "bottom-right":
                    el.HorizontalAlignment = HorizontalAlignment.Right;
                    el.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case "centered":
                case "center":
                    el.HorizontalAlignment = HorizontalAlignment.Center;
                    el.VerticalAlignment = VerticalAlignment.Center;
                    break;
                default:
                    el.HorizontalAlignment = HorizontalAlignment.Right;
                    el.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
            }
        }

        private static string TitleCase(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
    }
}
