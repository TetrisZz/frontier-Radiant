namespace Content.Shared._Goobstation.Languages;

/// <summary>
/// A character never learned their species' native language and can only use Galactic Common.
/// </summary>
[RegisterComponent]
public sealed partial class NativeLanguageUnfamiliarComponent : Component;

/// <summary>
/// A character communicates only in their species' native language and cannot understand Galactic Common.
/// </summary>
[RegisterComponent]
public sealed partial class NativeLanguageOnlyComponent : Component;
