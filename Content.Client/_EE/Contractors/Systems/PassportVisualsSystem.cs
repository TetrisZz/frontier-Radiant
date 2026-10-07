using Content.Shared._EE.Contractors.Components;
using Robust.Client.GameObjects;

namespace Content.Client._EE.Contractors.Systems;

public sealed class PassportVisualsSystem : EntitySystem
{
    private static readonly HashSet<string> PortraitSpecies = new()
    {
        "human", "oni", "arachnid", "diona", "dwarf", "felinid", "harpy", "ipc",
        "moth", "reptilian", "shadowkin", "slimeperson", "tajaran", "vulpkanin",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PassportComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PassportComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnInit(Entity<PassportComponent> ent, ref ComponentInit args) => UpdateSprite(ent);
    private void OnState(Entity<PassportComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateSprite(ent);

    private void UpdateSprite(Entity<PassportComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (ent.Comp.Design == "card_nt")
            return;

        var prefix = ent.Comp.Design.Length == 0 ? "passport" : $"passport_{ent.Comp.Design}";
        sprite.LayerSetState(0, $"{prefix}_{(ent.Comp.IsClosed ? "closed" : "open")}");
        var species = ent.Comp.Species.ToLowerInvariant();
        var hasPortrait = PortraitSpecies.Contains(species);
        sprite.LayerSetVisible(1, !ent.Comp.IsClosed && hasPortrait);
        if (hasPortrait)
            sprite.LayerSetState(1, "passport_species_" + species);
    }
}
