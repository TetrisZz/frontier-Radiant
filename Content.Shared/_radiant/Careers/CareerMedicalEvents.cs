using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;

namespace Content.Shared._radiant.Careers;

[ByRefEvent]
public readonly record struct CareerChemicalOperationEvent(EntityUid User, EntityUid Container);

[ByRefEvent]
public readonly record struct CareerChemicalSynthesisEvent(EntityUid Solution, string Reagent, double Quantity);

[ByRefEvent]
public readonly record struct CareerMedicineAdministeredEvent(EntityUid User, EntityUid Patient, Solution Solution);

[ByRefEvent]
public readonly record struct CareerChemicalHealingEvent(EntityUid Patient, string Reagent, DamageSpecifier Healed);
