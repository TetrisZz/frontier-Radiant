using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Content.Server._Triad.Shipyard;
using Content.Server._Triad.Shipyard.Persistence;
using Content.Server.Database;
using Content.Shared._Triad.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

[TestFixture]
public sealed class ShipTamperAuditTest
{
    [Test]
    public async Task ModesPermitsAuditAndClientWindowWork()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        try
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var config = pair.Server.ResolveDependency<IConfigurationManager>();
            var permits = pair.Server.ResolveDependency<ITriadShipyardPermitStore>();
            var audit = pair.Server.ResolveDependency<ITriadShipyardAuditLog>();
            var user = pair.Player!.UserId;
            var admin = Guid.NewGuid();
            AuthenticatedShipFile trusted = null!;
            AuthenticatedShipFile foreign = null!;
            var unsigned = AuthenticatedShipFile.FromShipData("ship: legacy");
            await pair.Server.WaitAssertion(() =>
            {
                var signing = entities.System<ShipSaveSigningSystem>();
                trusted = signing.Sign("ship: local", 100);
                var key = AuthenticatedShipFile.GetStaticPrivateKeyInfo();
                try
                {
                    using var rsa = RSA.Create(2048);
                    AuthenticatedShipFile.SetStaticKeyInfo(rsa.ExportRSAPrivateKey());
                    foreign = AuthenticatedShipFile.FromShipData("ship: foreign");
                    foreign.SignShip();
                }
                finally
                {
                    AuthenticatedShipFile.SetStaticKeyInfo(key);
                }
                var policy = entities.System<TriadTamperPolicyService>();
                config.SetCVar(TriadCCVars.TamperMode, "enforce");
                Assert.That(policy.EvaluateLoad(trusted, user, null).Allow, Is.True);
                Assert.That(policy.EvaluateLoad(unsigned, user, null).ResolvedEvent,
                    Is.EqualTo(TriadShipyardEventType.LoadRejectedUnsigned));
                Assert.That(policy.EvaluateLoad(foreign, user, null).ResolvedEvent,
                    Is.EqualTo(TriadShipyardEventType.LoadRejectedForeignKey));
                var modified = AuthenticatedShipFile.FromShipFile(trusted.ShipFileString().Replace(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("ship: local")),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("ship: edited"))));
                Assert.That(policy.EvaluateLoad(modified, user, null).ResolvedEvent,
                    Is.EqualTo(TriadShipyardEventType.LoadRejectedInvalidSignature));
                config.SetCVar(TriadCCVars.TamperMode, "notify");
                Assert.That(policy.EvaluateLoad(unsigned, user, null).Allow, Is.True);
                Assert.That(policy.EvaluateLoad(foreign, user, null).ResolvedEvent,
                    Is.EqualTo(TriadShipyardEventType.LoadVerifiedUntrusted));
                config.SetCVar(TriadCCVars.TamperMode, "off");
                Assert.That(policy.EvaluateLoad(unsigned, user, null).Allow, Is.True);
                config.SetCVar(TriadCCVars.TamperMode, "misspelled");
                Assert.That(policy.EvaluateLoad(unsigned, user, null).Allow, Is.False);
                config.SetCVar(TriadCCVars.TamperMode, "enforce");
            });

            await permits.GrantAsync(user.UserId, admin, DateTime.UtcNow, "Legacy import", default);
            await pair.Server.WaitAssertion(() =>
            {
                var policy = entities.System<TriadTamperPolicyService>();
                Assert.That(policy.EvaluateLoad(unsigned, user, null).ResolvedEvent,
                    Is.EqualTo(TriadShipyardEventType.LoadMigrated));
                var migrated = policy.ReSignForMigration(unsigned, 10);
                var signing = entities.System<ShipSaveSigningSystem>();
                Assert.That(signing.IsTrusted(migrated), Is.True);
                Assert.That(migrated.ShipYamlString(), Is.EqualTo(unsigned.ShipYamlString()));
                Assert.That(migrated.GetHash(), Is.EqualTo(unsigned.GetHash()));
                signing.MakeAvailable(trusted);
                signing.Consume(trusted);
                Assert.That(signing.IsKnown(trusted), Is.True);
                Assert.That(signing.IsAvailable(trusted), Is.False);
            });
            Assert.That(await permits.RevokeAsync(user.UserId, default), Is.True);
            Assert.That(permits.HasPermitFor(user.UserId), Is.False);
            await permits.GrantAsync(user.UserId, admin, DateTime.UtcNow, null, default);
            await permits.PopulateAsync(default);
            Assert.That(permits.HasPermitFor(user.UserId), Is.False, "Session permits must not survive startup");

            var now = DateTime.UtcNow;
            await audit.RecordBatchAsync(new[]
            {
                new TriadShipyardAuditEvent
                {
                    At = now, PlayerUserId = user.UserId, PlayerName = "Player",
                    ShipName = "Test_100%", RoundId = 12345, EventType = TriadShipyardEventType.SaveSigned
                },
                new TriadShipyardAuditEvent
                {
                    At = now.AddSeconds(1), PlayerUserId = user.UserId, PlayerName = "Player",
                    ShipName = "Other", RoundId = 12345, EventType = TriadShipyardEventType.LoadRejected
                }
            }, default);
            var filter = new AuditFilter(null, null, user.UserId, null, null, 12345);
            Assert.That((await audit.QueryAsync(filter, 0, 50, default)).TotalCount, Is.EqualTo(2));
            Assert.That((await audit.QueryAsync(filter with { ShipNameContains = "_100%" }, 0, 50, default))
                .Rows.Single().ShipName, Is.EqualTo("Test_100%"));
            Assert.That((await audit.QueryAsync(filter with { EventTypes = Array.Empty<TriadShipyardEventType>() },
                0, 50, default)).TotalCount, Is.Zero);
            Assert.That((await audit.QueryAsync(filter with { EventTypes = new[] { TriadShipyardEventType.SaveSigned } },
                0, 50, default)).TotalCount, Is.EqualTo(1));
            Assert.That((await audit.GetRoundPlayersAsync(12345, default)).Count, Is.EqualTo(1));

            await pair.Client.WaitAssertion(() =>
            {
                var window = new Content.Client._Triad.Shipyard.Admin.TriadTamperAdminWindow(
                    new Content.Client._Triad.Shipyard.Admin.TriadTamperAdminEui());
                window.UpdateState(new Content.Shared._Triad.Shipyard.Admin.TriadTamperAdminEuiState
                {
                    SelectedRoundId = 1, CurrentRoundId = 1, AuditPageSize = 50,
                    AuditPage = new()
                    {
                        new(1, now, "SaveSigned", user.UserId, "Player", "Ship", "abcd", "1234", 100, null, null, null, null)
                    }
                });
                window.Dispose();
                TestContext.Out.WriteLine("Ship tamper modes, permits, audit filters and client window assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Ship tamper audit regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
