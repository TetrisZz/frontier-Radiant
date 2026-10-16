using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Careers;

[Serializable, NetSerializable]
public sealed class CareerRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class CareerSelectJobTitleEvent(Guid characterId, string title) : EntityEventArgs
{
    public Guid CharacterId = characterId;
    public string Title = title;
}

[Serializable, NetSerializable]
public sealed class CareerSelectEvent(string profession, bool confirmReset) : EntityEventArgs
{
    public string Profession = profession;
    public bool ConfirmReset = confirmReset;
}

[Serializable, NetSerializable]
public sealed class CareerStateEvent(CareerData career, bool canChange, string? message = null) : EntityEventArgs
{
    public CareerData Career = career;
    public bool CanChange = canChange;
    public string? Message = message;
}

[Serializable, NetSerializable]
public sealed class CareerClaimRewardsEvent(NetEntity console) : EntityEventArgs
{
    public NetEntity Console = console;
}
