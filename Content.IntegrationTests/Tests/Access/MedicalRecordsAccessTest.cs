using Content.Shared._radiant.Dossiers;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Power.Components;
using Content.Shared.Roles;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Access;

public sealed class MedicalRecordsAccessTest
{
    [Test]
    public async Task MedicalRecordsAcceptHeadsAndAuthorizedMedicsButNotSeparatistsOrOrdinaryCrew()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var access = em.System<SharedAccessSystem>();
                var readers = em.System<AccessReaderSystem>();
                var consoles = new[]
                {
                    em.SpawnEntity("ComputerMedicalRecords", map.GridCoords),
                    em.SpawnEntity("ComputerTabletopMedicalRecords", map.GridCoords),
                };
                foreach (var console in consoles)
                {
                    Assert.That(em.GetComponent<DossierConsoleComponent>(console).Kind, Is.EqualTo(DossierKind.Medical));
                    Assert.That(em.HasComponent<ActivatableUIRequiresAccessComponent>(console), Is.True);
                    em.RemoveComponent<ActivatableUIRequiresPowerComponent>(console);
                }

                var allowed = new[]
                {
                    "Captain", "ChiefEngineer", "ChiefMedicalOfficer", "HeadOfPersonnel", "HeadOfSecurity",
                    "Quartermaster", "ResearchDirector", "StationRepresentative", "Sheriff", "PlantManager",
                    "StationTrafficController", "DirectorOfCare", "AcorpСommandant", "AcorpDirector",
                    "NavyKomandor", "NavyCaptain", "Bailiff", "AcorpMedical", "MedicalDoctor", "PhoenixMedic", "PhoenixAgent",
                };
                var denied = new[]
                {
                    "SeparatistsCommander", "SeparatistsPrivateSlave", "SeparatistsOperative", "SeparatistsWarden",
                    "SeparatistsRecruit", "SeparatistsSlave", "Contractor", "StationEngineer", "SalvageSpecialist",
                    "AcorpSecurity", "AcorpScience", "AcorpEngineer", "AcorpAssistant", "Deputy", "SeniorOfficer",
                    "NavyOfficer", "NavyCadet", "NFPirateCaptain",
                };
                foreach (var (jobs, expected) in new[] { (allowed, true), (denied, false) })
                {
                    foreach (var jobId in jobs)
                    {
                        var card = em.SpawnEntity("ContractorIDCard", map.GridCoords);
                        access.SetAccessToJob(card, pair.Server.ProtoMan.Index<JobPrototype>(jobId), false);
                        if (jobId is "MedicalDoctor" or "AcorpMedical")
                            Assert.That(readers.FindAccessTags(card), Does.Not.Contain(new ProtoId<AccessLevelPrototype>("Medical")),
                                "Records access must not grant general access to medical rooms.");
                        Check(card, expected, jobId);
                        em.DeleteEntity(card);
                    }
                }

                // Existing medical permissions and the new records-only permission
                // must each work independently; an empty card must never work.
                var testCard = em.SpawnEntity("ContractorIDCard", map.GridCoords);
                foreach (var tag in new[] { "Medical", "MedicalPhoenix", "MedicalRecords" })
                {
                    Assert.That(access.TrySetTags(testCard, new ProtoId<AccessLevelPrototype>[] { tag }), Is.True);
                    Check(testCard, true, tag);
                }
                Assert.That(access.TrySetTags(testCard, System.Array.Empty<ProtoId<AccessLevelPrototype>>()), Is.True);
                Check(testCard, false, "Empty card");

                void Check(EntityUid card, bool expected, string label)
                {
                    foreach (var console in consoles)
                    {
                        Assert.That(readers.IsAllowed(card, console), Is.EqualTo(expected), label);
                        var open = new ActivatableUIOpenAttemptEvent(card);
                        em.EventBus.RaiseLocalEvent(console, open);
                        Assert.That(open.Cancelled, Is.EqualTo(!expected), label);
                    }
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}
