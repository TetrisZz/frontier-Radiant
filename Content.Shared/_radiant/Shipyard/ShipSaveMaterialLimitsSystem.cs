using Content.Shared.Examine;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Shipyard;

public sealed class ShipSaveMaterialLimitsSystem : EntitySystem
{
    [Dependency] private readonly SharedMaterialStorageSystem _materials = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipSaveMaterialLimitsComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(Entity<ShipSaveMaterialLimitsComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("ship-save-materials-examine"));
    }

    public int GetSavedVolume(ShipSaveMaterialLimitsComponent limits, ProtoId<MaterialPrototype> material, int volume)
    {
        if (!_prototypes.TryIndex(material, out var prototype))
            return 0;

        var sheetVolume = _materials.GetSheetVolume(prototype);
        if (sheetVolume <= 0)
            return 0;

        var count = Math.Max(0, limits.Limits.GetValueOrDefault(material, limits.DefaultLimit));
        var limitVolume = Math.Min((long) count * sheetVolume, int.MaxValue);
        return (int) Math.Clamp((long) volume, 0, limitVolume);
    }

    public Dictionary<ProtoId<MaterialPrototype>, int> GetSavedMaterials(ShipSaveMaterialLimitsComponent limits,
        IReadOnlyDictionary<ProtoId<MaterialPrototype>, int> materials)
    {
        var saved = new Dictionary<ProtoId<MaterialPrototype>, int>();
        foreach (var (material, volume) in materials)
        {
            var retained = GetSavedVolume(limits, material, volume);
            if (retained > 0)
                saved.Add(material, retained);
        }
        return saved;
    }
}
