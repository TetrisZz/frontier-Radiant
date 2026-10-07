using Content.Server._EE.Contractors.Systems;
using Content.Shared._radiant.Passports;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Examine;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Passports;

public sealed class ServiceCredentialSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<ServiceCredentialComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<ServiceCredentialComponent, ExaminedEvent>(OnExamine);
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        if (HasComp<ServiceCredentialIssuedComponent>(args.Mob)
            || !TryComp<HumanoidAppearanceComponent>(args.Mob, out var appearance)
            || !ServiceCredentialJobs.TryGet(args.JobId, out var service, out var prototype)
            || !_prototypes.TryIndex<JobPrototype>(args.JobId!, out var job))
            return;

        var document = Spawn(prototype, Transform(args.Mob).Coordinates);
        var data = Comp<ServiceCredentialComponent>(document);
        data.Service = service;
        data.JobId = args.JobId!;
        data.JobName = job.Name;
        data.OwnerName = Name(args.Mob);
        data.Number = $"{(service == CredentialService.Dvb ? "DVB" : "KF")}-{ServiceCredentialNumbers.Create(6)}";
        data.PersonalNumber = ServiceCredentialNumbers.Create(8);
        data.RegistrationNumber = ServiceCredentialNumbers.Create(12);
        data.Species = appearance.Species.Id;
        data.Sex = appearance.Sex.ToString();
        data.Age = args.Profile.Age;
        data.Portrait = PassportSystem.CapturePortrait(appearance);
        if (_inventory.TryGetSlotEntity(args.Mob, "jumpsuit", out var uniform))
            data.Uniform = MetaData(uniform.Value).EntityPrototype?.ID ?? "";
        EnsureComp<ServiceCredentialIssuedComponent>(args.Mob);

        if (_inventory.TryGetSlotEntity(args.Mob, "back", out var bag)
            && TryComp<StorageComponent>(bag, out var storage)
            && _storage.Insert(bag.Value, document, out _, storageComp: storage, playSound: false))
            return;
        // Missing/full bags must not swallow the document: try a hand, otherwise leave it at the spawn location.
        _hands.TryPickupAnyHand(args.Mob, document);
    }

    private void OnUse(Entity<ServiceCredentialComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        var data = ent.Comp;
        _ui.SetUiState(ent.Owner, ServiceCredentialUiKey.Key, new ServiceCredentialUiState(data.Service,
            data.JobName, data.OwnerName, data.Number, data.Species, data.Sex, data.Age, data.Portrait, data.Uniform,
            data.PersonalNumber, data.RegistrationNumber));
        _ui.TryOpenUi(ent.Owner, ServiceCredentialUiKey.Key, args.User);
    }

    private void OnExamine(Entity<ServiceCredentialComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.OwnerName.Length == 0)
            return;
        args.PushText(Loc.GetString("service-credential-examine", ("name", ent.Comp.OwnerName),
            ("job", Loc.GetString(ent.Comp.JobName)), ("number", ent.Comp.Number)));
    }
}
