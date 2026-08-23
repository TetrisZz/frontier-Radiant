using Robust.Shared.Serialization;

namespace Content.Shared._Goobstation.Languages;

/// <summary>
/// Radiant Sector: asks the server for the local character's available speech languages.
/// </summary>
[Serializable, NetSerializable]
public sealed class LanguageMenuRequestEvent : EntityEventArgs;

/// <summary>
/// Radiant Sector: asks to use either Galactic Common or the species' native language.
/// </summary>
[Serializable, NetSerializable]
public sealed class LanguageMenuSelectEvent(bool native) : EntityEventArgs
{
    public bool Native { get; } = native;
}

/// <summary>
/// Radiant Sector: server-authoritative state displayed by the language selection window.
/// </summary>
[Serializable, NetSerializable]
public sealed class LanguageMenuStateEvent(string nativeLanguage, bool nativeSelected, bool canUseNative, bool canUseGalactic) : EntityEventArgs
{
    public string NativeLanguage { get; } = nativeLanguage;
    public bool NativeSelected { get; } = nativeSelected;
    public bool CanUseNative { get; } = canUseNative;
    public bool CanUseGalactic { get; } = canUseGalactic;
}
