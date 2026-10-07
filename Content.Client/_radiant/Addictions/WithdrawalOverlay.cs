using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._radiant.Addictions;

public sealed partial class WithdrawalOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly ShaderInstance _shader;
    public float Intensity;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public WithdrawalOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index<ShaderPrototype>("RadiantWithdrawal").InstanceUnique();
        ZIndex = -2; // Leave injury overlays and UI readable.
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Intensity > .001f
            && _entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye)
            && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;
        var level = Math.Clamp(Intensity, 0, 1);
        // Slow breathing-like pulse, not flashes. One cycle every eight seconds.
        var pulse = (1 + MathF.Sin((float) _timing.RealTime.TotalSeconds * MathF.PI / 4)) * .5f;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("darkness", level * (.20f + .05f * pulse));
        _shader.SetParameter("desaturation", Math.Max(0, level - 1f / 3) * .6f);
        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
