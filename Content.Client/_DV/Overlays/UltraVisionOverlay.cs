using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Content.Shared.Abilities;
using Content.Shared._radiant.Abilities.Shadowkin; // Radiant Sector
using Content.Shared.Humanoid; // Radiant Sector
using System.Numerics;

namespace Content.Client._DV.Overlays;

public sealed partial class UltraVisionOverlay : Overlay
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] IEntityManager _entityManager = default!;


    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    private readonly ShaderInstance _ultraVisionShader;

    public UltraVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _ultraVisionShader = _prototypeManager.Index<ShaderPrototype>("UltraVision").Instance().Duplicate();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity is not { Valid: true } player
            || !_entityManager.HasComponent<UltraVisionComponent>(player))
        {
            return false;
        }

        return base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null)
            return;

        _ultraVisionShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        // Radiant Sector: shadowkin ultraviolet vision takes the selected eye colour.
        var tint = Color.White;
        if (_playerManager.LocalEntity is { Valid: true } player &&
            _entityManager.HasComponent<ShadowkinUltravisionTintComponent>(player) &&
            _entityManager.TryGetComponent(player, out HumanoidAppearanceComponent? humanoid))
        {
            tint = humanoid.EyeColor;
        }

        _ultraVisionShader.SetParameter("tintColor", new Vector3(tint.R, tint.G, tint.B));

        var worldHandle = args.WorldHandle;
        var viewport = args.WorldBounds;
        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_ultraVisionShader);
        worldHandle.DrawRect(viewport, Color.White);
        worldHandle.UseShader(null); // important - as of writing, construction overlay breaks without this
    }
}
