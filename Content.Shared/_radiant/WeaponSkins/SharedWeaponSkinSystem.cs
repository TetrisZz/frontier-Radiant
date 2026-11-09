using System.Linq;
using Content.Shared.Interaction;
using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Popups;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.WeaponSkins;

public sealed class SharedWeaponSkinSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunComponent, InteractUsingEvent>(OnApply, before: [typeof(ItemSlotsSystem)]);
        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnRefreshGun);
        SubscribeLocalEvent<WeaponSkinAppliedComponent, ExaminedEvent>(OnExamined);
    }

    private void OnApply(Entity<GunComponent> target, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<WeaponSkinApplicatorComponent>(args.Used, out var applicator))
            return;

        // Handle the applicator before ItemSlotsSystem: otherwise an unrelated magazine/chamber
        // slot can reject it and display a misleading "doesn't fit" popup before the skin applies.
        args.Handled = true;
        if (string.IsNullOrEmpty(applicator.Skin.Id)
            || !_prototypes.TryIndex(applicator.Skin, out WeaponSkinPrototype? skin))
            return;

        var targetPrototype = MetaData(target.Owner).EntityPrototype?.ID;
        if (targetPrototype == null || !IsCompatible(skin, targetPrototype))
        {
            _popup.PopupClient(Loc.GetString("weapon-skin-incompatible"), args.User, args.User);
            return;
        }

        if (HasComp<WeaponSkinAppliedComponent>(target.Owner))
        {
            _popup.PopupClient(Loc.GetString("weapon-skin-already-applied"), args.User, args.User);
            return;
        }

        var applied = AddComp<WeaponSkinAppliedComponent>(target.Owner);
        applied.Skin = applicator.Skin;
        Dirty(target.Owner, applied);
        target.Comp.SoundGunshotModified = skin.FireSound;
        DirtyField(target.Owner, target.Comp, nameof(GunComponent.SoundGunshotModified));
        QueueDel(args.Used);
        _popup.PopupClient(Loc.GetString("weapon-skin-applied", ("skin", Loc.GetString(skin.Name))), args.User, args.User);
    }

    private void OnRefreshGun(Entity<GunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!TryComp<WeaponSkinAppliedComponent>(ent, out var applied)
            || string.IsNullOrEmpty(applied.Skin.Id)
            || !_prototypes.TryIndex(applied.Skin, out WeaponSkinPrototype? skin))
            return;

        args.SoundGunshot = skin.FireSound;
    }

    private void OnExamined(Entity<WeaponSkinAppliedComponent> ent, ref ExaminedEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Skin.Id)
            || !_prototypes.TryIndex(ent.Comp.Skin, out WeaponSkinPrototype? skin))
            return;

        args.PushMarkup(Loc.GetString("weapon-skin-examine", ("skin", Loc.GetString(skin.Name))));
    }

    private bool IsCompatible(WeaponSkinPrototype skin, string weaponPrototype)
    {
        return _prototypes.EnumerateParents<EntityPrototype>(weaponPrototype, includeSelf: true)
            .Any(parent => skin.Weapons.Any(weapon => weapon.Id == parent.ID));
    }
}
