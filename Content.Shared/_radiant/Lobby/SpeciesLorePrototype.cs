using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Lobby;

/// <summary>
/// Localized, player-facing information displayed by the species picker in the character editor.
/// </summary>
[Prototype("speciesLore")]
public sealed partial class SpeciesLorePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> Species { get; private set; } = default!;

    [DataField(required: true)]
    public string Category { get; private set; } = default!;

    [DataField(required: true)]
    public string Lore { get; private set; } = default!;

    /// <summary>
    /// Radiant Sector: concise in-universe biology and appearance notes, kept separate from mechanics.
    /// </summary>
    [DataField]
    public List<string> Biology { get; private set; } = new();

    /// <summary>
    /// Radiant Sector: lore-facing diet notes. These never describe mechanics unless explicitly stated.
    /// </summary>
    [DataField]
    public List<string> Diet { get; private set; } = new();

    /// <summary>
    /// Radiant Sector: cultural context displayed before practical roleplay guidance.
    /// </summary>
    [DataField]
    public List<string> Culture { get; private set; } = new();

    /// <summary>
    /// Radiant Sector: lore-facing height text; deliberately independent of character-editor height limits.
    /// </summary>
    [DataField]
    public string? LoreHeight { get; private set; }

    [DataField]
    public List<string> Features { get; private set; } = new();

    [DataField]
    public List<string> GameplayStats { get; private set; } = new();

    /// <summary>
    /// Radiant Sector: active racial actions and passive abilities, listed independently from resistances.
    /// </summary>
    [DataField]
    public List<string> Abilities { get; private set; } = new();

    /// <summary>
    /// Radiant Sector: native and common languages displayed separately from roleplay advice.
    /// </summary>
    [DataField]
    public List<string> Languages { get; private set; } = new();

    [DataField]
    public List<string> Communication { get; private set; } = new();

    [DataField]
    public List<string> InteractionTips { get; private set; } = new();

    [DataField]
    public List<string> RoleplayTips { get; private set; } = new();
}
