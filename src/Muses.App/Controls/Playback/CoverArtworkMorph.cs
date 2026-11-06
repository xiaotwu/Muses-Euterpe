using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Muses.App.Theme;

namespace Muses.App.Controls.Playback;

public sealed class CoverArtworkMorph : Control
{
    private static readonly SineEaseInOut Easing = new();
    private static readonly Geometry Placeholder = Geometry.Parse(
        "M12,3V13.55C11.41,13.21 10.73,13 10,13C7.79,13 6,14.79 6,17C6,19.21 7.79,21 10,21C12.21,21 14,19.21 14,17V7H18V3H12Z");
    private static readonly BoxShadows CoverShadow = BoxShadows.Parse("0 12 35 0 #65000000");
    private Bitmap? _artwork;
    private Rect _source;
    private Rect _frame;
    private double _progress;
    private double _destinationRadius;
    private int _generation;

    internal static Rect? GetFrame(Visual artwork, Visual relativeTo)
    {
        var size = artwork.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || artwork.TranslatePoint(default, relativeTo) is not { } start
            || artwork.TranslatePoint(new Point(size.Width, size.Height), relativeTo) is not { } end)
            return null;

        return new Rect(start, end);
    }

    internal void Prepare(Rect source, Bitmap? artwork)
    {
        _source = _frame = source;
        _artwork = artwork;
        _progress = 0;
        _destinationRadius = 6;
        IsVisible = true;
        InvalidateVisual();
    }

    internal void FlyTo(Rect destination, double cornerRadius, Action complete)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || MotionChrome.MorphDuration == TimeSpan.Zero)
        {
            complete();
            return;
        }

        var generation = ++_generation;
        _destinationRadius = cornerRadius;
        var duration = MotionChrome.MorphDuration;
        var start = Stopwatch.GetTimestamp();
        top.RequestAnimationFrame(Advance);

        void Advance(TimeSpan _)
        {
            if (generation != _generation) return;
            var t = Stopwatch.GetElapsedTime(start) / duration;
            if (t >= 1)
            {
                complete();
                return;
            }

            _progress = Easing.Ease(t);
            _frame = new Rect(Lerp(_source.X, destination.X, _progress), Lerp(_source.Y, destination.Y, _progress),
                Lerp(_source.Width, destination.Width, _progress), Lerp(_source.Height, destination.Height, _progress));
            InvalidateVisual();
            top.RequestAnimationFrame(Advance);
        }
    }

    internal void Clear()
    {
        ++_generation;
        _artwork = null;
        IsVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var radius = Lerp(6, _destinationRadius, _progress);
        var shape = new RoundedRect(_frame, radius);
        context.DrawRectangle(ThemeBrushes.Surface, null, shape);
        if (_progress > 0)
        {
            using var shadow = context.PushOpacity(_progress);
            context.DrawRectangle(Brushes.Transparent, null, shape, CoverShadow);
        }

        using var clip = context.PushClip(shape);
        if (_artwork is { } artwork)
        {
            var side = Math.Min(artwork.Size.Width, artwork.Size.Height);
            var crop = new Rect((artwork.Size.Width - side) / 2, (artwork.Size.Height - side) / 2, side, side);
            context.DrawImage(artwork, crop, _frame);
        }
        else
        {
            var glyphScale = Lerp(16, 64, _progress) / Placeholder.Bounds.Height;
            using var transform = context.PushTransform(Matrix.CreateTranslation(-Placeholder.Bounds.Center)
                * Matrix.CreateScale(glyphScale, glyphScale) * Matrix.CreateTranslation(_frame.Center));
            context.DrawGeometry(ThemeBrushes.WhiteOverlay80, null, Placeholder);
        }
    }

    private static double Lerp(double from, double to, double progress) => from + (to - from) * progress;
}
