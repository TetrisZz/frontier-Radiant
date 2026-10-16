namespace Content.Server._radiant.Careers;

/// <summary>Detached from the sold entities before cargo deletes them. Value already includes the market modifier.</summary>
public sealed record CareerSaleItem(
    IReadOnlyCollection<string> PrototypeIds,
    IReadOnlyCollection<string> Tags,
    int Quantity,
    double Value);

public sealed record CareerSaleSnapshot(IReadOnlyList<CareerSaleItem> Items, string? Location);

/// <summary>Raised only after the primary cash payout has been spawned. Payout excludes bonus currencies.</summary>
[ByRefEvent]
public readonly record struct CareerSaleCompletedEvent(EntityUid Seller, CareerSaleSnapshot Snapshot, int Payout);
