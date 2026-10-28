using System.Linq;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared._radiant.Medical.Virology;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Medical.Virology;

public sealed partial class VirologyLabSystem
{
    private HashSet<string> Knowledge(EntityUid uid, VirologySampleComponent sample)
    {
        var archive = Archive(uid, true)!;
        archive.KnownSymptoms.UnionWith(sample.KnownSymptoms);
        sample.KnownSymptoms.UnionWith(archive.KnownSymptoms);
        return sample.KnownSymptoms;
    }
    private void DiscoverSample(EntityUid uid, VirologySampleComponent sample)
    {
        if (sample.Diseases.Count == 0) return;
        sample.KnownSymptoms.Add("Fatigue");
        foreach (var id in sample.Diseases) sample.KnownSymptoms.UnionWith(VirologyStrains.Symptoms(id));
        Knowledge(uid, sample);
    }
    private IEnumerable<VirologySymptomPrototype> Compatible(string disease)
    {
        if (!VirologyStrains.TryResolve(_prototypes, disease, out var profile)) return [];
        return _prototypes.EnumeratePrototypes<VirologySymptomPrototype>().Where(s => s.Routes.Contains(profile.Route))
            .OrderBy(s => s.Cost).ThenBy(s => s.ID, StringComparer.Ordinal);
    }
    private string? NextStudy(EntityUid uid, VirologySampleComponent sample, string selected)
        => Compatible(selected).FirstOrDefault(s => !Knowledge(uid, sample).Contains(s.ID))?.ID;
    public bool ToggleSymptom(EntityUid uid, string id)
    {
        var machine = Comp<VirologyMachineComponent>(uid);
        var sample = Sample(machine);
        if (machine.Work != null || sample?.Analyzed != true || !Powered(uid) || !sample.Diseases.Contains(machine.Selected)
            || !Knowledge(uid, sample).Contains(id) || !Compatible(machine.Selected).Any(s => s.ID == id)) return false;
        if (machine.Draft.Remove(id)) return true;
        var draft = machine.Draft.Append(id).ToArray();
        var profile = VirologyStrains.Canonical(VirologyStrains.Family(machine.Selected), draft);
        if (draft.Length > (machine.Tier >= 4 ? VirologyStrains.MaxSymptoms : 1)
            || !VirologyStrains.TryResolve(_prototypes, profile, out _)) return false;
        machine.Draft.Add(id);
        return true;
    }
    private bool StartExperimental(EntityUid uid, VirologyMachineComponent machine, VirologySampleComponent sample, VirologyCommand command)
    {
        if (!sample.Analyzed || !sample.Diseases.Contains(machine.Selected)) return false;
        if (command == VirologyCommand.Study)
        {
            if (machine.Producer || NextStudy(uid, sample, machine.Selected) is not { } next) return false;
            machine.PendingProfile = next;
            machine.Duration = machine.StudySeconds;
        }
        else
        {
            var profile = VirologyStrains.Canonical(VirologyStrains.Family(machine.Selected), machine.Draft);
            if (!machine.Producer || machine.Draft.Count > (machine.Tier >= 4 ? VirologyStrains.MaxSymptoms : 1)
                || !machine.Draft.All(Knowledge(uid, sample).Contains)
                || !VirologyStrains.TryResolve(_prototypes, profile, out _)
                || !_materials.TryChangeMaterialAmount(uid, "Biomass", -machine.BiomassCost * 2)) return false;
            machine.PendingProfile = profile;
            machine.Duration = machine.CultureSeconds;
        }
        machine.Work = command;
        machine.Remaining = machine.Duration;
        return true;
    }
    private void FinishExperimental(EntityUid uid, VirologyMachineComponent machine, VirologySampleComponent sample)
    {
        if (machine.Work == VirologyCommand.Study)
        {
            sample.KnownSymptoms.Add(machine.PendingProfile);
            Knowledge(uid, sample);
            return;
        }
        if (!VirologyStrains.TryResolve(_prototypes, machine.PendingProfile, out var disease)) return;
        var bottle = Spawn("ChemistryEmptyBottle01", Transform(uid).Coordinates);
        if (!_solutions.TryGetSolution(bottle, "drink", out var solution, out _)) { QueueDel(bottle); return; }
        _solutions.TryAddReagent(solution.Value,
            new ReagentQuantity(new ReagentId(disease.Reagent, new List<ReagentData> { new VirologyStrainData { Strain = machine.PendingProfile } }), FixedPoint2.New(5)), out _);
        _metadata.SetEntityName(bottle, Loc.GetString("virology-culture-label", ("disease", DiseaseName(machine.PendingProfile))));
    }
    private void ExperimentalState(EntityUid uid, VirologyMachineComponent machine, VirologySampleComponent? sample, VirologyState state)
    {
        state.Tier = machine.Tier;
        state.Draft = machine.Draft.ToList();
        if (sample?.Analyzed != true || !sample.Diseases.Contains(machine.Selected))
        {
            state.Design = Loc.GetString("virology-design-needs-sample");
            return;
        }
        var known = Knowledge(uid, sample);
        foreach (var symptom in Compatible(machine.Selected).Where(s => known.Contains(s.ID)))
            state.Symptoms[symptom.ID] = Loc.GetString(symptom.Name) + " · " + symptom.Cost + "\n" + Loc.GetString(symptom.Description);
        var next = NextStudy(uid, sample, machine.Selected);
        state.Study = Loc.GetString(next == null ? "virology-study-complete" : "virology-study-next");
        state.CanStudy = !machine.Producer && machine.Work == null && Powered(uid) && next != null;
        state.CanDesign = machine.Producer && machine.Work == null && Powered(uid);
        var id = VirologyStrains.Canonical(VirologyStrains.Family(machine.Selected), machine.Draft);
        if (VirologyStrains.TryResolve(_prototypes, id, out var profile))
        {
            var cost = machine.Draft.Sum(s => _prototypes.Index<VirologySymptomPrototype>(s).Cost);
            state.Design = Loc.GetString("virology-design-profile", ("tier", machine.Tier), ("cost", cost),
                ("budget", VirologyStrains.Budget), ("count", machine.Draft.Count), ("max", machine.Tier >= 4 ? 3 : 1),
                ("incubation", (int) profile.Incubation), ("treatment", (int) profile.TreatmentSeconds),
                ("recovery", (int) profile.RecoveryAfter)) + "\n" + Loc.GetString("virology-design-treatment");
            state.CanCulture = state.CanDesign && _materials.GetMaterialAmount(uid, "Biomass") >= machine.BiomassCost * 2;
        }
    }
}
