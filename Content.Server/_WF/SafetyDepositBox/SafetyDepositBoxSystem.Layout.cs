using System.Linq;
using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Shared.Utility;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    private const string DepositLocationKey = "depositLocation";
    private const int LegacyLayoutSearchBudget = 50_000;

    [Dependency] private SharedItemSystem _items = default!;

    private List<EntityUid> RestoreDepositLayout(Entity<StorageComponent> box, List<(EntityUid Item, ItemStorageLocation? Location)> items)
    {
        var restored = 0;
        foreach (var (item, location) in items)
        {
            if (location is not { } spot
                || !_storage.InsertAt(box.AsNullable(), item, spot, out _, playSound: false, stackAutomatically: false))
                break;
            restored++;
        }

        if (restored == items.Count)
            return [];

        // Old records have no positions. Also handle positions invalidated by a prototype update.
        _containers.EmptyContainer(box.Comp.Container, force: true);
        var overflow = new List<EntityUid>();
        if (FindDepositLayout(box, items) is { } positions)
        {
            foreach (var (item, location) in positions)
            {
                if (!_storage.InsertAt(box.AsNullable(), item, location, out _, playSound: false, stackAutomatically: false))
                    overflow.Add(item);
            }
            return overflow;
        }

        // Preserve whatever still fits at its saved position, then fill the gaps.
        var remaining = new List<EntityUid>();
        foreach (var (item, location) in items)
        {
            if (location is not { } spot
                || !_storage.InsertAt(box.AsNullable(), item, spot, out _, playSound: false, stackAutomatically: false))
                remaining.Add(item);
        }

        foreach (var item in remaining.OrderByDescending(item => _items.GetItemShape(item).GetArea()))
        {
            if (!_storage.Insert(box, item, out _, storageComp: box.Comp, playSound: false, stackAutomatically: false))
                overflow.Add(item);
        }
        return overflow;
    }

    private List<(EntityUid Item, ItemStorageLocation Location)>? FindDepositLayout(
        Entity<StorageComponent> box,
        List<(EntityUid Item, ItemStorageLocation? Location)> items)
    {
        // All deposit box grids have at most 48 cells. Use a mask per placement so
        // searching alternative layouts never moves entities or fires insertion events.
        var cells = new Dictionary<Vector2i, ulong>();
        foreach (var rectangle in box.Comp.Grid)
        foreach (var cell in Cells(rectangle))
        {
            if (cells.ContainsKey(cell))
                continue;
            if (cells.Count == 64)
                return null;
            cells.Add(cell, 1UL << cells.Count);
        }

        var candidates = new List<(EntityUid Item, List<(ItemStorageLocation Location, ulong Mask)> Placements)>();
        var bounds = box.Comp.Grid.GetBoundingBox();
        foreach (var (item, _) in items)
        {
            if (!_storage.CanInsert(box, item, out _, box.Comp, ignoreStacks: true, ignoreLocation: true))
                return null;

            var placements = new List<(ItemStorageLocation, ulong)>();
            var masks = new HashSet<ulong>();
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var baseLocation = new ItemStorageLocation(Angle.FromDegrees(rotation * 90), Vector2i.Zero);
                var shape = _items.GetAdjustedItemShape(item, baseLocation);
                foreach (var origin in Cells(bounds))
                {
                    ulong mask = 0;
                    var fits = true;
                    foreach (var rectangle in shape)
                    foreach (var offset in Cells(rectangle))
                    {
                        fits &= cells.TryGetValue(origin + offset, out var bit);
                        mask |= bit;
                    }

                    if (fits && mask != 0 && masks.Add(mask))
                        placements.Add((baseLocation with { Position = origin }, mask));
                }
            }

            if (placements.Count == 0)
                return null;

            candidates.Add((item, placements));
        }

        candidates.Sort((a, b) => a.Placements.Count.CompareTo(b.Placements.Count));
        var layout = new List<(EntityUid, ItemStorageLocation)>(items.Count);
        var budget = LegacyLayoutSearchBudget;
        if (!PlaceNext(0, 0))
            return null;
        return layout;

        bool PlaceNext(int index, ulong occupied)
        {
            if (index == candidates.Count)
                return true;

            var (item, placements) = candidates[index];
            foreach (var (location, mask) in placements)
            {
                if (--budget < 0)
                    return false;
                if ((mask & occupied) != 0)
                    continue;
                layout.Add((item, location));
                if (PlaceNext(index + 1, occupied | mask))
                    return true;
                layout.RemoveAt(layout.Count - 1);
            }

            return false;
        }

        static IEnumerable<Vector2i> Cells(Box2i rectangle)
        {
            for (var x = rectangle.Left; x <= rectangle.Right; x++)
            for (var y = rectangle.Bottom; y <= rectangle.Top; y++)
                yield return new Vector2i(x, y);
        }
    }
}
