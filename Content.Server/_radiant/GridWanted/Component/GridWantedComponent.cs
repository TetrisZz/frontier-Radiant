using Robust.Shared.GameObjects;
using System.Collections.Generic;
using Robust.Shared.Timing;

namespace Content.Server._radiant.GridWanted;

[RegisterComponent]
public sealed partial class DangerZoneComponent : Component
{
    [DataField("reason")]
    public string Reason = "Долбаеб";

    [DataField]
    public HashSet<EntityUid> PreviousPlayers = new();

    [DataField]
    public Dictionary<string, TimeSpan> LastWantedNotification = new();

    [DataField]
    public HashSet<string> AlreadyWantedPlayers = new();
}
