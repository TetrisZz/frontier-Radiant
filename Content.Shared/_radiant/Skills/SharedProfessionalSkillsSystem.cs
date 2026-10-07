using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Network;

namespace Content.Shared._radiant.Skills;

public sealed partial class SharedProfessionalSkillsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private INetManager _net = default!;
    private readonly Dictionary<(string, SkillAction), ProfessionalSkillRequirementPrototype?> _cache = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ProfessionalSkillsComponent, UseAttemptEvent>(OnUse);
        SubscribeLocalEvent<GunComponent, AttemptShootEvent>(OnShoot);
        SubscribeLocalEvent<BoundUserInterfaceMessageAttempt>(OnInterface);
        InitializeToolDelays();
        _prototypes.PrototypesReloaded += OnReload;
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnReload;
        base.Shutdown();
    }

    private void OnReload(PrototypesReloadedEventArgs args) => _cache.Clear();

    public int Level(EntityUid user, ProfessionalSkill skill)
    {
        // NPCs, admin-spawned test mobs and non-profile roles retain their existing behavior.
        if (!TryComp<ProfessionalSkillsComponent>(user, out var skills))
            return ProfessionalSkillRules.Maximum(skill);
        var i = (int) skill;
        return i < skills.Levels.Length ? Math.Clamp(skills.Levels[i], 0, ProfessionalSkillRules.Maximum(skill)) : 0;
    }

    public bool Check(EntityUid user, ProfessionalSkill skill, int required, bool popup = true, bool serverPopup = false)
    {
        if (Level(user, skill) >= required)
            return true;
        if (popup && serverPopup && _net.IsServer)
            _popup.PopupEntity(Message(skill, required), user, user);
        else if (popup && !serverPopup)
            _popup.PopupClient(Message(skill, required), user, user);
        return false;
    }

    public string Message(ProfessionalSkill skill, int required)
        => Loc.GetString("professional-skills-required",
            ("skill", Loc.GetString(ProfessionalSkillRules.NameKey(skill))), ("level", required));

    public static bool IsCommonReagent(Content.Shared.Chemistry.Reagent.ReagentPrototype reagent)
        => reagent.Group is "Foods" or "Drinks"
            || reagent.ID is "Water" or "Blood" or "InsectBlood" or "CopperBlood" or "AmmoniaBlood";

    public bool CanIdentifyReagent(EntityUid user, string? id)
        => Level(user, ProfessionalSkill.Medicine) >= 2
            || id != null && _prototypes.TryIndex<Content.Shared.Chemistry.Reagent.ReagentPrototype>(id, out var reagent)
                && IsCommonReagent(reagent);

    public static int ConstructionLevel(string graph, bool structure = true)
    {
        if (graph.Contains("Tesla", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Singularity", StringComparison.OrdinalIgnoreCase)
            || graph.StartsWith("Teg", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("ThermoElectric", StringComparison.OrdinalIgnoreCase)
            || graph.StartsWith("Ame", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (graph.Contains("Pipe", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Canister", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Vent", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Scrubber", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Gas", StringComparison.OrdinalIgnoreCase))
            return 2;
        return structure ? 1 : 0;
    }

    public ProfessionalSkillRequirementPrototype? Requirement(EntityUid target, SkillAction action)
    {
        if (MetaData(target).EntityPrototype is not { } proto)
            return null;
        var key = (proto.ID, action);
        if (_cache.TryGetValue(key, out var cached))
            return cached;
        // Most-specific prototype overrides inherited rules (e.g. industrial guns).
        var seen = new HashSet<string>();
        // Abstract frames have no indexed EntityPrototype instance. Walking Parents via
        // TryIndex would stop at them and silently lose most weapon/category rules.
        foreach (var (id, _) in _prototypes.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true))
        {
            if (!seen.Add(id))
                continue;
            foreach (var rule in _prototypes.EnumeratePrototypes<ProfessionalSkillRequirementPrototype>())
            {
                if ((rule.Actions & action) != 0 && rule.Targets.Contains(id))
                    return _cache[key] = rule;
            }
        }
        return _cache[key] = null;
    }

    public bool CanUse(EntityUid user, EntityUid target, SkillAction action, bool popup = true)
        => Requirement(target, action) is not { } rule || Check(user, rule.Skill, rule.Level, popup);

    private void OnUse(Entity<ProfessionalSkillsComponent> ent, ref UseAttemptEvent args)
    {
        if (!args.Cancelled && !CanUse(ent, args.Used, SkillAction.Use))
            args.Cancel();
    }

    private void OnShoot(Entity<GunComponent> ent, ref AttemptShootEvent args)
    {
        var (skill, required) = GunRequirement(ent);
        if (args.Cancelled || Check(args.User, skill, required, false))
            return;
        args.Cancelled = true;
        args.Message = Message(skill, required);
    }

    public (ProfessionalSkill Skill, int Level) GunRequirement(Entity<GunComponent> gun)
    {
        if (gun.Comp.SkillRequiredLevel is { } explicitLevel)
            return (gun.Comp.RequiredSkill, explicitLevel);
        var rule = Requirement(gun, SkillAction.Shoot);
        return rule == null ? (ProfessionalSkill.Shooting, 1) : (rule.Skill, rule.Level);
    }

    public float GunMisfireChance(EntityUid user, Entity<GunComponent> gun)
    {
        if (!HasComp<ProfessionalSkillsComponent>(user))
            return 0;
        var rule = Requirement(gun, SkillAction.Shoot);
        var (skill, required) = GunRequirement(gun);
        var inaccurateAt = gun.Comp.InaccurateAtSkillLevel ?? rule?.InaccurateAtLevel;
        if (inaccurateAt == null || Level(user, skill) > inaccurateAt || Level(user, skill) < required)
            return 0;
        return Math.Clamp(gun.Comp.InaccurateAtSkillLevel != null ? gun.Comp.SkillMisfireChance : rule!.MisfireChance, 0, 1);
    }

    public double GunSkillSpreadDegrees(EntityUid user, Entity<GunComponent> gun)
    {
        var (skill, required) = GunRequirement(gun);
        if (!HasComp<ProfessionalSkillsComponent>(user) || skill != ProfessionalSkill.Shooting || required == 0)
            return 0;
        var level = Level(user, skill);
        if (level < required)
            return 0;
        return level switch { 1 => 10, 2 => 5, _ => 0 };
    }

    private void OnInterface(BoundUserInterfaceMessageAttempt args)
    {
        // Opening/reading/closing remains available. Reject actual commands on the server.
        if (args.Cancelled || args.Message is OpenBoundInterfaceMessage or CloseBoundInterfaceMessage)
            return;
        if (args.Message is Content.Shared.Cloning.CloningConsole.UiButtonPressedMessage
            { Button: Content.Shared.Cloning.CloningConsole.UiButton.Eject })
            return;
        if (Requirement(args.Target, SkillAction.Interface) is not { } rule
            || !rule.UiMessages.Contains(args.Message.GetType().Name))
            return;
        if (!Check(args.Actor, rule.Skill, rule.Level, serverPopup: true))
            args.Cancel();
    }
}
