using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._radiant.Arousal.Overlays;

/// <summary>
/// Pink circular vignette with pulse; mirrors <see cref="Content.Client.UserInterface.Systems.DamageOverlays.Overlays.DamageOverlay"/> brute-band math.
/// </summary>
public sealed class ArousalTintOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CircleMaskShader = "GradientCircleMask";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _shader;

    public float CurrentIntensity;
    public Color TintColor = Color.FromHex("#ff8fb3");

    public ArousalTintOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(CircleMaskShader).InstanceUnique();
        // Below default damage overlay (ZIndex null = 0) so pain vignette stays readable when injured.
        ZIndex = -1;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (CurrentIntensity <= 0.001f)
            return false;

        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        return args.Viewport.Eye == eyeComp.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var viewport = args.WorldAABB;
        var handle = args.WorldHandle;
        var distance = args.ViewportBounds.Width;
        var time = (float)_timing.RealTime.TotalSeconds;

        var level = Math.Clamp(CurrentIntensity, 0f, 1f);

        var pulseRate = 3f;
        var adjustedTime = time * pulseRate;

        float outerMaxLevel = 2.0f * distance;
        float outerMinLevel = 0.8f * distance;
        float innerMaxLevel = 0.6f * distance;
        float innerMinLevel = 0.2f * distance;

        var outerRadius = outerMaxLevel - level * (outerMaxLevel - outerMinLevel);
        var innerRadius = innerMaxLevel - level * (innerMaxLevel - innerMinLevel);

        var pulse = MathF.Max(0f, MathF.Sin(adjustedTime));

        _shader.SetParameter("time", pulse);
        _shader.SetParameter("color", new Vector3(TintColor.R, TintColor.G, TintColor.B));
        _shader.SetParameter("darknessAlphaOuter", 0.78f);

        _shader.SetParameter("outerCircleRadius", outerRadius);
        _shader.SetParameter("outerCircleMaxRadius", outerRadius + 0.2f * distance);
        _shader.SetParameter("innerCircleRadius", innerRadius);
        _shader.SetParameter("innerCircleMaxRadius", innerRadius + 0.02f * distance);
        handle.UseShader(_shader);
        handle.DrawRect(viewport, Color.White);
        handle.UseShader(null);
    }
}
