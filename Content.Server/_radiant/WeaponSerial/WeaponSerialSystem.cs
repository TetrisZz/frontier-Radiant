using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Shared._NF.Weapons.Rarity;
using Content.Shared.CartridgeLoader;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._radiant.WeaponSerial;
using Content.Shared._radiant.WeaponSerial.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Random;
using Robust.Shared.Timing;
namespace Content.Server._radiant.WeaponSerial;

/// <summary>
///     Server side of the weapon serial number system.
///     - Weapons from vending machines and uplinks automatically get a serial
///       number via TryAssignSerial. It is called from the sale points.
///     - The database is stored only in server memory and only for the round.
///       It is cleared on RoundRestartCleanupEvent. The full SQL database is untouched.
///     - The registration console automatically registers weapons inserted into it.
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
        SubscribeLocalEvent<WeaponSerialComponent, ExaminedEvent>(OnExamined);

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

        if (ent.Comp.WeaponSlot.Item is not { } weapon)
            return;

        // Registration: serial number + registry entry. RegisterWeapon internally
        // calls RaiseRegistryChanged, so every open PDA gets a fresh list.
        var serial = RegisterWeapon(weapon);
        if (serial == null)
        {
            _popup.PopupEntity(Loc.GetString("weapon-registration-not-weapon"), ent);
            return;
        }

        _popup.PopupEntity(Loc.GetString("weapon-registration-success", ("serial", serial)), ent);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/printer.ogg"), ent);

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
            return;

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
        string? rarity = null;
        string? owner = null;

        if (ent.Comp.WeaponSlot.Item is { } weapon)
        {
            // Name and rarity come from the weapon itself (what the player sees),
            // while serial and owner come from the registry entry (the server is
            // the source of truth).
            weaponName = MetaData(weapon).EntityName;
            rarity = RarityKey(TryComp<RareWeaponComponent>(weapon, out var rare)
                ? rare.Rarity
                : WeaponRarity.Common);

            if (TryComp<WeaponSerialComponent>(weapon, out var serialComp)
                && serialComp.SerialNumber is { } sn
                && _registry.TryGetValue(sn, out var entry))
            {
                serial = entry.SerialNumber;
                owner = entry.Owner;
            }
        }

        _ui.SetUiState(ent.Owner, WeaponRegistrationConsoleUiKey.Key,
            new WeaponRegistrationConsoleState(serial, weaponName, rarity, owner));
    }

    /// <summary>
    ///     The rarity localization key is written in camelCase ("uniqueWrittenoff")
    ///     while the enum is PascalCase, so only the first letter is lowered.
    ///     Same trick as in the PDA fragment (WeaponRegistryUiFragment).
    /// </summary>
    private static string RarityKey(WeaponRarity rarity)
    {
        var name = rarity.ToString();
        return string.Concat(name.Substring(0, 1).ToLowerInvariant(), name.Substring(1));
    }


    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // New round means new database. All entries are wiped. Local only, no SQL.
        _registry.Clear();
    }
    /// <summary>
    ///     Shows the serial number when the weapon is examined, if any.
    /// </summary>
    private void OnExamined(EntityUid uid, WeaponSerialComponent component, ExaminedEvent args)
    {
        if (component.SerialNumber != null)
            args.PushMarkup(Loc.GetString("weapon-serial-examine", ("serial", component.SerialNumber)));
    }

    /// <summary>
    ///     Issues a serial number to the weapon, if it does not have one yet.
    ///     Called from vending machines and uplinks when an item is sold.
    /// </summary>
    public bool TryAssignSerial(EntityUid uid)
    {
        // Only work with firearms, i.e. GunComponent.
        if (!HasComp<GunComponent>(uid))
            return false;
        if (TryComp<WeaponSerialComponent>(uid, out var comp) && comp.SerialNumber != null)
            return false; // already has a number, do not reissue
        comp = EnsureComp<WeaponSerialComponent>(uid);
        comp.SerialNumber = GenerateSerial();
        Dirty(uid, comp);
        return true;

    }

    /// <summary>
    ///     Registers the weapon in the round database. If the weapon has no serial
    ///     number yet, one is issued. Returns the serial number, or null when this
    ///     is not a weapon.


    /// </summary>
    public string? RegisterWeapon(EntityUid weaponUid)
    {
        if (!HasComp<GunComponent>(weaponUid))
            return null;
        var comp = EnsureComp<WeaponSerialComponent>(weaponUid);
        if (comp.SerialNumber == null)
            comp.SerialNumber = GenerateSerial();
        // The number is guaranteed to exist below.



        var serial = comp.SerialNumber!;
        // Write the weapon into the round-scoped local database.



        // Rarity comes from the existing NF system; weapons without it count as Common.
        var rarity = TryComp<RareWeaponComponent>(weaponUid, out var rare)
            ? rare.Rarity
            : WeaponRarity.Common;

        // Re-registering must not wipe an owner name entered by a player earlier.
        _registry[serial] = new WeaponSerialEntry(serial,
            MetaData(weaponUid).EntityPrototype?.ID ?? "unknown",
            MetaData(weaponUid).EntityName,
            _timing.CurTime,
            rarity,
            _registry.TryGetValue(serial, out var old) ? old.Owner : null);
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
            .Select(e => new WeaponRegistryEntry(e.SerialNumber, e.PrototypeId, e.WeaponName, e.Rarity, e.Owner))
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
///     Entry about a registered weapon in the round local database.
/// </summary>
public sealed record WeaponSerialEntry(
    string SerialNumber,
    string PrototypeId,
    string WeaponName,
    TimeSpan RegisteredAt,
    WeaponRarity Rarity = WeaponRarity.Common,
    string? Owner = null);

