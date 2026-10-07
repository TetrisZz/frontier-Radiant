using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Medical.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private IRobustRandom _failureRandom = default!;
    protected override void OnSurgicalFailure(EntityUid user, EntityUid body, EntityUid part, float successRate)
    {
        // The failed step is never marked complete. Do not rewind successful organ removal/insertion.
        var consequence = _failureRandom.Next(3);
        var blood = EntityManager.System<SharedBloodstreamSystem>();
        var damage = EntityManager.System<DamageableSystem>();
        if (consequence == 0 && UsesSurgicalSterility(body, part))
        {
            blood.TryModifyBloodLevel(body, -15);
            blood.TryModifyBleedAmount(body, 3);
        }
        else
        {
            var target = consequence == 1 && HasComp<DamageableComponent>(part) ? part : body;
            damage.TryChangeDamage(target, new DamageSpecifier
            {
                DamageDict = new() { ["Slash"] = FixedPoint2.New(5) },
            }, interruptsDoAfters: false);
        }
        _popup.PopupEntity(Loc.GetString(consequence == 0 && UsesSurgicalSterility(body, part)
            ? "surgical-failure-bleeding" : "surgical-failure-injury"), body, user, PopupType.MediumCaution);
    }
}
