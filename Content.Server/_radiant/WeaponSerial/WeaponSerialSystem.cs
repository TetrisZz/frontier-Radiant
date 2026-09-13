using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Shared._NF.Weapons.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._radiant.WeaponSerial;
using Content.Shared._radiant.WeaponSerial.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Random;
using Robust.Shared.Timing;
namespace Content.Server._radiant.WeaponSerial;

/// <summary>
///     Server side of the weapon serial number system.
///     - Vendors carrying GiveSerialNumberComponent stamp a serial number
///       (plus an origin note) onto bought weapons via RegisterWeapon.
///     - The database is stored only in server memory and only for the round.
///       It is cleared on RoundRestartCleanupEvent. The full SQL database is untouched.
///     - The registration console: the number is stamped by a one-time
///       button, the owner is rewritten by the "change owner" button.
/// </summary>
public sealed partial class WeaponSerialSystem : SharedWeaponSerialSystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    // Round-scoped database. Serial number -> entry about the registered weapon.
    private readonly Dictionary<string, WeaponSerialEntry> _registry = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        // Every firearm gets the serial component when its Gun component starts.
        // The _NF base gun prototype already declares it, but vanilla (upstream)
        // guns are not parented to those bases — this fallback covers them, so
        // the "wiped" examine note exists on every gun, no exceptions.
        // The directed pair (GunComponent, ComponentStartup) is free; MapInit
        // on GunComponent is already taken by SharedGunSystem (the directed bus
        // allows only ONE handler per component/event pair), and ComponentInit
        // is taken by GunSystem and GunUpgradeSystem.
        SubscribeLocalEvent<GunComponent, ComponentStartup>(OnGunStartup);

        // OSK weapon registry cartridge (a standalone program, separate from the
        // wanted list cartridge). Subscribed through its own component: the
        // directed event bus allows a single handler per (component, event) pair,
        // and CriminalRecordsSystem already owns
        // (WantedListCartridgeComponent, CartridgeUiReadyEvent).
        //
        // The push flow mirrors CriminalRecordsSystem/WantedListCartridge exactly:
        // - every registry change raises WeaponRegistryChangedEvent on every OSK
        //   cartridge entity; the handler rebuilds the FULL list from _registry
        //   and pushes it through UpdateCartridgeUiState (only a HasUi check
        //   inside, no ActiveProgram/IsUiOpen guards that silently dropped pushes),
        // - CartridgeUiReadyEvent (the program window attached its fragment) pushes
        //   the snapshot again, so a freshly opened PDA always shows current data.
        SubscribeLocalEvent<WeaponRegistryCartridgeComponent, CartridgeUiReadyEvent>(OnCartridgeUiReady);
        SubscribeLocalEvent<WeaponRegistryCartridgeComponent, WeaponRegistryChangedEvent>(OnRegistryChanged);
        SubscribeLocalEvent<WeaponRegistryCartridgeComponent, WeaponRegistryUiMessageEvent>(OnRegistryMessage);

        // Console window: open -> show the summary; slot changed -> refresh the
        // window; the "Save" button -> write the owner into the registry.
        Subs.BuiEvents<WeaponRegistrationConsoleComponent>(WeaponRegistrationConsoleUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>(OnConsoleUiOpened);
                subs.Event<WeaponRegistrationStampSerial>(OnConsoleStampSerial);
                subs.Event<WeaponRegistrationSetOwner>(OnConsoleSetOwner);
        });
        SubscribeLocalEvent<WeaponRegistrationConsoleComponent, EntInsertedIntoContainerMessage>(OnConsoleSlotInserted);
        SubscribeLocalEvent<WeaponRegistrationConsoleComponent, EntRemovedFromContainerMessage>(OnConsoleSlotRemoved);
    }

    /// <summary>
    ///     Console window just opened — show the summary of what is already in
    ///     the slot. Like a clerk's counter: you see the weapon's card as it is.
    /// </summary>
    private void OnConsoleUiOpened(Entity<WeaponRegistrationConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateConsoleUi(ent);
    }

    /// <summary>
    ///     A weapon was inserted — register it first (the old OnWeaponInserted
    ///     flow), then refresh the open console window.
    /// </summary>
    private void OnConsoleSlotInserted(Entity<WeaponRegistrationConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (ent.Comp.WeaponSlot.ID != args.Container.ID)
            return;

        if (ent.Comp.WeaponSlot.Item == null)
            return;

        // The number is NOT stamped automatically on insert anymore: stamping
        // happens only via the one-time button (OnConsoleStampSerial).
        // Here we just refresh the summary.
        UpdateConsoleUi(ent);
    }

    /// <summary>
    ///     Weapon removed — the summary empties, showing the "insert a weapon" hint.
    /// </summary>
    private void OnConsoleSlotRemoved(Entity<WeaponRegistrationConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (ent.Comp.WeaponSlot.ID != args.Container.ID)
            return;

        UpdateConsoleUi(ent);
    }

    /// <summary>
    ///     The "Save" button in the console: writes the owner into the registry
    ///     entry of the weapon CURRENTLY in the slot. The serial is NOT taken
    ///     from the client — the server reads the slot itself; otherwise anyone
    ///     could claim a foreign serial number.
    /// </summary>
    private void OnConsoleSetOwner(Entity<WeaponRegistrationConsoleComponent> ent, ref WeaponRegistrationSetOwner msg)
    {
        var weapon = ent.Comp.WeaponSlot.Item;
        if (weapon == null)
            return;

        if (!TryComp<WeaponSerialComponent>(weapon.Value, out var serialComp)
            || string.IsNullOrWhiteSpace(serialComp.SerialNumber))
            return;

        if (!_registry.TryGetValue(serialComp.SerialNumber, out var entry))
        {
            // The weapon has a number, but there is no registry entry for it:
            // nothing to rewrite. E.g. a number that was not issued this round.
            _popup.PopupEntity(Loc.GetString("weapon-registration-serial-not-found"), ent, msg.Actor);
            return;
        }

        var owner = string.IsNullOrWhiteSpace(msg.Owner) ? null : msg.Owner.Trim();
        _registry[entry.SerialNumber] = entry with { Owner = owner };

        // Refresh the console window (summary) and all open PDAs (list).
        UpdateConsoleUi(ent);
        RaiseRegistryChanged();

        _popup.PopupEntity(
            Loc.GetString(owner == null ? "weapon-registration-owner-cleared" : "weapon-registration-owner-saved", ("owner", owner ?? string.Empty)),
            ent,
            msg.Actor);
    }

    /// <summary>
    ///     Summary of the weapon in the slot -> into the console window. Empty
    ///     slot means all fields are null and the window shows the "insert a
    ///     weapon" hint.
    /// </summary>
    private void UpdateConsoleUi(Entity<WeaponRegistrationConsoleComponent> ent)
    {
        string? serial = null;
        string? weaponName = null;
        string? weaponClass = null;
        string? origin = null;
        string? owner = null;

        if (ent.Comp.WeaponSlot.Item is { } weapon)
        {
            // Name and class come from the weapon itself (what the player sees),
            // serial/origin/owner — from the registry entry (the server is the
            // source of truth). If there is no entry yet, the origin stamped on
            // the weapon by its issuing vendor is still shown.
            weaponName = MetaData(weapon).EntityName;
            weaponClass = TryComp<NFWeaponDetailsComponent>(weapon, out var details)
                ? details.Class
                : null;

            if (TryComp<WeaponSerialComponent>(weapon, out var serialComp)
                && serialComp.SerialNumber is { } sn)
            {
                serial = sn;
                if (_registry.TryGetValue(sn, out var entry))
                {
                    origin = entry.Origin ?? serialComp.Origin;
                    owner = entry.Owner;
                }
                else
                {
                    origin = serialComp.Origin;
                }
            }
        }

        _ui.SetUiState(ent.Owner, WeaponRegistrationConsoleUiKey.Key,
            new WeaponRegistrationConsoleState(serial, weaponName, weaponClass, origin, owner));
    }


    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // New round means new database. All entries are wiped. Local only, no SQL.
        _registry.Clear();
    }
    /// <summary>
    ///     Fallback: every firearm gets the serial component when its Gun
    ///     component starts up — see Initialize for why MapInit/ComponentInit
    ///     are unavailable for (GunComponent, ...).
    /// </summary>
    private void OnGunStartup(EntityUid uid, GunComponent component, ComponentStartup args)
        => EnsureComp<WeaponSerialComponent>(uid);

    /// <summary>
    ///     The one-time "stamp the number and enter it into the database" button.
    ///     The console does NOT stamp an origin note: the origin belongs to the
    ///     vendor that sold the weapon.
    /// </summary>
    private void OnConsoleStampSerial(Entity<WeaponRegistrationConsoleComponent> ent, ref WeaponRegistrationStampSerial msg)
    {
        if (ent.Comp.WeaponSlot.Item is not { } weapon)
            return;

        // One-shot guarantee: a weapon that already has a number is ignored.
        // The button is hidden on the client, but the message itself could be
        // crafted, so the server checks the weapon, not the UI. A popup is
        // still shown so a stale client (button visible while the server
        // already stamped the number) does not look like a silent failure.
        if (TryComp<WeaponSerialComponent>(weapon, out var existing) && existing.SerialNumber != null)
        {
            _popup.PopupEntity(Loc.GetString("weapon-registration-already-stamped"), ent);
            return;
        }

        var serial = RegisterWeapon(weapon);
        if (serial == null)
        {
            _popup.PopupEntity(Loc.GetString("weapon-registration-not-weapon"), ent);
            return;
        }

        _popup.PopupEntity(Loc.GetString("weapon-registration-stamp-success", ("serial", serial)), ent);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/printer.ogg"), ent);

        UpdateConsoleUi(ent);
    }

    /// <summary>
    ///     Registers the weapon in the round database. If the weapon has no serial
    ///     number yet, one is issued. Returns the serial number, or null when this
    ///     is not a weapon.


    /// </summary>
    public string? RegisterWeapon(EntityUid weaponUid, LocId? origin = null)
    {
        if (!HasComp<GunComponent>(weaponUid))
            return null;
        var comp = EnsureComp<WeaponSerialComponent>(weaponUid);
        if (comp.SerialNumber == null)
            comp.SerialNumber = GenerateSerial();
        // The number is guaranteed to exist below.
        var serial = comp.SerialNumber!;

        // The origin note is stamped together with the number by the issuing
        // vendor; re-registration must not wipe a note stamped earlier.
        if (origin != null)
            comp.Origin ??= origin;

        // Write the weapon into the round-scoped local database. Re-registering
        // must not wipe an owner name entered by a player earlier, nor the origin.
        var meta = MetaData(weaponUid);
        if (_registry.TryGetValue(serial, out var old))
        {
            _registry[serial] = new WeaponSerialEntry(serial,
                meta.EntityPrototype?.ID ?? "unknown",
                meta.EntityName,
                _timing.CurTime,
                old.Origin ?? comp.Origin,
                old.Owner);
        }
        else
        {
            _registry[serial] = new WeaponSerialEntry(serial,
                meta.EntityPrototype?.ID ?? "unknown",
                meta.EntityName,
                _timing.CurTime,
                comp.Origin,
                null);
        }
        Dirty(weaponUid, comp);

        // Push the fresh registry to every OSK database cartridge so the list updates live.
        RaiseRegistryChanged();

        return serial;
    }
    /// <summary>
    ///     The OSK database cartridge UI is ready: send it the current registry.
    ///     Mirrors CriminalRecordsSystem.OnCartridgeUiReady — no extra checks,
    ///     UpdateReaderUi itself resolves and validates the loader.
    /// </summary>
    private void OnCartridgeUiReady(Entity<WeaponRegistryCartridgeComponent> ent, ref CartridgeUiReadyEvent args) =>
        UpdateReaderUi(ent, args.Loader);

    /// <summary>
    ///     Сообщение от КПК-картриджа. КПК read-only: единственное, что он может
    ///     попросить — свежий снепшот реестра (пустой serial = "пришли список").
    ///     Владелец из клиентского сообщения сознательно НЕ принимается: клиент
    ///     может прислать что угодно. Владельца меняет только консоль регистрации
    ///     (OnConsoleSetOwner), которая берёт серийник со слота, а не из сети.
    /// </summary>
    private void OnRegistryMessage(EntityUid uid, WeaponRegistryCartridgeComponent component, WeaponRegistryUiMessageEvent args)
    {
        // Всё, кроме запроса на обновление, игнорируем — реестр правится только в консоли.
        if (!string.IsNullOrWhiteSpace(args.Serial))
            return;

        // Картридж знает свой загрузчик (тот же источник, что и StateChanged);
        // LoaderUid из сообщения — запасной вариант.
        if (TryComp<CartridgeComponent>(uid, out var cartridge) && cartridge.LoaderUid is { } loaderUid)
            UpdateReaderUi(uid, loaderUid);
        else
            UpdateReaderUi(uid, GetEntity(args.LoaderUid));
    }

    private List<WeaponRegistryEntry> BuildRegistrySnapshot()
    {
        return _registry.Values
            .OrderBy(e => e.SerialNumber, StringComparer.Ordinal)
            .Select(e => new WeaponRegistryEntry(e.SerialNumber, e.PrototypeId, e.WeaponName, e.Origin, e.Owner))
            .ToList();
    }

    /// <summary>
    ///     Mirrors CriminalRecordsSystem.StateChanged: resolves the loader this
    ///     cartridge is attached to and pushes a fresh snapshot to its window.
    /// </summary>
    private void StateChanged(Entity<WeaponRegistryCartridgeComponent> ent)
    {
        // The loader is known from activation/insertion (CartridgeLoaderSystem
        // sets CartridgeComponent.LoaderUid) — the same source the wanted list uses.
        if (Comp<CartridgeComponent>(ent).LoaderUid is not { } loaderUid)
            return;

        UpdateReaderUi(ent, loaderUid);
    }

    private void OnRegistryChanged(Entity<WeaponRegistryCartridgeComponent> ent, ref WeaponRegistryChangedEvent args) =>
        StateChanged(ent);

    /// <summary>
    ///     Rebuilds the FULL registry list from _registry and pushes it to the
    ///     loader window. The fragment UI lives inside the loader/PDA window: the
    ///     client's CartridgeLoaderBoundUserInterface forwards any
    ///     non-CartridgeLoaderUiState it receives on the loader's UiKey to the
    ///     active program fragment. UpdateCartridgeUiState itself validates that
    ///     the window is open — exactly the way the wanted list updates its UI,
    ///     without the old ActiveProgram/IsUiOpen guards that silently dropped
    ///     pushes and left the list stale until a weapon was re-registered.
    /// </summary>
    private void UpdateReaderUi(EntityUid cartridgeUid, EntityUid loaderUid) =>
        _cartridge.UpdateCartridgeUiState(loaderUid, new WeaponRegistryUiState(BuildRegistrySnapshot()));

    /// <summary>
    ///     Raises WeaponRegistryChangedEvent on every OSK database cartridge,
    ///     mirroring how CriminalRecordsSystem notifies wanted-list cartridges
    ///     (OverwriteStatus → query loop → RaiseLocalEvent).
    /// </summary>
    private void RaiseRegistryChanged()
    {
        var ev = new WeaponRegistryChangedEvent();
        var query = EntityQueryEnumerator<WeaponRegistryCartridgeComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RaiseLocalEvent(uid, ref ev);
        }
    }

    /// <summary>
    ///     Generates a serial number in the CON-1234-5678 format.
    /// </summary>
    private string GenerateSerial()
    {
        // Uniqueness is guaranteed against the registry so one serial can never
        // point at two weapons (that would silently overwrite another entry).
        string serial;
        do
        {
            serial = $"CON-{_random.Next(0, 10000):D4}-{_random.Next(0, 10000):D4}";
        }
        while (_registry.ContainsKey(serial));

        return serial;
    }
}

/// <summary>
///     Raised on every OSK database cartridge when the round registry changes,
///     mirroring CriminalRecordChangedEvent for the wanted list cartridge.
/// </summary>
[ByRefEvent]
public record struct WeaponRegistryChangedEvent;

/// <summary>
///     Entry about a registered weapon in the round local database. The origin
///     (fluent id) replaced the old rarity field: where the weapon came from is
///     what the registry is actually about.
/// </summary>
public sealed record WeaponSerialEntry(
    string SerialNumber,
    string PrototypeId,
    string WeaponName,
    TimeSpan RegisteredAt,
    string? Origin = null,
    string? Owner = null);

