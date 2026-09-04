using System.Linq;
using Content.Server._Starlight.Medical.Surgery.Components;
using Content.Server.Radio.Components;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Chat;
using Content.Shared.Electrocution;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>
/// Installs the compatible components supplied by Starlight surgical implants.
/// Kept independent from Starlight's language, abductor and cybernetics subsystems.
/// </summary>
public sealed class FunctionalOrganSystem : EntitySystem
{
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly SharedSurgerySystem _surgery = default!;
    [Dependency] private readonly SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EncryptionKeySystem _encryption = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FunctionalOrganComponent, SurgeryOrganImplantationCompleted>(OnImplanted);
        SubscribeLocalEvent<FunctionalOrganComponent, SurgeryOrganExtracted>(OnExtracted);
        // Radiant sector: keep a surgically installed communications implant synchronized with its keys.
        SubscribeLocalEvent<SurgicalCommsImplantComponent, EncryptionChannelsChangedEvent>(OnEncryptionChannelsChanged);
        SubscribeLocalEvent<SurgicalCommsImplantComponent, GetDefaultRadioChannelEvent>(OnGetDefaultRadioChannel);
        SubscribeLocalEvent<SurgicalCommsImplantComponent, ComponentShutdown>(OnCommsShutdown);
    }

    private void OnImplanted(Entity<FunctionalOrganComponent> ent, ref SurgeryOrganImplantationCompleted args)
    {
        foreach (var registration in (ent.Comp.Components ?? []).Values)
        {
            var type = registration.Component.GetType();
            if (HasComp(args.Body, type))
                continue;

            var component = _serialization.CreateCopy(registration.Component, notNullableOverride: true);
            AddComp(args.Body, component);
            // Radiant sector: force the freshly copied insulation value through its
            // owning system so it is dirtied and immediately affects electrocution.
            if (component is InsulatedComponent insulated)
                _electrocution.SetInsulatedSiemensCoefficient(args.Body, insulated.Coefficient);
            _surgery.AddInstalledComponent(ent.Owner, type);
        }

        // Radiant sector: encryption keys belong to the installed body while the implant is active.
        MoveEncryptionKeys(ent.Owner, args.Body);
        SyncRadioChannels(args.Body);
    }

    private void OnExtracted(Entity<FunctionalOrganComponent> ent, ref SurgeryOrganExtracted args)
    {
        // Radiant sector: return the keys with the extracted implant before its body-side holder is removed.
        MoveEncryptionKeys(args.Body, ent.Owner);

        foreach (var type in ent.Comp.Installed)
        {
            if (EntityManager.TryGetComponent(args.Body, type, out var component))
                RemComp(args.Body, component);
        }

        _surgery.ClearInstalledComponents(ent.Owner);
    }

    private void OnEncryptionChannelsChanged(
        EntityUid uid,
        SurgicalCommsImplantComponent component,
        EncryptionChannelsChangedEvent args)
    {
        SyncRadioChannels(uid, component, args.Component);
    }

    private void OnGetDefaultRadioChannel(
        EntityUid uid,
        SurgicalCommsImplantComponent component,
        GetDefaultRadioChannelEvent args)
    {
        if (TryComp<EncryptionKeyHolderComponent>(uid, out var holder))
            args.Channel ??= holder.DefaultChannel;
    }

    private void OnCommsShutdown(EntityUid uid, SurgicalCommsImplantComponent component, ComponentShutdown args)
    {
        ClearRadioChannels(uid, component);
    }

    private void SyncRadioChannels(
        EntityUid uid,
        SurgicalCommsImplantComponent? component = null,
        EncryptionKeyHolderComponent? holder = null)
    {
        if (!Resolve(uid, ref component, false) || !Resolve(uid, ref holder, false))
            return;

        ClearRadioChannels(uid, component);

        var receiver = EnsureComp<ActiveRadioComponent>(uid);
        var transmitter = EnsureComp<IntrinsicRadioTransmitterComponent>(uid);
        EnsureComp<IntrinsicRadioReceiverComponent>(uid);

        foreach (var channel in holder.Channels)
        {
            if (receiver.Channels.Add(channel))
                component.AddedReceiverChannels.Add(channel);
            if (transmitter.Channels.Add(channel))
                component.AddedTransmitterChannels.Add(channel);
        }
    }

    private void ClearRadioChannels(EntityUid uid, SurgicalCommsImplantComponent component)
    {
        if (TryComp<ActiveRadioComponent>(uid, out var receiver))
        {
            receiver.Channels.ExceptWith(component.AddedReceiverChannels);
            component.AddedReceiverChannels.Clear();
        }

        if (TryComp<IntrinsicRadioTransmitterComponent>(uid, out var transmitter))
        {
            transmitter.Channels.ExceptWith(component.AddedTransmitterChannels);
            component.AddedTransmitterChannels.Clear();
        }
    }

    private void MoveEncryptionKeys(EntityUid source, EntityUid destination)
    {
        if (!TryComp<EncryptionKeyHolderComponent>(source, out var sourceHolder) ||
            !TryComp<EncryptionKeyHolderComponent>(destination, out var destinationHolder))
            return;

        foreach (var key in sourceHolder.KeyContainer.ContainedEntities.ToArray())
            _container.Insert(key, destinationHolder.KeyContainer);

        _encryption.UpdateChannels(source, sourceHolder);
        _encryption.UpdateChannels(destination, destinationHolder);
    }
}
