using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.CartridgeLoader;
using Content.Shared._radiant.Messenger;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.PDA;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Messenger;

public sealed class MessengerCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _loader = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IAdminLogManager _logs = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    private TimeSpan _nextRefresh;
    private const int HistoryLimit = 200;

    public override void Initialize()
    {
        SubscribeLocalEvent<MessengerCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<MessengerCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextRefresh)
            return;
        _nextRefresh = _timing.CurTime + TimeSpan.FromSeconds(2);
        var query = EntityQueryEnumerator<MessengerCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var messenger, out var cartridge))
        {
            if (cartridge.LoaderUid is { } pda && IsActive(uid, pda))
                Refresh(uid, pda, messenger);
        }
    }

    private bool IsActive(EntityUid uid, EntityUid pda) =>
        TryComp<CartridgeLoaderComponent>(pda, out var loader) && loader.ActiveProgram == uid &&
        _ui.IsUiOpen(pda, loader.UiKey);

    public bool IsContactAvailable(EntityUid pda) =>
        Exists(pda) && !TerminatingOrDeleted(pda) &&
        TryComp<PdaComponent>(pda, out var owner) && !string.IsNullOrWhiteSpace(owner.OwnerName) &&
        owner.PdaOwner is { } character && Exists(character) && !TerminatingOrDeleted(character) &&
        HasComp<MobStateComponent>(character) && !HasComp<GhostComponent>(character) &&
        TryComp<ActorComponent>(character, out var actor) &&
        actor.PlayerSession.Status == SessionStatus.InGame && actor.PlayerSession.AttachedEntity == character &&
        _loader.HasProgram<MessengerCartridgeComponent>(pda);

    private void OnReady(EntityUid uid, MessengerCartridgeComponent comp, CartridgeUiReadyEvent args)
    {
        Refresh(uid, args.Loader, comp);
    }

    private void OnMessage(EntityUid uid, MessengerCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (args is not MessengerUiMessage message)
            return;
        var pda = GetEntity(args.LoaderUid);
        comp.Error = null;
        switch (message.Action)
        {
            case MessengerAction.ToggleMute:
                comp.Muted = !comp.Muted;
                break;
            case MessengerAction.Select:
                if (message.Target is { } selected && Contacts(pda, comp).Any(c => c.Address == selected))
                {
                    comp.Selected = selected;
                    comp.Unread.Remove(selected);
                }
                break;
            case MessengerAction.Send:
                Send(pda, comp, message, args.Actor);
                break;
        }
        Refresh(uid, pda, comp);
    }

    private void Send(EntityUid pda, MessengerCartridgeComponent comp, MessengerUiMessage message, EntityUid actor)
    {
        if (message.Target is not { } address || address != comp.Selected || string.IsNullOrEmpty(message.Text) ||
            message.Text.Length > MessengerUiMessage.MaxLength)
            return;
        var text = new string(message.Text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length == 0)
            return;
        if (_timing.CurTime < comp.NextSend)
        {
            comp.Error = "messenger-error-rate";
            return;
        }
        if (!TryGetEntity(address, out var target) || target == null || target == pda ||
            !IsContactAvailable(target.Value) ||
            !TryComp<PdaComponent>(target, out var receiverPda) || string.IsNullOrWhiteSpace(receiverPda.OwnerName) ||
            !TryComp<PdaComponent>(pda, out var senderPda) || string.IsNullOrWhiteSpace(senderPda.OwnerName) ||
            !_loader.TryGetProgram<MessengerCartridgeComponent>(target.Value, out var program, out var receiver))
        {
            comp.Error = "messenger-error-offline";
            return;
        }

        comp.NextSend = _timing.CurTime + TimeSpan.FromSeconds(1);
        var senderAddress = GetNetEntity(pda);
        Append(comp, new MessengerMessage(address, receiverPda.OwnerName, text, _timing.CurTime, false));
        Append(receiver, new MessengerMessage(senderAddress, senderPda.OwnerName, text, _timing.CurTime, true));
        if (receiver.Selected != senderAddress || !IsActive(program.Value, target.Value))
            receiver.Unread[senderAddress] = Math.Min(receiver.Unread.GetValueOrDefault(senderAddress) + 1,
                receiver.Messages.Count(m => m.Peer == senderAddress && m.Incoming));
        if (!receiver.Muted)
            _loader.SendNotification(target.Value, Loc.GetString("messenger-notification",
                ("name", FormattedMessage.EscapeText(senderPda.OwnerName))), text);
        _logs.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(actor)} sent a PDA message from {ToPrettyString(pda)} to {ToPrettyString(target.Value)}: {text}");
        if (IsActive(program.Value, target.Value))
            Refresh(program.Value, target.Value, receiver);
    }

    private static void Append(MessengerCartridgeComponent comp, MessengerMessage message)
    {
        comp.Messages.Add(message);
        if (comp.Messages.Count > HistoryLimit)
            comp.Messages.RemoveAt(0);
        // Keep metadata bounded along with the history, including deleted PDAs.
        foreach (var peer in comp.Unread.Keys.Where(p => comp.Messages.All(m => m.Peer != p)).ToArray())
            comp.Unread.Remove(peer);
    }

    private List<MessengerContact> Contacts(EntityUid pda, MessengerCartridgeComponent comp)
    {
        var contacts = new Dictionary<NetEntity, MessengerContact>();
        var activity = new Dictionary<NetEntity, int>();
        for (var i = 0; i < comp.Messages.Count; i++)
        {
            var message = comp.Messages[i];
            activity[message.Peer] = i;
            contacts[message.Peer] = new MessengerContact(message.Peer, message.PeerName, false,
                comp.Unread.GetValueOrDefault(message.Peer), message.Text);
        }
        var query = EntityQueryEnumerator<PdaComponent, CartridgeLoaderComponent>();
        while (query.MoveNext(out var peer, out var owner, out _))
        {
            if (peer == pda || string.IsNullOrWhiteSpace(owner.OwnerName) || !IsContactAvailable(peer))
                continue;
            var address = GetNetEntity(peer);
            contacts[address] = new MessengerContact(address, owner.OwnerName, true,
                comp.Unread.GetValueOrDefault(address), contacts.GetValueOrDefault(address)?.Preview ?? "");
        }
        return contacts.Values.OrderByDescending(c => c.Unread > 0).ThenByDescending(c => activity.GetValueOrDefault(c.Address, -1))
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Address.ToString()).ToList();
    }

    private void Refresh(EntityUid uid, EntityUid pda, MessengerCartridgeComponent comp)
    {
        if (!IsActive(uid, pda))
            return;
        if (comp.Selected is { } selected)
            comp.Unread.Remove(selected);
        _loader.UpdateCartridgeUiState(pda, new MessengerUiState(Contacts(pda, comp),
            comp.Messages.Where(m => m.Peer == comp.Selected).ToList(), comp.Selected, comp.Muted, comp.Error));
    }
}
