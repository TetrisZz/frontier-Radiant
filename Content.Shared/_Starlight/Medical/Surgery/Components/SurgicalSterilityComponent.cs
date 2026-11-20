using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Medical.Surgery.Components;

[Serializable, NetSerializable]
public enum SurgicalSite : byte { Surface, Ribcage, Abdomen, Groin }

/// <summary>Persists on the actual body part, including after incision closure or amputation.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgicalSterilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<SurgicalSite, int> Contamination = new();

    /// <summary>Independent from contamination: washing does not instantly cure established infection.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<SurgicalSite, int> Infection = new();

    [DataField]
    public Dictionary<SurgicalSite, int> RecoveryCredit = new();

    [DataField] public Dictionary<SurgicalSite, int> ExposureCredit = new();
    [DataField] public Dictionary<SurgicalSite, int> CleanupCredit = new();

    [DataField] public Dictionary<SurgicalSite, float> SevereSeconds = new();
    [DataField, AutoNetworkedField] public Dictionary<SurgicalSite, float> NecrosisSeconds = new();

    [DataField, AutoNetworkedField] public HashSet<SurgicalSite> Draped = new();
    [DataField, AutoNetworkedField] public HashSet<SurgicalSite> Lavaged = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgicalItemSterilityComponent : Component
{
    /// <summary>Built-in instruments that sterilize themselves between contacts.</summary>
    [DataField, AutoNetworkedField] public bool SelfSterilizing;
    [DataField, AutoNetworkedField] public bool Dirty;
    [DataField, AutoNetworkedField] public bool Used;
    [DataField, AutoNetworkedField] public int UsesSinceCleaning;
    [DataField, AutoNetworkedField] public int ContainerContamination;
    // Patient identity is deliberately not exposed through the item's network state.
    [DataField] public EntityUid? LastPatient;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalProtectionComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalOuterwearComponent : Component;

/// <summary>Preserves cleanliness; never disinfects items placed inside.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SterileSurgicalStorageComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class NonContactSurgicalToolComponent : Component;

[Serializable, NetSerializable]
public enum SurgicalTreatment : byte { Drape, RemoveDrape, Lavage, Debride, RestoreHeart }

/// <summary>Travels with the actual organ, never with its former slot or patient.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgicalOrganNecrosisComponent : Component
{
    [DataField, AutoNetworkedField] public float Seconds;
    [DataField, AutoNetworkedField] public bool Dead;
    [DataField] public bool NamedAsDead;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalDeadLimbComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgerySiteTreatmentComponent : Component
{
    [DataField] public SurgicalSite Site;
    [DataField] public SurgicalTreatment Treatment;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalDrapeComponent : Component, ISurgeryToolComponent
{
    public string ToolName => "surgical-tool-drape";
    public string ToolType => "SurgicalDrape";
    public float Speed => 1;
    public float SuccessRate => 1;
    public SoundSpecifier? StartSound => null;
    public SoundSpecifier? EndSound => null;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalLavageComponent : Component, ISurgeryToolComponent
{
    public string ToolName => "surgical-tool-lavage";
    public string ToolType => "SurgicalLavage";
    public float Speed => 1;
    public float SuccessRate => 1;
    public SoundSpecifier? StartSound => null;
    public SoundSpecifier? EndSound => null;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalAntisepticComponent : Component, ISurgeryToolComponent
{
    public string ToolName => "surgical-tool-antiseptic";
    public string ToolType => "SurgicalAntiseptic";
    public float Speed => 1f;
    public float SuccessRate => 1f;
    public SoundSpecifier? StartSound => null;
    public SoundSpecifier? EndSound => null;
}

/// <summary>Explicit site for a preparation operation; does not require the cavity to be open.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryDisinfectionComponent : Component
{
    [DataField] public SurgicalSite Site;
}

[Serializable, NetSerializable]
public sealed partial class SurgicalDisinfectionDoAfterEvent : SimpleDoAfterEvent;
