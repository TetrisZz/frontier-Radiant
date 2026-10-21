using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._radiant.Decorations;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalCartVisualsComponent : Component
{
    [DataField]
    public Dictionary<string, string> ToolStates = new()
    {
        ["Scalpel"] = "scalpel_normal", ["ScalpelAdvanced"] = "scalpel_advanced", ["ScalpelLaser"] = "scalpel_advanced",
        ["ScalpelAbductor"] = "scalpel_alien", ["ScalpelCyber"] = "scalpel_advanced", ["ScalpelRipper"] = "scalpel_cruel",
        ["Cautery"] = "cautery_normal", ["CauteryAbductor"] = "cautery_alien", ["CauteryCyber"] = "cautery_advanced", ["CauteryRipper"] = "cautery_cruel",
        ["Drill"] = "drill_normal", ["DrillCyber"] = "drill_normal", ["DrillRipper"] = "drill_normal",
        ["Hemostat"] = "hemostat_normal", ["HemostatAdvanced"] = "hemostat_normal", ["HemostatAbductor"] = "hemostat_alien",
        ["HemostatCyber"] = "hemostat_normal", ["HemostatRipper"] = "hemostat_cruel",
        ["Retractor"] = "retractor_normal", ["RetractorStarlight"] = "retractor_normal", ["RetractorAdvanced"] = "retractor_advanced",
        ["RetractorAbductor"] = "retractor_alien", ["RetractorCyber"] = "retractor_advanced", ["RetractorRipper"] = "retractor_cruel",
        ["Saw"] = "saw_normal", ["SawElectric"] = "saw_normal", ["SawAdvanced"] = "saw_normal",
        ["SawAbductor"] = "saw_alien", ["SawCyber"] = "saw_normal", ["SawRipper"] = "saw_normal",
        ["BoneSetter"] = "bonesetter", ["BoneSetterAdvanced"] = "bonesetter", ["BoneSetterAbductor"] = "bonesetter",
        ["BoneSetterCyber"] = "bonesetter", ["BoneSetterRipper"] = "bonesetter", ["BoneGel"] = "gel", ["BoneGelCyber"] = "gel", ["BoneGelRipper"] = "gel",
    };
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class AquariumComponent : Component
{
    [DataField] public int Food = 30;
    [DataField] public int MaxFood = 100;
    [DataField] public int Capacity = 4;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextFeeding;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class AquariumDecorationComponent : Component
{
    [DataField(required: true)] public string State = string.Empty;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AquariumFeedComponent : Component
{
    [DataField, AutoNetworkedField] public int Uses = 20;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class BeehiveComponent : Component
{
    [DataField] public bool Occupied;
    [DataField] public float PlantRange = 4f;
    [DataField] public float ProductionAmount = 2f;
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(30);
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextProduction;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class HoneyFrameComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class BeeColonyComponent : Component;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class HourglassComponent : Component
{
    [DataField] public int Duration = 60;
    [DataField] public bool Running;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan StartedAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan EndsAt;
}

[Serializable, NetSerializable]
public sealed class AquariumFishVisualState : ICloneable
{
    public readonly string[] States;
    public readonly bool[] Alive;
    public readonly Color[] Colors;
    public readonly bool[] Juveniles;

    public AquariumFishVisualState(string[] states, bool[] alive, Color[]? colors = null, bool[]? juveniles = null)
    {
        States = states;
        Alive = alive;
        Colors = colors ?? Array.Empty<Color>();
        Juveniles = juveniles ?? Array.Empty<bool>();
    }

    public object Clone()
    {
        return new AquariumFishVisualState((string[]) States.Clone(), (bool[]) Alive.Clone(), (Color[]) Colors.Clone(), (bool[]) Juveniles.Clone());
    }
}

[Serializable, NetSerializable]
public enum AquariumVisuals : byte { Fish, Decorations, Water }

[Serializable, NetSerializable]
public enum SurgicalCartVisuals : byte { Tools }

[Serializable, NetSerializable]
public enum HourglassVisuals : byte { State }

[Serializable, NetSerializable]
public enum HoneyFrameVisuals : byte { Filled }
