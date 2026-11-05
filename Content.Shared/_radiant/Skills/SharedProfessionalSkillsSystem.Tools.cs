using Content.Shared.DoAfter;
using Content.Shared.Construction.Components;
using Content.Shared.Interaction;
using Content.Shared.Tools.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Skills;

public sealed partial class SharedProfessionalSkillsSystem
{
    [Dependency] private SharedDoAfterSystem _skillDoAfter = default!;
    private readonly HashSet<EntityUid> _preparedToolUsers = new();

    /// <summary>
    /// Installing a machine uses its installation requirement and the tool's skill,
    /// rather than the profession needed to operate the machine.
    /// </summary>
    public bool CanInteractUsing(EntityUid user, EntityUid used, EntityUid target)
    {
        var action = TryComp<AnchorableComponent>(target, out var anchorable)
            && EntityManager.System<SharedToolSystem>().HasQuality(used, anchorable.Tool)
                ? SkillAction.Anchor
                : SkillAction.Interact;
        return CanUse(user, target, action);
    }

    public TimeSpan ToolDelay(EntityUid user, EntityUid tool, TimeSpan duration, float speed)
    {
        if (Level(user, ProfessionalSkill.Engineering) != 0
            || Requirement(tool, SkillAction.Use) is not { Skill: ProfessionalSkill.Engineering })
            return duration;
        var seconds = Math.Max(5, duration.TotalSeconds / speed * 2);
        // InteractUsing has already waited five seconds; do not charge that time twice.
        if (_preparedToolUsers.Contains(user))
            seconds = Math.Max(0, seconds - 5);
        return TimeSpan.FromSeconds(seconds * speed);
    }

    private void InitializeToolDelays()
        => SubscribeLocalEvent<ProfessionalSkillsComponent, NoviceToolDoAfterEvent>(OnNoviceToolComplete);

    public bool DelayNoviceTool(EntityUid user, EntityUid used, EntityUid target)
    {
        if (Level(user, ProfessionalSkill.Engineering) != 0
            || Requirement(used, SkillAction.Use) is not { Skill: ProfessionalSkill.Engineering, Level: 0 })
            return false;
        _skillDoAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, 5f, new NoviceToolDoAfterEvent(),
            user, target: target, used: used)
        {
            NeedHand = true, BreakOnMove = true, BreakOnDamage = true,
        });
        return true;
    }

    private void OnNoviceToolComplete(Entity<ProfessionalSkillsComponent> ent, ref NoviceToolDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || args.Used is not { } used
            || !Exists(target) || !Exists(used))
            return;
        args.Handled = true;
        var interaction = EntityManager.System<SharedInteractionSystem>();
        if (!interaction.InRangeUnobstructed(args.User, target))
            return;
        _preparedToolUsers.Add(args.User);
        try
        {
            interaction.InteractUsing(args.User, used, target,
                Transform(target).Coordinates, skillDelayComplete: true);
        }
        finally
        {
            _preparedToolUsers.Remove(args.User);
        }
    }
}

[Serializable, NetSerializable]
public sealed partial class NoviceToolDoAfterEvent : SimpleDoAfterEvent;
