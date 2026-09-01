using System.Linq;
using Content.Server._radiant.Governor.Components;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Content.Shared._radiant.Governor;
using Content.Shared._radiant.Governor.Components;
using Content.Shared._radiant.Governor.Prototypes;
using Content.Shared.UserInterface;
using Content.Shared.Chemistry.Reagent;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Governor.Systems;

public sealed partial class GovernorSystem : SharedGovernorSystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GovernorBountyConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GovernorBountyConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<GovernorBountyConsoleComponent, GovernorBountyAcceptMessage>(OnAccept);
        SubscribeLocalEvent<GovernorBountyConsoleComponent, GovernorBountySkipMessage>(OnSkip);
        SubscribeLocalEvent<GovernorBountyConsoleComponent, GovernorBountyRedeemMessage>(OnRedeem);
    }

    // Console spawned: fill it with random bounties.
    private void OnMapInit(EntityUid uid, GovernorBountyConsoleComponent component, MapInitEvent args)
    {
        FillBounties(component);
    }

    // Keeps topping up the bounty list until it reaches MaxBounties.
    private void FillBounties(GovernorBountyConsoleComponent component)
    {
        while (component.Bounties.Count < component.MaxBounties)
        {
            if (!TryAddRandomBounty(component))
                break;
        }
    }

    // Picks a random bounty prototype and creates a bounty entry for it.
    private bool TryAddRandomBounty(GovernorBountyConsoleComponent component)
    {
        var allBounties = _proto.EnumeratePrototypes<GovernorBountyPrototype>().ToList();

        // Prototypes that are not already present in the list.
        var available = new List<GovernorBountyPrototype>();
        foreach (var proto in allBounties)
        {
            if (component.Bounties.Any(b => b.Bounty == proto.ID))
                continue;
            available.Add(proto);
        }

        // If everything is taken, fall back to the full list (duplicates allowed).
        var pool = available.Count == 0 ? allBounties : available;
        var bounty = _random.Pick(pool);

        // Random number 0-999 for the GOV-XXX id.
        component.Bounties.Add(new GovernorBountyData(bounty, _random.Next(1000), false));
        return true;
    }
    // Window opened: top up bounties and send the current list to the client.
    private void OnUiOpened(EntityUid uid, GovernorBountyConsoleComponent component, BoundUIOpenedEvent args)
    {
        FillBounties(component);
        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, GovernorBountyConsoleComponent component)
    {
        var untilNextSkip = component.NextSkipTime - _timing.CurTime;
        _ui.SetUiState(uid, GovernorConsoleUiKey.Bounty,
            new GovernorBountyConsoleState(component.Bounties, untilNextSkip));
    }

    // "Accept" button: find the bounty by ID and mark it as accepted.
    private void OnAccept(EntityUid uid, GovernorBountyConsoleComponent component, GovernorBountyAcceptMessage args)
    {
        // Delay between accepting bounties.
        if (_timing.CurTime < component.NextPrintTime)
            return;

        for (var i = 0; i < component.Bounties.Count; i++)
        {
            var bounty = component.Bounties[i];

            if (bounty.Id != args.BountyId)   // not our entry, keep looking
                continue;

            if (bounty.Accepted)              // already accepted, nothing to do
                return;

            component.Bounties[i] = bounty with { Accepted = true };

            // Start the accept cooldown.
            component.NextPrintTime = _timing.CurTime + component.PrintDelay;

            // Spawn a paper manifest describing the accepted bounty.
            if (_proto.TryIndex<GovernorBountyPrototype>(bounty.Bounty, out var acceptedProto))
                SpawnBountyManifest(uid, component, component.Bounties[i], acceptedProto);

            _audio.PlayPvs(component.PrintSound, uid);

            UpdateUi(uid, component);
            return;
        }
    }

    // Spawns a paper manifest describing the accepted bounty, left on the console.
    private void SpawnBountyManifest(EntityUid uid, GovernorBountyConsoleComponent component, GovernorBountyData bounty, GovernorBountyPrototype prototype)
    {
        var paper = SpawnAtPosition(component.BountyLabelId, Transform(uid).Coordinates);
        _meta.SetEntityName(paper, Loc.GetString("governor-bounty-manifest-name", ("id", bounty.Id)));

        if (!TryComp<PaperComponent>(paper, out var paperComp))
            return;

        var tokenName = string.Empty;
        if (_proto.TryIndex<StackPrototype>(prototype.TokenReward, out var tokenProto))
            tokenName = Loc.GetString(tokenProto.Name);

        var msg = new FormattedMessage();
        msg.TryAddMarkup(Loc.GetString("governor-bounty-manifest-header", ("id", bounty.Id)), out var _);
        msg.PushNewline();
        msg.AddText(Loc.GetString("governor-bounty-manifest-list-start"));
        msg.PushNewline();
        foreach (var entry in prototype.Entries)
        {
            // Liquids show units ("150u"), items show count ("3x").
            var line = entry.Reagent != string.Empty
                ? Loc.GetString("governor-bounty-console-manifest-entry-liquid",
                    ("amount", entry.ReagentAmount),
                    ("item", Loc.GetString(entry.Name)))
                : Loc.GetString("governor-bounty-console-manifest-entry",
                    ("amount", entry.Amount),
                    ("item", Loc.GetString(entry.Name)));
            msg.TryAddMarkup($"- {line}", out var _);
            msg.PushNewline();
        }

        msg.TryAddMarkup(Loc.GetString("governor-bounty-manifest-reward-label",
            ("reward", prototype.Reward),
            ("count", prototype.TokenRewardCount),
            ("token", tokenName)), out var _);

        _paper.SetContent((paper, paperComp), msg.ToMarkup());
    }

    // "Skip" button: remove the bounty and replace it with a fresh one.
    private void OnSkip(EntityUid uid, GovernorBountyConsoleComponent component, GovernorBountySkipMessage args)
    {
        // Cannot skip more often than once per SkipDelay.
        if (_timing.CurTime < component.NextSkipTime)
            return;

        // Find the entry and remove it.
        for (var i = 0; i < component.Bounties.Count; i++)
        {
            if (component.Bounties[i].Id != args.BountyId)
                continue;

            component.Bounties.RemoveAt(i);
            break;
        }

        // Start the cooldown and hand out a new bounty.
        component.NextSkipTime = _timing.CurTime + component.SkipDelay;
        FillBounties(component);
        UpdateUi(uid, component);
        _audio.PlayPvs(component.AcceptSound, uid);
    }

    // "Redeem" button: the heart of the system.
    private void OnRedeem(EntityUid uid, GovernorBountyConsoleComponent component, GovernorBountyRedeemMessage args)
    {
        // 1. Grab the container slot by the name stored in the component.
        if (!_container.TryGetContainer(uid, component.ItemContainer, out var container))
        {
            _popup.PopupEntity(Loc.GetString("governor-bounty-redeem-no-container"), args.Actor);
            _audio.PlayPvs(component.DenySound, uid);
            return;
        }

        // 2. The slot must contain an item.
        if (container.ContainedEntities.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("governor-bounty-redeem-empty"), args.Actor);
            _audio.PlayPvs(component.DenySound, uid);
            return;
        }

        var item = container.ContainedEntities[0];

        // 3. Item bounties first: match prototype ID (or prefix) and stack count.
        var itemPrototypeId = MetaData(item).EntityPrototype?.ID;

        if (itemPrototypeId != null)
        {
            for (var i = 0; i < component.Bounties.Count; i++)
            {
                var bounty = component.Bounties[i];
                if (!bounty.Accepted)
                    continue;

                if (!_proto.TryIndex<GovernorBountyPrototype>(bounty.Bounty, out var proto))
                    continue;

                foreach (var entry in proto.Entries)
                {
                    // Liquid entries are handled below; skip them here.
                    if (entry.Reagent != string.Empty)
                        continue;

                    // Stacks match by their stack type; single items match by prototype.
                    var matchId = itemPrototypeId;
                    var itemCount = 1;
                    if (TryComp<StackComponent>(item, out var stack))
                    {
                        itemCount = stack.Count;
                        if (!string.IsNullOrEmpty(stack.StackTypeId))
                            matchId = stack.StackTypeId;
                    }

                    var matches = entry.Prefix != string.Empty
                        ? matchId.StartsWith(entry.Prefix)
                        : matchId == entry.ID;

                    if (!matches)
                        continue;

                    if (itemCount < entry.Amount)
                        continue;

                    // Found it! Pay out the reward, take the item, close the bounty.
                    var stackUid = _stack.Spawn(proto.Reward, "Credit", Transform(uid).Coordinates);
                    if (!_hands.TryPickupAnyHand(args.Actor, stackUid))
                        _transform.SetLocalRotation(stackUid, Angle.Zero);

                    // Token reward defined by the bounty prototype (stack + count).
                    var tokenUid = _stack.Spawn(proto.TokenRewardCount, proto.TokenReward, Transform(uid).Coordinates);
                    if (!_hands.TryPickupAnyHand(args.Actor, tokenUid))
                        _transform.SetLocalRotation(tokenUid, Angle.Zero);

                    Del(item);
                    component.Bounties.RemoveAt(i);
                    FillBounties(component);
                    UpdateUi(uid, component);
                    _audio.PlayPvs(component.AcceptSound, uid);
                    _popup.PopupEntity(Loc.GetString("governor-bounty-redeem-success"), args.Actor);
                    return;
                }
            }

            // Tagged item, but matches no accepted bounty.
            _popup.PopupEntity(Loc.GetString("governor-bounty-redeem-no-match"), args.Actor);
            _audio.PlayPvs(component.DenySound, uid);
            return;
        }

        // 4. No tag: try liquid bounties (the item is a container of liquid).
        if (TryComp<SolutionContainerManagerComponent>(item, out var solutionMan))
        {
            for (var i = 0; i < component.Bounties.Count; i++)
            {
                var bounty = component.Bounties[i];
                if (!bounty.Accepted)
                    continue;

                if (!_proto.TryIndex<GovernorBountyPrototype>(bounty.Bounty, out var proto))
                    continue;

                foreach (var entry in proto.Entries)
                {
                    if (entry.Reagent == string.Empty)
                        continue;

                    // Find a solution that holds enough of the reagent.
                    var reagentId = new ReagentId(entry.Reagent, null);
                    foreach (var solutionName in solutionMan.Containers)
                    {
                        if (!_solutionContainer.TryGetSolution(item, solutionName, out _, out var solution))
                            continue;

                        if (solution.GetReagentQuantity(reagentId) < entry.ReagentAmount)
                            continue;

                        // 4a. Take the required amount of liquid out of the container.
                        solution.RemoveReagent(reagentId, entry.ReagentAmount);

                        // 4b. Pay out the reward.
                        var stackUid = _stack.Spawn(proto.Reward, "Credit", Transform(uid).Coordinates);
                        if (!_hands.TryPickupAnyHand(args.Actor, stackUid))
                            _transform.SetLocalRotation(stackUid, Angle.Zero);

                        // Token reward defined by the bounty prototype (stack + count).
                        var tokenUid = _stack.Spawn(proto.TokenRewardCount, proto.TokenReward, Transform(uid).Coordinates);
                        if (!_hands.TryPickupAnyHand(args.Actor, tokenUid))
                            _transform.SetLocalRotation(tokenUid, Angle.Zero);

                        // 4c. Free the slot and hand the empty container back to the player.
                        _container.RemoveEntity(uid, item);
                        if (!_hands.TryPickupAnyHand(args.Actor, item))
                            _transform.SetLocalRotation(item, Angle.Zero);

                        // 4d. Close the bounty.
                        component.Bounties.RemoveAt(i);
                        FillBounties(component);
                        UpdateUi(uid, component);
                        _audio.PlayPvs(component.AcceptSound, uid);
                        _popup.PopupEntity(Loc.GetString("governor-bounty-redeem-success"), args.Actor);
                        return;
                    }
                }
            }

            // Liquid, but matches no accepted bounty.
            _popup.PopupEntity(Loc.GetString("governor-bounty-redeem-no-match"), args.Actor);
            _audio.PlayPvs(component.DenySound, uid);
            return;
        }

        // 5. The item is neither a tagged item nor a container of liquid.
        _popup.PopupEntity(Loc.GetString("governor-bounty-redeem-no-match"), args.Actor);
        _audio.PlayPvs(component.DenySound, uid);
    }
}
