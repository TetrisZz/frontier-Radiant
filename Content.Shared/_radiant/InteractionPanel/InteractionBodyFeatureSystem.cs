using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Interaction.Panel;

public sealed class InteractionBodyFeatureSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;

    public bool IsAllowed(EntityUid user, EntityUid? target, InteractionPrototype prototype)
    {
        return HasRequiredFeatures(user, prototype.UserRequiredBodyFeatures)
            && (prototype.TargetRequiredBodyFeatures is not { Count: > 0 } targetFeatures
                || target is { } partner && HasRequiredFeatures(partner, targetFeatures));
    }

    public bool HasRequiredFeatures(EntityUid uid, List<HumanoidBodyFeature>? features)
    {
        if (features == null)
            return true;
        foreach (var feature in features)
        {
            if (!HasFeature(uid, feature))
                return false;
        }
        return true;
    }

    public bool HasFeature(EntityUid uid, HumanoidBodyFeature feature)
    {
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var appearance))
            return false;

        // Equipment only conceals features. Permanent hiding also represents removed parts.
        if (feature == HumanoidBodyFeature.Tail
            && !appearance.PermanentlyHidden.Contains(HumanoidVisualLayers.Tail)
            && TryComp<BodyComponent>(uid, out var body)
            && _body.BodyHasPartType(uid, BodyPartType.Tail, body))
            return true;

        foreach (var markings in appearance.MarkingSet.Markings.Values)
        {
            foreach (var marking in markings)
            {
                if (!marking.Visible
                    || !_prototypes.TryIndex<MarkingPrototype>(marking.MarkingId, out var prototype)
                    || !prototype.BodyFeatures.Contains(feature)
                    || appearance.PermanentlyHidden.Contains(prototype.BodyPart))
                    continue;

                if (GetLayer(appearance, prototype.BodyPart) is { AllowsMarkings: true })
                    return true;
            }
        }

        foreach (var layer in Enum.GetValues<HumanoidVisualLayers>())
        {
            if (!appearance.PermanentlyHidden.Contains(layer)
                && GetLayer(appearance, layer) is { BaseSprite: not null } sprite
                && sprite.BodyFeatures.Contains(feature))
                return true;
        }
        return false;
    }

    private HumanoidSpeciesSpriteLayer? GetLayer(HumanoidAppearanceComponent appearance, HumanoidVisualLayers layer)
    {
        string? id;
        if (appearance.CustomBaseLayers.TryGetValue(layer, out var custom))
            id = custom.Id;
        else if (_prototypes.TryIndex(appearance.Species, out var species)
                 && _prototypes.TryIndex(species.SpriteSet, out var sprites)
                 && sprites.Sprites.TryGetValue(layer, out var baseId))
            id = HumanoidVisualLayersExtension.GetSexMorph(layer, appearance.Sex, baseId);
        else
            return null;

        return id != null && _prototypes.TryIndex<HumanoidSpeciesSpriteLayer>(id, out var prototype)
            ? prototype
            : null;
    }
}
