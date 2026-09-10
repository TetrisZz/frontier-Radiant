using System.Linq;
using Content.Server.Body.Components;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;

namespace Content.Server._Starlight.Medical.Surgery;

public sealed partial class BodyScannerSystem
{
    /// <summary>
    /// Read-only treatment suggestions from actual anatomy, not parsed diagnostic text.
    /// No elective/ERP procedures are suggested and no surgery is executed by the console.
    /// Recomputed for the current patient so completed work cannot leak to the next patient.
    /// </summary>
    public List<BodyScannerDiagnosticEntry> BuildOperationPlan(EntityUid patient)
    {
        var tasks = new List<(int Priority, BodyScannerDiagnosticEntry Entry)>();
        if (!TryComp<BodyComponent>(patient, out var body))
            return new();

        void Add(int priority, string key, string part, BodyScannerDiagnosticSeverity severity)
        {
            tasks.Add((priority, new BodyScannerDiagnosticEntry(
                Loc.GetString($"body-scanner-plan-{key}", ("part", part)), severity)));
        }

        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (partId, part) in _body.GetBodyChildren(patient, body))
        {
            occupied.Add(GetPartIdentity(part));
            var partName = GetPartName(part);
            if (HasComp<SurgicalDeadLimbComponent>(partId))
            {
                Add(10, "dead-limb", partName, BodyScannerDiagnosticSeverity.Critical);
                continue;
            }

            TryComp<SurgicalSterilityComponent>(partId, out var sterility);
            TryComp<SurgicalCavityStateComponent>(partId, out var cavities);
            foreach (var site in Enum.GetValues<SurgicalSite>())
            {
                if (site != SurgicalSite.Surface && part.PartType != BodyPartType.Torso)
                    continue;
                var name = site == SurgicalSite.Surface ? partName
                    : Loc.GetString($"surgical-site-{site.ToString().ToLowerInvariant()}");
                var open = site switch
                {
                    SurgicalSite.Ribcage => cavities?.RibcageOpen == true,
                    SurgicalSite.Abdomen => cavities?.AbdomenOpen == true,
                    SurgicalSite.Groin => cavities?.GroinOpen == true,
                    // Torso's compatibility marker is not a fourth open cavity.
                    _ => cavities == null && HasComp<IncisionOpenComponent>(partId),
                };
                var necrotic = sterility?.NecrosisSeconds.ContainsKey(site) == true;
                var contamination = sterility?.Contamination.GetValueOrDefault(site) ?? 0;
                var infection = sterility?.Infection.GetValueOrDefault(site) ?? 0;
                if (necrotic)
                    Add(10, "debride", name, BodyScannerDiagnosticSeverity.Critical);
                if (infection > 0)
                    Add(30, _surgery.IsSurgicalSlime(patient) ? "slime-infection" : "infection",
                        name, BodyScannerDiagnosticSeverity.Warning);
                if (contamination > 0)
                    Add(40, open ? "clean" : "observe", name, BodyScannerDiagnosticSeverity.Warning);
                if (open)
                    Add(90, "close", name, BodyScannerDiagnosticSeverity.Info);
                if (sterility?.Draped.Contains(site) == true)
                    Add(95, "undrape", name, BodyScannerDiagnosticSeverity.Info);
            }

            if (TryComp<EmbeddedGlassComponent>(partId, out var glass) && glass.Fragments.Count > 0)
                Add(20, "glass", partName, BodyScannerDiagnosticSeverity.Warning);

            foreach (var (slot, organ) in GetInstalledOrgansBySlot(partId, part))
            {
                var name = $"{partName}: {MetaData(organ).EntityName}";
                if (TryComp<SurgicalOrganNecrosisComponent>(organ, out var necrosis))
                {
                    // Isolated organ necrosis does not necessarily enable site debridement.
                    var site = HasComp<OrganLungsComponent>(organ) ? SurgicalSite.Ribcage : SurgicalSite.Abdomen;
                    var canDebride = (HasComp<OrganLungsComponent>(organ) || HasComp<OrganStomachComponent>(organ)
                                      || HasComp<OrganLiverComponent>(organ) || HasComp<OrganKidneysComponent>(organ)
                                      || HasComp<OrganAppendixComponent>(organ))
                                     && sterility?.NecrosisSeconds.ContainsKey(site) == true;
                    Add(10, necrosis.Dead ? "dead-organ" : HasComp<OrganHeartComponent>(organ)
                        ? "heart" : canDebride ? "organ-necrosis" : "replace-organ",
                        name, BodyScannerDiagnosticSeverity.Critical);
                }
                if (TryComp<SurgicalSterilityComponent>(organ, out var organState)
                    && (organState.Infection.Values.Any(value => value > 0)
                        || organState.Contamination.Values.Any(value => value > 0)))
                    Add(35, "organ-infection", name, BodyScannerDiagnosticSeverity.Warning);
            }

            foreach (var slot in part.Organs.Keys)
            {
                if (IsRequiredOrganSlot(slot) && !IsOrganSlotOccupied(partId, slot))
                    Add(15, "missing-organ", $"{partName}: {GetOrganName(slot)}", BodyScannerDiagnosticSeverity.Warning);
            }
        }

        if (body.Prototype is { } id && _prototypes.TryIndex<BodyPrototype>(id, out var prototype))
        {
            foreach (var slot in prototype.Slots.Keys)
            {
                if (!occupied.Contains(GetExpectedPartIdentity(slot)))
                    Add(25, "missing-part", GetExpectedPartName(slot), BodyScannerDiagnosticSeverity.Warning);
            }
        }

        if (tasks.Count == 0)
            return new();

        var result = new List<BodyScannerDiagnosticEntry>();
        if (tasks.Any(task => task.Priority <= 25 || task.Priority == 90))
            result.Add(new BodyScannerDiagnosticEntry(Loc.GetString("body-scanner-plan-prepare"), BodyScannerDiagnosticSeverity.Info));
        result.AddRange(tasks.OrderBy(task => task.Priority).Select(task => task.Entry));
        result.Add(new BodyScannerDiagnosticEntry(Loc.GetString("body-scanner-plan-recheck"), BodyScannerDiagnosticSeverity.Info));
        return result;
    }
}
