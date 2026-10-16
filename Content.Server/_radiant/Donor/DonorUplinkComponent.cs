using Robust.Shared.Network;

namespace Content.Server._radiant.Donor;

/// <summary>Account ownership is deliberately kept on the server, not in UI state.</summary>
[RegisterComponent]
public sealed partial class DonorUplinkComponent : Component
{
    [DataField] public NetUserId? Owner;
    public TimeSpan NextFeedback;
}
