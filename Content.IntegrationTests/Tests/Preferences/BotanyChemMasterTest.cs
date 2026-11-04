using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Skills;
using Content.Client.Chemistry.UI;
using Content.Shared._radiant.Skills;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DragDrop;
using Content.Shared.FixedPoint;
using Content.Shared.Storage;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class BotanyChemMasterTest
{
    private const string Oligomers = "PolypyryliumOligomers";
    private const string Contaminant = "Lexorin";

    [Test]
    public async Task BotanistsSeparateAndBottlePureChemicalsWithoutMixingOrMakingPills()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            EntityUid user = default;
            await pair.Server.WaitAssertion(() =>
            {
                user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                players.SetAttachedEntity(players.Sessions.Single(), user);
                var ranks = em.AddComponent<ProfessionalSkillsComponent>(user);
                var skills = em.System<SharedProfessionalSkillsSystem>();
                var solutions = em.System<SharedSolutionContainerSystem>();
                var slots = em.System<ItemSlotsSystem>();
                var machine = em.SpawnEntity("ChemMaster", map.GridCoords);
                var buffer = Liquid(machine, SharedChemMaster.BufferSolutionName);
                var source = Container("LargeBeaker", (Oligomers, 40), (Contaminant, 10));
                Slot(SharedChemMaster.InputSlotName, source);

                ranks.Levels[(int) ProfessionalSkill.Botany] = 1;
                Assert.That(skills.CanIdentifyReagent(user, Oligomers), Is.False);
                Assert.That(Allowed(new ChemMasterReagentAmountButtonMessage(new(Oligomers, null), ChemMasterReagentAmount.U20, false)), Is.False);
                Transfer(Oligomers, ChemMasterReagentAmount.U20, false);
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.Zero));

                var dumpSource = Container("LargeBeaker", (Oligomers, 5), (Contaminant, 5));
                var drag = new DragDropDraggedEvent(user, machine);
                em.EventBus.RaiseLocalEvent(dumpSource, ref drag);
                Assert.That(Liquid(dumpSource).Volume, Is.EqualTo(FixedPoint2.New(10)), "Dragging must not bypass the skill gate.");

                ranks.Levels[(int) ProfessionalSkill.Botany] = 2;
                Assert.That(skills.CanIdentifyReagent(user, Oligomers), Is.True);
                Assert.That(skills.CanIdentifyReagent(user, Contaminant), Is.True);
                Assert.That(skills.CanMixChemicals(user), Is.False);
                Assert.That(Allowed(new ChemMasterReagentAmountButtonMessage(new(Oligomers, null), ChemMasterReagentAmount.U20, false)), Is.True);
                Transfer(Oligomers, ChemMasterReagentAmount.U20, false);
                Assert.That(buffer.GetTotalPrototypeQuantity(Oligomers), Is.EqualTo(FixedPoint2.New(20)));
                Assert.That(Liquid(source).GetTotalPrototypeQuantity(Contaminant), Is.EqualTo(FixedPoint2.New(10)), "The contaminant must stay in the source.");
                Transfer(Contaminant, ChemMasterReagentAmount.All, false);
                Assert.That(buffer.Contents, Has.Count.EqualTo(2), "The separator may store multiple isolated chemicals.");
                Assert.That(buffer.CanReact, Is.False);

                var bottle = Container("ChemistryEmptyBottle01");
                Slot(SharedChemMaster.OutputSlotName, bottle);
                Send(new ChemMasterOutputToBottleMessage(5, "denied blend"));
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(30)));
                Assert.That(Liquid(bottle).Volume, Is.EqualTo(FixedPoint2.Zero), "Bottling a mixed buffer must not consume anything.");

                var pureBeaker = Container("LargeBeaker");
                Slot(SharedChemMaster.InputSlotName, pureBeaker);
                Transfer(Oligomers, ChemMasterReagentAmount.U10, true);
                Transfer(Contaminant, ChemMasterReagentAmount.U5, true);
                Assert.That(Liquid(pureBeaker).GetTotalPrototypeQuantity(Oligomers), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(Liquid(pureBeaker).GetTotalPrototypeQuantity(Contaminant), Is.EqualTo(FixedPoint2.Zero), "Even non-reacting mixtures are forbidden.");
                Assert.That(buffer.GetTotalPrototypeQuantity(Contaminant), Is.EqualTo(FixedPoint2.New(10)));
                Transfer(Oligomers, ChemMasterReagentAmount.All, true);
                Assert.That(Liquid(pureBeaker).Volume, Is.EqualTo(FixedPoint2.New(20)), "Topping up the same pure chemical is allowed.");

                Send(new ChemMasterOutputToBottleMessage(5, "pure lexorin"));
                Assert.That(Liquid(bottle).GetTotalPrototypeQuantity(Contaminant), Is.EqualTo(FixedPoint2.New(5)));
                var incompatibleBottle = Container("ChemistryEmptyBottle01", (Oligomers, 5));
                Slot(SharedChemMaster.OutputSlotName, incompatibleBottle);
                Send(new ChemMasterOutputToBottleMessage(5, "denied top-up"));
                Assert.That(Liquid(incompatibleBottle).Volume, Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(5)));
                Slot(SharedChemMaster.OutputSlotName, bottle);
                Send(new ChemMasterOutputToBottleMessage(5, "pure lexorin"));
                Assert.That(Liquid(bottle).Volume, Is.EqualTo(FixedPoint2.New(10)));

                Transfer(Oligomers, ChemMasterReagentAmount.U10, false);
                var pillCanister = em.SpawnEntity("PillCanister", map.GridCoords);
                Slot(SharedChemMaster.OutputSlotName, pillCanister);
                Assert.That(Allowed(new ChemMasterCreatePillsMessage(5, 1, "denied pill")), Is.False);
                Send(new ChemMasterCreatePillsMessage(5, 1, "denied pill"));
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(em.GetComponent<StorageComponent>(pillCanister).Container.ContainedEntities, Is.Empty);

                var lexorinBeaker = Container("LargeBeaker", (Contaminant, 10));
                Slot(SharedChemMaster.InputSlotName, lexorinBeaker);
                Transfer(Oligomers, ChemMasterReagentAmount.U5, true);
                Assert.That(Liquid(lexorinBeaker).GetTotalPrototypeQuantity(Oligomers), Is.EqualTo(FixedPoint2.Zero));
                ranks.Levels[(int) ProfessionalSkill.Medicine] = 2;
                Transfer(Oligomers, ChemMasterReagentAmount.U5, true);
                Assert.That(Liquid(lexorinBeaker).GetTotalPrototypeQuantity(Oligomers), Is.EqualTo(FixedPoint2.New(5)), "Medicine retains full mixing access.");
                Transfer(Contaminant, ChemMasterReagentAmount.U5, false);
                Slot(SharedChemMaster.OutputSlotName, bottle);
                Send(new ChemMasterOutputToBottleMessage(5, "medical mixture"));
                Assert.That(Liquid(bottle).GetTotalPrototypeQuantity(Oligomers), Is.GreaterThan(FixedPoint2.Zero));
                Slot(SharedChemMaster.OutputSlotName, pillCanister);
                Assert.That(Allowed(new ChemMasterCreatePillsMessage(5, 1, "medical pill")), Is.True);
                Send(new ChemMasterCreatePillsMessage(5, 1, "medical pill"));
                Assert.That(em.GetComponent<StorageComponent>(pillCanister).Container.ContainedEntities, Has.Count.EqualTo(1));

                ranks.Levels[(int) ProfessionalSkill.Medicine] = 0;
                Assert.That(Allowed(new ReagentDispenserDispenseReagentMessage(default),
                    em.SpawnEntity("ChemDispenser", map.GridCoords), ReagentDispenserUiKey.Key), Is.False,
                    "Botany must not unlock the chemical dispenser.");
                drag = new DragDropDraggedEvent(user, machine);
                em.EventBus.RaiseLocalEvent(dumpSource, ref drag);
                Assert.That(Liquid(dumpSource).Volume, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(10)), "Botany 2 may load a mixture into the non-reacting separator.");
                em.Dirty(user, ranks);

                bool Allowed(BoundUserInterfaceMessage message, EntityUid? target = null, Enum? key = null)
                {
                    var attempt = new BoundUserInterfaceMessageAttempt(user, target ?? machine, key ?? ChemMasterUiKey.Key, message);
                    em.EventBus.RaiseEvent(EventSource.Local, attempt);
                    return !attempt.Cancelled;
                }
                void Send<T>(T message) where T : BoundUserInterfaceMessage
                {
                    message.Actor = user;
                    em.EventBus.RaiseLocalEvent(machine, message);
                }
                void Transfer(string reagent, ChemMasterReagentAmount amount, bool fromBuffer)
                    => Send(new ChemMasterReagentAmountButtonMessage(new(reagent, null), amount, fromBuffer));
                void Slot(string id, EntityUid item)
                {
                    var slot = em.GetComponent<ItemSlotsComponent>(machine).Slots[id];
                    if (slot.Item != null)
                        Assert.That(slots.TryEject(machine, slot, null, out _), Is.True);
                    Assert.That(slots.TryInsert(machine, slot, item, user), Is.True);
                }
                EntityUid Container(string prototype, params (string Reagent, int Amount)[] reagents)
                {
                    var uid = em.SpawnEntity(prototype, map.GridCoords);
                    var solution = Liquid(uid);
                    solution.CanReact = false;
                    foreach (var (reagent, amount) in reagents)
                        solution.AddReagent(reagent, amount);
                    return uid;
                }
                Solution Liquid(EntityUid uid, string? name = null)
                {
                    if (name != null)
                    {
                        Assert.That(solutions.TryGetSolution(uid, name, out _, out var solution), Is.True);
                        return solution!;
                    }
                    if (solutions.TryGetFitsInDispenser(uid, out _, out var contents))
                        return contents!;
                    Assert.That(solutions.TryGetSolution(uid, SharedChemMaster.BottleSolutionName, out _, out contents), Is.True);
                    return contents!;
                }
            });

            await pair.RunTicksSync(10);
            await pair.Client.WaitAssertion(() =>
            {
                var client = pair.Client.EntMan;
                var local = pair.Client.ResolveDependency<Robust.Client.Player.IPlayerManager>().LocalEntity;
                Assert.That(local, Is.Not.Null);
                var ranks = client.GetComponent<ProfessionalSkillsComponent>(local!.Value);
                Assert.That(ranks.Levels[(int) ProfessionalSkill.Botany], Is.EqualTo(2));
                Assert.That(SkillPresentation.CanIdentifyReagent(Oligomers), Is.True);
                Assert.That(SkillPresentation.CanIdentifyReagent(Contaminant), Is.True);
                Assert.That(SkillPresentation.CanMixChemicals, Is.False);
                using var window = new ChemMasterWindow();
                var pure = new List<ReagentQuantity> { new(Oligomers, 10) };
                var lexorin = new List<ReagentQuantity> { new(Contaminant, 5) };
                var empty = new List<ReagentQuantity>();

                window.UpdateState(State(pure, empty, empty));
                Assert.That(window.FindControl<RichTextLabel>("SeparationNotice").Visible, Is.True);
                Assert.That(window.FindControl<Button>("CreateBottleButton").Disabled, Is.False);
                Assert.That(Descendants(window).OfType<ReagentButton>().Where(b => b.IsBuffer).All(b => !b.Disabled), Is.True);
                window.SetSize = new Vector2(720, 760);
                window.Measure(new Vector2(720, 760));
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(720, 760)));
                Assert.That(window.FindControl<RichTextLabel>("SeparationNotice").DesiredSize.X, Is.LessThanOrEqualTo(720));

                window.UpdateState(State(pure, lexorin, lexorin));
                Assert.That(window.FindControl<Button>("CreateBottleButton").Disabled, Is.True);
                Assert.That(Descendants(window).OfType<ReagentButton>().Where(b => b.IsBuffer).All(b => b.Disabled), Is.True);
                window.UpdateState(State(pure.Concat(lexorin).ToList(), empty, empty));
                Assert.That(window.FindControl<Button>("CreateBottleButton").Disabled, Is.True);
                Assert.That(Descendants(window).OfType<ReagentButton>().Where(b => b.IsBuffer).All(b => !b.Disabled), Is.True,
                    "A mixed buffer must still permit separating one reagent into an empty input container.");
                window.UpdateState(State(pure, empty, empty, pills: true));
                Assert.That(window.FindControl<Button>("CreatePillButton").Disabled, Is.True);

                ranks.Levels[(int) ProfessionalSkill.Medicine] = 2;
                window.UpdateState(State(pure, lexorin, lexorin));
                Assert.That(window.FindControl<RichTextLabel>("SeparationNotice").Visible, Is.False);
                Assert.That(window.FindControl<Button>("CreateBottleButton").Disabled, Is.False);
                Assert.That(Descendants(window).OfType<ReagentButton>().Where(b => b.IsBuffer).All(b => !b.Disabled), Is.True);
                window.UpdateState(State(pure, empty, empty, pills: true));
                Assert.That(window.FindControl<Button>("CreatePillButton").Disabled, Is.False);

                ChemMasterBoundUserInterfaceState State(List<ReagentQuantity> buffer, List<ReagentQuantity> input,
                    List<ReagentQuantity> output, bool pills = false)
                    => new(ChemMasterMode.Transfer, ChemMasterSortingType.None,
                        new ContainerInfo("input", input.Sum(r => r.Quantity.Int()), 100) { Reagents = input },
                        pills ? new ContainerInfo("pill canister", 0, 10) { Entities = new() }
                            : new ContainerInfo("bottle", output.Sum(r => r.Quantity.Int()), 30) { Reagents = output },
                        buffer, buffer.Sum(r => r.Quantity.Int()), 0, 20, false);
            });
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
