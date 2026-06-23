using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Animals;

/// <summary>
///     Gives the ability to produce a solution;
///     produces endlessly if the owner does not have a HungerComponent.
/// </summary>
[RegisterComponent, AutoGenerateComponentState, AutoGenerateComponentPause, NetworkedComponent]
public sealed partial class UdderComponent : Component
{
    /// <summary>
    ///     The reagent to produce.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ReagentPrototype> ReagentId = new();

    /// <summary>
    ///     Reagents to produce for specific humanoid species.
    /// </summary>
    [DataField]
    public Dictionary<string, ProtoId<ReagentPrototype>> SpeciesReagents = new();

    /// <summary>
    ///     Humanoid species that cannot produce or be milked.
    /// </summary>
    [DataField]
    public HashSet<string> BlockedSpecies = new();

    /// <summary>
    ///     The name of <see cref="Solution"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string SolutionName = "udder";

    /// <summary>
    ///     The solution to add reagent to.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Entity<SolutionComponent>? Solution = null;

    /// <summary>
    ///     The amount of reagent to be generated on update.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 QuantityPerUpdate = 25;

    /// <summary>
    ///     The amount of nutrient consumed on update.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HungerUsage = 10f;

    /// <summary>
    ///     How long to wait before producing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan GrowthDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     When to next try to produce.
    /// </summary>
    [DataField, AutoPausedField, Access(typeof(UdderSystem))]
    public TimeSpan NextGrowth = TimeSpan.Zero;

    /// <summary>
    ///     If set, only this sex can be milked and produce milk.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Sex? SexRestriction;

    /// <summary>
    ///     Inventory slots that must be empty before milking can begin.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> RequiredEmptySlots = new();
}
