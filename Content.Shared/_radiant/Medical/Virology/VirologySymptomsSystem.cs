using Content.Shared.Body.Events;
using Content.Shared.Movement.Systems;

namespace Content.Shared._radiant.Medical.Virology;

public sealed class VirologySymptomsSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<VirologySymptomsComponent, RefreshMovementSpeedModifiersEvent>(OnMovement);
        SubscribeLocalEvent<VirologySymptomsComponent, BloodRegenerationEvent>(OnRegeneration);
    }
    private void OnMovement(Entity<VirologySymptomsComponent> ent, ref RefreshMovementSpeedModifiersEvent args) =>
        args.ModifySpeed(ent.Comp.SpeedMultiplier);
    private void OnRegeneration(Entity<VirologySymptomsComponent> ent, ref BloodRegenerationEvent args) =>
        args.Amount *= ent.Comp.BloodRegenerationMultiplier;
}
