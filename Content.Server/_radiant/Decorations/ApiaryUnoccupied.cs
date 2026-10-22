using Content.Shared._radiant.Decorations;
using Content.Shared.Construction;
using Content.Shared.Examine;
using JetBrains.Annotations;

namespace Content.Server._radiant.Decorations;

[UsedImplicitly, DataDefinition]
public sealed partial class ApiaryUnoccupied : IGraphCondition
{
    public bool Condition(EntityUid uid, IEntityManager entityManager) =>
        !entityManager.TryGetComponent<BeehiveComponent>(uid, out var hive) || !hive.Occupied;

    public bool DoExamine(ExaminedEvent args)
    {
        if (!IoCManager.Resolve<IEntityManager>().TryGetComponent<BeehiveComponent>(args.Examined, out var hive) || !hive.Occupied)
            return false;
        args.PushText(Loc.GetString("apiary-empty-first"));
        return true;
    }

    public IEnumerable<ConstructionGuideEntry> GenerateGuideEntry()
    {
        yield return new ConstructionGuideEntry { Localization = "apiary-empty-first" };
    }
}
