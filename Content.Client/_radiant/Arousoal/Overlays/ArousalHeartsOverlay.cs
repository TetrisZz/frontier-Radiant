using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Arousal.Overlays;

/// <summary> Pet-style floating hearts (EffectHearts sprite) around screen edges and corners. </summary>
public sealed class ArousalHeartsOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Texture _heartTex;

    public float CurrentIntensity;

    public ArousalHeartsOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 7;

        var sprites = _entManager.System<SpriteSystem>();
        _heartTex = sprites.Frame0(new SpriteSpecifier.Rsi(new ResPath("/Textures/Effects/hearts.rsi"), "hearts"));
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return CurrentIntensity > 0.06f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var vp = args.Viewport.Size;
        var time = (float)_timing.RealTime.TotalSeconds;

        var alpha = Math.Clamp(CurrentIntensity * 0.36f, 0f, 0.34f);
        var margin = 24f;

        // anchor: normalized 0–1 (x from left, y from top); second pass repeats the same set in bottom-right quadrant
        DrawHeartDual(handle, vp, time, margin, alpha, 0.08f, 0.32f, 3.2f, 2.4f, 0f, 1.25f);
        DrawHeartDual(handle, vp, time, margin, alpha, 0.92f, 0.30f, 3.0f, 2.6f, 1.1f, 1.2f);
        DrawHeartDual(handle, vp, time, margin, alpha, 0.12f, 0.10f, 2.8f, 3.1f, 0.3f, 1.0f);
        DrawHeartDual(handle, vp, time, margin, alpha, 0.88f, 0.08f, 3.15f, 2.9f, 2.2f, 0.95f);
        DrawHeartDual(handle, vp, time, margin, alpha, 0.10f, 0.88f, 3.05f, 2.7f, 1.5f, 0.92f);
        DrawHeartDual(handle, vp, time, margin, alpha, 0.90f, 0.90f, 2.9f, 3.0f, 0.8f, 0.9f);
        DrawHeartDual(handle, vp, time, margin, alpha, 0.38f, 0.06f, 2.5f, 3.3f, 2.7f, 0.82f);
        DrawHeartDual(handle, vp, time, margin, alpha, 0.62f, 0.94f, 2.6f, 2.8f, 1.9f, 0.85f);
    }

    /// <summary> Same floating heart plus a copy remapped into the bottom-right quadrant (same motion params, offset phase). </summary>
    private void DrawHeartDual(
        DrawingHandleScreen handle,
        Vector2 vp,
        float time,
        float margin,
        float baseAlpha,
        float anchorX,
        float anchorY,
        float freq1,
        float freq2,
        float phase,
        float scaleMul)
    {
        DrawHeart(handle, vp, time, margin, baseAlpha, anchorX, anchorY, freq1, freq2, phase, scaleMul);

        var (brX, brY) = AnchorToBottomRightCluster(anchorX, anchorY);
        DrawHeart(handle, vp, time, margin, baseAlpha, brX, brY, freq1, freq2, phase + MathF.PI * 1.25f, scaleMul);
    }

    /// <summary> Maps a full-screen normalized anchor into a compact strip in the bottom-right corner (keeps layout shape). </summary>
    private static (float BrX, float BrY) AnchorToBottomRightCluster(float anchorX, float anchorY)
    {
        const float lo = 0.71f;
        const float span = 0.24f;
        return (lo + anchorX * span, lo + anchorY * span);
    }

    private void DrawHeart(
        DrawingHandleScreen handle,
        Vector2 vp,
        float time,
        float margin,
        float baseAlpha,
        float anchorX,
        float anchorY,
        float freq1,
        float freq2,
        float phase,
        float scaleMul)
    {
        var wobble = new Vector2(MathF.Sin(time * freq1 + phase) * 10f, MathF.Sin(time * freq2 + phase * 1.3f) * 12f);
        var pulse = MathF.Sin(time * freq1 + phase);
        var scale = (1.05f + pulse * 0.06f * CurrentIntensity) * scaleMul * 1.15f;
        var size = _heartTex.Size * scale;

        var px = anchorX * vp.X + wobble.X;
        var py = anchorY * vp.Y + wobble.Y;
        px = Math.Clamp(px, margin, vp.X - margin - size.X);
        py = Math.Clamp(py, margin, vp.Y - margin - size.Y);

        var color = Color.White.WithAlpha(baseAlpha * (0.82f + 0.18f * MathF.Abs(pulse)));
        handle.DrawTextureRect(_heartTex, UIBox2.FromDimensions(new Vector2(px, py), size), color);
    }
}
