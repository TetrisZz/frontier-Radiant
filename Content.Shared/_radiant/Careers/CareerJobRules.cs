using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Careers;

public static class CareerJobRules
{
    public static bool IsCareerPreference(IPrototypeManager prototypes, string profession, string job)
        => profession != "Adventurer" && prototypes.TryIndex<CareerProfessionPrototype>(profession, out var definition)
            && definition.Jobs.Contains(job);

    public static bool IsAllowed(IPrototypeManager prototypes, string profession, string job)
    {
        if (!prototypes.TryIndex<CareerProfessionPrototype>(profession, out var definition))
            return false;
        return profession == "Adventurer"
            ? !prototypes.EnumeratePrototypes<CareerProfessionPrototype>()
                .Any(other => other.ID != "Adventurer" && (other.Jobs.Contains(job) || other.InterviewJob?.Id == job))
            : definition.Jobs.Contains(job) || definition.InterviewJob?.Id == job;
    }
}
