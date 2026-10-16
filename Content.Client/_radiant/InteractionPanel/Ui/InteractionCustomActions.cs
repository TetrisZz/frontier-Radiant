using System.Linq;
using Content.Shared.Chat.Prototypes;

namespace Content.Client.Interaction.Panel.Ui;

public static class InteractionCustomActions
{
    public static List<InteractionPrototype> Merge(IEnumerable<InteractionPrototype> current,
        IEnumerable<InteractionPrototype> incoming, Func<string, bool> isBuiltIn,
        out int added, out int updated, out int skipped)
    {
        var result = current.ToDictionary(action => action.ID, StringComparer.Ordinal);
        added = updated = skipped = 0;
        foreach (var action in incoming)
        {
            if (isBuiltIn(action.ID))
            {
                skipped++;
                continue;
            }
            if (result.ContainsKey(action.ID)) updated++;
            else added++;
            result[action.ID] = action;
        }
        return result.Values.ToList();
    }
}
