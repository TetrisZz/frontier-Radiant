using System.Linq;
using Content.Shared._radiant.Careers;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Careers;

public sealed class CareerConstructionSystem : EntitySystem
{
    [Dependency] private readonly CareerSystem _careers = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private static readonly HashSet<string> FinishedStructures = new()
    {
        "BaseStructureWall", "Airlock", "Window", "WindowDirectional", "BaseAPC",
        "BaseSubstation", "BaseSMES", "Thruster", "Gyroscope",
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<CareerConstructionCompletedEvent>(OnCompleted);
    }

    private void OnCompleted(ref CareerConstructionCompletedEvent args)
    {
        if (!Exists(args.Entity))
            return;

        // Carry credit across machine/frame and wall/girder replacements, including mapped machines.
        if (args.Previous is { } old && Exists(old)
            && (HasComp<CareerConstructionCreditComponent>(old) || FinishedStructures.Overlaps(PrototypeIds(old))))
            EnsureComp<CareerConstructionCreditComponent>(args.Entity);

        var ids = PrototypeIds(args.Entity);
        if (!FinishedStructures.Overlaps(ids) || HasComp<CareerConstructionCreditComponent>(args.Entity))
            return;

        EnsureComp<CareerConstructionCreditComponent>(args.Entity);
        if (args.User is not { } user || !_careers.IsWorking(user))
            return;

        var participants = _careers.GetProgressParticipants(user).ToArray();
        foreach (var participant in participants)
            _careers.RecordActivity(participant, CareerObjectiveType.Construction, 1, ids, Array.Empty<string>());
    }

    private HashSet<string> PrototypeIds(EntityUid uid)
    {
        var ids = new HashSet<string>();
        if (MetaData(uid).EntityPrototype is { } prototype)
        {
            // Abstract construction bases have no indexed EntityPrototype instance.
            foreach (var (id, _) in _prototypes.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true))
                ids.Add(id);
        }
        return ids;
    }
}

[RegisterComponent]
public sealed partial class CareerConstructionCreditComponent : Component;
