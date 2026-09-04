using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared._radiant.ERP;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
namespace Content.Shared._Starlight.Medical.Surgery.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryClampBleedEffectComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryStepAttachLimbEffectComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryStepBleedEffectComponent : Component
{
    [DataField]
    public DamageSpecifier? Damage;
}
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryStepAmputationEffectComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryRemoveAccentComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryClearProgressComponent : Component;

/// <summary>Radiant sector: applies an adult-anatomy operation after a surgery step succeeds.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryStepAdultOrganComponent : Component
{
    [DataField(required: true)]
    public AdultSurgeryOperation Operation;

    [DataField(required: true)]
    public AdultOrganType Organ;
}

public enum AdultSurgeryOperation : byte
{
    Insert,
    Extract,
    EnlargeBreasts,
    ReduceBreasts,
    RemoveLactation,
    DenervatePenis,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryStepEmoteEffectComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<EmotePrototype> Emote = "Scream";
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryStepSpawnEffectComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId Entity;
}
