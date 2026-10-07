using Content.Shared._radiant.Medical.Genetics;

namespace Content.Server._radiant.Medical.Genetics;

public sealed partial class GeneticBodySystem
{
    private static bool IsTherapy(GeneticProduct product)
        => GeneFor(product) != null;

    private static string? GeneFor(GeneticProduct product) => product switch
    {
        GeneticProduct.Hematopoiesis or GeneticProduct.HematopoiesisRemoval => "RadiantHematopoiesis",
        GeneticProduct.Coagulation or GeneticProduct.CoagulationRemoval => "RadiantCoagulation",
        GeneticProduct.Regeneration or GeneticProduct.RegenerationRemoval => "RadiantRegeneration",
        GeneticProduct.Ultravision or GeneticProduct.UltravisionRemoval => "RadiantUltravision",
        GeneticProduct.Strength or GeneticProduct.StrengthRemoval => "RadiantStrength",
        GeneticProduct.Sobriety or GeneticProduct.SobrietyRemoval => "RadiantSobriety",
        GeneticProduct.Insulation or GeneticProduct.InsulationRemoval => "RadiantInsulation",
        _ => null,
    };

    private bool CanProduce(GeneticSampleComponent data, GeneticProduct product, GeneticBodyGrowerComponent machine)
        => product == GeneticProduct.Body
            || IsTherapy(product) && data.Analyzed && data.TherapyCompatible && ResearchFor(data).Decoded.Contains(GeneFor(product)!)
            || !IsTherapy(product) && OrganPrototype(data, product) != null;

    public bool TryResearch(EntityUid uid)
    {
        if (!TryComp<GeneticBodyGrowerComponent>(uid, out var machine)
            || machine.Stage != GeneticGrowthStage.Idle || !Powered(uid)
            || machine.Sample.ContainedEntity is not { } cartridge
            || !TryComp<GeneticSampleComponent>(cartridge, out var sample)
            || !sample.Analyzed || !sample.TherapyCompatible || !HasNewEvidence(machine, sample))
            return false;
        machine.Stage = GeneticGrowthStage.Researching;
        machine.Remaining = machine.ResearchSeconds;
        machine.PowerFailure = 0;
        return true;
    }

    private EntityUid? ReleaseTherapy(EntityUid uid, GeneticBodyGrowerComponent machine, GeneticSampleComponent sample)
    {
        if (!IsTherapy(machine.Product) || !sample.TherapyCompatible
            || machine.ProductionSample != sample && !CanProduce(sample, machine.Product, machine))
            return null;
        var prototype = machine.Product switch
        {
            GeneticProduct.Coagulation => "GeneticCoagulationInjector",
            GeneticProduct.CoagulationRemoval => "GeneticCoagulationRemover",
            GeneticProduct.Regeneration => "GeneticRegenerationInjector",
            GeneticProduct.RegenerationRemoval => "GeneticRegenerationRemover",
            GeneticProduct.Hematopoiesis => "GeneticHematopoiesisInjector",
            GeneticProduct.Ultravision => "GeneticUltravisionInjector",
            GeneticProduct.UltravisionRemoval => "GeneticUltravisionRemover",
            GeneticProduct.Strength => "GeneticStrengthInjector",
            GeneticProduct.StrengthRemoval => "GeneticStrengthRemover",
            GeneticProduct.Sobriety => "GeneticSobrietyInjector",
            GeneticProduct.SobrietyRemoval => "GeneticSobrietyRemover",
            GeneticProduct.Insulation => "GeneticInsulationInjector",
            GeneticProduct.InsulationRemoval => "GeneticInsulationRemover",
            _ => "GeneticHematopoiesisRemover",
        };
        var result = Spawn(prototype, Transform(uid).Coordinates);
        var therapy = Comp<GeneticTherapyComponent>(result);
        therapy.Dna = sample.Dna;
        therapy.Species = sample.Species;
        _metadata.SetEntityName(result, Loc.GetString("genetics-therapy-labelled",
            ("name", Name(result)), ("donor", sample.Donor)));
        Reset(machine);
        return result;
    }
}
