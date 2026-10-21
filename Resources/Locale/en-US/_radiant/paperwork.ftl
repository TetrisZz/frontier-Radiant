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
photocopier-copy-name = Copy: { $name }
photocopier-no-supplies = Not enough paper or toner.
photocopier-template-error = The form could not be loaded. Printing cancelled; no paper or toner was spent.
photocopier-supplies = Paper: { $paper } sheets / Toner: { $ink } pages
photocopier-document-header =
    { "[head=2]" }{ $title }[/head]
    Printed on: { $date }
    Time since shift start: { $time }
    Station / shuttle: { $ship }
document-letterhead-frontier =
    { "[mono][color=#1B487E]" }███░███░░░░██░░░░
    ░██░████░░░██░░░░
    ░░█░██░██░░██░█░░
    ░░░░██░░██░██░██░
    ░░░░██░░░████░███[/color][/mono]
    { "[head=3][color=#1B487E]" }Frontier Outpost[/color][/head]
    Sector administration
document-letterhead-security =
    { "[mono][color=#003153]" }████░░░░████
    ████░░░████░
    ████░░████░░
    ████░████░░░
    ████░░████░░
    ████░░░████░
    ████░░░░████[/color][/mono]
    { "[head=3][color=#003153]" }OSK Outpost[/color][/head]
    Internal Security Department / Space Fleet
document-letterhead-acorp =
    { "[head=3][color=#354F66]" }Acolyte Corporation[/color][/head]
    Command center / Horizon research base
    { "[color=#354F66]" }--------------------------------[/color]
document-letterhead-separatists =
    { "[head=3][color=#873D3D]" }Separatist Cartel[/color][/head]
    Faction documentation
    { "[color=#873D3D]" }--------------------------------[/color]
document-letterhead-medical =
    { "[head=3][color=#28716B]" }Frontier Medical Service[/color][/head]
    Medical documentation
    { "[color=#28716B]" }--------------------------------[/color]

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
    Accused: [form]
    Description: [form]
    Case number: [form]
    Time and location of offense: [form]
    Charges: [form]
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
    Event description: [form]
    Measures taken: [form]
    Incident status: [form]
    Additional notes: [form]

    Signatures and stamps:
document-content-separatist-sale =
    Name of transferred person: [form]
    Description: [form]
    Condition: [form]
    Seller: Separatist Cartel
    Buyer: [form]
    Price and currency: [form]
    Transfer conditions: [form]

    Cartel faction document. Does not constitute authorization from Frontier authorities.
    Party signatures and stamps:
document-content-separatist-lease =
    Name of transferred person: [form]
    Description and condition: [form]
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
