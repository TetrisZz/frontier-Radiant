using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared._radiant.Medical.Virology;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Medical.Virology;

/// <summary>Fictional infections. No spontaneous outbreaks; strains are defined by prototypes.</summary>
public sealed partial class VirologySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedBloodstreamSystem _blood = default!;
    [Dependency] private ThirstSystem _thirst = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private Content.Shared.Interaction.SharedInteractionSystem _interaction = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private ISharedAdminLogManager _logs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BloodstreamComponent, ReactionEntityEvent>(OnReaction);
        SubscribeLocalEvent<VirologyCarrierComponent, IngestingEvent>(OnIngesting);
        SubscribeLocalEvent<OrganComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
        SubscribeLocalEvent<VirologyOrganComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<VirologyCarrierComponent, RejuvenateEvent>(OnRejuvenate);
    }

    // Animals share organic diseases with humanoids. Mechanical bodies and slimes do not.
    public bool Susceptible(EntityUid uid) =>
        HasComp<BloodstreamComponent>(uid) && HasComp<HungerComponent>(uid)
        && (!TryComp<HumanoidAppearanceComponent>(uid, out var look)
            || look.Species.Id is not ("SlimePerson" or "IPC"))
        && !_mobs.IsDead(uid);

    public bool Expose(EntityUid uid, string disease, float dose)
    {
        if (dose <= 0 || !float.IsFinite(dose) || !Susceptible(uid)
            || !VirologyStrains.TryResolve(_prototypes, disease, out var proto))
            return false;
        var carrier = EnsureComp<VirologyCarrierComponent>(uid);
        if (carrier.Infections.Keys.Any(id => VirologyStrains.Family(id) == VirologyStrains.Family(disease))
            || carrier.Immunity.GetValueOrDefault(VirologyStrains.Family(disease)) > 0
            || carrier.Infections.Count >= 2)
            return false;
        var total = carrier.Exposure.GetValueOrDefault(disease) + dose;
        if (total < proto.ExposureThreshold)
        {
            carrier.Exposure[disease] = total;
            return false;
        }
        carrier.Exposure.Remove(disease);
        carrier.Infections[disease] = 0;
        _logs.Add(LogType.Damaged, LogImpact.Medium, $"{ToPrettyString(uid):target} contracted {disease}");
        return true;
    }

    public bool ApplyDose(EntityUid uid, string disease, bool vaccine)
    {
        if (!Susceptible(uid) || !VirologyStrains.TryResolve(_prototypes, disease, out var proto))
            return false;
        var carrier = EnsureComp<VirologyCarrierComponent>(uid);
        if (vaccine)
        {
            var family = VirologyStrains.Family(disease);
            if (carrier.Infections.Keys.Any(id => VirologyStrains.Family(id) == family) || carrier.Immunity.GetValueOrDefault(family) > 0)
                return false;
            carrier.Immunity[family] = proto.ImmunitySeconds;
            foreach (var id in carrier.Exposure.Keys.Where(id => VirologyStrains.Family(id) == family).ToArray()) carrier.Exposure.Remove(id);
        }
        else
        {
            var matching = carrier.Infections.Keys.Where(id => VirologyStrains.Family(id) == VirologyStrains.Family(disease)).ToArray();
            if (matching.Length == 0 || matching.All(carrier.Treatment.ContainsKey))
                return false;
            foreach (var id in matching) carrier.Treatment[id] = Math.Min(Disease(id).TreatmentSeconds, 180);
        }
        RefreshSymptoms(uid, carrier);
        _logs.Add(LogType.Healed, LogImpact.Low, $"{ToPrettyString(uid):target} received virology treatment for {disease}, vaccine={vaccine}");
        return true;
    }

    private void OnReaction(Entity<BloodstreamComponent> ent, ref ReactionEntityEvent args)
    {
        if (args.ReagentQuantity.Reagent.Data?.OfType<VirologyStrainData>().FirstOrDefault() is { } strain)
        {
            if (VirologyStrains.TryResolve(_prototypes, strain.Strain, out var profile) && profile.Reagent == args.Reagent.ID
                && (args.Method == ReactionMethod.Injection || args.Method == ReactionMethod.Ingestion && profile.Route == "ingestion"))
                Expose(ent, strain.Strain, args.ReagentQuantity.Quantity.Float() * profile.LiquidDose);
            return;
        }
        foreach (var disease in _prototypes.EnumeratePrototypes<RadiantDiseasePrototype>())
        {
            if (args.Reagent.ID != disease.Reagent)
                continue;
            if (args.Method == ReactionMethod.Injection
                || args.Method == ReactionMethod.Ingestion && disease.Route == "ingestion")
                Expose(ent, disease.ID, args.ReagentQuantity.Quantity.Float() * disease.LiquidDose);
        }
    }

    /// <summary>Replace a small fraction, preserving liquid volume and chemical transfer limits.</summary>
    public void Contaminate(EntityUid donor, Solution solution, string route)
    {
        if (solution.Volume <= 0 || !TryComp<VirologyCarrierComponent>(donor, out var carrier))
            return;
        foreach (var (id, age) in carrier.Infections)
        {
            var disease = Disease(id);
            if (disease.Route != route || age < disease.Incubation)
                continue;
            var amount = FixedPoint2.Min(solution.Volume, FixedPoint2.New(solution.Volume.Float() * 0.01f));
            if (amount <= 0) continue;
            solution.SplitSolution(amount);
            solution.AddReagent(new ReagentId(disease.Reagent, id == VirologyStrains.Family(id) ? null
                : new List<ReagentData> { new VirologyStrainData { Strain = id } }), amount);
        }
    }

    private void OnIngesting(Entity<VirologyCarrierComponent> ent, ref IngestingEvent args)
    {
        // Sharing leftovers or an open drink with an infectious patient.
        if (TryComp<EdibleComponent>(args.Food, out var edible)
            && _solutions.TryGetSolution(args.Food, edible.Solution, out var solution, out var contents))
        {
            Contaminate(ent.Owner, contents, "ingestion");
            _solutions.UpdateChemicals(solution.Value);
        }
    }

    private void OnOrganRemoved(Entity<OrganComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        // Body cleanup also removes organs, but terminating entities cannot retain infections.
        if (TerminatingOrDeleted(ent.Owner)) return;
        if (!TryComp<VirologyCarrierComponent>(args.OldBody, out var carrier)) return;
        var diseases = carrier.Infections.Keys.Where(id => Disease(id).Route == "blood").ToList();
        if (diseases.Count == 0) return;
        EnsureComp<VirologyOrganComponent>(ent).Diseases = diseases;
    }

    private void OnOrganAdded(Entity<VirologyOrganComponent> ent, ref OrganAddedToBodyEvent args)
    {
        foreach (var disease in ent.Comp.Diseases)
            Expose(args.Body, disease, Disease(disease).ExposureThreshold);
    }

    private void OnRejuvenate(Entity<VirologyCarrierComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Infections.Clear();
        ent.Comp.Exposure.Clear();
        ent.Comp.Treatment.Clear();
        ent.Comp.Immunity.Clear();
        RefreshSymptoms(ent, ent.Comp);
    }

    public float DropletProtection(EntityUid uid)
    {
        if (!_inventory.TryGetSlotEntity(uid, "mask", out var mask)
            || !TryComp<VirologyProtectionComponent>(mask, out var protection)
            || TryComp<MaskComponent>(mask, out var toggle) && toggle.IsToggled)
            return 1;
        return Math.Clamp(protection.DropletMultiplier, 0, 1);
    }

    public void Cough(EntityUid source, RadiantDiseasePrototype disease)
    {
        // Use the real emote, rather than a plain chat line: VocalSystem then selects the
        // speaker's normal cough sound (and species-specific variants where available).
        _chat.TryEmoteWithChat(source, "Cough", ChatTransmitRange.NoGhosts,
            hideLog: true, ignoreActionBlocker: true, forceEmote: true);
        foreach (var (target, _) in _lookup.GetEntitiesInRange<BloodstreamComponent>(Transform(source).Coordinates, 2))
        {
            if (target == source || !_interaction.InRangeUnobstructed(source, target, 2))
                continue;
            Expose(target, disease.ID, disease.CoughDose * DropletProtection(source) * DropletProtection(target));
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<VirologyCarrierComponent>();
        while (query.MoveNext(out var uid, out var carrier))
        {
            if (MetaData(uid).EntityPaused) continue;
            carrier.Accumulator += frameTime;
            if (carrier.Accumulator < 1) continue;
            var elapsed = carrier.Accumulator;
            carrier.Accumulator = 0;
            Advance(uid, elapsed);
        }
    }

    public void Advance(EntityUid uid, float seconds)
    {
        if (seconds <= 0 || !float.IsFinite(seconds) || !TryComp<VirologyCarrierComponent>(uid, out var carrier)
            || _mobs.IsDead(uid))
            return;
        // Expedition zombies are a reservoir, not patients: their strain must remain available for sampling.
        if (HasComp<VirologyZombieSourceComponent>(uid))
            return;
        foreach (var id in carrier.Immunity.Keys.ToArray())
        {
            carrier.Immunity[id] -= seconds;
            if (carrier.Immunity[id] <= 0) carrier.Immunity.Remove(id);
        }
        foreach (var id in carrier.Exposure.Keys.ToArray())
        {
            carrier.Exposure[id] -= Disease(id).ExposureDecay * seconds;
            if (carrier.Exposure[id] <= 0) carrier.Exposure.Remove(id);
        }

        carrier.SymptomClock += seconds;
        var pulse = carrier.SymptomClock >= 45;
        if (pulse) carrier.SymptomClock %= 45;
        foreach (var id in carrier.Infections.Keys.ToArray())
        {
            var disease = Disease(id);
            carrier.Infections[id] += seconds;
            var age = carrier.Infections[id];
            if (carrier.Treatment.ContainsKey(id)) carrier.Treatment[id] -= seconds;
            if (age >= disease.RecoveryAfter || carrier.Treatment.TryGetValue(id, out var remaining) && remaining <= 0)
            {
                carrier.Infections.Remove(id);
                carrier.Treatment.Remove(id);
                carrier.Immunity[VirologyStrains.Family(id)] = disease.ImmunitySeconds;
                _popup.PopupEntity(Loc.GetString("virology-recover"), uid, uid);
                continue;
            }
            if (age < disease.Incubation || !pulse) continue;
            if (!disease.SuppressEarlyWarnings || age >= disease.SevereAfter)
                _popup.PopupEntity(Loc.GetString("virology-symptom-" + disease.Route), uid, uid);
            if (disease.WaterPerPulse > 0 && TryComp<ThirstComponent>(uid, out var water))
                _thirst.ModifyThirst(uid, water, -disease.WaterPerPulse);
            if (disease.Route == "droplet") Cough(uid, disease);
            if (disease.Route == "ingestion" && TryComp<ThirstComponent>(uid, out var thirst))
                _thirst.ModifyThirst(uid, thirst, -2);
            if (!carrier.Treatment.ContainsKey(id))
            {
                var loss = age >= disease.SevereAfter ? disease.SevereBloodLossPerPulse : disease.BloodLossPerPulse;
                if (loss > 0) _blood.TryModifyBloodLevel(uid, FixedPoint2.New(-loss));
            }
            if (age >= disease.SevereAfter && !carrier.Treatment.ContainsKey(id))
                _damage.TryChangeDamage(uid, new DamageSpecifier { DamageDict = new() { ["Poison"] = FixedPoint2.New(1) } },
                    ignoreResistances: true, interruptsDoAfters: false);
        }
        RefreshSymptoms(uid, carrier);
    }

    private void RefreshSymptoms(EntityUid uid, VirologyCarrierComponent carrier)
    {
        var severity = 0;
        var speed = 1f;
        var bloodRecovery = 1f;
        foreach (var (id, age) in carrier.Infections)
        {
            var disease = Disease(id);
            severity = Math.Max(severity, age >= disease.SevereAfter ? 2 : age >= disease.Incubation ? 1 : 0);
            if (age >= disease.Incubation && !carrier.Treatment.ContainsKey(id))
            {
                speed = Math.Min(speed, age >= disease.SevereAfter ? disease.SevereSpeed : disease.MildSpeed);
                bloodRecovery = Math.Min(bloodRecovery, disease.BloodRegenerationMultiplier);
            }
        }
        var symptoms = EnsureComp<VirologySymptomsComponent>(uid);
        if (symptoms.Severity == severity && symptoms.SpeedMultiplier == speed
            && symptoms.BloodRegenerationMultiplier == bloodRecovery) return;
        symptoms.Severity = severity;
        symptoms.SpeedMultiplier = speed;
        symptoms.BloodRegenerationMultiplier = bloodRecovery;
        Dirty(uid, symptoms);
        EntityManager.System<Content.Shared.Movement.Systems.MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(uid);
    }

    public RadiantDiseasePrototype Disease(string id) => VirologyStrains.TryResolve(_prototypes, id, out var disease)
        ? disease : _prototypes.Index<RadiantDiseasePrototype>(VirologyStrains.Family(id));

    public string DiseaseName(string id) => Loc.GetString(Disease(id).Name)
        + (id.Contains('~') ? " · " + string.Join(", ", VirologyStrains.Symptoms(id)
            .Select(s => Loc.GetString(_prototypes.Index<VirologySymptomPrototype>(s).Name))) : "");
}
