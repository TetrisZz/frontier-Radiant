// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._radiant.Fishing.Components;

public enum FishingBaitKind : byte
{
    None,
    Natural,
    Luminous,
}

[RegisterComponent]
public sealed partial class FishingBaitComponent : Component
{
    [DataField(required: true)]
    public FishingBaitKind Kind;
}
