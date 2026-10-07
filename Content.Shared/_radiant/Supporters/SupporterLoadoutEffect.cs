using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._radiant.Supporters;

public sealed partial class SupporterLoadoutEffect : LoadoutEffect
{
    public override bool Validate(HumanoidCharacterProfile profile, RoleLoadout loadout,
        ICommonSession? session, IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        if (!collection.Resolve<IConfigurationManager>().GetCVar(SupporterCVars.Enabled))
        {
            return new JobRequirementLoadoutEffect
            {
                Requirement = new RoleTimeRequirement { Role = "JobBoxer", Time = TimeSpan.FromHours(100) },
            }.Validate(profile, loadout, session, collection, out reason);
        }

        var allowed = session != null && collection.Resolve<IEntityManager>()
            .System<SharedSupporterSystem>().HasAccess(session.UserId);
        reason = allowed ? null : FormattedMessage.FromMarkupOrThrow(Loc.GetString("supporter-loadout-required"));
        return allowed;
    }
}
