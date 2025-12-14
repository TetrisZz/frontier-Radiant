using Content.Server.DoAfter;
using Content.Server.Pinpointer;
using Content.Server.Radio.EntitySystems;
using Content.Server._Goobstation.PanicButton;
using Content.Shared._Goobstation.Security;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Radio;
using Content.Shared.Timing;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;

public sealed partial class PanicButtonSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radioSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly UseDelaySystem _useDelaySystem = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PanicButtonComponent, UseInHandEvent>(OnButtonPressed);
    }

    private void OnButtonPressed(Entity<PanicButtonComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        EnsureComp<UseDelayComponent>(ent.Owner, out var useDelay);
        if (_useDelaySystem.IsDelayed((ent.Owner, useDelay)))
            return;

        var comp = ent.Comp;
        var uid = ent.Owner;

        if (_useDelaySystem.IsDelayed((ent.Owner, useDelay)))
            return;

        _useDelaySystem.TryResetDelay((uid, useDelay));

        var xform = Transform(uid);
        var coordinates = xform.Coordinates;

        var grid = xform.GridUid;
        var gridName = grid.HasValue ? _stationSystem.GetOwningStation(grid.Value)?.ToString() ?? "" : "";

        var pos = xform.MapPosition;
        var x = (int)pos.X;
        var y = (int)pos.Y;
        var posText = $"({x}, {y})";

        var station = _stationSystem.GetOwningStation(uid);
        
        string stationText = "";
        if (station.HasValue)
        {
            if (EntityManager.TryGetComponent(station.Value, out StationNameSetupComponent? stationComponent))
            {
                stationText = stationComponent.StationNameTemplate ?? "Unnamed Station";
            }
        }

        var coordinatesText = $"Coordinates: {coordinates.X}, {coordinates.Y}";
        var gridText = gridName;

        var distressMessage = Loc.GetString(comp.DistressMessage, 
            ("position", posText), 
            ("grid", stationText));

        _radioSystem.SendRadioMessage(uid, distressMessage, _prototypeManager.Index<RadioChannelPrototype>(comp.RadioChannel), uid);

        args.Handled = true;
    }
}
