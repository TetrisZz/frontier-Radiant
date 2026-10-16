using Robust.Shared.Network;

namespace Content.Server._radiant.Careers;

[RegisterComponent]
public sealed partial class CareerActorComponent : Component
{
    public NetUserId User;
    public int Slot;
    public Guid CharacterId;
    public string Job = string.Empty;
}
