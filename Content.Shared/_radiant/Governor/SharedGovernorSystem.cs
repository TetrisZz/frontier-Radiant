using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Governor;

[NetSerializable, Serializable]
public enum GovernorConsoleUiKey : byte
{
    Bounty
}

public abstract class SharedGovernorSystem : EntitySystem { }

[NetSerializable, Serializable]
public sealed class GovernorBountyConsoleState : BoundUserInterfaceState
{
    public List<GovernorBountyData> Bounties;
    public TimeSpan UntilNextSkip;

    public GovernorBountyConsoleState(List<GovernorBountyData> bounties, TimeSpan untilNextSkip)
    {
        Bounties = bounties;
        UntilNextSkip = untilNextSkip;
    }
}


[Serializable, NetSerializable]
public sealed class GovernorBountyAcceptMessage : BoundUserInterfaceMessage
{
    public string BountyId;

    public GovernorBountyAcceptMessage(string bountyId)
    {
        BountyId = bountyId;
    }
}

[Serializable, NetSerializable]
public sealed class GovernorBountySkipMessage : BoundUserInterfaceMessage
{
    public string BountyId;

    public GovernorBountySkipMessage(string bountyId)
    {
        BountyId = bountyId;
    }
}

[Serializable, NetSerializable]
public sealed class GovernorBountyRedeemMessage : BoundUserInterfaceMessage
{
    public GovernorBountyRedeemMessage() { }
}
