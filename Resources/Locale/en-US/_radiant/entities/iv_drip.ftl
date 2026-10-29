ent-RadiantIVDrip = IV drip stand
    .desc = A wheeled stand for infusions and blood collection. Insert a fluid bag, then drag the stand onto the patient. Right-click for modes and dosage; click with an empty hand to disconnect.
ent-RadiantIVDripTelescopic = telescopic IV drip stand
    .desc = A compact stand for infusions and blood collection. Insert a fluid bag, then drag the stand onto the patient. Right-click for modes and dosage; click with an empty hand to disconnect.

ent-RadiantIVBag = IV fluid bag
    .desc = A refillable medical bag holding 200 units of fluid. Fill it from a container or syringe and insert it into an IV stand.
    .suffix = Empty
ent-RadiantIVBagBlood = { ent-RadiantIVBag }
    .desc = { ent-RadiantIVBag.desc }
    .suffix = Blood, Full

radiant-iv-bag-slot = Fluid bag
radiant-iv-attached = IV connected to patient: { $patient }.
radiant-iv-ripped = { $patient } pulled the IV needle out by moving away!
radiant-iv-detach = Disconnect IV
radiant-iv-draw-mode = Switch to blood collection
radiant-iv-inject-mode = Switch to infusion
radiant-iv-mode-inject = Mode: infusion.
radiant-iv-mode-draw = Mode: blood collection.
radiant-iv-patient = Connected patient: { $patient }.
radiant-iv-examine-rate = Dose: { $amount } units every { $interval } seconds.
radiant-iv-dose-help = The dose is the total mixture volume. Monitor drug concentrations: overdosing is possible.
radiant-iv-set-rate = { $amount } units every { $interval } seconds
construction-name-radiant-iv = IV drip stand
construction-name-radiant-iv-telescopic = telescopic IV drip stand
construction-desc-radiant-iv = A stand holding a bag for intravenous infusions and blood collection.
