ent-Photocopier = photocopier
    .desc = Prints standard forms and copies paper documents. Refill with paper sheets and toner.
ent-PrinterToner = printer toner
    .desc = A cartridge for refilling a photocopier.
photocopier-title = Photocopier - documents
photocopier-search = Search forms...
photocopier-print = Print form
photocopier-copy = Copy original
photocopier-copies = Copies:
photocopier-original = Original document
paper-form-writing-required = Hold a pen in your hand. The document must allow writing.
photocopier-copy-name = Copy: { $name }
photocopier-no-supplies = Not enough paper or toner.
photocopier-template-error = The form could not be loaded. Printing cancelled; no paper or toner was spent.
photocopier-supplies = Paper: { $paper } sheets / Toner: { $ink } pages
photocopier-document-header =
    { document-divider }
    { "[head=2]" }{ $title }[/head]
    { document-divider }
    Printed on: { $date }
    Time since shift start: { $time }
    Station / shuttle: { $ship }
# Letterheads adapted from https://radiantsector.fandom.com/ru/wiki/Бумажная_Работа?oldid=1312
document-divider = ═════════════════════════════
document-stamp-area =
    { document-divider }
    { "[italic]" }Signatures and stamps[/italic]
document-research-base-unknown = Research base [form]
document-letterhead-frontier =
    { "[mono][color=#1b487e]" }████░░░░████[/color][/mono]
    { "[mono][color=#1b487e]" }████░░░████░[/color]  [/mono][head=3]Document form[/head]
    { "[mono][color=#1b487e]" }████░░████░░[/color][/mono]
    { "[mono][color=#1b487e]" }████░████░░░[/color]  [/mono][bold]Shuttle { $ship }[/bold]
    { "[mono][color=#1b487e]" }████░░████░░[/color][/mono]
    { "[mono][color=#1b487e]" }████░░░████░[/color][/mono]
    { "[mono][color=#1b487e]" }████░░░░████[/color][/mono]
document-letterhead-security =
    { "[mono][color=#003153]" }████░░░░████[/color][/mono]
    { "[mono][color=#003153]" }████░░░████░[/color]  [/mono][head=3]Document form[/head]
    { "[mono][color=#003153]" }████░░████░░[/color][/mono]
    { "[mono][color=#003153]" }████░████░░░[/color]  [/mono][head=3]OSK Outpost[/head]
    { "[mono][color=#003153]" }████░░████░░[/color][/mono]
    { "[mono][color=#003153]" }████░░░████░[/color][/mono]
    { "[mono][color=#003153]" }████░░░░████[/color][/mono]
document-letterhead-acorp =
    { "[mono][color=black]" }██████░░░░░██████[/color][/mono]
    { "[mono][color=black]" }█████░░███░░█████[/color]  [/mono][head=3]Document form[/head]
    { "[mono][color=black]" }█████░░░░░░░█████[/color]  [/mono][bold]{ $base }[/bold]
    { "[mono][color=black]" }█████░░███░░█████[/color]  [/mono][head=3]Acolyte Corporation[/head]
    { "[mono][color=black]" }█████░░███░░█████[/color][/mono]
document-letterhead-separatists =
    { "[mono][color=#6e3b3b]" }█████░░░░░[/color][/mono]
    { "[mono][color=#6e3b3b]" }██░░░░░░░░[/color]  [/mono][head=3]Document form[/head]
    { "[mono][color=#6e3b3b]" }██░░░░░░░░[/color][/mono]
    { "[mono][color=#6e3b3b]" }██░░░░░░░░[/color]  [/mono][head=3]Separatist Cartel[/head]
    { "[mono][color=#6e3b3b]" }█████░░░░░[/color][/mono]
document-letterhead-medical =
    { "[mono][color=#28716b]" }░░░████░░░[/color][/mono]
    { "[mono][color=#28716b]" }░░░████░░░[/color]  [/mono][head=3]Medical Service[/head]
    { "[mono][color=#28716b]" }██████████[/color]  [/mono][bold]Frontier[/bold]
    { "[mono][color=#28716b]" }░░░████░░░[/color]  [/mono]Medical documentation
    { "[mono][color=#28716b]" }░░░████░░░[/color][/mono]

document-category-frontier = Frontier - general documents
document-category-security = OSK outpost - DSB and fleet
document-category-acorp = Acolyte Corporation
document-category-separatists = Separatist Cartel
document-category-medical = Medical documents
document-form-general = Standard document form
document-form-salary = Salary request
document-form-mercenary = Mercenary license
document-form-arrest = Arrest warrant
document-form-search = Search warrant
document-form-fine = Fine notice
document-form-acorp-general = Acorp - standard form
document-form-acorp-work = Acorp - work report
document-form-acorp-artifact = Acorp - artifact research
document-form-acorp-anomaly = Acorp - anomaly report
document-form-separatist-sale = Slave sale agreement
document-form-separatist-lease = Slave lease agreement
document-form-crew = Crew employment agreement
document-form-expedition = Expedition report
document-form-handover = Cargo handover record
document-form-medical-consent = Medical consent form
document-content-general =
    Author: [form]
    Position: [form]
    Document title: [form]

    Contents:
    { "[form]" }

    Signatures and stamps:
document-content-salary =
    Employee name: [form]
    Position: [form]
    Work started at: [form]
    Time worked: [form]
    Duties performed: [form]
    Requested amount: [form]

    Applicant signature: [form]
    Authorized decision: [form]
    Signatures and stamps:
document-content-mercenary =
    Name: [form]
    Position / organization: [form]
    Reason for application: [form]
    Authorized equipment: [form]
    License validity period: [form]
    Authorizing officer: [form]

    This blank form grants no permissions. It must be completed by an authorized officer.
    Signatures and stamps:
document-content-arrest =
    { "[head=3]" }Accused[/head]
    Accused: [form]
    Description: [form]
    { "[head=3]" }Offense details[/head]
    Case number: [form]
    Time and location of offense: [form]
    Charges: [form]
    { "[head=3]" }Applicant information[/head]
    Applicant: [form]
    Authorized officer: [form]

    Valid only after approval, signature and certification by an authorized officer under sector law.
    Signatures and stamps:
document-content-search =
    Author: [form]
    Position: [form]
    Person / property to search: [form]
    Captain and shuttle designation: [form]
    Grounds for search: [form]
    Authorized search scope: [form]

    Invalid without approval by an authorized security / fleet officer or Sector Representative.
    Signatures and stamps:
document-content-fine =
    Issuing officer and position: [form]
    Shuttle designation: [form]
    Captain / offender: [form]
    Grounds for fine: [form]
    Amount: [form]
    Payment location: OSK outpost
    Payment confirmation: [form]

    Invalid unless completed by an authorized officer.
    Signatures and stamps:
document-content-acorp-general =
    Author: [form]
    Position: [form]
    Document title: [form]
    Contents:
    { "[form]" }

    Signatures and stamps:
document-content-acorp-work =
    Author: [form]
    Full position title: [form]
    Shift started at: [form]
    Work started at: [form]
    Recipient - Expedition Commandant / Research Director: [form]

    Report on assigned work and duties:
    { "[form]" }

    Signatures and stamps:
document-content-acorp-artifact =
    Author and position: [form]
    Recipient: [form]
    { "[head=3]" }Research results[/head]
    Artifact appearance: [form]
    Researched nodes: [form]
    Stimuli: [form]
    Reactions: [form]
    Special properties: [form]
    Safety precautions and recommendations: [form]

    Signatures and stamps:
document-content-acorp-anomaly =
    Incident time: [form]
    Discovery location: [form]
    Responsible employee and position: [form]
    Anomaly type: [form]
    Threat level: [form]
    { "[head=3]" }Description and response[/head]
    Event description: [form]
    Measures taken: [form]
    Incident status: [form]
    Additional notes: [form]

    Signatures and stamps:
document-content-separatist-sale =
    { "[head=3]" }Slave[/head]
    Name of transferred person: [form]
    Description: [form]
    Condition: [form]
    { "[head=3]" }Transaction[/head]
    Seller: Separatist Cartel
    Buyer: [form]
    Price and currency: [form]
    Transfer conditions: [form]

    Cartel faction document. Does not constitute authorization from Frontier authorities.
    Party signatures and stamps:
document-content-separatist-lease =
    { "[head=3]" }Slave[/head]
    Name of transferred person: [form]
    Description and condition: [form]
    { "[head=3]" }Lease conditions[/head]
    Lessor: Separatist Cartel
    Lessee: [form]
    Lease period: [form]
    Purpose and conditions: [form]
    Price and currency: [form]
    Return and compensation conditions: [form]

    Cartel faction document. Does not constitute authorization from Frontier authorities.
    Party signatures and stamps:
document-content-crew =
    Captain and shuttle designation: [form]
    Employee: [form]
    Position and duties: [form]
    Employment period: [form]
    Pay and settlement procedure: [form]
    Termination conditions: [form]

    Captain and employee signatures:
document-content-expedition =
    Expedition leader and crew: [form]
    Shuttle: [form]
    Destination: [form]
    Departure / return time: [form]
    Objectives: [form]
    Results and discoveries: [form]
    Hazards, losses and incidents: [form]
    Attachments and transferred samples: [form]

    Signatures and stamps:
document-content-handover =
    Transferring party: [form]
    Receiving party: [form]
    Shuttles / transfer location: [form]
    Cargo and quantity: [form]
    Condition and packaging: [form]
    Value / grounds for transfer: [form]
    Party remarks: [form]

    Transferring and receiving party signatures:
document-content-medical-consent =
    Patient: [form]
    Treating specialist: [form]
    Proposed procedure: [form]
    Explained risks and alternatives: [form]
    Patient restrictions: [form]
    Patient decision: [form]

    Completing this form does not replace the patient's actual consent.
    Patient signature: [form]
    Specialist signature: [form]

document-form-medical-examination = Medical examination report
document-form-medical-referral = Medical referral
document-form-medical-surgery = Surgical report
document-form-medical-discharge = Medical discharge summary
document-form-medical-prescription = Treatment prescription
document-form-medical-death = Medical death certificate
document-content-medical-examination =
    { "[head=3]" }Patient information[/head]
    Name and species: [form]
    Treating specialist: [form]
    Examination time: [form]

    { "[head=3]" }Examination findings[/head]
    Complaints and history: [form]
    Observations and investigations: [form]
    Conclusion: [form]
    Restrictions and recommendations: [form]
    Specialist signature: [form]
document-content-medical-referral =
    Patient: [form]
    Referring specialist: [form]
    Receiving medical unit / specialist: [form]
    Urgency: [form]

    { "[head=3]" }Reason for referral[/head]
    Complaints and preliminary findings: [form]
    Requested investigations / consultation: [form]
    Contraindications and special requirements: [form]
    Specialist signature: [form]
document-content-medical-surgery =
    Patient: [form]
    Surgeon and assistants: [form]
    Indication: [form]
    Patient consent / emergency justification: [form]
    Start and end time: [form]

    { "[head=3]" }Operative record[/head]
    Procedure and anesthesia: [form]
    Implants / removed tissue: [form]
    Complications and response: [form]
    Outcome and patient condition: [form]
    Postoperative instructions: [form]
    Specialist signature: [form]
document-content-medical-discharge =
    Patient: [form]
    Admission and discharge time: [form]
    Treating specialist: [form]
    Reason for admission / conclusion: [form]

    { "[head=3]" }Treatment summary[/head]
    Investigations and procedures: [form]
    Condition on discharge: [form]
    Further treatment and restrictions: [form]
    Follow-up examination: [form]
    Specialist signature: [form]
document-content-medical-prescription =
    Patient: [form]
    Prescribing specialist: [form]
    Indication: [form]

    { "[head=3]" }Treatment plan[/head]
    Medication / procedure: [form]
    Dose and administration: [form]
    Frequency and duration: [form]
    Restrictions and contraindications: [form]
    Monitoring and follow-up: [form]
    Valid after signature by a medical specialist.
    Specialist signature: [form]
document-content-medical-death =
    Deceased name and species: [form]
    Method of identification: [form]
    Date, time and location of discovery: [form]
    Time death was confirmed: [form]

    { "[head=3]" }Medical findings[/head]
    Signs and presumed cause of death: [form]
    Resuscitation attempts and outcome: [form]
    Possibility of further restoration: [form]
    Transfer of body and personal effects: [form]
    Confirming specialist and signature: [form]
    Records medical circumstances; does not replace an investigation.
