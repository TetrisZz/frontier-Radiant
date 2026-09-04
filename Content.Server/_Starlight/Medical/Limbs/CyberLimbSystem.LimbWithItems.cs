using Content.Shared._Starlight.Medical.Limbs;
using Content.Shared.Body.Part;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction.Components;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Medical.Limbs;
// Radiant sector: adapted to Frontier hands and body systems.
public sealed partial class CyberLimbSystem : EntitySystem
{
    public void InitializeLimbWithItems()
    {
        SubscribeLocalEvent<LimbItemDeployerComponent, ToggleLimbEvent>(OnLimbToggle);
    }

    private void OnLimbToggle(Entity<LimbItemDeployerComponent > ent, ref ToggleLimbEvent args)
    {
        if (!TryComp<LimbItemStorageComponent>(ent, out var storage))
            return;

        ent.Comp.Toggled = !ent.Comp.Toggled;

        if (ent.Comp.Toggled)
        {
            foreach (var item in storage.ItemEntities)
            {
                var handId = $"{ent.Owner}_{item}";
                var hands = EnsureComp<HandsComponent>(args.Performer);
                _hands.AddHand((args.Performer, hands), handId, HandLocation.Middle, whitelist: ent.Comp.HandWhitelist);
                _hands.DoPickup(args.Performer, handId, item, hands);
                EnsureComp<UnremoveableComponent>(item);
            }
        }
        else
        {
            var container = _container.EnsureContainer<Container>(ent.Owner, ent.Comp.ContainerId, out _);
            foreach (var item in storage.ItemEntities)
            {
                var handId = $"{ent.Owner}_{item}";
                RemComp<UnremoveableComponent>(item);
                _container.Insert(_slEnt.Entity<TransformComponent, MetaDataComponent, PhysicsComponent>(item), container, force: true);
                _hands.RemoveHand(args.Performer, handId);
            }
        }

        UpdateDeployedLimbVisual(ent, args.Performer);
        _audio.PlayPvs(ent.Comp.Sound, args.Performer);

        Dirty(ent);
    }

    // Radiant sector: Starlight defines separate humanoid sprite layers for a
    // closed cyberhand and for the same hand with its tools deployed. The port
    // previously toggled only the item hands and never applied the second layer.
    private void UpdateDeployedLimbVisual(Entity<LimbItemDeployerComponent> ent, EntityUid performer)
    {
        if (!TryComp<BodyPartComponent>(ent, out var part)
            || part.Body != performer
            || part.ToHumanoidLayers() is not { } visualLayer
            || !TryComp<HumanoidAppearanceComponent>(performer, out var humanoid))
            return;

        Dictionary<string, ProtoId<HumanoidSpeciesSpriteLayer>?>? layers = null;
        if (ent.Comp.Toggled && TryComp<BaseLayerIdToggledComponent>(ent, out var toggledLayers))
            layers = toggledLayers.Layers;
        else if (TryComp<BaseLayerIdComponent>(ent, out var baseLayers))
            layers = baseLayers.Layers;

        if (layers == null
            || !(layers.TryGetValue(humanoid.Species, out var layerId)
                 || layers.TryGetValue("Default", out layerId))
            || layerId is null)
            return;

        _humanoidAppearance.SetBaseLayerId(performer, visualLayer, layerId.Value, false, humanoid);
        var layerPrototype = _prototypeManager.Index<HumanoidSpeciesSpriteLayer>(layerId.Value);
        _humanoidAppearance.SetBaseLayerColor(
            performer,
            visualLayer,
            layerPrototype.MatchSkin ? humanoid.SkinColor : Color.White,
            false,
            humanoid);
        Dirty(performer, humanoid);
    }

}
