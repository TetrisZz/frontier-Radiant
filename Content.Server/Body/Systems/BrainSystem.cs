using Content.Server.Body.Components;
using Content.Server.Ghost.Components;
using Content.Shared.Body.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Pointing;

namespace Content.Server.Body.Systems;

public sealed class BrainSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BrainComponent, OrganAddedToBodyEvent>((uid, _, args) => HandleMind(args.Body, uid));
        SubscribeLocalEvent<BrainComponent, OrganRemovedFromBodyEvent>((uid, _, args) => HandleMind(uid, args.OldBody));
        SubscribeLocalEvent<BrainComponent, PointAttemptEvent>(OnPointAttempt);
    }

    private void HandleMind(EntityUid newEntity, EntityUid oldEntity)
    {
        if (TerminatingOrDeleted(newEntity) || TerminatingOrDeleted(oldEntity))
            return;

        if (HasComp<BrainComponent>(newEntity))
            EntityManager.System<Content.Server._radiant.Medical.Surgery.BrainRestorationSystem>()
                .CaptureCareer(newEntity, oldEntity);
        else if (HasComp<BrainComponent>(oldEntity))
            EntityManager.System<Content.Server._radiant.Medical.Surgery.BrainRestorationSystem>()
                .RestoreCareer(oldEntity, newEntity, overwriteSkills: true);

        EnsureComp<MindContainerComponent>(newEntity);
        EnsureComp<MindContainerComponent>(oldEntity);

        var ghostOnMove = EnsureComp<GhostOnMoveComponent>(newEntity);
        ghostOnMove.MustBeDead = HasComp<MobStateComponent>(newEntity); // Don't ghost living players out of their bodies.

        if (!_mindSystem.TryGetMind(oldEntity, out var mindId, out var mind))
            return;

        _mindSystem.TransferTo(mindId, newEntity, mind: mind);
        // Account balance comes from the player's existing profile after attachment, never a saved amount.
        if (HasComp<Content.Shared._NF.Bank.Components.BankAccountComponent>(newEntity))
            EntityManager.System<Content.Server._NF.Bank.BankSystem>().RestoreCharacterAccount(newEntity);
    }

    private void OnPointAttempt(Entity<BrainComponent> ent, ref PointAttemptEvent args)
    {
        args.Cancel();
    }
}

