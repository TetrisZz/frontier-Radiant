namespace Content.Shared.Speech;

public sealed class ListenEvent : EntityEventArgs
{
    public readonly string Message;
    public readonly EntityUid Source;
    /// <summary>
    ///     Radiant Sector: the native language used by the speaker, if this was racial speech.
    /// </summary>
    public readonly string? Language;

    public ListenEvent(string message, EntityUid source, string? language = null)
    {
        Message = message;
        Source = source;
        Language = language;
    }
}

public sealed class ListenAttemptEvent : CancellableEntityEventArgs
{
    public readonly EntityUid Source;

    public ListenAttemptEvent(EntityUid source)
    {
        Source = source;
    }
}
