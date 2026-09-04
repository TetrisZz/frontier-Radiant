using Content.Shared._Starlight.Medical.Body.Part;
using Content.Shared.Body.Part;
using Content.Shared.Humanoid.Prototypes;
using BodyPartType = Content.Shared.Body.Part.BodyPartType;
using Content.Shared.Item;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared._radiant.ERP;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
namespace Content.Shared._Starlight.Medical.Surgery.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryAnyAccentConditionComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryAnyLimbSlotConditionComponent : Component;
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))] public sealed partial class SurgeryOperatingTableConditionComponent : Component;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryLimbSlotConditionComponent : Component
{
    [DataField]
    public string Slot;
}

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryItemSizeConditionComponent : Component
{
    [DataField]
    public ProtoId<ItemSizePrototype> Size = "Small";
}

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryPartConditionComponent : Component
{
    [DataField]
    public List<BodyPartType> Parts = [];
}

/// <summary>Radiant sector: requires one particular torso cavity to be open or closed.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryCavityConditionComponent : Component
{
    [DataField(required: true)]
    public SurgicalCavity Cavity;

    [DataField]
    public bool Open = true;
}
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgerySpeciesConditionComponent : Component
{
    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>> SpeciesBlacklist = [];

    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>> SpeciesWhitelist = [];
}
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryOrganExistConditionComponent : Component
{
    [DataField]
    public ComponentRegistry? Organ;

    [DataField]
    public string? Container;
}
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryHasCompConditionComponent : Component
{
    [DataField]
    public ComponentRegistry? Component;
}
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryOrganDontExistConditionComponent : Component
{
    [DataField]
    public ComponentRegistry? Organ;

    [DataField]
    public string? Container;
}

/// <summary>Radiant sector: hides and rejects adult surgery when the patient opted out of ERP.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryErpConsentConditionComponent : Component;

/// <summary>Radiant sector: checks the patient's actual surgically mutable ERP anatomy.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryAdultOrganConditionComponent : Component
{
    [DataField(required: true)]
    public AdultOrganType Organ;

    [DataField]
    public bool Present = true;
}

/// <summary>Radiant sector: prevents breast-resize operations from exceeding the supported range.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryAdultBreastSizeConditionComponent : Component
{
    [DataField(required: true)]
    public AdultBreastSize DisallowedSize;
}

/// <summary>Radiant sector: exposes penile denervation only while the nerve is intact.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryPenisNerveConditionComponent : Component;

/// <summary>Radiant sector: a penis can only be installed into a cleared genital socket.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SurgeryPenisInstallationConditionComponent : Component;
