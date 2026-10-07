using Content.Shared._Starlight.Medical.Surgery.Components;

namespace Content.Shared._Starlight.Medical.Surgery;

/// <summary>First-stage balancing rules. Contamination is not an infection or damage amount.</summary>
public static class SurgicalSterilityRules
{
    public const int UntreatedSite = 10;
    public const int ContaminatedSite = 25;
    public const int ToolUsesBeforeDirty = 6;
    public const int GloveUsesBeforeDirty = 10;

    public static int EnvironmentRisk(int trash, int puddles, bool sterilizer)
        => Math.Min(20, Math.Max(0, trash) * 2 + Math.Max(0, puddles) * 3) - (sterilizer ? 5 : 0);

    public static int ApplyFieldProtection(int incoming, bool draped)
        => Math.Max(0, incoming - (draped ? 5 : 0));

    // Protective equipment mitigates new contact risk, never removes existing contamination.
    public static int EquipmentRisk(bool cleanMedicalGloves, bool cleanMedicalMask, bool unsuitableOuterwear)
        => Math.Max(0, 6 + (cleanMedicalGloves ? -4 : 10)
            + (cleanMedicalMask ? -2 : 5) + (unsuitableOuterwear ? 5 : 0));

    public static int Contact(SurgicalItemSterilityComponent item, EntityUid patient, int contamination,
        bool countUses = false, int usesBeforeDirty = ToolUsesBeforeDirty)
    {
        var crossContaminated = item.Dirty || item.Used && item.LastPatient != patient;
        var transferred = crossContaminated ? 15 : item.ContainerContamination;
        item.Used = true;
        item.LastPatient = patient;
        // Tools and gloves remain safe for the same patient for the whole operation.
        // External dirt and cross-patient contact persist until disinfection.
        // Extracted organs still carry contamination from their original tissue.
        item.Dirty |= crossContaminated || !countUses && contamination >= ContaminatedSite;
        return transferred;
    }

    public static void AddContamination(SurgicalSterilityComponent wound, SurgicalSite site, int amount)
        => wound.Contamination[site] = Math.Clamp(
            wound.Contamination.GetValueOrDefault(site, UntreatedSite) + amount, 0, 100);

    public static void Disinfect(SurgicalItemSterilityComponent item)
    {
        item.Dirty = false;
        item.Used = false;
        item.LastPatient = null;
        item.UsesSinceCleaning = 0;
        item.ContainerContamination = 0;
    }
}
