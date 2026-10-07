using System.Linq;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Medical.Surgery;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BrainRestorationRecordComponent : Component
{
    [DataField, AutoNetworkedField] public string Species = "";
    [DataField, AutoNetworkedField] public Sex Sex;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryRestoreIdentityComponent : Component
{
    [DataField] public IdentityRestorationStage Stage = IdentityRestorationStage.Appearance;
}

public enum IdentityRestorationStage : byte
{
    Appearance,
    Eyes,
    Voice,
}

public sealed partial class SharedBrainRestorationSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SurgeryRestoreIdentityComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<SurgeryRestoreIdentityComponent, SurgeryCanPerformStepEvent>(OnCanPerform);
    }

    public EntityUid? FindRecord(EntityUid patient)
    {
        foreach (var part in _body.GetBodyChildren(patient))
        foreach (var organ in _body.GetPartOrgans(part.Id, part.Component))
            if (HasComp<BrainRestorationRecordComponent>(organ.Id))
                return organ.Id;
        return null;
    }

    public string? Failure(EntityUid patient)
    {
        if (FindRecord(patient) is not { } brain
            || !TryComp<BrainRestorationRecordComponent>(brain, out var record)
            || !TryComp<HumanoidAppearanceComponent>(patient, out var appearance))
            return "restoration-no-record";
        if (appearance.Species.Id != record.Species)
            return "restoration-wrong-species";
        if (appearance.Sex != record.Sex)
            return "restoration-wrong-sex";
        return null;
    }

    private void OnValid(Entity<SurgeryRestoreIdentityComponent> ent, ref SurgeryValidEvent args)
        => args.Cancelled |= FindRecord(args.Body) == null;

    private void OnCanPerform(Entity<SurgeryRestoreIdentityComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (Failure(args.Body) is not { } reason)
            return;
        args.Invalid = StepInvalidReason.IdentityMismatch;
        args.Popup = Loc.GetString(reason);
    }
}
