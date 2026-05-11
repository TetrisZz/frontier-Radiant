using Content.Client._radiant.Arousal.Overlays;
using Content.Shared._radiant.Arousal.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._radiant.Arousal.Systems;

public sealed class ArousalOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private ArousalTintOverlay _tint = default!;
    private ArousalHeartsOverlay _hearts = default!;
    private const float RiseLerp = 3.5f;
    private const float FallLerp = 1.6f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArousalComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ArousalComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);

        _tint = new ArousalTintOverlay();
        _hearts = new ArousalHeartsOverlay();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var local = _player.LocalEntity;
        if (local == null || !TryComp<ArousalComponent>(local.Value, out var arousal))
            return;

        var normalized = arousal.MaxArousal > 0f
            ? Math.Clamp(arousal.CurrentArousal / arousal.MaxArousal, 0f, 1f)
            : 0f;

        var target = EvaluateCurve(normalized, arousal.VisualCurve);
        var current = _tint.CurrentIntensity;
        var rate = target > current ? RiseLerp : FallLerp;
        var next = current + (target - current) * Math.Clamp(rate * frameTime, 0f, 1f);

        if (next < 0.005f)
            next = 0f;

        _tint.CurrentIntensity = next;
        _hearts.CurrentIntensity = next;
    }

    private static float EvaluateCurve(float x, ArousalVisualCurve curve)
    {
        return curve switch
        {
            ArousalVisualCurve.Linear => x,
            ArousalVisualCurve.SmoothStep => x * x * (3f - 2f * x),
            ArousalVisualCurve.Pow2 => x * x,
            ArousalVisualCurve.Pow3 => x * x * x,
            _ => x
        };
    }

    private void OnInit(EntityUid uid, ArousalComponent component, ComponentInit args)
    {
        if (_player.LocalEntity == uid)
            TryAddOverlays();
    }

    private void OnShutdown(EntityUid uid, ArousalComponent component, ComponentShutdown args)
    {
        if (_player.LocalEntity != uid)
            return;

        _tint.CurrentIntensity = 0f;
        _hearts.CurrentIntensity = 0f;
        _overlayMan.RemoveOverlay(_tint);
        _overlayMan.RemoveOverlay(_hearts);
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent args)
    {
        if (HasComp<ArousalComponent>(args.Entity))
            TryAddOverlays();
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _tint.CurrentIntensity = 0f;
        _hearts.CurrentIntensity = 0f;
        _overlayMan.RemoveOverlay(_tint);
        _overlayMan.RemoveOverlay(_hearts);
    }

    private void TryAddOverlays()
    {
        if (!_overlayMan.HasOverlay<ArousalTintOverlay>())
            _overlayMan.AddOverlay(_tint);
        if (!_overlayMan.HasOverlay<ArousalHeartsOverlay>())
            _overlayMan.AddOverlay(_hearts);
    }
}
