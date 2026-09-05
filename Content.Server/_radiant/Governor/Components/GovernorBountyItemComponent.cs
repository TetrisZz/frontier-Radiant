using Content.Shared._radiant.Governor;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Governor.Components;

[RegisterComponent]
public sealed partial class GovernorBountyItemComponent : Component
{
    [IdDataField]
    public string ID;
}
