using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Genetics;

[Serializable, NetSerializable]
public enum DnaVaultUiKey : byte { Key }
[Serializable, NetSerializable]
public sealed class DnaVaultMessage(string reward = "", bool eject = false) : BoundUserInterfaceMessage
{
    public readonly string Reward = reward;
    public readonly bool Eject = eject;
}
[Serializable, NetSerializable]
public sealed class DnaVaultState : BoundUserInterfaceState
{
    public int Humans, Plants, Animals, HumanGoal, PlantGoal, AnimalGoal, Biomass, Cost;
    public string Status = "", Sample = "", Archive = "";
    public float Progress;
    public bool CanPrepare, CanEject;
    public Dictionary<string, string> Rewards = new();
}
[Serializable, NetSerializable]
public sealed partial class BiodiversityScanEvent : SimpleDoAfterEvent;
