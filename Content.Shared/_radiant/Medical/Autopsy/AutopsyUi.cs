using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Autopsy;

[RegisterComponent]
public sealed partial class AutopsyScannerComponent : Component
{
    [DataField] public int RequiredSkill = 2;
    [DataField] public int DetailedSkill = 3;
    [DataField] public float ScanSeconds = 8;
    [DataField] public string Report = "";
    [DataField] public string Summary = "";
    [DataField] public string Subject = "";
    public EntityUid? Target;
    public bool Busy;
    public bool Recorded;
}

[Serializable, NetSerializable]
public enum AutopsyUiKey : byte { Key }
[Serializable, NetSerializable]
public enum AutopsyAction : byte { Detailed, Print, Record }
[Serializable, NetSerializable]
public sealed class AutopsyActionMessage(AutopsyAction action) : BoundUserInterfaceMessage
{
    public AutopsyAction Action = action;
}
[Serializable, NetSerializable]
public sealed class AutopsyUiState(string report, bool detailed, bool record, bool busy, int paper) : BoundUserInterfaceState
{
    public string Report = report;
    public bool Detailed = detailed;
    public bool Record = record;
    public bool Busy = busy;
    public int Paper = paper;
}
[Serializable, NetSerializable]
public sealed partial class AutopsyScanDoAfterEvent : SimpleDoAfterEvent
{
    [DataField] public bool Detailed;
}
