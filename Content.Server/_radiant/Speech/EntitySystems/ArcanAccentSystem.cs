using System.Text.RegularExpressions;
using Content.Server.Speech;
using Content.Server.Speech.EntitySystems;
using Content.Server._radiant.Speech.Components;
using Robust.Shared.Random;

namespace Content.Server._radiant.Speech.EntitySystems;

public sealed partial class ArcanAccentSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ReplacementAccentSystem _replacement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArcanAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    public string Accentuate(string message, ArcanAccentComponent component)
    {

        var msg = message;

        // apply word replacements
        msg = _replacement.ApplyReplacements(msg, "arcan");

        if (_random.Prob(component.heartChance))
        {
            var choice = _random.Pick(new[]
            {
                "arcan-suffix-1",
                "arcan-suffix-2"
            });
            msg += Loc.GetString(choice);
        }

        return msg;
    }

    private void OnAccentGet(EntityUid uid, ArcanAccentComponent component, AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message, component);
    }
}
