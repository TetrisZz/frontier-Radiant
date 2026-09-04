using Robust.Shared.GameStates;

namespace Content.Shared._radiant.ERP;

/// <summary>
/// Radiant sector: server-authoritative ERP anatomy. This is intentionally
/// separate from HumanoidAppearanceComponent.Sex so surgery can change anatomy
/// without changing identity or character-profile data.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AdultAnatomyComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool AnatomyInitialized;

    /// <summary>
    /// Radiant sector: once surgery changes anatomy, later appearance/profile
    /// changes must not recreate the original organs.
    /// </summary>
    [DataField]
    public bool SurgicallyModified;

    [DataField, AutoNetworkedField]
    public bool HasPenis;

    [DataField, AutoNetworkedField]
    public bool PenisSurgicallyRemoved;

    [DataField, AutoNetworkedField]
    public bool HasVagina;

    [DataField, AutoNetworkedField]
    public bool VaginaSurgicallyRemoved;

    /// <summary>
    /// Radiant sector: the penis and testicles are one surgical organ. Cutting
    /// its sensory nerve prevents climax without removing the organ.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PenisNervesIntact = true;

    [DataField, AutoNetworkedField]
    public bool HasBreasts;

    [DataField, AutoNetworkedField]
    public bool BreastsSurgicallyRemoved;

    [DataField, AutoNetworkedField]
    public AdultBreastSize BreastSize = AdultBreastSize.Medium;

    /// <summary>
    /// Radiant sector: the body scanner should not report a default profile
    /// breast size as a surgical finding. It becomes visible only after surgery
    /// installs or resizes breasts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool BreastSizeSurgicallyChanged;
}

[RegisterComponent]
public sealed partial class AdultOrganItemComponent : Component
{
    [DataField(required: true)]
    public AdultOrganType Organ;

    [DataField]
    public AdultBreastSize BreastSize = AdultBreastSize.Medium;

    /// <summary>Radiant sector: preserves denervation across extraction and transplantation.</summary>
    [DataField]
    public bool PenisNervesIntact = true;
}

/// <summary>Radiant sector: state of a condom currently worn on an installed penis.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CondomWornComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Used;

    [DataField, AutoNetworkedField]
    public string ItemPrototype = "Condom";
}

public enum AdultOrganType : byte
{
    Penis,
    Vagina,
    Breasts,
}

public enum AdultBreastSize : byte
{
    Small,
    Medium,
    Large,
}

public static class AdultAnatomyHelpers
{
    public static bool HasOrgan(AdultAnatomyComponent anatomy, AdultOrganType organ)
    {
        return organ switch
        {
            AdultOrganType.Penis => anatomy.HasPenis,
            AdultOrganType.Vagina => anatomy.HasVagina,
            AdultOrganType.Breasts => anatomy.HasBreasts,
            _ => false,
        };
    }

    public static void SetOrgan(AdultAnatomyComponent anatomy, AdultOrganType organ, bool present)
    {
        switch (organ)
        {
            case AdultOrganType.Penis:
                anatomy.HasPenis = present;
                break;
            case AdultOrganType.Vagina:
                anatomy.HasVagina = present;
                break;
            case AdultOrganType.Breasts:
                anatomy.HasBreasts = present;
                break;
        }
    }
}
