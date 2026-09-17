using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared._radiant.WeaponSerial.Components;
using Robust.Shared.Localization;

namespace Content.Shared._radiant.WeaponSerial;

/// <summary>
/// Shared part of the weapon serial-number system: registration-console slot
/// (click-to-insert / click-to-eject) and examine strings.
/// Examine is implemented in shared code: the string is visible to the player
/// on both client and server, so the client does not need a server request
/// to see the weapon serial while it is held in hand.
/// Examine shows the STAMPED NUMBER ONLY. Where the weapon came from (the
/// origin, "personal weapon from the meow uplink" etc.) is registry data: it
/// belongs to the OSK database program, not to the weapon's own description.
/// </summary>
public abstract partial class SharedWeaponSerialSystem : EntitySystem
{
    /// <summary>Slot ID used inside ItemSlotsSystem.</summary>
    public const string WeaponSlotId = "WeaponRegistrationConsole-weaponSlot";

    /// <summary>
    ///     Maximum length of an owner name. The serial number is a database key,
    ///     but the owner is free text typed by a player, so it gets a hard cap:
    ///     it has to fit the registry list row, and a crafted client must not be
    ///     able to push a novel through the network into every PDA.
    ///     Both sides read this single constant: the console input field rejects
    ///     longer edits, and the server clamps the incoming message.
    /// </summary>
    public const int MaxOwnerLength = 128;

    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WeaponRegistrationConsoleComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<WeaponSerialComponent, ExaminedEvent>(OnSerialExamined);
    }

    private void OnComponentInit(EntityUid uid, WeaponRegistrationConsoleComponent component, ComponentInit args)
    {
        // Register the slot so click-to-insert and use-to-eject work.
        _itemSlots.AddItemSlot(uid, WeaponSlotId, component.WeaponSlot);
    }

    /// <summary>
    /// Examine a weapon: show the serial number, or the line "number wiped" when
    /// it has not been stamped yet.
    /// Analogy: a serial-stamp on the barrel — by it you can tell legal vs. unmarked
    /// weapon; if no stamp is present, we explicitly write that it is wiped.
    /// The origin is deliberately NOT printed here: a stamp tells you the number,
    /// the OSK database tells you where the weapon came from.
    /// </summary>
    private void OnSerialExamined(Entity<WeaponSerialComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.SerialNumber is { } serial)
            args.PushMarkup(Loc.GetString("weapon-serial-examine", ("serial", serial)));
        else
            args.PushMarkup(Loc.GetString("weapon-serial-wiped"));
    }
}

