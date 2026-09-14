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
/// </summary>
public abstract partial class SharedWeaponSerialSystem : EntitySystem
{
    /// <summary>Slot ID used inside ItemSlotsSystem.</summary>
    public const string WeaponSlotId = "WeaponRegistrationConsole-weaponSlot";

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
    /// Examine a weapon: show the serial number (or the line "number wiped" if it
    /// has not been stamped yet) + an origin line when known.
    /// Analogy: a serial-stamp on the barrel — by it you can tell legal vs. unmarked
    /// weapon; if no stamp is present, we explicitly write that it is wiped.
    /// </summary>
    private void OnSerialExamined(Entity<WeaponSerialComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.SerialNumber is { } serial)
            args.PushMarkup(Loc.GetString("weapon-serial-examine", ("serial", serial)));
        else
            args.PushMarkup(Loc.GetString("weapon-serial-wiped"));

        if (ent.Comp.Origin is { } origin)
            args.PushMarkup(Loc.GetString(origin));
    }
}

