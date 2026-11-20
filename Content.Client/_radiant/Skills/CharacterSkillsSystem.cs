using Content.Client.CharacterInfo;
using Content.Client._radiant.Careers;
using Content.Shared._radiant.Skills;
using Robust.Client.Player;

namespace Content.Client._radiant.Skills;

public sealed class CharacterSkillsSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnCharacterControls,
            after: [typeof(CareerSystem)]);
    }

    private void OnCharacterControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent ev)
    {
        // Read the current body, since lobby preferences may already describe the next character.
        if (ev.Entity == _players.LocalEntity && TryComp<ProfessionalSkillsComponent>(ev.Entity, out var skills))
            ev.Controls.Add(new CharacterSkillsControl(skills.Levels));
    }
}
