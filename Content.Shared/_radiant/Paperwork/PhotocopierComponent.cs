using System.Globalization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._radiant.Paperwork;

[Prototype("documentForm")]
public sealed partial class DocumentFormPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public LocId Category;
    [DataField(required: true)] public LocId Letterhead;
    [DataField(required: true)] public LocId Content;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class PhotocopierComponent : Component
{
    /// <summary>
    /// The available documentForm IDs. An empty list permits copying originals only.
    /// </summary>
    [DataField] public List<ProtoId<DocumentFormPrototype>> Forms = new();
    [DataField] public TimeSpan PrintDuration = TimeSpan.FromSeconds(2);
    [DataField] public int Paper = 20;
    [DataField] public int Capacity = 200;
    [DataField] public int Ink = 100;
    [DataField] public int InkCapacity = 200;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReadyAt;
}

public static class PaperworkClock
{
    public static string Date(DateTimeOffset utcNow)
        => utcNow.ToOffset(TimeSpan.FromHours(3)).AddYears(1000).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string Shift(TimeSpan duration)
        => $"{Math.Max(0, (int)duration.TotalHours):00}:{Math.Max(0, duration.Minutes):00}:{Math.Max(0, duration.Seconds):00}";
}

[Serializable, NetSerializable]
public enum PhotocopierUiKey : byte { Key }

[Serializable, NetSerializable]
public enum PhotocopierVisuals : byte { Printing }

[Serializable, NetSerializable]
public sealed record DocumentFormEntry(string Id, string Name, string Category, string Content);

[Serializable, NetSerializable]
public sealed class PhotocopierUiState(List<DocumentFormEntry> forms, int paper, int ink, bool original, bool ready)
    : BoundUserInterfaceState
{
    public readonly List<DocumentFormEntry> Forms = forms;
    public readonly int Paper = paper;
    public readonly int Ink = ink;
    public readonly bool Original = original;
    public readonly bool Ready = ready;
}

[Serializable, NetSerializable]
public sealed class PhotocopierPrintMessage(string? form, int copies) : BoundUserInterfaceMessage
{
    public readonly string? Form = form;
    public readonly int Copies = copies;
}

[RegisterComponent]
public sealed partial class PrinterTonerComponent : Component
{
    [DataField] public int Ink = 100;
}
