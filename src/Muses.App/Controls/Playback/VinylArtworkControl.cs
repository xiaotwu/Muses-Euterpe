using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.VisualTree;
using Muses.App.Theme;

namespace Muses.App.Controls.Playback;

public sealed class VinylArtworkControl : ArtworkControl
{
    private static readonly TimeSpan RevolutionDuration = TimeSpan.FromSeconds(16);
    private static readonly LinearEasing RotationEasing = new();
    private CompositionVisual? _visual;
    private Guid? _trackId;
    private bool _isPlaying;
    private long? _activeSince;
    private double _phase;

    public VinylArtworkControl()
    {
        IsHitTestVisible = false;
        SizeChanged += (_, e) =>
        {
            UpdateGeometry(e.NewSize);
            SynchronizeRotation();
        };
    }

    public void SetPlaybackState(Guid? trackId, bool isPlaying)
    {
        if (_trackId != trackId)
        {
            FreezeRotation();
            _trackId = trackId;
            _phase = 0;
            if (_visual is { } visual)
                visual.RotationAngle = 0;
        }

        _isPlaying = isPlaying;
        SynchronizeRotation();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _visual = ElementComposition.GetElementVisual(this);
        if (_visual is { } visual)
            visual.RotationAngle = (float)_phase;
        UpdateGeometry(Bounds.Size);
        SynchronizeRotation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FreezeRotation();
        _visual = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateGeometry(Size size)
    {
        var side = Math.Min(size.Width, size.Height);
        var x = size.Width / 2;
        var y = size.Height / 2;
        Clip = new EllipseGeometry(new Rect(x - side / 2, y - side / 2, side, side));
        GlyphSize = side * .17;
        if (_visual is { } visual)
            visual.CenterPoint = new Vector3D(x, y, 0);
    }

    private void SynchronizeRotation()
    {
        if (_visual is not { } visual || _trackId is null || !_isPlaying ||
            !IsEffectivelyVisible || MotionChrome.PreferReducedMotion)
        {
            FreezeRotation();
            return;
        }

        if (_activeSince is not null)
            return;

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = RevolutionDuration;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0, (float)_phase);
        animation.InsertKeyFrame(1, (float)(_phase + Math.Tau), RotationEasing);
        _activeSince = Stopwatch.GetTimestamp();
        visual.StartAnimation(nameof(CompositionVisual.RotationAngle), animation);
    }

    private void FreezeRotation()
    {
        if (_activeSince is not { } startedAt || _visual is not { } visual)
            return;

        var revolutions = Stopwatch.GetElapsedTime(startedAt) / RevolutionDuration;
        _phase = (_phase + revolutions * Math.Tau) % Math.Tau;
        _activeSince = null;
        var angle = (float)_phase;
        // Assigning a different direct value cancels both running and pending animations.
        visual.RotationAngle = angle == visual.RotationAngle ? angle + MathF.Tau : angle;
    }
}
