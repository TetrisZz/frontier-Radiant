using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Fishing.Components;

[RegisterComponent]
public sealed partial class FishScannerComponent : Component
{
    [DataField] public TimeSpan ScanCooldown = TimeSpan.FromSeconds(0.5);
    public TimeSpan ReadyAt;
    public FishScannerUiState? LastScan;
}

[Serializable, NetSerializable]
public enum FishScannerUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed record FishScanEntry(string Name, string Sex, bool Juvenile, bool Breedable, bool InWater,
    string Health, float Damage, int Growth, Color Tint);

[Serializable, NetSerializable]
public sealed class FishScannerUiState(string target, bool aquarium, float water, int food, int capacity,
    List<FishScanEntry> fish, int charge) : BoundUserInterfaceState
{
    public readonly string Target = target;
    public readonly bool Aquarium = aquarium;
    public readonly float Water = water;
    public readonly int Food = food;
    public readonly int Capacity = capacity;
    public readonly List<FishScanEntry> Fish = fish;
    public readonly int Charge = charge;
}
