using Content.Shared.Body.Part;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Medical.Limbs;

/// <summary>
/// Radiant sector: converts loose Starlight cyberlimbs between their left and right prototypes.
/// </summary>
public sealed class ReversibleCyberLimbSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ReversibleCyberLimbComponent, AttemptSimpleToolUseEvent>(OnAttempt);
        SubscribeLocalEvent<ReversibleCyberLimbComponent, SimpleToolDoAfterEvent>(OnConverted);
    }

    private void OnAttempt(Entity<ReversibleCyberLimbComponent> ent, ref AttemptSimpleToolUseEvent args)
    {
        if (!TryComp<BodyPartComponent>(ent, out var part) || part.Body != null || !TryGetCounterpart(ent, out _))
        {
            args.Cancelled = true;
            _popup.PopupClient(Loc.GetString("cyber-limb-reverse-unavailable"), ent, args.User);
        }
    }

    private void OnConverted(Entity<ReversibleCyberLimbComponent> ent, ref SimpleToolDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled ||
            !TryComp<BodyPartComponent>(ent, out var part) ||
            part.Body != null ||
            !TryGetCounterpart(ent, out var counterpart))
            return;

        var replacement = PredictedSpawnAtPosition(counterpart, Transform(ent).Coordinates);
        PredictedDel(ent.Owner);

        _popup.PopupPredicted(Loc.GetString("cyber-limb-reverse-success", ("limb", replacement)),
            replacement,
            args.User);
        args.Handled = true;
    }

    private bool TryGetCounterpart(EntityUid uid, out EntProtoId counterpart)
    {
        counterpart = default;
        var id = MetaData(uid).EntityPrototype?.ID;
        if (id == null)
            return false;

        string mirror;
        if (id.StartsWith("Left", StringComparison.Ordinal))
            mirror = $"Right{id[4..]}";
        else if (id.StartsWith("Right", StringComparison.Ordinal))
            mirror = $"Left{id[5..]}";
        else
            return false;

        if (!_prototypes.HasIndex<EntityPrototype>(mirror))
            return false;

        counterpart = mirror;
        return true;
    }
}
