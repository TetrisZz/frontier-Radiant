using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>Configures the iris colour stored by a loose decorative cyber-eye organ.</summary>
public sealed partial class ConfigurableCyberEyesSystem : EntitySystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ConfigurableCyberEyesComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ConfigurableCyberEyesComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ConfigurableCyberEyesComponent, ConfigurableCyberEyesSaveMessage>(OnSave);
    }

    private void OnGetVerbs(Entity<ConfigurableCyberEyesComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("configurable-cyber-eyes-verb"),
            Act = () =>
            {
                UpdateUi(ent.Owner);
                _ui.TryOpenUi(ent.Owner, ConfigurableCyberEyesUiKey.Key, user);
            },
        });
    }

    private void OnUiOpened(Entity<ConfigurableCyberEyesComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.Owner);
    }

    private void OnSave(Entity<ConfigurableCyberEyesComponent> ent, ref ConfigurableCyberEyesSaveMessage args)
    {
        if (!TryComp<DecorativeCyberEyesComponent>(ent, out var eyes))
            return;

        eyes.IrisColor = args.IrisColor.WithAlpha(1f);
        Dirty(ent.Owner, eyes);
        UpdateUi(ent.Owner);
    }

    private void UpdateUi(EntityUid uid)
    {
        if (!TryComp<DecorativeCyberEyesComponent>(uid, out var eyes))
            return;

        _ui.SetUiState(uid, ConfigurableCyberEyesUiKey.Key,
            new ConfigurableCyberEyesUiState(eyes.IrisColor));
    }
}
