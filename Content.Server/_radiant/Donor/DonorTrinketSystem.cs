using Content.Shared._radiant.Donor;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Donor;

public sealed class DonorTrinketSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DonorTrinketComponent, UseInHandEvent>(OnUse);
    }

    private void OnUse(Entity<DonorTrinketComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (ent.Comp.NextUse > _timing.CurTime)
            return;
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(2);
        var message = _random.Prob(0.5f) ? "donor-token-yes" : "donor-token-no";
        _popup.PopupEntity(Loc.GetString(message), ent, args.User);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/UserInterface/click.ogg",
            AudioParams.Default.WithVolume(-10)), ent);
        RaiseNetworkEvent(new DonorTrinketAnimationEvent(GetNetEntity(ent)), Filter.Pvs(ent));
    }
}
