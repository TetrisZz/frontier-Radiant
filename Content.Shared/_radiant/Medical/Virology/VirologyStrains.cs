using System.Linq;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Virology;

[Prototype]
public sealed partial class VirologySymptomPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Name = "";
    [DataField(required: true)] public string Description = "";
    [DataField] public int Cost = 1;
    [DataField] public float Incubation = 1;
    [DataField] public float Recovery = 1;
    [DataField] public float Treatment = 1;
    [DataField] public float Cough = 1;
    [DataField] public float SpeedPenalty;
    [DataField] public float Water;
    [DataField] public bool Quiet;
    [DataField] public List<string> Routes = new() { "blood", "droplet", "ingestion" };
}

/// <summary>Canonical profile is its identity. No mutable registry or per-strain reagent prototypes.</summary>
public static class VirologyStrains
{
    public const int MaxSymptoms = 3, Budget = 4;
    public static string Family(string id) => id.Split('~', 2)[0];
    public static string[] Symptoms(string id) => id.Contains('~') ? id.Split('~', 2)[1].Split(',') : [];
    public static string Canonical(string family, IEnumerable<string> symptoms)
    {
        var ordered = symptoms.Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return ordered.Length == 0 ? family : family + "~" + string.Join(',', ordered);
    }
    public static bool TryResolve(IPrototypeManager prototypes, string id, out RadiantDiseasePrototype disease)
    {
        disease = default!;
        if (id.Length > 256 || !prototypes.TryIndex<RadiantDiseasePrototype>(Family(id), out var original)) return false;
        if (id == original.ID) { disease = original; return true; }
        var ids = Symptoms(id);
        if (ids.Length is < 1 or > MaxSymptoms || Canonical(original.ID, ids) != id) return false;
        var effects = new List<VirologySymptomPrototype>();
        foreach (var symptom in ids)
        {
            if (!prototypes.TryIndex<VirologySymptomPrototype>(symptom, out var effect)
                || !effect.Routes.Contains(original.Route) || effect.Cost < 1) return false;
            effects.Add(effect);
        }
        if (effects.Sum(s => s.Cost) > Budget) return false;
        disease = new(id)
        {
            Name = original.Name, Description = original.Description, Reagent = original.Reagent, Route = original.Route,
            Incubation = Math.Clamp(original.Incubation * effects.Aggregate(1f, (a, s) => a * s.Incubation), 120, 360),
            SevereAfter = Math.Max(480, original.SevereAfter),
            RecoveryAfter = Math.Clamp(original.RecoveryAfter * effects.Aggregate(1f, (a, s) => a * s.Recovery), 1200, 1800),
            TreatmentSeconds = Math.Clamp(original.TreatmentSeconds * effects.Aggregate(1f, (a, s) => a * s.Treatment), 60, 180),
            ImmunitySeconds = original.ImmunitySeconds, ExposureThreshold = original.ExposureThreshold,
            ExposureDecay = original.ExposureDecay, LiquidDose = original.LiquidDose,
            CoughDose = Math.Clamp(original.CoughDose * effects.Aggregate(1f, (a, s) => a * s.Cough), 0, 45),
            MildSpeed = Math.Max(0.9f, original.MildSpeed - effects.Sum(s => s.SpeedPenalty)),
            SevereSpeed = Math.Max(0.85f, original.SevereSpeed - effects.Sum(s => s.SpeedPenalty)),
            BloodRegenerationMultiplier = original.BloodRegenerationMultiplier,
            BloodLossPerPulse = original.BloodLossPerPulse, SevereBloodLossPerPulse = original.SevereBloodLossPerPulse,
            WaterPerPulse = Math.Clamp(effects.Sum(s => s.Water), 0, 2), SuppressEarlyWarnings = effects.Any(s => s.Quiet),
        };
        return true;
    }
}

[Serializable, NetSerializable]
public sealed partial class VirologyStrainData : ReagentData
{
    [DataField] public string Strain = "";
    public override bool Equals(ReagentData? other) => other is VirologyStrainData data && Strain == data.Strain;
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Strain);
    public override ReagentData Clone() => new VirologyStrainData { Strain = Strain };
}
