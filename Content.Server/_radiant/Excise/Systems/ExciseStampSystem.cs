using Content.Server._NF.Cargo.Systems;
using Content.Server.Cargo.Systems;
using Content.Shared._radiant.Excise.Components;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._radiant.Excise.Systems;

/// <summary>
/// Handles excise stamps: crates whose label is an excise stamp are exempt from cargo sale tax.
/// </summary>
public sealed class ExciseStampSystem : EntitySystem
{
    [Dependency] private readonly LabelSystem _labels = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;

    /// <summary>
    /// Sums up the value of stamped-and-closed crates near the cargo console.
    /// This amount should be subtracted from the taxable price.
    /// </summary>
    public double CalculateTaxExemptAmount(EntityUid consoleUid)
    {
        var consoleXform = Transform(consoleUid);
        if (consoleXform.GridUid is not EntityUid gridUid)
            return 0;

        double total = 0;
        var query = AllEntityQuery<EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var storage, out var xform))
        {
            // Only crates on the same grid as the console, within pallet distance.
            if (xform.ParentUid != gridUid)
                continue;

            var distance = NFCargoSystem.CalculateDistance(xform.Coordinates, consoleXform.Coordinates);
            if (distance > 8)
                continue;

            if (storage.Open)
                continue;

            if (!_labels.TryGetLabel<ExciseStampComponent>((uid, null), out _))
                continue;

            // Crate + everything inside its containers is exempt.
            total += GetCrateAndContentsPrice(uid);
        }

        return total;
    }

    // Sums the price of the crate itself plus everything inside its containers.
    private double GetCrateAndContentsPrice(EntityUid uid)
    {
        var price = _pricing.GetPrice(uid, includeContents: false); // crate itself only

        if (TryComp<ContainerManagerComponent>(uid, out var containers))
        {
            foreach (var container in containers.Containers.Values)
            {
                foreach (var ent in container.ContainedEntities)
                    price += _pricing.GetPrice(ent); // includes nested contents
            }
        }

        return price;
    }
}

