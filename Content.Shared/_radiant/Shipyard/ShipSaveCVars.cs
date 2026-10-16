using Robust.Shared.Configuration;

namespace Content.Shared._radiant.Shipyard;

[CVarDefs]
public sealed class ShipSaveCVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("radiant.ship_save.enabled", true, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> LoadShipPrice =
        CVarDef.Create("triad.load_ship_price", 0.15f, CVar.SERVER | CVar.REPLICATED);
}
