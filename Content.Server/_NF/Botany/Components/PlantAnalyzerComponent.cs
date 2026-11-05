using Content.Shared._NF.PlantAnalyzer;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;

namespace Content.Server.Botany.Components;

/// <summary>
///    After scanning, retrieves the target Uid to use with its related UI.
/// </summary>
[RegisterComponent]
public sealed partial class PlantAnalyzerComponent : Component
{
    [DataDefinition]
    public partial struct PlantAnalyzerSetting
    {
        [DataField]
        public bool AdvancedScan;

        [DataField]
        public float ScanDelay;

        [DataField]
        public float AdvScanDelay;
    }

    [DataField, ViewVariables]
    public PlantAnalyzerSetting Settings = new();

    // An in-progress scan belongs to this round, not to a saved item.
    [ViewVariables(VVAccess.ReadOnly)]
    public DoAfterId? DoAfter;

    [DataField]
    public SoundSpecifier? ScanningEndSound;

    /// <summary>
    ///    Runtime display cache for the last scanned plant. Contains round-local entity references.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public PlantAnalyzerScannedPlantInformation? PlantInfo;
}
