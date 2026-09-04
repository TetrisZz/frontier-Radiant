using Content.Server.Actions;
using Content.Server.Hands.Systems;
using Content.Server.Humanoid;
using Content.Shared._Starlight;
using Robust.Server.Audio;
using Robust.Server.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Medical.Limbs;
public sealed partial class CyberLimbSystem : EntitySystem
{
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private StarlightEntitySystem _slEnt = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private HumanoidAppearanceSystem _humanoidAppearance = default!; // Radiant sector: deployed limb visuals.
    [Dependency] private IPrototypeManager _prototypeManager = default!; // Radiant sector: deployed limb visuals.

    public override void Initialize()
    {
        base.Initialize();
        InitializeLimbWithItems();
        InitializeToggleable();
    }
}
