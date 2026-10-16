using System.Threading.Tasks;
using Content.Server.Preferences.Managers;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Actions;
using Content.Shared.Ghost;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared._radiant.GhostSkins;
using Content.Shared._radiant.Supporters;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._radiant.GhostSkins;

public sealed class GhostSkinSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly IDependencyCollection _dependencies = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly HashSet<NetUserId> _saving = new();
    private readonly Dictionary<NetUserId, bool> _watchers = new();
    private TimeSpan _nextCheck;
    private readonly SupporterLoadoutEffect _supporterAccess = new();
    private readonly RoleLoadout _supporterRole = new("JobContractor");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhostSkinComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GhostSkinComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GhostSkinComponent, PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<GhostSkinComponent, PreferencesLoadedEvent>(OnPreferencesLoaded);
        SubscribeNetworkEvent<GhostSkinRequestEvent>(OnRequest);
        SubscribeNetworkEvent<GhostSkinSelectEvent>(OnSelect);
        _players.PlayerStatusChanged += OnStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnStatusChanged;
        _watchers.Clear();
        base.Shutdown();
    }

    private void OnMapInit(Entity<GhostSkinComponent> ent, ref MapInitEvent args)
        => _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);

    private void OnShutdown(Entity<GhostSkinComponent> ent, ref ComponentShutdown args)
        => _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);

    private void OnAttached(Entity<GhostSkinComponent> ent, ref PlayerAttachedEvent args)
        => Apply(ent, args.Player);

    private void OnPreferencesLoaded(Entity<GhostSkinComponent> ent, ref PreferencesLoadedEvent args)
        => Apply(ent, args.Session);

    private void OnStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _watchers.Remove(args.Session.UserId);
    }

    private bool TryGhost(ICommonSession session, out Entity<GhostSkinComponent> ghost)
    {
        ghost = default;
        if (session.AttachedEntity is not { } uid || !HasComp<GhostComponent>(uid)
            || !TryComp<GhostSkinComponent>(uid, out var component))
            return false;
        ghost = (uid, component);
        return true;
    }

    private string SavedSkin(ICommonSession session)
        => _preferences.TryGetCachedPreferences(session.UserId, out var preferences)
            ? preferences.GhostSkin : GhostSkinRules.Default;

    private bool HasAccess(ICommonSession session)
    {
        if (!_preferences.TryGetCachedPreferences(session.UserId, out var preferences)
            || preferences.SelectedCharacter is not HumanoidCharacterProfile profile
            || !_playTime.TryGetTrackerTimes(session, out _))
            return false;
        // Match donor loadouts, including manual unlocks while Discord integration is disabled.
        return _supporterAccess.Validate(profile, _supporterRole, session, _dependencies, out _);
    }

    private bool Apply(Entity<GhostSkinComponent> ghost, ICommonSession session)
    {
        if (!HasComp<GhostComponent>(ghost))
            return false;
        var selected = GhostSkinRules.Resolve(_prototypes, SavedSkin(session), HasAccess(session));
        if (ghost.Comp.Skin == selected)
            return false;
        ghost.Comp.Skin = selected;
        Dirty(ghost);
        return true;
    }

    private void SendState(ICommonSession session, string? message = null)
    {
        if (session.Status == SessionStatus.Disconnected || !TryGhost(session, out var ghost))
            return;
        Apply(ghost, session);
        var access = HasAccess(session);
        _watchers[session.UserId] = access;
        RaiseNetworkEvent(new GhostSkinStateEvent(GetNetEntity(ghost), ghost.Comp.Skin,
            SavedSkin(session), access, _saving.Contains(session.UserId), message), session.Channel);
    }

    private void OnRequest(GhostSkinRequestEvent args, EntitySessionEventArgs session)
        => SendState(session.SenderSession);

    private async void OnSelect(GhostSkinSelectEvent args, EntitySessionEventArgs session)
    {
        var player = session.SenderSession;
        if (!TryGhost(player, out var ghost) || GetNetEntity(ghost) != args.Ghost)
            return;
        if (args.Skin == null || args.Skin.Length > 64 || !_prototypes.TryIndex<GhostSkinPrototype>(args.Skin, out var skin)
            || !GhostSkinRules.CanSelect(skin, HasAccess(player)))
        {
            SendState(player, "ghost-skins-unavailable");
            return;
        }
        if (!_saving.Add(player.UserId))
        {
            SendState(player);
            return;
        }

        var saved = false;
        try
        {
            saved = await _preferences.SetGhostSkin(player.UserId, skin.ID);
        }
        catch (Exception error)
        {
            Log.Error($"Failed to save ghost skin for {player.UserId}: {error}");
        }
        finally
        {
            _saving.Remove(player.UserId);
        }
        SendState(player, saved ? null : "ghost-skins-save-failed");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(2);
        var query = EntityQueryEnumerator<GhostSkinComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var skin, out var actor))
        {
            var changed = Apply((uid, skin), actor.PlayerSession);
            if (_watchers.TryGetValue(actor.PlayerSession.UserId, out var access)
                && (changed || access != HasAccess(actor.PlayerSession)))
                SendState(actor.PlayerSession);
        }
    }
}
