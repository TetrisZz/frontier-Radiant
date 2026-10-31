using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Artisan;

[Serializable, NetSerializable]
public enum ArtisanStationKind : byte { Pottery, Kiln, Furnace, Anvil, GlassBench, Quench, Fermenter, Bottler, Sculpting }
[Serializable, NetSerializable]
public enum ArtisanToolKind : byte { None, Tongs, Hammer, Blowpipe, Paddle, Chisel }

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArtisanStationComponent : Component
{
    public const string Container = "artisan_inputs";
    public const string Solution = "artisan_tank";
    [DataField(required: true)] public ArtisanStationKind Kind;
    [DataField] public int Capacity = 20;
    [DataField] public bool NeedsPower;
    [DataField] public float Temperature = 20;
    [DataField] public float MaximumTemperature = 1100;
    [DataField] public float HeatingRate = 60;
    [DataField] public string BatchName = "";
    [DataField] public int BottleAmount = 30;
    [DataField] public bool Bottling;
    [DataField] public float Remaining;
    [DataField] public float Duration;
    [DataField] public string? Recipe;
    [DataField] public EntityUid? Workpiece;
    [DataField] public EntityUid? Operator;
    [DataField] public EntityUid? LiquidSource;
    [DataField] public string Maker = "";
    [DataField] public bool Reheating;
    [DataField, AutoNetworkedField] public bool DrainMode;
    [AutoNetworkedField] public bool LiquidLocked;
    [DataField] public SoundSpecifier? FillModeSound;
    [DataField] public SoundSpecifier? DrainModeSound;
    public float UiCountdown;
    public bool Busy => Remaining > 0;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArtisanWorkpieceComponent : Component
{
    [DataField(required: true), AutoNetworkedField] public ProtoId<ArtisanRecipePrototype> Recipe;
    [DataField, AutoNetworkedField] public int Stage;
    // Remaining working heat is a duration, so pauses and shuttle serialization cannot create stale timestamps.
    [DataField, AutoNetworkedField] public float Heat;
    [DataField, AutoNetworkedField] public string Maker = "";
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ArtisanProductComponent : Component
{
    [DataField, AutoNetworkedField] public Color Glaze = Color.White;
    [DataField, AutoNetworkedField] public string Maker = "";
}

[RegisterComponent]
public sealed partial class ArtisanToolComponent : Component
{
    public const string Container = "artisan_tool";
    [DataField(required: true)] public ArtisanToolKind Kind;
}

[Prototype]
public sealed partial class ArtisanRecipePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public EntProtoId Result;
    [DataField] public EntProtoId? Blank;
    [DataField] public string? Stack;
    [DataField] public int Amount = 1;
    [DataField] public List<EntProtoId> Ingredients = new();
    [DataField] public int IngredientCount = 3;
    [DataField] public string? Drink;
    [DataField] public FixedPoint2 DrinkAmount = 60;
    [DataField(required: true)] public List<ArtisanStep> Steps = new();
}

[DataDefinition]
public sealed partial class ArtisanStep
{
    [DataField(required: true)] public ArtisanStationKind Station;
    [DataField(required: true)] public LocId Name;
    [DataField] public ArtisanToolKind Tool;
    [DataField] public float Duration = 5;
    [DataField] public float MinTemperature;
    [DataField] public bool NeedsHeat;
    [DataField] public bool GivesHeat;
    [DataField] public FixedPoint2 Water;
    [DataField] public SoundSpecifier? StartSound;
    [DataField] public SoundSpecifier? FinishSound;
}
