using Robust.Shared.GameObjects;
using System.Collections.Generic;
using Robust.Shared.Timing;
using Robust.Shared.Network;

namespace Content.Server._radiant.GridWanted;

[RegisterComponent]
public sealed partial class DangerZoneComponent : Component
{
    [DataField("reason")]
    public string Reason = "Проникновение в хранилище конфедерации с цель ограбления";

    // Храним по UserId (уникальный идентификатор аккаунта)
    [DataField]
    public Dictionary<NetUserId, TimeSpan> PlayerCooldowns = new();

    [DataField]
    public HashSet<NetUserId> AlreadyWantedThisRound = new();
}
