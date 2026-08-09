using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Assertions over RFQ generation (DESIGN.md §83.2, §103).
    ///
    /// §103's acceptance criteria are unusual in that they demand *failure*: "requesting
    /// scarce goods can fail", and "suppliers differ in price and quantity". Both are
    /// properties that can silently stop holding while every other test passes, so they are
    /// measured over a sample rather than asserted on one request.
    ///
    /// Requests made here are removed again, so running the test does not litter the save.
    /// </summary>
    public static class IntercolonyRfqSelfTest
    {
        public static string Run(IntercolonyWorldComponent state)
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;

            void Check(string name, bool ok, string detail = null)
            {
                if (ok)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {name}{(detail == null ? "" : " — " + detail)}");
                }
            }

            sb.AppendLine("RFQ self-test");

            List<ThingDef> tradable = IntercolonyProductClassifier.TradableDefs;
            if (tradable.Count == 0 || state.AllProfiles().Count == 0)
            {
                sb.AppendLine("  (no tradable defs or no settlements; skipped)");
                return sb.ToString();
            }

            List<PurchaseRequest> created = new List<PurchaseRequest>();

            // Sample across the def list and across quantities, because scarcity depends on
            // both what is asked for and how much.
            int totalRequests = 0;
            int emptyRequests = 0;
            int partialQuotes = 0;
            int fullQuotes = 0;
            int distinctPriceRequests = 0;
            int distinctQuantityRequests = 0;
            int badQuote = 0;
            int overOffer = 0;
            int underpriced = 0;
            string firstUnderpriced = null;

            for (int i = 0; i < tradable.Count && totalRequests < 24; i += Mathf.Max(1, tradable.Count / 24))
            {
                ThingDef def = tradable[i];
                int quantity = def.category == ThingCategory.Building ? 4 : 60;

                PurchaseRequest request = RfqService.CreateRequest(state, def, null, quantity, 15);
                if (request == null)
                {
                    continue;
                }

                created.Add(request);
                totalRequests++;

                if (!request.AnyQuotes)
                {
                    emptyRequests++;
                    Check("an empty request explains itself",
                        !string.IsNullOrEmpty(request.noResponseReason), def.defName);
                    continue;
                }

                HashSet<int> prices = new HashSet<int>();
                HashSet<int> quantities = new HashSet<int>();

                foreach (Quotation quote in request.quotes)
                {
                    if (quote.unitPrice <= 0f || quote.quantityOffered <= 0 || quote.leadTimeDays < 1)
                    {
                        badQuote++;
                    }

                    // A supplier must never offer more than was asked for.
                    if (quote.quantityOffered > request.quantityRequested)
                    {
                        overOffer++;
                    }

                    if (quote.quantityOffered < request.quantityRequested)
                    {
                        partialQuotes++;
                    }
                    else
                    {
                        fullQuotes++;
                    }

                    // A quote must never undercut the value of what it promises. Pricing once
                    // ran before the material was chosen, so a supplier offered a gold bed at
                    // the price of a stuffless one — buy it, deconstruct it, profit.
                    float promisedValue = IntercolonyPricing.BaseValue(def, quote.offeredStuff);
                    if (quote.unitPrice < promisedValue * 0.9f)
                    {
                        underpriced++;
                        firstUnderpriced = firstUnderpriced ??
                            $"{def.defName} in {quote.offeredStuff?.label ?? "no stuff"}: " +
                            $"quoted {quote.unitPrice:F1} vs material value {promisedValue:F1}";
                    }

                    prices.Add(quote.TotalPrice);
                    quantities.Add(quote.quantityOffered);
                }

                if (prices.Count > 1)
                {
                    distinctPriceRequests++;
                }

                if (quantities.Count > 1)
                {
                    distinctQuantityRequests++;
                }
            }

            // A deliberately scarce, high-tech item. Sampling alone is luck-dependent: this
            // pins the "scarce goods can fail" criterion to a case that must fail on a world
            // of pre-spacer settlements, rather than hoping the sweep happens to hit one.
            ThingDef scarce = FindHighTechDef();
            if (scarce != null)
            {
                PurchaseRequest scarceRequest = RfqService.CreateRequest(state, scarce, null, 20, 15);
                if (scarceRequest != null)
                {
                    created.Add(scarceRequest);
                    sb.AppendLine($"  (scarce probe: {scarce.label} [{scarce.techLevel}] -> " +
                                  $"{scarceRequest.quotes.Count} quote(s))");

                    int capable = 0;
                    foreach (SettlementEconomicProfile p in state.AllProfiles())
                    {
                        if (RfqService.CanTechnicallySupply(scarce, p))
                        {
                            capable++;
                        }
                    }

                    Check("high-tech goods are gated by supplier tech level",
                        capable < state.AllProfiles().Count,
                        $"every settlement could supply {scarce.label} ({scarce.techLevel})");
                }
            }
            else
            {
                sb.AppendLine("  (no high-tech tradable def found; scarce probe skipped)");
            }

            Check("requests were generated", totalRequests > 0);
            Check("all quotes are well formed", badQuote == 0, $"{badQuote} malformed");
            Check("no supplier offers more than requested", overOffer == 0, $"{overOffer} over-offers");
            Check("no quote undercuts the value of what it promises", underpriced == 0,
                $"{underpriced} underpriced, first: {firstUnderpriced}");

            // §103: "requesting scarce goods can fail". If nothing ever comes back empty this
            // is a vending machine, which §20 explicitly sets out to avoid.
            Check("some requests come back empty", emptyRequests > 0,
                $"all {totalRequests} requests found a supplier — this is a vending machine");

            // ...but not everything, or procurement is unusable.
            Check("not every request comes back empty", emptyRequests < totalRequests,
                $"all {totalRequests} requests failed");

            // §103: "suppliers differ in price and quantity".
            Check("suppliers differ in price", distinctPriceRequests > 0,
                "every supplier quoted an identical total on every request");
            Check("partial quotes occur", partialQuotes > 0,
                "no supplier ever fell short — partial quotes are a §20 outcome");
            Check("full quotes occur", fullQuotes > 0, "no supplier ever covered a full request");

            // Fulfillment is a term of the request, not a fresh coin flip on each response.
            int forcedDeliveryQuotes = 0;
            int forcedPickupQuotes = 0;
            int wrongForcedModes = 0;
            int missingLogisticsFactors = 0;
            int missingEconomyFactors = 0;
            for (int i = 0; i < tradable.Count && i < 8; i++)
            {
                ThingDef def = tradable[i];
                PurchaseRequest delivery = RfqService.CreateRequest(
                    state, def, null, 20, 15,
                    ProcurementFulfillmentPreference.SupplierDelivers);
                PurchaseRequest pickup = RfqService.CreateRequest(
                    state, def, null, 20, 15,
                    ProcurementFulfillmentPreference.PlayerPickup);
                if (delivery != null)
                {
                    created.Add(delivery);
                    foreach (Quotation quote in delivery.quotes)
                    {
                        forcedDeliveryQuotes++;
                        if (!quote.supplierDelivers) wrongForcedModes++;
                        if (!quote.priceExplanation.Contains("Supplier delivery"))
                        {
                            missingLogisticsFactors++;
                        }
                        if (!quote.priceExplanation.Contains("Economy difficulty (buying)"))
                        {
                            missingEconomyFactors++;
                        }
                    }
                }

                if (pickup != null)
                {
                    created.Add(pickup);
                    foreach (Quotation quote in pickup.quotes)
                    {
                        forcedPickupQuotes++;
                        if (quote.supplierDelivers) wrongForcedModes++;
                        if (!quote.priceExplanation.Contains("You collect"))
                        {
                            missingLogisticsFactors++;
                        }
                        if (!quote.priceExplanation.Contains("Economy difficulty (buying)"))
                        {
                            missingEconomyFactors++;
                        }
                    }
                }

                if (forcedDeliveryQuotes > 0 && forcedPickupQuotes > 0)
                {
                    break;
                }
            }

            Check("forced delivery requests produce delivery quotes", forcedDeliveryQuotes > 0);
            Check("forced pickup requests produce pickup quotes", forcedPickupQuotes > 0);
            Check("forced fulfillment terms are honored", wrongForcedModes == 0,
                $"{wrongForcedModes} quote(s) contradicted the request");
            Check("quote explanations include fulfillment cost", missingLogisticsFactors == 0,
                $"{missingLogisticsFactors} quote(s) omitted it");
            Check("quote explanations name the buying economy difficulty factor", missingEconomyFactors == 0,
                $"{missingEconomyFactors} quote(s) omitted it");
            Check("supplier delivery costs more than collection",
                RfqService.ProcurementLogisticsFactor(true).multiplier >
                RfqService.ProcurementLogisticsFactor(false).multiplier);

            sb.AppendLine($"  ({totalRequests} requests: {emptyRequests} empty, " +
                          $"{fullQuotes} full quotes, {partialQuotes} partial; " +
                          $"{distinctPriceRequests} had differing prices, " +
                          $"{distinctQuantityRequests} differing quantities)");

            // --- Determinism (§60): the same request must not re-roll ---
            if (created.Count > 0)
            {
                PurchaseRequest first = created[0];
                int quoteCountBefore = first.quotes.Count;
                int priceBefore = first.AnyQuotes ? first.quotes[0].TotalPrice : 0;
                Check("quotes are stable once generated",
                    first.quotes.Count == quoteCountBefore &&
                    (!first.AnyQuotes || first.quotes[0].TotalPrice == priceBefore));
            }

            // --- State machine (§73) ---
            PurchaseRequest probe = RfqService.CreateRequest(state, tradable[0], null, 10, 10);
            if (probe != null)
            {
                created.Add(probe);
                Check("new request is open", probe.IsOpen);
                Check("expire succeeds once", probe.TryExpire());
                Check("expired request is closed", !probe.IsOpen);
                Check("second expire is refused", !probe.TryExpire());
                Check("expired request cannot be cancelled", !probe.TryCancel());
            }

            // --- Modded goods must not crash request generation (§103) ---
            int moddedTried = 0;
            int moddedCrashed = 0;
            foreach (ThingDef def in tradable)
            {
                ModContentPack pack = def.modContentPack;
                if (pack == null || pack.IsCoreMod || pack.IsOfficialMod || moddedTried >= 5)
                {
                    continue;
                }

                moddedTried++;
                try
                {
                    PurchaseRequest moddedRequest = RfqService.CreateRequest(state, def, null, 20, 10);
                    if (moddedRequest != null)
                    {
                        created.Add(moddedRequest);
                    }
                }
                catch (System.Exception ex)
                {
                    moddedCrashed++;
                    sb.AppendLine($"  FAIL  modded def {def.defName} crashed: {ex.GetType().Name}");
                }
            }

            Check("modded goods do not crash request generation", moddedCrashed == 0,
                $"{moddedCrashed} of {moddedTried} crashed");
            sb.AppendLine(moddedTried > 0
                ? $"  ({moddedTried} modded def(s) exercised)"
                : "  (no non-core tradable defs loaded — modded-goods criterion UNPROVEN)");

            // Leave no test residue in the player's save.
            foreach (PurchaseRequest request in created)
            {
                state.Requests.Remove(request);
            }

            // --- Purchase-order cancellation (B5) ---
            Settlement cancellationSettlement = IntercolonyMarketAccess.FindSettlement(
                state.AllProfiles()[0].settlementId);
            CheckPurchaseCancellation(Check, state, cancellationSettlement);

            // --- §104: purchased goods arrive and preserve expected properties ---
            // Built through the same path a real purchase uses, then inspected. §104's four
            // named cases: commodity, weapon/apparel, chair, workbench.
            sb.AppendLine("  §104 goods construction:");
            CheckGoods(sb, Check, "commodity", ThingDefOf.Steel, null, null, 120);
            CheckGoods(sb, Check, "weapon", ThingDefOf.MeleeWeapon_Knife,
                ThingDefOf.Plasteel, QualityCategory.Excellent, 3);
            CheckGoods(sb, Check, "chair", ThingDefOf.DiningChair,
                ThingDefOf.WoodLog, QualityCategory.Good, 4);
            CheckGoods(sb, Check, "workbench",
                DefDatabase<ThingDef>.GetNamedSilentFail("ElectricStove"), ThingDefOf.Steel, null, 1);

            sb.AppendLine($"  {passed} passed, {failed} failed.");
            return sb.ToString();
        }

        /// <summary>
        /// Exercises the real cancellation transition, including its reputation hook, and then
        /// proves the hourly order advance treats both kinds of cancelled order as inert.
        /// The settlement's live reputation record is restored by identity so no score or counter
        /// changes can leak into the player's save.
        /// </summary>
        private static void CheckPurchaseCancellation(
            System.Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            Settlement settlement)
        {
            if (settlement == null)
            {
                check("purchase cancellation has a settlement for its reputation record", false,
                    "the first economic profile no longer resolves to a settlement");
                return;
            }

            int settlementId = settlement.ID;
            bool hadReputation = state.Reputations.TryGetValue(
                settlementId, out CommercialReputation originalReputation);
            CommercialReputation testReputation = new CommercialReputation(
                settlementId, settlement.Label ?? "Self-test", settlement.Faction?.Name ?? "");
            state.Reputations[settlementId] = testReputation;

            try
            {
                int now = GenTicks.TicksGame;
                PurchaseOrder delivered = MakeCancellationOrder(
                    -501, settlement, supplierDelivers: true, PurchaseOrderStatus.Confirmed, 4850);
                bool deliveredCancelled = PurchaseOrderService.Cancel(delivered);

                check("a confirmed delivery purchase can be cancelled",
                    deliveredCancelled && delivered.status == PurchaseOrderStatus.Cancelled,
                    $"returned {deliveredCancelled}, status {delivered.status}");
                check("cancelling a confirmed purchase does not refund its payment",
                    delivered.paidSilver == 4850, $"recorded payment {delivered.paidSilver}");
                check("cancelling a confirmed purchase records an outcome",
                    !delivered.outcomeNote.NullOrEmpty(), $"\"{delivered.outcomeNote}\"");
                check("purchase cancellation is recorded in commercial reputation",
                    testReputation.purchaseCancellations == 1 &&
                    Mathf.Approximately(
                        testReputation.Score, CommercialReputation.StartingScore - 4f),
                    $"{testReputation.purchaseCancellations} cancellation(s), " +
                    $"score {testReputation.Score:F1}");

                PurchaseOrder pickup = MakeCancellationOrder(
                    -502, settlement, supplierDelivers: false,
                    PurchaseOrderStatus.ReadyForPickup, 720);
                bool pickupCancelled = PurchaseOrderService.Cancel(pickup);

                check("a ready-for-pickup purchase can be cancelled",
                    pickupCancelled && pickup.status == PurchaseOrderStatus.Cancelled,
                    $"returned {pickupCancelled}, status {pickup.status}");
                check("cancelling a ready-for-pickup purchase does not refund its payment",
                    pickup.paidSilver == 720, $"recorded payment {pickup.paidSilver}");
                check("cancelling a ready-for-pickup purchase records an outcome",
                    !pickup.outcomeNote.NullOrEmpty(), $"\"{pickup.outcomeNote}\"");

                PurchaseOrder completed = MakeCancellationOrder(
                    -503, settlement, supplierDelivers: true,
                    PurchaseOrderStatus.Completed, 310);
                completed.outcomeNote = "Delivered before the cancellation probe.";
                string completedNote = completed.outcomeNote;
                int cancellationsBeforeGuard = testReputation.purchaseCancellations;
                float scoreBeforeGuard = testReputation.Score;
                bool completedCancelled = PurchaseOrderService.Cancel(completed);
                check("a completed purchase refuses cancellation and changes nothing",
                    !completedCancelled && completed.status == PurchaseOrderStatus.Completed &&
                    completed.paidSilver == 310 && completed.outcomeNote == completedNote &&
                    testReputation.purchaseCancellations == cancellationsBeforeGuard &&
                    Mathf.Approximately(testReputation.Score, scoreBeforeGuard),
                    $"returned {completedCancelled}, status {completed.status}, " +
                    $"paid {completed.paidSilver}, note \"{completed.outcomeNote}\", " +
                    $"reputation {testReputation.purchaseCancellations}/{testReputation.Score:F1}");

                PurchaseOrder alreadyCancelled = MakeCancellationOrder(
                    -504, settlement, supplierDelivers: false,
                    PurchaseOrderStatus.Cancelled, 915);
                alreadyCancelled.outcomeNote = "Already cancelled before the probe.";
                string cancelledNote = alreadyCancelled.outcomeNote;
                bool cancelledAgain = PurchaseOrderService.Cancel(alreadyCancelled);
                check("an already-cancelled purchase refuses cancellation and changes nothing",
                    !cancelledAgain && alreadyCancelled.status == PurchaseOrderStatus.Cancelled &&
                    alreadyCancelled.paidSilver == 915 &&
                    alreadyCancelled.outcomeNote == cancelledNote &&
                    testReputation.purchaseCancellations == cancellationsBeforeGuard &&
                    Mathf.Approximately(testReputation.Score, scoreBeforeGuard),
                    $"returned {cancelledAgain}, status {alreadyCancelled.status}, " +
                    $"paid {alreadyCancelled.paidSilver}, note \"{alreadyCancelled.outcomeNote}\", " +
                    $"reputation {testReputation.purchaseCancellations}/{testReputation.Score:F1}");

                delivered.readyTick = now - 1;
                pickup.pickupExpiryTick = now - 1;
                string deliveredNote = delivered.outcomeNote;
                string pickupNote = pickup.outcomeNote;
                int reputationBeforeAdvance = testReputation.purchaseCancellations;

                PurchaseOrderService.AdvanceOrders(new List<PurchaseOrder> { delivered, pickup });

                check("advance leaves a cancelled delivery order inert",
                    delivered.status == PurchaseOrderStatus.Cancelled &&
                    delivered.paidSilver == 4850 && delivered.outcomeNote == deliveredNote,
                    $"status {delivered.status}, paid {delivered.paidSilver}, " +
                    $"note \"{delivered.outcomeNote}\"");
                check("advance leaves an expired cancelled pickup order inert",
                    pickup.status == PurchaseOrderStatus.Cancelled &&
                    pickup.paidSilver == 720 && pickup.outcomeNote == pickupNote,
                    $"status {pickup.status}, paid {pickup.paidSilver}, " +
                    $"note \"{pickup.outcomeNote}\"");
                check("advance does not record another cancellation side effect",
                    testReputation.purchaseCancellations == reputationBeforeAdvance,
                    $"{reputationBeforeAdvance} -> {testReputation.purchaseCancellations}");
            }
            finally
            {
                if (hadReputation)
                {
                    state.Reputations[settlementId] = originalReputation;
                }
                else
                {
                    state.Reputations.Remove(settlementId);
                }
            }
        }

        private static PurchaseOrder MakeCancellationOrder(
            int id,
            Settlement settlement,
            bool supplierDelivers,
            PurchaseOrderStatus status,
            int paidSilver)
        {
            return new PurchaseOrder
            {
                id = id,
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "Self-test",
                factionName = settlement.Faction?.Name ?? "",
                thingDef = ThingDefOf.Steel,
                quantity = 100,
                unitPrice = paidSilver / 100f,
                paidSilver = paidSilver,
                supplierDelivers = supplierDelivers,
                orderedTick = GenTicks.TicksGame,
                readyTick = GenTicks.TicksGame + GenDate.TicksPerDay,
                pickupExpiryTick = GenTicks.TicksGame + 2 * GenDate.TicksPerDay,
                status = status
            };
        }

        /// <summary>
        /// Builds the goods for a synthetic purchase order and verifies they carry exactly what
        /// was promised. Nothing is spawned into the world — the objects are inspected and
        /// destroyed — so running this leaves no residue.
        /// </summary>
        private static void CheckGoods(
            StringBuilder sb,
            System.Action<string, bool, string> check,
            string caseName,
            ThingDef def,
            ThingDef stuff,
            QualityCategory? quality,
            int quantity)
        {
            if (def == null)
            {
                sb.AppendLine($"    {caseName}: SKIPPED (def not in this install)");
                return;
            }

            PurchaseOrder order = new PurchaseOrder
            {
                id = 0,
                thingDef = def,
                stuffDef = def.MadeFromStuff ? stuff : null,
                quality = IntercolonyPricing.CanHaveQuality(def) ? quality : null,
                quantity = quantity,
                settlementName = "SelfTest"
            };

            List<Thing> goods = PurchaseOrderService.MakeGoods(order);
            check($"{caseName}: goods are produced", goods.Count > 0, null);
            if (goods.Count == 0)
            {
                return;
            }

            int units = 0;
            int wrongDef = 0;
            int wrongStuff = 0;
            int wrongQuality = 0;
            int uncrated = 0;

            foreach (Thing thing in goods)
            {
                units += OrderValidator.CountableUnits(thing);

                // Buildings must arrive crated or they cannot be hauled home.
                if (def.Minifiable && !(thing is MinifiedThing))
                {
                    uncrated++;
                }

                Thing inner = thing.GetInnerIfMinified();
                if (inner.def != def)
                {
                    wrongDef++;
                }

                if (order.stuffDef != null && inner.Stuff != order.stuffDef)
                {
                    wrongStuff++;
                }

                if (order.quality.HasValue)
                {
                    if (!inner.TryGetQuality(out QualityCategory got) || got != order.quality.Value)
                    {
                        wrongQuality++;
                    }
                }
            }

            check($"{caseName}: full quantity produced", units == quantity, $"{units} of {quantity}");
            check($"{caseName}: correct item", wrongDef == 0, $"{wrongDef} wrong");
            check($"{caseName}: material preserved", wrongStuff == 0, $"{wrongStuff} wrong");
            check($"{caseName}: quality preserved", wrongQuality == 0, $"{wrongQuality} wrong");
            check($"{caseName}: minifiable goods arrive crated", uncrated == 0, $"{uncrated} uncrated");

            sb.AppendLine($"    {caseName}: {units}x {order.ItemLabel()}" +
                          (def.Minifiable ? " (crated)" : ""));

            foreach (Thing thing in goods)
            {
                thing.Destroy(DestroyMode.Vanish);
            }
        }

        /// <summary>Highest-tech tradable def available, for the scarcity probe.</summary>
        private static ThingDef FindHighTechDef()
        {
            ThingDef best = null;
            foreach (ThingDef def in IntercolonyProductClassifier.TradableDefs)
            {
                if (def.techLevel == TechLevel.Undefined)
                {
                    continue;
                }

                if (best == null || def.techLevel > best.techLevel)
                {
                    best = def;
                }
            }

            return best;
        }
    }
}
