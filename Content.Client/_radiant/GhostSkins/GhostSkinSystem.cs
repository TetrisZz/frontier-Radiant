using Content.Client.Lobby;
using Content.Shared.Ghost;
using Content.Shared._radiant.GhostSkins;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._radiant.GhostSkins;

public sealed class GhostSkinSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IClientPreferencesManager _preferences = default!;
    private GhostSkinWindow? _window;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhostSkinComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GhostSkinComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<GhostSkinComponent, GhostSkinActionEvent>(OnAction);
        SubscribeLocalEvent<GhostSkinComponent, LocalPlayerDetachedEvent>(OnDetached);
        SubscribeNetworkEvent<GhostSkinStateEvent>(OnMenuState);
    }

    public override void Shutdown()
    {
        _window?.Close();
        base.Shutdown();
    }

    private void OnStartup(Entity<GhostSkinComponent> ent, ref ComponentStartup args) => Apply(ent);
    private void OnState(Entity<GhostSkinComponent> ent, ref AfterAutoHandleStateEvent args) => Apply(ent);

    private void Apply(Entity<GhostSkinComponent> ent)
    {
        if (!_prototypes.TryIndex<GhostSkinPrototype>(ent.Comp.Skin, out var skin)
            || !TryComp<SpriteComponent>(ent, out var sprite))
            return;
        _sprites.LayerSetSprite((ent, sprite), 0, skin.Sprite);
        _sprites.SetScale((ent, sprite), skin.Scale);
        _sprites.SetColor((ent, sprite), skin.Color);
        if (TryComp<GhostComponent>(ent, out var ghost))
            _sprites.LayerSetColor((ent, sprite), 0, ghost.Color);
    }

    private void OnAction(Entity<GhostSkinComponent> ent, ref GhostSkinActionEvent args)
    {
        if (args.Handled || ent.Owner != _players.LocalEntity || !HasComp<GhostComponent>(ent))
            return;
        args.Handled = true;
        if (_window == null)
        {
            var window = new GhostSkinWindow();
            _window = window;
            window.SelectRequested += OnSelect;
            window.OnClose += () =>
            {
                window.SelectRequested -= OnSelect;
                _window = null;
                window.Dispose();
            };
        }
        _window.SetLoading();
        _window.OpenCentered();
        RaiseNetworkEvent(new GhostSkinRequestEvent());
    }

    private void OnDetached(Entity<GhostSkinComponent> ent, ref LocalPlayerDetachedEvent args) => _window?.Close();

    private void OnSelect(string skin)
    {
        if (_players.LocalEntity is not { } uid || !HasComp<GhostComponent>(uid))
        {
            _window?.Close();
            return;
        }
        RaiseNetworkEvent(new GhostSkinSelectEvent(GetNetEntity(uid), skin));
    }

    private void OnMenuState(GhostSkinStateEvent args, EntitySessionEventArgs session)
    {
        if (_players.LocalEntity is not { } uid || !HasComp<GhostComponent>(uid) || GetNetEntity(uid) != args.Ghost)
            return;
        if (_preferences.Preferences != null)
            _preferences.Preferences.GhostSkin = args.SavedSkin;
        _window?.UpdateState(args);
    }
}
