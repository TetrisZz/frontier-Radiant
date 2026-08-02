using Content.Server.Chat.Systems;
using Content.Shared.Dataset;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._radiant.VendingMachines;

[RegisterComponent]
public sealed partial class VendingPurchaseSpeechComponent : Component
{
    [DataField(required: true)]
    public ProtoId<LocalizedDatasetPrototype> Pack;
}

public sealed class VendingMachinePurchaseEvent(EntityUid buyer) : EntityEventArgs
{
    public EntityUid Buyer { get; } = buyer;
}

public sealed class VendingPurchaseSpeechSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VendingPurchaseSpeechComponent, VendingMachinePurchaseEvent>(OnPurchase);
    }

    private void OnPurchase(Entity<VendingPurchaseSpeechComponent> ent, ref VendingMachinePurchaseEvent args)
    {
        var dataset = _prototypes.Index(ent.Comp.Pack);
        var message = Loc.GetString(_random.Pick(dataset.Values));
        _chat.TrySendInGameICMessage(ent, message, InGameICChatType.Speak, true);
    }
}

