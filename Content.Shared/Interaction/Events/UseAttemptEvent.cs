namespace Content.Shared.Interaction.Events
{
    public sealed class UseAttemptEvent(EntityUid uid, EntityUid used, bool ignoreUseSkillRequirement = false) : CancellableEntityEventArgs
    {
        public EntityUid Uid { get; } = uid;

        public EntityUid Used = used;

        // Storage insertion is not use of the held item. Skill systems may ignore their
        // item-use restriction for that interaction while other blockers still apply.
        public bool IgnoreUseSkillRequirement { get; } = ignoreUseSkillRequirement;
    }
}
