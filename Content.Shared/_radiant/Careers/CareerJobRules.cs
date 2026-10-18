using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Careers;

public static class CareerJobRules
{
    public static bool IsAllowed(IPrototypeManager prototypes, string profession, string job)
    {
        if (!prototypes.TryIndex<CareerProfessionPrototype>(profession, out var definition))
            return false;
        return profession == "Adventurer"
            ? !prototypes.EnumeratePrototypes<CareerProfessionPrototype>()
                .Any(other => other.ID != "Adventurer" && other.Jobs.Contains(job))
            : definition.Jobs.Contains(job);
    }
}
