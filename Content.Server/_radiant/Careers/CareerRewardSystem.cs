using System.Linq;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server._NF.Bank;
using Content.Shared._radiant.Careers;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Careers;

public sealed class CareerRewardSystem : EntitySystem
{
    [Dependency] private readonly CareerSystem _careers = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    private readonly HashSet<Guid> _busy = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<SafetyDepositConsoleComponent, GetVerbsEvent<Verb>>(OnVerbs);
        SubscribeNetworkEvent<CareerClaimRewardsEvent>(OnClaimMessage);
    }

    private bool CanUse(EntityUid console, EntityUid player) => Exists(console) && Exists(player)
        && HasComp<SafetyDepositConsoleComponent>(console) && _power.IsPowered(console)
        && _blocker.CanInteract(player, console)
        && _interaction.InRangeUnobstructed(player, console)
        && _careers.IsWorking(player) && _careers.TryGetCareer(player, out var career)
        && !_busy.Contains(career.CharacterId);

    private void OnVerbs(Entity<SafetyDepositConsoleComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_careers.TryGetCareer(args.User, out var career)
            || !_careers.IsWorking(args.User))
            return;
        var player = args.User;
        if (career.PendingRewards.Count > 0)
            args.Verbs.Add(new Verb { Text = Loc.GetString("career-claim-rewards"), Act = () => Claim(ent, player) });
        if (!career.StarterBoxGranted)
            args.Verbs.Add(new Verb { Text = Loc.GetString("career-starter-box"), Act = () => GrantBox(ent, player) });
        else if (career.BoxTier < UnlockedTier(career) && ent.Comp.BoxSlot.Item != null)
            args.Verbs.Add(new Verb { Text = Loc.GetString("career-upgrade-box"), Act = () => UpgradeBox(ent, player) });
    }

    private void OnClaimMessage(CareerClaimRewardsEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is { } player && TryGetEntity(args.Console, out var console)
            && console != null)
            Claim(console.Value, player);
    }

    private void Claim(EntityUid console, EntityUid player)
    {
        if (!CanUse(console, player) || !_careers.TryGetCareer(player, out var career) ||
            !_prototypes.TryIndex<CareerProfessionPrototype>(career.Profession, out var profession))
            return;
        var levels = career.PendingRewards.Where(index => index > 0 && index <= profession.Levels.Count)
            .Distinct().Select(index => _prototypes.Index(profession.Levels[index - 1])).ToArray();
        if (levels.Length == 0)
            return;
        var money = levels.Sum(level => level.RewardMoney);
        var outstanding = career.PendingRewards.ToArray();
        career.PendingRewards.Clear();
        if (money > 0 && !_bank.TryBankDeposit(player, money))
        {
            career.PendingRewards.AddRange(outstanding);
            return;
        }
        // No permanent claimed-reward ledger: only outstanding grants survive between rounds.
        foreach (var level in levels)
        {
            foreach (var item in level.RewardItems)
            {
                var reward = Spawn(item, Transform(player).Coordinates);
                _hands.TryPickupAnyHand(player, reward);
            }
        }
        _careers.Save(player);
        _popup.PopupEntity(Loc.GetString("career-reward-success"), console, player);
    }

    private int UnlockedTier(CareerData career)
    {
        var tier = 1;
        if (!_prototypes.TryIndex<CareerProfessionPrototype>(career.Profession, out var profession))
            return tier;
        foreach (var level in profession.Levels.Take(career.Level))
            tier = Math.Max(tier, _prototypes.Index(level).BoxTier);
        return Math.Clamp(tier, 1, 3);
    }

    private static string BoxPrototype(int tier) => tier switch
    {
        2 => "SafetyDepositBoxMedium",
        3 => "SafetyDepositBoxLarge",
        _ => "SafetyDepositBoxSmall",
    };

    private async void GrantBox(EntityUid console, EntityUid player)
    {
        if (!CanUse(console, player) || !_careers.TryGetCareer(player, out var career)
            || career.StarterBoxGranted || !TryComp<CareerActorComponent>(player, out var actor))
            return;
        _busy.Add(career.CharacterId);
        try
        {
            var record = await _db.PurchaseSafetyDepositBox(actor.User.UserId, actor.Slot,
                Name(player), BoxPrototype(1));
            if (_careers.TryGetCareer(player, out var current) && current.CharacterId == career.CharacterId)
                career = current;
            career.StarterBoxGranted = true;
            career.BoxTier = 1;
            career.BoxId = record.BoxId;
            _careers.Save(player);
            // If the round ends during the DB operation, leave it deposited for later withdrawal.
            if (!Exists(player) || TerminatingOrDeleted(player))
                return;
            var box = Spawn(BoxPrototype(1), Transform(player).Coordinates);
            SetOwner(box, record.BoxId, actor, Name(player));
            _hands.TryPickupAnyHand(player, box);
            await _db.ClearSafetyDepositBoxItems(record.BoxId, _ticker.RoundId);
            if (Exists(console))
                _popup.PopupEntity(Loc.GetString("career-box-success"), console, player);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed granting career box: {exception}");
        }
        finally
        {
            _busy.Remove(career.CharacterId);
        }
    }

    private async void UpgradeBox(Entity<SafetyDepositConsoleComponent> console, EntityUid player)
    {
        if (!CanUse(console, player) || !_careers.TryGetCareer(player, out var career)
            || !TryComp<CareerActorComponent>(player, out var actor)
            || career.BoxTier >= UnlockedTier(career) || career.BoxId is not { } boxId
            || console.Comp.BoxSlot.Item is not { } oldBox
            || !TryComp<SafetyDepositBoxComponent>(oldBox, out var box)
            || box.BoxId != boxId || box.OwnerId != actor.User.UserId || box.CharacterIndex != actor.Slot
            || !TryComp<StorageComponent>(oldBox, out var storage) || storage.Container.ContainedEntities.Count != 0)
            return;
        _busy.Add(career.CharacterId);
        var tier = UnlockedTier(career);
        try
        {
            if (!await _db.UpgradeCareerSafetyDepositBox(boxId, actor.User.UserId, actor.Slot,
                    BoxPrototype(career.BoxTier), BoxPrototype(tier)))
                return;
            if (_careers.TryGetCareer(player, out var current) && current.CharacterId == career.CharacterId)
                career = current;
            career.BoxTier = tier;
            _careers.Save(player);
            // DB keeps the identity and contents. Never delete an item-filled physical box.
            if (!Exists(oldBox) || TerminatingOrDeleted(oldBox)
                || storage.Container.ContainedEntities.Count != 0)
                return;
            var coordinates = Transform(oldBox).Coordinates;
            // Container coordinates become invalid when the old entity is deleted; use the terminal's tile.
            if (Exists(console.Owner))
                coordinates = Transform(console.Owner).Coordinates;
            var replacement = Spawn(BoxPrototype(tier), coordinates);
            SetOwner(replacement, boxId, actor, box.OwnerName ?? Name(player));
            QueueDel(oldBox);
            _hands.TryPickupAnyHand(player, replacement);
            _popup.PopupEntity(Loc.GetString("career-box-upgraded"), console.Owner, player);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed upgrading career box: {exception}");
        }
        finally
        {
            _busy.Remove(career.CharacterId);
        }
    }

    private void SetOwner(EntityUid uid, Guid id, CareerActorComponent actor, string name)
    {
        var box = Comp<SafetyDepositBoxComponent>(uid);
        box.BoxId = id;
        box.OwnerId = actor.User.UserId;
        box.CharacterIndex = actor.Slot;
        box.OwnerName = name;
        Dirty(uid, box);
    }
}
