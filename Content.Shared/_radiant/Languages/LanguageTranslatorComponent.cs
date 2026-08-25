using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared._radiant.Languages;

/// <summary>
/// Marks a handheld scanner capable of translating written documents.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LanguageTranslatorComponent : Component
{
    /// <summary>
    /// Time required to scan a document.
    /// </summary>
    [DataField]
    public float ScanDelay = 2f;

    [DataField]
    public float ChargePerScan = 25f;
}

[Serializable, NetSerializable]
public enum LanguageTranslatorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class LanguageTranslatorUiState(string language, string text) : BoundUserInterfaceState
{
    public readonly string Language = language;
    public readonly string Text = text;
}

[Serializable, NetSerializable]
public sealed partial class LanguageTranslatorScanDoAfterEvent : SimpleDoAfterEvent;
