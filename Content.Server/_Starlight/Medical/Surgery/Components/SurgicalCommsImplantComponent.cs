namespace Content.Server._Starlight.Medical.Surgery.Components;

/// <summary>
/// Radiant sector: marks a body that currently receives radio channels from a surgical communications implant.
/// The tracked sets ensure extraction removes only the channels supplied by this implant.
/// </summary>
[RegisterComponent]
public sealed partial class SurgicalCommsImplantComponent : Component
{
    public HashSet<string> AddedReceiverChannels = [];
    public HashSet<string> AddedTransmitterChannels = [];
}
