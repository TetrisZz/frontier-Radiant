using Robust.Shared.Audio;

namespace Content.Server._radiant.ResearchBlueprints;

[RegisterComponent]
public sealed partial class BlueprintResearchPrinterComponent : Component
{
    [DataField("duration")]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    [DataField("scanFinishSound")]
    public SoundSpecifier ScanFinishSound =
        new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");

    [DataField("printSound")]
    public SoundSpecifier PrintSound =
        new SoundPathSpecifier("/Audio/Machines/printer.ogg");
}
