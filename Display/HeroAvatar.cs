using System.Windows;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// Rounded hero portrait with an optional accent ring. Drawn directly in OnRender
    /// (no nested shapes or bitmap effects) to keep the overlay cheap to compose.
    /// A hard relationship gets a thicker ring and a corner pip.
    /// </summary>
    public class HeroAvatar : FrameworkElement
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source), typeof(ImageSource), typeof(HeroAvatar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            nameof(Accent), typeof(Brush), typeof(HeroAvatar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsHardProperty = DependencyProperty.Register(
            nameof(IsHard), typeof(bool), typeof(HeroAvatar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DimmedProperty = DependencyProperty.Register(
            nameof(Dimmed), typeof(bool), typeof(HeroAvatar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
            nameof(RingThickness), typeof(double), typeof(HeroAvatar),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CornerRatioProperty = DependencyProperty.Register(
            nameof(CornerRatio), typeof(double), typeof(HeroAvatar),
            new FrameworkPropertyMetadata(0.24, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowPipProperty = DependencyProperty.Register(
            nameof(ShowPip), typeof(bool), typeof(HeroAvatar),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Corner radius as a fraction of the smaller side.</summary>
        public double CornerRatio
        {
            get => (double)GetValue(CornerRatioProperty);
            set => SetValue(CornerRatioProperty, value);
        }

        /// <summary>Whether a hard relationship also gets the corner pip.</summary>
        public bool ShowPip
        {
            get => (bool)GetValue(ShowPipProperty);
            set => SetValue(ShowPipProperty, value);
        }

        public ImageSource? Source
        {
            get => (ImageSource?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public Brush? Accent
        {
            get => (Brush?)GetValue(AccentProperty);
            set => SetValue(AccentProperty, value);
        }

        public bool IsHard
        {
            get => (bool)GetValue(IsHardProperty);
            set => SetValue(IsHardProperty, value);
        }

        public bool Dimmed
        {
            get => (bool)GetValue(DimmedProperty);
            set => SetValue(DimmedProperty, value);
        }

        public double RingThickness
        {
            get => (double)GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        static HeroAvatar()
        {
            SnapsToDevicePixelsProperty.OverrideMetadata(typeof(HeroAvatar), new FrameworkPropertyMetadata(true));
        }

        public HeroAvatar()
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, ImageBrush[]> PortraitBrushes = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Brush, Brush> FadedBrushes = new();

        private static ImageBrush PortraitBrush(ImageSource source, bool dimmed)
        {
            var brushes = PortraitBrushes.GetValue(source, s =>
            {
                ImageBrush Create(double opacity)
                {
                    var brush = new ImageBrush(s) { Stretch = Stretch.Fill, Opacity = opacity };
                    RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                    brush.Freeze();
                    return brush;
                }
                return [Create(1.0), Create(0.4)];
            });
            return brushes[dimmed ? 1 : 0];
        }

        private static Brush WithOpacity(Brush brush, double opacity) => FadedBrushes.GetValue(brush, b =>
        {
            var faded = b.CloneCurrentValue();
            faded.Opacity = opacity;
            faded.Freeze();
            return faded;
        });

        protected override Size MeasureOverride(Size availableSize) => new(
            double.IsNaN(Width) ? 48 : Width,
            double.IsNaN(Height) ? 48 : Height);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            var rect = new Rect(0, 0, w, h);
            double radius = Math.Min(w, h) * CornerRatio;

            dc.DrawRoundedRectangle(HudPalette.AvatarBackground, null, rect, radius, radius);

            // Brush fills instead of PushClip/PushOpacity: the overlay renders in software,
            // where every clip/opacity push rasterizes an intermediate layer per avatar.
            if (Source != null)
            {
                dc.DrawRoundedRectangle(PortraitBrush(Source, Dimmed), null, rect, radius, radius);
            }

            var accent = Accent;
            if (accent == null) return;

            double thickness = IsHard ? RingThickness * 1.75 : RingThickness;
            var ringRect = new Rect(thickness / 2, thickness / 2, w - thickness, h - thickness);
            double ringRadius = Math.Max(0, radius - thickness / 2);

            var ringBrush = IsHard ? accent : WithOpacity(accent, 0.75);
            dc.DrawRoundedRectangle(null, new Pen(ringBrush, thickness), ringRect, ringRadius, ringRadius);

            if (IsHard && ShowPip)
            {
                double pip = Math.Max(5, w * 0.13);
                var center = new Point(w - pip * 0.55, pip * 0.55);
                dc.DrawEllipse(accent, new Pen(HudPalette.AvatarBackground, Math.Max(1.5, pip * 0.35)), center, pip, pip);
            }
        }
    }
}
