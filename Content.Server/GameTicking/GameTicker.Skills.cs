using Content.Shared._radiant.Skills;
using Content.Shared.Preferences;
using Robust.Shared.Player;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    private bool CheckSkillSelection(ICommonSession player, HumanoidCharacterProfile? profile = null)
    {
        if (profile == null)
        {
            if (!_prefsManager.TryGetCachedPreferences(player.UserId, out var preferences))
                return false;
            profile = preferences.SelectedCharacter as HumanoidCharacterProfile;
        }
        if (ProfessionalSkillRules.CanJoin(profile?.SkillLevels))
            return true;

        RaiseNetworkEvent(new SkillSelectionRequiredEvent(), player.Channel);
        return false;
    }
}
