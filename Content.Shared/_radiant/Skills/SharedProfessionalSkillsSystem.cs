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
    private readonly Dictionary<(string, SkillAction, string?), ProfessionalSkillRequirementPrototype?> _cache = new();

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
        => Level(user, ProfessionalSkill.Medicine) >= 4
            || id != null && _prototypes.TryIndex<Content.Shared.Chemistry.Reagent.ReagentPrototype>(id, out var reagent)
                && IsCommonReagent(reagent);

    public static int ConstructionLevel(string graph, bool structure = true)
    {
        if (graph.Contains("Tesla", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Singularity", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("ParticleAccelerator", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Containment", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Emitter", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (graph.Contains("Pipe", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Canister", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Vent", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Scrubber", StringComparison.OrdinalIgnoreCase)
            || graph.Contains("Gas", StringComparison.OrdinalIgnoreCase))
            return 2;
        return structure ? 1 : 0;
    }

    public ProfessionalSkillRequirementPrototype? Requirement(EntityUid target, SkillAction action, string? uiMessage = null)
    {
        if (MetaData(target).EntityPrototype is not { } proto)
            return null;
        var key = (proto.ID, action, uiMessage);
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
                if ((rule.Actions & action) != 0 && rule.Targets.Contains(id) &&
                    (uiMessage == null || rule.UiMessages.Count == 0 || rule.UiMessages.Contains(uiMessage)))
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
        if (!HasGunSkillPenalty(user, gun, out var level) || level >= 3)
            return 0;
        return 0.1f;
    }

    /// <summary>Additional cone width, including weapons with no native spread.</summary>
    public Angle GunSkillSpread(EntityUid user, Entity<GunComponent> gun)
        => HasGunSkillPenalty(user, gun, out var level) && level <= 3 ? Angle.FromDegrees(5) : Angle.Zero;

    private bool HasGunSkillPenalty(EntityUid user, Entity<GunComponent> gun, out int level)
    {
        level = Level(user, ProfessionalSkill.Shooting);
        var (skill, required) = GunRequirement(gun);
        // Tools gated by another profession have no shooting prerequisite, but still use Gun.
        var shootingTier = skill == ProfessionalSkill.Shooting ? required : 0;
        return HasComp<ProfessionalSkillsComponent>(user) && Level(user, skill) >= required && level == shootingTier;
    }

    public bool CanControlShipWeapons(EntityUid user, EntityUid console, bool popup = false, bool serverPopup = false)
    {
        var rule = Requirement(console, SkillAction.Interface,
            nameof(Content.Shared._Mono.FireControl.FireControlConsoleFireMessage));
        return Check(user, rule?.Skill ?? ProfessionalSkill.Shooting, rule?.Level ?? 4, popup, serverPopup);
    }

    private void OnInterface(BoundUserInterfaceMessageAttempt args)
    {
        if (args.Cancelled || args.Message is CloseBoundInterfaceMessage)
            return;
        // Selective rules allow inspection. Rules without a command list gate the entire interface.
        if (args.Message is OpenBoundInterfaceMessage)
        {
            if (Requirement(args.Target, SkillAction.Interface) is { UiMessages.Count: 0 } opening
                && !Check(args.Actor, opening.Skill, opening.Level, serverPopup: true))
                args.Cancel();
            return;
        }
        if (args.Message is Content.Shared.Cloning.CloningConsole.UiButtonPressedMessage
            { Button: Content.Shared.Cloning.CloningConsole.UiButton.Eject })
            return;
        if (Requirement(args.Target, SkillAction.Interface, args.Message.GetType().Name) is not { } rule)
            return;
        if (!Check(args.Actor, rule.Skill, rule.Level, serverPopup: true))
            args.Cancel();
    }
}
