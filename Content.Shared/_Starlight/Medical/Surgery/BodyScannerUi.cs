using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Medical.Surgery;

// Radiant sector start - dedicated surgical body scanner interface.
[Serializable, NetSerializable]
public enum BodyScannerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum BodyScannerDiagnosticSeverity : byte
{
    Info,
    Warning,
    Critical,
    Cybernetic,
    Implant,
    Anatomy,
}

[Serializable, NetSerializable]
public sealed class BodyScannerDiagnosticEntry
{
    public string Text { get; private set; } = string.Empty;
    public BodyScannerDiagnosticSeverity Severity { get; private set; }

    public BodyScannerDiagnosticEntry()
    {
    }

    public BodyScannerDiagnosticEntry(string text, BodyScannerDiagnosticSeverity severity)
    {
        Text = text;
        Severity = severity;
    }
}

[Serializable, NetSerializable]
public sealed class BodyScannerBoundUserInterfaceState : BoundUserInterfaceState
{
    public NetEntity? Target { get; }
    public float Temperature { get; }
    public float BloodLevel { get; }
    public bool Bleeding { get; }
    public List<BodyScannerDiagnosticEntry> Diagnostics { get; }

    public BodyScannerBoundUserInterfaceState(
        NetEntity? target,
        float temperature,
        float bloodLevel,
        bool bleeding,
        List<BodyScannerDiagnosticEntry>? diagnostics = null)
    {
        Target = target;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        Bleeding = bleeding;
        Diagnostics = diagnostics ?? new();
    }
}
// Radiant sector end
