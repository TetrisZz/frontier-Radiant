namespace Content.Server._radiant.Storage;

// Passwords persist with the entity, but this server-only component is never sent to clients.
[RegisterComponent]
public sealed partial class PasswordSafeCodeComponent : Component
{
    [DataField]
    public string? Code;

    [ViewVariables]
    public TimeSpan NextAttempt;
}
