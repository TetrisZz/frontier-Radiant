using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared._radiant.WeaponSerial.Components;
using Robust.Shared.Localization;

namespace Content.Shared._radiant.WeaponSerial;


/// <summary>
///     Shared-часть системы серийных номеров: слот консоли регистрации
///     (клик-вставка / клик-извлечение) и строки осмотра.
///     Осмотр живёт именно здесь, в shared: строку видит игрок, поэтому её
///     рисуют и клиент, и сервер (клиенту не нужен запрос к серверу, чтобы
///     увидеть номер на оружии в руках).
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
    ///     Осмотр оружия: серийник (или надпись "номер стёрт", если его ещё
    ///     не набили) + строка происхождения, если оно известно.
    ///     Аналогия: клеймо на стволе — по нему видно, легальное ли оружие;
    ///     клейма нет — так и пишем, что стёрт.
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

