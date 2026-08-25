using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Paper;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PaperComponent : Component
{
    public PaperAction Mode;
    [DataField("content"), AutoNetworkedField]
    public string Content { get; set; } = "";

    /// <summary>
    /// Radiant Sector: null means Galactic Common; otherwise this is the native language used to write the document.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? Language { get; set; }

    [DataField("contentSize")]
    public int ContentSize { get; set; } = 10000;

    [DataField("stampedBy"), AutoNetworkedField]
    public List<StampDisplayInfo> StampedBy { get; set; } = new();

    /// <summary>
    ///     Stamp to be displayed on the paper, state from bureaucracy.rsi
    /// </summary>
    [DataField("stampState"), AutoNetworkedField]
    public string? StampState { get; set; }

    [DataField, AutoNetworkedField]
    public bool EditingDisabled;

    /// <summary>
    /// Sound played after writing to the paper.
    /// </summary>
    [DataField("sound")]
    public SoundSpecifier? Sound { get; private set; } = new SoundCollectionSpecifier("PaperScribbles", AudioParams.Default.WithVariation(0.1f));

    // Frontier:
    /// <summary>
    /// Sound played after writing to the paper.
    /// </summary>
    [DataField]
    public bool DestroyOnFax { get; private set; }

    [DataField]
    public string? DestroyMessage { get; private set; }
    // End Frontier

    [Serializable, NetSerializable]
    public sealed class PaperBoundUserInterfaceState : BoundUserInterfaceState
    {
        public readonly string Text;
        public readonly List<StampDisplayInfo> StampedBy;
        public readonly PaperAction Mode;
        public readonly string? Language;

        public PaperBoundUserInterfaceState(string text, List<StampDisplayInfo> stampedBy, PaperAction mode = PaperAction.Read, string? language = null)
        {
            Text = text;
            StampedBy = stampedBy;
            Mode = mode;
            Language = language;
        }
    }

    [Serializable, NetSerializable]
    public sealed class PaperInputTextMessage : BoundUserInterfaceMessage
    {
        public readonly string Text;
        public readonly bool NativeLanguage;

        public PaperInputTextMessage(string text, bool nativeLanguage = false)
        {
            Text = text;
            NativeLanguage = nativeLanguage;
        }
    }

    [Serializable, NetSerializable]
    public enum PaperUiKey
    {
        Key
    }

    [Serializable, NetSerializable]
    public enum PaperAction
    {
        Read,
        Write,
    }

    [Serializable, NetSerializable]
    public enum PaperVisuals : byte
    {
        Status,
        Stamp
    }

    [Serializable, NetSerializable]
    public enum PaperStatus : byte
    {
        Blank,
        Written
    }
}
