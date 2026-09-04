using Content.Shared.DeviceLinking.Events;
using Content.Shared.Buckle.Components;
using Content.Shared._Starlight.Medical.Surgery;
using System.Linq;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._radiant.ERP;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Server.Temperature.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;

namespace Content.Server._Starlight.Medical.Surgery;

public sealed partial class BodyScannerSystem : SharedBodyScannerSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private float _updateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OperatingTableComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<OperatingTableComponent, UnstrappedEvent>(OnUnstrapped);

        SubscribeLocalEvent<BodyScannerComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<BodyScannerComponent, PortDisconnectedEvent>(OnPortDisconnected);

        Subs.BuiEvents<BodyScannerComponent>(BodyScannerUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateAccumulator += frameTime;
        if (_updateAccumulator < 1f)
            return;

        _updateAccumulator = 0f;
        var query = EntityQueryEnumerator<BodyScannerComponent>();
        while (query.MoveNext(out var uid, out var scanner))
            UpdateScannerUi((uid, scanner));
    }

    private void OnStrapped(Entity<OperatingTableComponent> ent, ref StrappedEvent args)
    {
        if (ent.Comp.Scanner is { } scanner && TryComp<BodyScannerComponent>(scanner, out var scannerComp))
            UpdateScannerUi((scanner, scannerComp));
    }

    private void OnUnstrapped(Entity<OperatingTableComponent> ent, ref UnstrappedEvent args)
    {
        if (ent.Comp.Scanner is { } scanner && TryComp<BodyScannerComponent>(scanner, out var scannerComp))
            UpdateScannerUi((scanner, scannerComp));
    }

    private void OnNewLink(Entity<BodyScannerComponent> ent, ref NewLinkEvent args)
    {
        if (!TryComp<OperatingTableComponent>(args.Sink, out var table) || !TryComp<StrapComponent>(args.Sink, out var strap))
            return;

        ent.Comp.TableEntity = args.Sink;

        table.Scanner = ent.Owner;
        Dirty(args.Sink, table);
        Dirty(ent);
        UpdateScannerUi(ent);
    }

    private void OnPortDisconnected(Entity<BodyScannerComponent> ent, ref PortDisconnectedEvent args)
    {
        var tableEntityUid = ent.Comp.TableEntity;
        if (args.Port != ent.Comp.LinkingPort || tableEntityUid == null)
            return;

        if (TryComp<OperatingTableComponent>(tableEntityUid, out var table))
        {
            table.Scanner = null;
            Dirty(tableEntityUid.Value, table);
        }

        ent.Comp.TableEntity = null;
        Dirty(ent);
        UpdateScannerUi(ent);
    }

    private void OnUiOpened(Entity<BodyScannerComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateScannerUi(ent);
    }

    private void UpdateScannerUi(Entity<BodyScannerComponent> scanner)
    {
        EntityUid? target = null;
        if (scanner.Comp.TableEntity is { } table
            && TryComp<StrapComponent>(table, out var strap)
            && strap.BuckledEntities.Count > 0)
        {
            target = strap.BuckledEntities.First();
        }

        var temperature = float.NaN;
        var bloodLevel = float.NaN;
        var bleeding = false;
        var diagnostics = new List<BodyScannerDiagnosticEntry>();

        if (target is { } patient)
        {
            if (TryComp<TemperatureComponent>(patient, out var temp))
                temperature = temp.CurrentTemperature;

            if (TryComp<BloodstreamComponent>(patient, out var bloodstream)
                && _solutions.ResolveSolution(patient, bloodstream.BloodSolutionName,
                    ref bloodstream.BloodSolution, out var bloodSolution))
            {
                bloodLevel = bloodSolution.FillFraction;
                bleeding = bloodstream.BleedAmount > 0;
            }

            diagnostics = BuildDiagnostics(patient);
        }

        _ui.SetUiState(scanner.Owner, BodyScannerUiKey.Key,
            new BodyScannerBoundUserInterfaceState(
                target is { } uid ? GetNetEntity(uid) : null,
                temperature,
                bloodLevel,
                bleeding,
                diagnostics));
    }

    private List<BodyScannerDiagnosticEntry> BuildDiagnostics(EntityUid patient)
    {
        var diagnostics = new List<BodyScannerDiagnosticEntry>();

        if (TryComp<BodyComponent>(patient, out var body))
        {
            var occupiedBodySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (partId, part) in _body.GetBodyChildren(patient, body))
            {
                occupiedBodySlots.Add(GetPartIdentity(part));

                if (TryComp<SurgicalCavityStateComponent>(partId, out var cavities))
                {
                    AddCavityDiagnostic(diagnostics, cavities.RibcageOpen, "health-analyzer-cavity-ribcage-open");
                    AddCavityDiagnostic(diagnostics, cavities.AbdomenOpen, "health-analyzer-cavity-abdomen-open");
                    AddCavityDiagnostic(diagnostics, cavities.GroinOpen, "health-analyzer-cavity-groin-open");
                }
                else if (HasComp<IncisionOpenComponent>(partId))
                {
                    diagnostics.Add(new BodyScannerDiagnosticEntry(
                        Loc.GetString("body-scanner-part-open", ("part", GetPartName(part))),
                        BodyScannerDiagnosticSeverity.Critical));
                }

                foreach (var slotId in part.Organs.Keys)
                {
                    if (!IsRequiredOrganSlot(slotId) || IsOrganSlotOccupied(partId, slotId))
                        continue;

                    diagnostics.Add(new BodyScannerDiagnosticEntry(
                        Loc.GetString("body-scanner-organ-missing",
                            ("organ", GetOrganName(slotId)),
                            ("part", GetPartName(part))),
                        BodyScannerDiagnosticSeverity.Warning));
                }

                foreach (var (slotId, organ) in GetInstalledOrgansBySlot(partId, part))
                {
                    if (!IsBudgetCyberOrgan(organ))
                        continue;

                    diagnostics.Add(new BodyScannerDiagnosticEntry(
                        Loc.GetString("body-scanner-budget-cyber-organ",
                            ("part", GetPartName(part)),
                            ("organ", GetOrganName(slotId)),
                            ("name", MetaData(organ).EntityName)),
                        BodyScannerDiagnosticSeverity.Cybernetic));
                }

                var metadata = MetaData(partId);
                if (metadata.EntityPrototype?.ID.Contains("Cyber", StringComparison.OrdinalIgnoreCase) == true)
                {
                    diagnostics.Add(new BodyScannerDiagnosticEntry(
                        Loc.GetString("health-analyzer-cyber-part",
                            ("part", GetPartName(part)),
                            ("name", metadata.EntityName)),
                        BodyScannerDiagnosticSeverity.Cybernetic));
                }

                var implants = _body.GetPartOrgans(partId, part)
                    .Where(organ => IsSurgicalImplant(organ.Id))
                    .Select(organ => MetaData(organ.Id).EntityName)
                    .ToList();
                if (implants.Count > 0)
                {
                    diagnostics.Add(new BodyScannerDiagnosticEntry(
                        Loc.GetString("health-analyzer-part-implants",
                            ("part", GetPartName(part)),
                            ("implants", string.Join(", ", implants))),
                        BodyScannerDiagnosticSeverity.Implant));
                }
            }

            if (body.Prototype is { } prototypeId
                && _prototypes.TryIndex<BodyPrototype>(prototypeId, out var prototype))
            {
                foreach (var expectedSlot in prototype.Slots.Keys)
                {
                    if (occupiedBodySlots.Contains(GetExpectedPartIdentity(expectedSlot)))
                        continue;

                    diagnostics.Add(new BodyScannerDiagnosticEntry(
                        Loc.GetString("body-scanner-part-missing",
                            ("part", GetExpectedPartName(expectedSlot))),
                        BodyScannerDiagnosticSeverity.Warning));
                }
            }
        }

        if (TryComp<AdultAnatomyComponent>(patient, out var anatomy))
        {
            AddAdultAnatomyDiagnostics(diagnostics, anatomy);
        }

        return diagnostics;
    }

    private void AddAdultAnatomyDiagnostics(List<BodyScannerDiagnosticEntry> diagnostics, AdultAnatomyComponent anatomy)
    {
        if (anatomy.PenisSurgicallyRemoved)
        {
            diagnostics.Add(new BodyScannerDiagnosticEntry(
                Loc.GetString("body-scanner-adult-organ-removed",
                    ("part", Loc.GetString("body-scanner-part-groin")),
                    ("organ", Loc.GetString("body-scanner-adult-organ-penis"))),
                BodyScannerDiagnosticSeverity.Warning));
        }

        if (anatomy.VaginaSurgicallyRemoved)
        {
            diagnostics.Add(new BodyScannerDiagnosticEntry(
                Loc.GetString("body-scanner-adult-organ-removed",
                    ("part", Loc.GetString("body-scanner-part-groin")),
                    ("organ", Loc.GetString("body-scanner-adult-organ-vagina"))),
                BodyScannerDiagnosticSeverity.Warning));
        }

        if (anatomy.BreastsSurgicallyRemoved)
        {
            diagnostics.Add(new BodyScannerDiagnosticEntry(
                Loc.GetString("body-scanner-adult-organ-removed",
                    ("part", Loc.GetString("body-scanner-part-chest")),
                    ("organ", Loc.GetString("body-scanner-adult-organ-breasts"))),
                BodyScannerDiagnosticSeverity.Warning));
        }

        if (!anatomy.HasBreasts || !anatomy.BreastSizeSurgicallyChanged)
            return;

        diagnostics.Add(new BodyScannerDiagnosticEntry(
            Loc.GetString("health-analyzer-breast-size",
                ("size", Loc.GetString($"adult-anatomy-size-{anatomy.BreastSize.ToString().ToLowerInvariant()}"))),
            BodyScannerDiagnosticSeverity.Anatomy));
    }

    private bool IsSurgicalImplant(EntityUid organ)
    {
        return HasComp<EyeImplantComponent>(organ)
               || HasComp<NoseImplantComponent>(organ)
               || HasComp<HandImplantComponent>(organ)
               || HasComp<BrainImplantComponent>(organ);
    }

    private IEnumerable<(string SlotId, EntityUid Organ)> GetInstalledOrgansBySlot(EntityUid partId, BodyPartComponent part)
    {
        foreach (var slotId in part.Organs.Keys)
        {
            var containerId = SharedBodySystem.GetOrganContainerId(slotId);
            if (!_containers.TryGetContainer(partId, containerId, out var container))
                continue;

            foreach (var entity in container.ContainedEntities)
            {
                if (HasComp<Content.Shared.Body.Organ.OrganComponent>(entity))
                    yield return (slotId, entity);
            }
        }
    }

    private bool IsBudgetCyberOrgan(EntityUid organ)
    {
        var metadata = MetaData(organ);
        return metadata.EntityPrototype?.ID.StartsWith("BudgetCyber", StringComparison.OrdinalIgnoreCase) == true;
    }

    private void AddCavityDiagnostic(List<BodyScannerDiagnosticEntry> diagnostics, bool open, string key)
    {
        if (!open)
            return;

        diagnostics.Add(new BodyScannerDiagnosticEntry(
            Loc.GetString(key),
            BodyScannerDiagnosticSeverity.Critical));
    }

    private bool IsOrganSlotOccupied(EntityUid part, string slotId)
    {
        return _containers.TryGetContainer(part, SharedBodySystem.GetOrganContainerId(slotId), out var container)
               && container.ContainedEntities.Any(entity => HasComp<Content.Shared.Body.Organ.OrganComponent>(entity));
    }

    private string GetPartName(BodyPartComponent part) => (part.PartType, part.Symmetry) switch
    {
        (BodyPartType.Arm, BodyPartSymmetry.Left) => Loc.GetString("health-analyzer-part-left-arm"),
        (BodyPartType.Arm, BodyPartSymmetry.Right) => Loc.GetString("health-analyzer-part-right-arm"),
        (BodyPartType.Hand, BodyPartSymmetry.Left) => Loc.GetString("health-analyzer-part-left-hand"),
        (BodyPartType.Hand, BodyPartSymmetry.Right) => Loc.GetString("health-analyzer-part-right-hand"),
        (BodyPartType.Leg, BodyPartSymmetry.Left) => Loc.GetString("health-analyzer-part-left-leg"),
        (BodyPartType.Leg, BodyPartSymmetry.Right) => Loc.GetString("health-analyzer-part-right-leg"),
        (BodyPartType.Foot, BodyPartSymmetry.Left) => Loc.GetString("health-analyzer-part-left-foot"),
        (BodyPartType.Foot, BodyPartSymmetry.Right) => Loc.GetString("health-analyzer-part-right-foot"),
        (BodyPartType.Head, _) => Loc.GetString("health-analyzer-part-head"),
        (BodyPartType.Tail, _) => Loc.GetString("body-scanner-part-tail"),
        _ => Loc.GetString("health-analyzer-part-torso"),
    };

    private static bool IsRequiredOrganSlot(string slotId) => slotId.ToLowerInvariant() is
        "brain" or "eyes" or "tongue" or "heart" or "lungs" or "stomach" or "liver" or "kidneys" or "core";

    private string GetOrganName(string slotId)
    {
        var normalized = slotId.ToLowerInvariant();
        var key = normalized switch
        {
            "brain" => "body-scanner-organ-brain",
            "eyes" => "body-scanner-organ-eyes",
            "tongue" => "body-scanner-organ-tongue",
            "heart" => "body-scanner-organ-heart",
            "lungs" => "body-scanner-organ-lungs",
            "stomach" => "body-scanner-organ-stomach",
            "liver" => "body-scanner-organ-liver",
            "kidneys" => "body-scanner-organ-kidneys",
            "core" => "body-scanner-organ-core",
            _ => "body-scanner-organ-unknown",
        };
        return Loc.GetString(key);
    }

    private string GetExpectedPartName(string slotId)
    {
        var normalized = slotId.Replace('_', ' ').ToLowerInvariant();
        var left = normalized.Contains("left", StringComparison.Ordinal);
        var right = normalized.Contains("right", StringComparison.Ordinal);
        var type = normalized switch
        {
            var value when value.Contains("hand", StringComparison.Ordinal) => BodyPartType.Hand,
            var value when value.Contains("arm", StringComparison.Ordinal) => BodyPartType.Arm,
            var value when value.Contains("foot", StringComparison.Ordinal) || value.Contains("feet", StringComparison.Ordinal) => BodyPartType.Foot,
            var value when value.Contains("leg", StringComparison.Ordinal) => BodyPartType.Leg,
            var value when value.Contains("head", StringComparison.Ordinal) => BodyPartType.Head,
            var value when value.Contains("tail", StringComparison.Ordinal) => BodyPartType.Tail,
            var value when value.Contains("torso", StringComparison.Ordinal) => BodyPartType.Torso,
            _ => BodyPartType.Other,
        };

        return (type, left, right) switch
        {
            (BodyPartType.Arm, true, _) => Loc.GetString("health-analyzer-part-left-arm"),
            (BodyPartType.Arm, _, true) => Loc.GetString("health-analyzer-part-right-arm"),
            (BodyPartType.Hand, true, _) => Loc.GetString("health-analyzer-part-left-hand"),
            (BodyPartType.Hand, _, true) => Loc.GetString("health-analyzer-part-right-hand"),
            (BodyPartType.Leg, true, _) => Loc.GetString("health-analyzer-part-left-leg"),
            (BodyPartType.Leg, _, true) => Loc.GetString("health-analyzer-part-right-leg"),
            (BodyPartType.Foot, true, _) => Loc.GetString("health-analyzer-part-left-foot"),
            (BodyPartType.Foot, _, true) => Loc.GetString("health-analyzer-part-right-foot"),
            (BodyPartType.Head, _, _) => Loc.GetString("health-analyzer-part-head"),
            (BodyPartType.Tail, _, _) => Loc.GetString("body-scanner-part-tail"),
            (BodyPartType.Torso, _, _) => Loc.GetString("health-analyzer-part-torso"),
            _ => Loc.GetString("body-scanner-part-other"),
        };
    }

    private static string GetPartIdentity(BodyPartComponent part)
    {
        var side = part.Symmetry switch
        {
            BodyPartSymmetry.Left => "left-",
            BodyPartSymmetry.Right => "right-",
            _ => string.Empty,
        };
        var type = part.PartType switch
        {
            BodyPartType.Torso => "torso",
            BodyPartType.Head => "head",
            BodyPartType.Arm => "arm",
            BodyPartType.Hand => "hand",
            BodyPartType.Leg => "leg",
            BodyPartType.Foot => "foot",
            BodyPartType.Tail => "tail",
            _ => "other",
        };
        return $"{side}{type}";
    }

    private static string GetExpectedPartIdentity(string slotId)
    {
        var normalized = slotId.Replace('_', ' ').ToLowerInvariant();
        var side = normalized.Contains("left", StringComparison.Ordinal)
            ? "left-"
            : normalized.Contains("right", StringComparison.Ordinal)
                ? "right-"
                : string.Empty;
        var type = normalized switch
        {
            var value when value.Contains("hand", StringComparison.Ordinal) => "hand",
            var value when value.Contains("arm", StringComparison.Ordinal) => "arm",
            var value when value.Contains("foot", StringComparison.Ordinal) || value.Contains("feet", StringComparison.Ordinal) => "foot",
            var value when value.Contains("leg", StringComparison.Ordinal) => "leg",
            var value when value.Contains("head", StringComparison.Ordinal) => "head",
            var value when value.Contains("tail", StringComparison.Ordinal) => "tail",
            var value when value.Contains("torso", StringComparison.Ordinal) => "torso",
            _ => "other",
        };
        return $"{side}{type}";
    }
}
