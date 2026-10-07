// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Content.Shared._radiant.Fishing.Components;
[RegisterComponent]
public sealed partial class FishComponent : Component
{
    public const float DefaultDifficulty = 0.021f;
    [DataField("difficulty")]
    public float FishDifficulty = DefaultDifficulty;
}
