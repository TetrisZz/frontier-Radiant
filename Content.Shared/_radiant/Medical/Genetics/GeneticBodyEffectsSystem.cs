using Content.Shared.Electrocution;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Medical.Genetics;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GeneticBodyEffectsComponent : Component
{
    [DataField, AutoNetworkedField] public float UnarmedMultiplier = 1;
    [DataField, AutoNetworkedField] public float Speed = 1;
    [DataField, AutoNetworkedField] public bool Insulated;
    [DataField, AutoNetworkedField] public bool PoorVision;
}

public sealed class GeneticBodyEffectsSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<GeneticBodyEffectsComponent, GetMeleeDamageEvent>(OnDamage);
        SubscribeLocalEvent<GeneticBodyEffectsComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<GeneticBodyEffectsComponent, ElectrocutionAttemptEvent>(OnShock);
        SubscribeLocalEvent<GeneticBodyEffectsComponent, GetBlurEvent>(OnBlur);
    }

    private void OnDamage(Entity<GeneticBodyEffectsComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.Weapon == ent.Owner && args.User == ent.Owner)
            args.Damage *= ent.Comp.UnarmedMultiplier;
    }

    private void OnSpeed(Entity<GeneticBodyEffectsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
        => args.ModifySpeed(ent.Comp.Speed);

    private void OnShock(EntityUid uid, GeneticBodyEffectsComponent comp, ElectrocutionAttemptEvent args)
    {
        if (comp.Insulated) args.SiemensCoefficient = 0;
    }

    private void OnBlur(EntityUid uid, GeneticBodyEffectsComponent comp, GetBlurEvent args)
    {
        if (comp.PoorVision) args.Blur += 3;
    }
}
