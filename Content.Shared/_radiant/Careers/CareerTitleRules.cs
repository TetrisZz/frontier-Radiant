using System.Linq;

namespace Content.Shared._radiant.Careers;

public static class CareerTitleRules
{
    public static CareerJobTitle? GetSelected(CareerProfessionPrototype profession, CareerData career)
    {
        return profession.JobTitles.FirstOrDefault(title => title.Id == career.JobTitle && IsUnlocked(title, career))
            ?? profession.JobTitles.FirstOrDefault(title => IsUnlocked(title, career));
    }

    public static bool IsUnlocked(CareerJobTitle title, CareerData career) => title.RequiredLevel <= career.Level;
}
