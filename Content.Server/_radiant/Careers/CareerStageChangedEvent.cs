namespace Content.Server._radiant.Careers;

public sealed class CareerStageChangedEvent(EntityUid player) : EntityEventArgs
{
    public EntityUid Player = player;
}
