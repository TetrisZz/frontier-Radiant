using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Events;
using Content.Server.PowerCell;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;

namespace Content.Server.Stunnable.Systems
{
    public sealed class StunbatonSystem : SharedStunbatonSystem
    {
        [Dependency] private readonly RiggableSystem _riggableSystem = default!;
        [Dependency] private readonly SharedPopupSystem _popup = default!;
        [Dependency] private readonly BatterySystem _battery = default!;
        [Dependency] private readonly PowerCellSystem _powerCell = default!; // Radiant sector: support replaceable-cell stun weapons.
        [Dependency] private readonly ItemToggleSystem _itemToggle = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<StunbatonComponent, ExaminedEvent>(OnExamined);
            SubscribeLocalEvent<StunbatonComponent, SolutionContainerChangedEvent>(OnSolutionChange);
            SubscribeLocalEvent<StunbatonComponent, StaminaDamageOnHitAttemptEvent>(OnStaminaHitAttempt);
            SubscribeLocalEvent<StunbatonComponent, ChargeChangedEvent>(OnChargeChanged);
        }

        private void OnStaminaHitAttempt(Entity<StunbatonComponent> entity, ref StaminaDamageOnHitAttemptEvent args)
        {
            if (!_itemToggle.IsActivated(entity.Owner) ||
                !TryGetBattery(entity.Owner, out var batteryUid, out var battery) ||
                !_battery.TryUseCharge(batteryUid, entity.Comp.EnergyPerUse, battery))
            {
                args.Cancelled = true;
            }
        }

        private void OnExamined(Entity<StunbatonComponent> entity, ref ExaminedEvent args)
        {
            var onMsg = _itemToggle.IsActivated(entity.Owner)
            ? Loc.GetString("comp-stunbaton-examined-on")
            : Loc.GetString("comp-stunbaton-examined-off");
            args.PushMarkup(onMsg);

            if (TryGetBattery(entity.Owner, out _, out var battery))
            {
                var count = (int) (battery.CurrentCharge / entity.Comp.EnergyPerUse);
                args.PushMarkup(Loc.GetString("melee-battery-examine", ("color", "yellow"), ("count", count)));
            }
        }

        protected override void TryTurnOn(Entity<StunbatonComponent> entity, ref ItemToggleActivateAttemptEvent args)
        {
            base.TryTurnOn(entity, ref args);

            if (!TryGetBattery(entity.Owner, out _, out var battery) || battery.CurrentCharge < entity.Comp.EnergyPerUse)
            {
                args.Cancelled = true;
                if (args.User != null)
                {
                    _popup.PopupEntity(Loc.GetString("stunbaton-component-low-charge"), (EntityUid) args.User, (EntityUid) args.User);
                }
                return;
            }

            if (TryComp<RiggableComponent>(entity, out var rig) && rig.IsRigged)
            {
                _riggableSystem.Explode(entity.Owner, battery, args.User);
            }
        }

        // https://github.com/space-wizards/space-station-14/pull/17288#discussion_r1241213341
        private void OnSolutionChange(Entity<StunbatonComponent> entity, ref SolutionContainerChangedEvent args)
        {
            // Explode if baton is activated and rigged.
            if (!TryComp<RiggableComponent>(entity, out var riggable) ||
                !TryComp<BatteryComponent>(entity, out var battery))
                return;

            if (_itemToggle.IsActivated(entity.Owner) && riggable.IsRigged)
                _riggableSystem.Explode(entity.Owner, battery);
        }

        private void SendPowerPulse(EntityUid target, EntityUid? user, EntityUid used)
        {
            RaiseLocalEvent(target, new PowerPulseEvent()
            {
                Used = used,
                User = user
            });
        }

        private void OnChargeChanged(Entity<StunbatonComponent> entity, ref ChargeChangedEvent args)
        {
            if (TryComp<BatteryComponent>(entity.Owner, out var battery) &&
                battery.CurrentCharge < entity.Comp.EnergyPerUse)
            {
                _itemToggle.TryDeactivate(entity.Owner, predicted: false);
            }
        }

        // Radiant sector start - cyber stun fists use a normal replaceable power cell,
        // while vanilla stun batons keep their battery directly on the weapon.
        private bool TryGetBattery(EntityUid uid, out EntityUid batteryUid, out BatteryComponent battery)
        {
            if (TryComp<BatteryComponent>(uid, out var directBattery))
            {
                battery = directBattery;
                batteryUid = uid;
                return true;
            }

            if (_powerCell.TryGetBatteryFromSlot(uid, out var cellUid, out var cellBattery)
                && cellUid is { } resolvedCellUid
                && cellBattery is not null)
            {
                batteryUid = resolvedCellUid;
                battery = cellBattery;
                return true;
            }

            batteryUid = default;
            battery = default!;
            return false;
        }
        // Radiant sector end
    }
}
