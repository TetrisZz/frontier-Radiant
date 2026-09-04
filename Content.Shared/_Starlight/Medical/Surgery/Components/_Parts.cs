using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
namespace Content.Shared._Starlight.Medical.Surgery.Components;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
[AutoGenerateComponentPause]
public sealed partial class IncisionOpenComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
}

/// <summary>Radiant sector: independently tracks the three torso surgical openings.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgicalCavityStateComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool RibcageOpen;

    [DataField, AutoNetworkedField]
    public bool AbdomenOpen;

    [DataField, AutoNetworkedField]
    public bool GroinOpen;

    public bool IsOpen(SurgicalCavity cavity) => cavity switch
    {
        SurgicalCavity.Ribcage => RibcageOpen,
        SurgicalCavity.Abdomen => AbdomenOpen,
        SurgicalCavity.Groin => GroinOpen,
        _ => false,
    };

    public void SetOpen(SurgicalCavity cavity, bool value)
    {
        switch (cavity)
        {
            case SurgicalCavity.Ribcage:
                RibcageOpen = value;
                break;
            case SurgicalCavity.Abdomen:
                AbdomenOpen = value;
                break;
            case SurgicalCavity.Groin:
                GroinOpen = value;
                break;
        }
    }
}

public enum SurgicalCavity : byte
{
    Ribcage,
    Abdomen,
    Groin,
}

/// <summary>Radiant sector: changes one cavity without affecting the other incisions.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryStepCavityEffectComponent : Component
{
    [DataField(required: true)]
    public SurgicalCavity Cavity;

    [DataField]
    public bool Open = true;
}
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SkinRetractedComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class BleedersClampedComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryStepOrganExtractComponent : Component
{
    [DataField]
    public ComponentRegistry? Organ;

    [DataField]
    public string? Slot;
}

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryStepOrganInsertComponent : Component
{
    [DataField(required: true)]
    public string Slot;
}
