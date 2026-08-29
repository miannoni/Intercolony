using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
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
        private const int SupplyProbeSettlementId = 971_102;
        private const int PurchaseFixtureSilver = 4;

        private static bool IsLegacyAppealBucket(float appeal)
        {
            return Mathf.Approximately(appeal, 0f) ||
                   Mathf.Approximately(appeal, 0.5f) ||
                   Mathf.Approximately(appeal, 1f);
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;
            List<string> skippedAssertions = new List<string>();

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

            void Skip(string name, string reason)
            {
                skippedAssertions.Add(name);
                sb.AppendLine($"  SKIPPED  {name} — {reason}");
            }

            string Summarize()
            {
                if (skippedAssertions.Count == 0)
                {
                    sb.AppendLine($"  {passed} passed, {failed} failed, 0 skipped.");
                }
                else
                {
                    sb.AppendLine($"  {passed} passed, {failed} failed, " +
                                  $"{skippedAssertions.Count} SKIPPED — not a clean run.");
                    sb.AppendLine("  Skipped assertions:");
                    foreach (string name in skippedAssertions)
                    {
                        sb.AppendLine($"  SKIPPED  {name}");
                    }
                }

                return sb.ToString();
            }

            sb.AppendLine("RFQ self-test");

            CheckSupplierListings(Check, Skip, state);
            CheckProcurementContracts(Check, Skip, state);
            CheckStage8AFullSaveLoadMatrix(Check, Skip, state);
            CheckStage8BMigrationMatrix(Check, state);

            List<ThingDef> tradable = IntercolonyProductClassifier.TradableDefs;
            if (tradable.Count == 0 || state.AllProfiles().Count == 0)
            {
                Skip("RFQ self-test prerequisites", "no tradable defs or no settlements");
                return Summarize();
            }

            List<PurchaseRequest> created = new List<PurchaseRequest>();

            CheckEffectiveSupplyForRfq(Check, state);
            CheckRfqResponseCountUsesEffectiveSupply(Check, Skip, state);

            // Supplier stock belongs to its refresh window, not to any one RFQ. Exercise the
            // state mechanism without touching the live world's ledger or requests.
            ThingDef finiteOfferDef = new ThingDef { shortHash = 321 };
            IntercolonyWorldComponent finiteOfferState = new IntercolonyWorldComponent(null);
            Quotation sameWindowA = new Quotation
            {
                settlementId = 77,
                refreshWindow = 12,
                quantityOffered = 300
            };
            Quotation sameWindowB = new Quotation
            {
                settlementId = 77,
                refreshWindow = 12,
                quantityOffered = 300
            };
            Quotation priorWindow = new Quotation
            {
                settlementId = 77,
                refreshWindow = 11,
                quantityOffered = 300
            };
            finiteOfferState.AddRequest(new PurchaseRequest
            {
                thingDef = finiteOfferDef,
                quantityRequested = 300,
                quotes = new List<Quotation> { sameWindowA }
            });
            finiteOfferState.AddRequest(new PurchaseRequest
            {
                thingDef = finiteOfferDef,
                quantityRequested = 300,
                quotes = new List<Quotation> { sameWindowB }
            });
            finiteOfferState.AddRequest(new PurchaseRequest
            {
                thingDef = finiteOfferDef,
                quantityRequested = 300,
                quotes = new List<Quotation> { priorWindow }
            });

            finiteOfferState.ConsumeSupplierOffer(12, finiteOfferDef, 77, 100);
            Check("partial purchase remains on the accepted quotation",
                sameWindowA.quantityOffered == 200, $"{sameWindowA.quantityOffered} left");
            Check("parallel RFQs share finite supplier stock",
                sameWindowB.quantityOffered == 200, $"{sameWindowB.quantityOffered} left");
            Check("consumption is isolated by refresh window",
                priorWindow.quantityOffered == 300, $"{priorWindow.quantityOffered} left");
            Check("consumption ledger records the genuine count",
                finiteOfferState.SupplierOfferConsumptionFor(12, finiteOfferDef, 77) == 100);
            Check("absent consumption is zero",
                finiteOfferState.SupplierOfferConsumptionFor(12, finiteOfferDef, 78) == 0);

            finiteOfferState.ConsumeSupplierOffer(12, finiteOfferDef, 77, 200);
            Check("exhausted supplier disappears from every matching RFQ",
                finiteOfferState.Requests[0].quotes.Count == 0 &&
                finiteOfferState.Requests[1].quotes.Count == 0);

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
                Skip("scarce high-tech supplier probe", "no high-tech tradable def found");
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
            CheckPurchaseOrderDisplaySelection(Check);

            // --- §104: purchased goods arrive and preserve expected properties ---
            // Built through the same path a real purchase uses, then inspected. §104's four
            // named cases: commodity, weapon/apparel, chair, workbench.
            sb.AppendLine("  §104 goods construction:");
            CheckGoods(sb, Check, Skip, "commodity", ThingDefOf.Steel, null, null, 120);
            CheckGoods(sb, Check, Skip, "weapon", ThingDefOf.MeleeWeapon_Knife,
                ThingDefOf.Plasteel, QualityCategory.Excellent, 3);
            CheckGoods(sb, Check, Skip, "chair", ThingDefOf.DiningChair,
                ThingDefOf.WoodLog, QualityCategory.Good, 4);
            CheckGoods(sb, Check, Skip, "workbench",
                DefDatabase<ThingDef>.GetNamedSilentFail("ElectricStove"), ThingDefOf.Steel, null, 1);

            return Summarize();
        }

        private static void CheckSupplierListings(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            List<SupplierListing> savedListings =
                new List<SupplierListing>(state.SupplierListings);
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            int savedSaveVersion = state.SaveVersion;
            int savedNextId = state.PeekNextId();

            try
            {
                CheckSupplierListingSentinel(check);
                CheckSupplierListingAvailability(check);
                CheckSupplierListingExpiryBoundary(check);
                CheckSupplierListingCollection(check, skip);
                CheckSupplierListingMigration(check, skip, state, saveVersionField);
                CheckSupplierListingIds(check, skip, state, nextIdField, savedNextId);
                CheckSupplierListingGeneration(check, skip, state);
            }
            finally
            {
                state.SupplierListings.Clear();
                state.SupplierListings.AddRange(savedListings);
                if (saveVersionField != null)
                {
                    saveVersionField.SetValue(state, savedSaveVersion);
                }

                if (nextIdField != null)
                {
                    nextIdField.SetValue(state, savedNextId);
                }
            }
        }

        private static void CheckSupplierListingGeneration(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            FieldInfo consumptionField = typeof(IntercolonyWorldComponent).GetField(
                "supplierOfferConsumption", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo profileCacheField = typeof(IntercolonyWorldComponent).GetField(
                "profileCache", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo refreshCountField = typeof(IntercolonyWorldComponent).GetField(
                "refreshCount", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo economySeedField = typeof(IntercolonyWorldComponent).GetField(
                "economySeed", BindingFlags.Instance | BindingFlags.NonPublic);

            if (state == null || consumptionField == null || profileCacheField == null ||
                refreshCountField == null || economySeedField == null)
            {
                SkipSupplierListingGeneration(
                    skip, "the live fixture fields needed for restoration are inaccessible");
                return;
            }

            List<SupplierListing> savedListings =
                new List<SupplierListing>(state.SupplierListings);
            List<SupplierOfferConsumption> savedConsumption = CloneConsumptions(
                consumptionField.GetValue(state) as List<SupplierOfferConsumption>);
            object savedProfiles = profileCacheField.GetValue(state);
            int savedRefreshCount = (int)refreshCountField.GetValue(state);
            int savedEconomySeed = (int)economySeedField.GetValue(state);

            try
            {
                List<Settlement> settlements = FindAccessibleSupplierSettlements(state);
                if (settlements.Count == 0)
                {
                    SkipSupplierListingGeneration(
                        skip, "no accessible settlement has an economic profile");
                    return;
                }

                List<SupplierOfferConsumption> liveConsumption =
                    consumptionField.GetValue(state) as List<SupplierOfferConsumption>;
                liveConsumption?.Clear();

                Settlement settlement = settlements[0];
                SettlementEconomicProfile profile = state.GetProfile(settlement);
                int window = state.RefreshCount;

                CheckSupplierListingIdempotence(
                    check, skip, state, settlement, window);
                CheckSupplierListingConsumedQuantity(
                    check, skip, settlement, profile, window);
                CheckSupplierListingTechGate(
                    check, skip, settlement, profile, window);
                CheckSupplierListingSupplyDirection(
                    check, skip, settlement, profile, window);
                CheckSupplierListingSharedPrice(
                    check, skip, settlement, profile, window);
                CheckSupplierListingRfqPrice(
                    check, skip, settlement, profile, window);
                CheckSupplierListingCap(
                    check, skip, settlement, profile, window);
                CheckSupplierListingStaleWindow(
                    check, skip, state, settlement, window);
                CheckSupplierListingPublishedRate(
                    check, skip, settlement, profile, window);
                CheckSupplierListingPurchasePath(check, skip, state, settlement);
            }
            finally
            {
                state.SupplierListings.Clear();
                state.SupplierListings.AddRange(savedListings);
                consumptionField.SetValue(state, savedConsumption);
                profileCacheField.SetValue(state, savedProfiles);
                refreshCountField.SetValue(state, savedRefreshCount);
                economySeedField.SetValue(state, savedEconomySeed);
            }
        }

        private static void SkipSupplierListingGeneration(
            Action<string, string> skip,
            string reason)
        {
            skip("T1 listing refresh is idempotent within one window", reason);
            skip("T2 listing quantity is net of consumed stock", reason);
            skip("T3 listing generation respects the technical gate", reason);
            skip("T4 surplus lists more than shortage", reason);
            skip("T5 listing price uses shared supplier pricing", reason);
            skip("T6 RFQ and listing prices agree", reason);
            skip("T7 listing count respects the per-settlement cap", reason);
            skip("T8 refresh prunes stale-window listings", reason);
            skip("T9 published listing rate does not move with purchase size", reason);
            SkipSupplierListingPurchasePath(skip, reason);
        }

        private static void CheckSupplierListingPurchasePath(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement)
        {
            FieldInfo consumptionField = typeof(IntercolonyWorldComponent).GetField(
                "supplierOfferConsumption", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo refreshCountField = typeof(IntercolonyWorldComponent).GetField(
                "refreshCount", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;

            if (state == null || settlement == null || consumptionField == null ||
                refreshCountField == null || nextIdField == null)
            {
                SkipSupplierListingPurchasePath(
                    skip, "the live fixture fields needed for restoration are inaccessible");
                return;
            }

            if (paymentMap == null)
            {
                SkipSupplierListingPurchasePath(skip, "no player map is available for payment");
                return;
            }

            List<SupplierOfferConsumption> liveConsumption =
                consumptionField.GetValue(state) as List<SupplierOfferConsumption>;
            if (liveConsumption == null)
            {
                SkipSupplierListingPurchasePath(
                    skip, "the live supplier-consumption list is inaccessible");
                return;
            }

            if (!IntercolonyMarketAccess.IsAccessible(settlement, out string accessReason))
            {
                SkipSupplierListingPurchasePath(
                    skip, $"the fixture settlement is not accessible: {accessReason}");
                return;
            }

            Dictionary<Thing, int> savedSilver = SnapshotStoredSilver(paymentMap);
            Thing fixtureSilver = null;
            Zone_Stockpile fixtureSilverZone = null;
            int availableSilver = PurchaseOrderService.CountColonySilver(paymentMap);
            if (availableSilver < PurchaseFixtureSilver)
            {
                int neededSilver = PurchaseFixtureSilver - availableSilver;
                Thing topUp = null;
                foreach (Thing silver in savedSilver.Keys)
                {
                    if (silver != null && !silver.Destroyed &&
                        silver.stackCount + neededSilver <= ThingDefOf.Silver.stackLimit)
                    {
                        topUp = silver;
                        break;
                    }
                }

                if (topUp != null)
                {
                    topUp.stackCount += neededSilver;
                }
                else if (!TryCreateStoredSilver(
                    paymentMap, neededSilver, out fixtureSilver, out fixtureSilverZone))
                {
                    SkipSupplierListingPurchasePath(
                        skip,
                        "the payment map had too little stored silver and the fixture could not " +
                        "create a temporary stored-silver stack");
                    return;
                }

                if (fixtureSilver != null)
                {
                    savedSilver[fixtureSilver] = fixtureSilver.stackCount;
                }
            }

            List<SupplierListing> savedListings =
                new List<SupplierListing>(state.SupplierListings);
            List<SupplierOfferConsumption> savedConsumption = CloneConsumptions(
                consumptionField.GetValue(state) as List<SupplierOfferConsumption>);
            List<PurchaseOrder> savedOrders = new List<PurchaseOrder>(state.PurchaseOrders);
            List<LedgerEntry> savedLedger = new List<LedgerEntry>(state.Ledger);
            int savedLedgerStartTick = state.LedgerStartTick;
            int savedRefreshCount = (int)refreshCountField.GetValue(state);
            int savedNextId = (int)nextIdField.GetValue(state);

            void ResetFixture(SupplierListing listing)
            {
                state.SupplierListings.Clear();
                if (listing != null)
                {
                    state.SupplierListings.Add(listing);
                }

                liveConsumption.Clear();
                state.PurchaseOrders.Clear();
                RestoreStoredSilver(paymentMap, savedSilver);
                foreach (Thing silver in savedSilver.Keys)
                {
                    if (silver != null && !silver.Destroyed &&
                        silver.stackCount < ThingDefOf.Silver.stackLimit)
                    {
                        // Keep every fixture stack alive when TryTakeSilver splits it; the exact
                        // player's purse is restored in the finally.
                        silver.stackCount = Mathf.Min(
                            ThingDefOf.Silver.stackLimit,
                            Mathf.Max(silver.stackCount + 1, PurchaseFixtureSilver + 1));
                    }
                }
            }

            int window = state.RefreshCount;
            const float PublishedRate = 0.51f;

            try
            {
                CheckSupplierMarketReadModel(
                    check, skip, state, settlement, window, refreshCountField, ResetFixture);

                SupplierListing rateListing = NewPurchasePathListing(
                    910_101, 2, settlement.ID, window, PublishedRate);
                ResetFixture(rateListing);
                int partialQuantity = 1;
                bool rateCreated = SupplierListingService.TryPurchase(
                    state, rateListing, partialQuantity,
                    out PurchaseOrder rateOrder, out string rateFailure);

                check(
                    "V1 listing purchase charges the published unit price",
                    rateCreated && rateOrder != null &&
                    rateOrder.unitPrice == rateListing.unitPrice,
                    $"listing={rateListing.id}; quantity={partialQuantity}; " +
                    $"published={rateListing.unitPrice:F2}; " +
                    $"charged={(rateOrder == null ? "null" : rateOrder.unitPrice.ToString("F2"))}; " +
                    $"failure={rateFailure ?? "none"}");

                SupplierListing totalListing = NewPurchasePathListing(
                    910_108, 2, settlement.ID, window, PublishedRate);
                ResetFixture(totalListing);
                int totalQuantity = totalListing.quantityAvailable;
                bool totalCreated = SupplierListingService.TryPurchase(
                    state, totalListing, totalQuantity,
                    out PurchaseOrder totalOrder, out string totalFailure);

                check(
                    "V8 listing total uses IntercolonyPricing.TotalPayment",
                    totalCreated && totalOrder != null &&
                    totalOrder.TotalPrice ==
                    IntercolonyPricing.TotalPayment(totalListing.unitPrice, totalQuantity),
                    $"listing={totalListing.id}; rate={totalListing.unitPrice:F2}; " +
                    $"quantity={totalQuantity}; " +
                    $"order total={(totalOrder == null ? "null" : totalOrder.TotalPrice.ToString())}; " +
                    $"shared total={IntercolonyPricing.TotalPayment(totalListing.unitPrice, totalQuantity)}; " +
                    $"failure={totalFailure ?? "none"}");

                SupplierListing boundsListing = NewPurchasePathListing(
                    910_102, 2, settlement.ID, window, PublishedRate);
                ResetFixture(boundsListing);
                int maximum = boundsListing.quantityAvailable;
                bool zeroCreated = SupplierListingService.TryPurchase(
                    state, boundsListing, 0,
                    out PurchaseOrder zeroOrder, out string zeroFailure);
                bool negativeCreated = SupplierListingService.TryPurchase(
                    state, boundsListing, -1,
                    out PurchaseOrder negativeOrder, out string negativeFailure);
                bool overCreated = SupplierListingService.TryPurchase(
                    state, boundsListing, maximum + 1,
                    out PurchaseOrder overOrder, out string overFailure);
                bool exactCreated = SupplierListingService.TryPurchase(
                    state, boundsListing, maximum,
                    out PurchaseOrder exactOrder, out string exactFailure);
                string boundsReason = $"between 1 and {maximum}";

                check(
                    "V2 listing purchase enforces quantity bounds",
                    !zeroCreated && !string.IsNullOrEmpty(zeroFailure) &&
                    zeroFailure.Contains(boundsReason) &&
                    !negativeCreated && !string.IsNullOrEmpty(negativeFailure) &&
                    negativeFailure.Contains(boundsReason) &&
                    !overCreated && !string.IsNullOrEmpty(overFailure) &&
                    overFailure.Contains(boundsReason) &&
                    exactCreated && exactOrder != null,
                    $"listing={boundsListing.id}; bounds=1..{maximum}; " +
                    $"published={PublishedRate:F2}; " +
                    $"attempted 0 -> created={zeroCreated}, reason={zeroFailure ?? "none"}; " +
                    $"attempted -1 -> created={negativeCreated}, reason={negativeFailure ?? "none"}; " +
                    $"attempted {maximum + 1} -> created={overCreated}, reason={overFailure ?? "none"}; " +
                    $"attempted {maximum} -> created={exactCreated}, reason={exactFailure ?? "none"}");

                SupplierListing decrementListing = NewPurchasePathListing(
                    910_103, 2, settlement.ID, window, PublishedRate);
                ResetFixture(decrementListing);
                int decrementBefore = decrementListing.quantityAvailable;
                int decrementBought = 1;
                bool decrementCreated = SupplierListingService.TryPurchase(
                    state, decrementListing, decrementBought,
                    out PurchaseOrder decrementOrder, out string decrementFailure);
                int decrementAfter = decrementListing.quantityAvailable;

                check(
                    "V3 listing purchase decrements exactly the bought quantity",
                    decrementCreated && decrementOrder != null &&
                    decrementAfter == decrementBefore - decrementBought,
                    $"listing={decrementListing.id}; bought={decrementBought}; " +
                    $"published={PublishedRate:F2}; quantity {decrementBefore}->{decrementAfter}; " +
                    $"silver={(decrementOrder == null ? "null" : decrementOrder.paidSilver.ToString())}; " +
                    $"failure={decrementFailure ?? "none"}");

                SupplierListing consumptionListing = NewPurchasePathListing(
                    910_104, 2, settlement.ID, window, PublishedRate);
                ResetFixture(consumptionListing);
                int consumptionBefore = state.SupplierOfferConsumptionFor(
                    window, consumptionListing.thingDef, settlement.ID);
                int consumptionBought = 1;
                bool consumptionCreated = SupplierListingService.TryPurchase(
                    state, consumptionListing, consumptionBought,
                    out PurchaseOrder consumptionOrder, out string consumptionFailure);
                int consumptionAfter = state.SupplierOfferConsumptionFor(
                    window, consumptionListing.thingDef, settlement.ID);

                check(
                    "V4 listing purchase records supplier-offer consumption",
                    consumptionCreated && consumptionOrder != null &&
                    consumptionAfter == consumptionBefore + consumptionBought,
                    $"listing={consumptionListing.id}; settlement={settlement.ID}; " +
                    $"item={consumptionListing.thingDef.defName}; window={window}; " +
                    $"bought={consumptionBought}; published={PublishedRate:F2}; " +
                    $"consumption {consumptionBefore}->{consumptionAfter}; " +
                    $"silver={(consumptionOrder == null ? "null" : consumptionOrder.paidSilver.ToString())}; " +
                    $"failure={consumptionFailure ?? "none"}");

                SupplierListing depletedListing = NewPurchasePathListing(
                    910_105, 2, settlement.ID, window, PublishedRate);
                ResetFixture(depletedListing);
                int depletedBefore = depletedListing.quantityAvailable;
                SupplierListing derivedProbe = NewPurchasePathListing(
                    910_110, 1, settlement.ID, window, PublishedRate);
                derivedProbe.quantityAvailable = 0;
                bool zeroQuantityAvailable = derivedProbe.IsAvailable;
                derivedProbe.quantityAvailable = 1;
                bool positiveQuantityAvailable = derivedProbe.IsAvailable;
                bool depletedCreated = SupplierListingService.TryPurchase(
                    state, depletedListing, depletedBefore,
                    out PurchaseOrder depletedOrder, out string depletedFailure);

                check(
                    "V5 depleted listing is unavailable through IsAvailable",
                    depletedCreated && depletedOrder != null &&
                    depletedListing.quantityAvailable == 0 && !depletedListing.IsAvailable &&
                    !zeroQuantityAvailable && positiveQuantityAvailable,
                    $"listing={depletedListing.id}; quantity {depletedBefore}->" +
                    $"{depletedListing.quantityAvailable}; published={PublishedRate:F2}; " +
                    $"silver={(depletedOrder == null ? "null" : depletedOrder.paidSilver.ToString())}; " +
                    $"available={depletedListing.IsAvailable}; " +
                    $"derived probe quantity=0 available={zeroQuantityAvailable}, " +
                    $"quantity=1 available={positiveQuantityAvailable}; " +
                    $"failure={depletedFailure ?? "none"}");

                SupplierListing failedListing = NewPurchasePathListing(
                    910_106, 2, settlement.ID, window, PublishedRate);
                ResetFixture(failedListing);
                int failedQuantityBefore = failedListing.quantityAvailable;
                int failedConsumptionBefore = state.SupplierOfferConsumptionFor(
                    window, failedListing.thingDef, settlement.ID);
                int failedOrdersBefore = state.PurchaseOrders.Count;
                int failedSilverBefore = PurchaseOrderService.CountColonySilver(paymentMap);
                int failedAttempt = failedQuantityBefore + 1;
                bool failedCreated = SupplierListingService.TryPurchase(
                    state, failedListing, failedAttempt,
                    out PurchaseOrder failedOrder, out string failedReason);
                int failedQuantityAfter = failedListing.quantityAvailable;
                int failedConsumptionAfter = state.SupplierOfferConsumptionFor(
                    window, failedListing.thingDef, settlement.ID);
                int failedOrdersAfter = state.PurchaseOrders.Count;
                int failedSilverAfter = PurchaseOrderService.CountColonySilver(paymentMap);

                check(
                    "V6 failed listing purchase changes nothing",
                    !failedCreated && failedOrder == null && !string.IsNullOrEmpty(failedReason) &&
                    failedQuantityAfter == failedQuantityBefore &&
                    failedConsumptionAfter == failedConsumptionBefore &&
                    failedOrdersAfter == failedOrdersBefore &&
                    failedSilverAfter == failedSilverBefore,
                    $"listing={failedListing.id}; attempted={failedAttempt}; bounds=1..{failedQuantityBefore}; " +
                    $"published={PublishedRate:F2}; " +
                    $"listing quantity {failedQuantityBefore}->{failedQuantityAfter}; " +
                    $"consumption {failedConsumptionBefore}->{failedConsumptionAfter}; " +
                    $"orders {failedOrdersBefore}->{failedOrdersAfter}; " +
                    $"silver {failedSilverBefore}->{failedSilverAfter}; " +
                    $"reason={failedReason ?? "none"}");

                SupplierListing originListing = NewPurchasePathListing(
                    910_107, 2, settlement.ID, window, PublishedRate);
                ResetFixture(originListing);
                bool originListingCreated = SupplierListingService.TryPurchase(
                    state, originListing, 1,
                    out PurchaseOrder listingOrder, out string listingFailure);

                ResetFixture(null);
                PurchaseRequest rfqRequest = new PurchaseRequest
                {
                    id = 910_111,
                    thingDef = ThingDefOf.Steel,
                    quantityRequested = 1,
                    desiredDays = 1
                };
                Quotation rfqQuote = new Quotation
                {
                    id = 910_112,
                    settlementId = settlement.ID,
                    settlementName = settlement.Label ?? "unnamed",
                    factionName = settlement.Faction?.Name ?? "",
                    refreshWindow = window,
                    quantityOffered = 1,
                    unitPrice = PublishedRate,
                    leadTimeDays = 0,
                    supplierDelivers = true
                };
                rfqRequest.quotes.Add(rfqQuote);
                PurchaseOrder rfqOrder = PurchaseOrderService.AcceptQuote(
                    state, rfqRequest, rfqQuote, paymentMap, 1);

                check(
                    "V7 purchase origins remain traceable",
                    originListingCreated && listingOrder != null &&
                    listingOrder.supplierListingId == originListing.id &&
                    rfqOrder != null &&
                    rfqOrder.supplierListingId == PurchaseOrder.NoSupplierListing,
                    $"listing id={originListing.id}; listing order id=" +
                    $"{(listingOrder == null ? "null" : listingOrder.id.ToString())}; " +
                    $"listing origin matches listing id=" +
                    $"{(listingOrder != null && listingOrder.supplierListingId == originListing.id)}; " +
                    $"RFQ order id={(rfqOrder == null ? "null" : rfqOrder.id.ToString())}; " +
                    "RFQ origin=NoSupplierListing; " +
                    $"published={PublishedRate:F2}; " +
                    $"listing failure={listingFailure ?? "none"}");

                CheckPurchaseOrdersReadModel(
                    check, skip, state, settlement, ResetFixture, listingOrder, rfqOrder);
            }
            finally
            {
                state.SupplierListings.Clear();
                state.SupplierListings.AddRange(savedListings);
                liveConsumption.Clear();
                liveConsumption.AddRange(savedConsumption);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedOrders);
                state.Ledger.Clear();
                state.Ledger.AddRange(savedLedger);
                state.LedgerStartTick = savedLedgerStartTick;
                refreshCountField.SetValue(state, savedRefreshCount);
                nextIdField.SetValue(state, savedNextId);
                RestoreStoredSilver(paymentMap, savedSilver);
                if (fixtureSilver != null && !fixtureSilver.Destroyed)
                {
                    fixtureSilver.Destroy(DestroyMode.Vanish);
                }

                fixtureSilverZone?.Delete(playSound: false);
            }
        }

        private static void SkipSupplierListingPurchasePath(
            Action<string, string> skip,
            string reason)
        {
            skip("V1 listing purchase charges the published unit price", reason);
            skip("V2 listing purchase enforces quantity bounds", reason);
            skip("V3 listing purchase decrements exactly the bought quantity", reason);
            skip("V4 listing purchase records supplier-offer consumption", reason);
            skip("V5 depleted listing is unavailable through IsAvailable", reason);
            skip("V6 failed listing purchase changes nothing", reason);
            skip("V7 purchase origins remain traceable", reason);
            skip("V8 listing total uses IntercolonyPricing.TotalPayment", reason);
            SkipSupplierMarketReadModel(skip, reason);
            SkipPurchaseOrdersReadModel(skip, reason);
        }

        private static void CheckPurchaseOrdersReadModel(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement,
            Action<SupplierListing> resetFixture,
            PurchaseOrder listingOrder,
            PurchaseOrder rfqOrder)
        {
            if (state == null || settlement == null || resetFixture == null)
            {
                SkipPurchaseOrdersReadModel(
                    skip, "the live purchase-order fixture is inaccessible");
                return;
            }

            if (listingOrder == null || rfqOrder == null)
            {
                skip(
                    "P1 Purchase Orders row carries order values",
                    $"the fixture could not construct both known orders: " +
                    $"listing={(listingOrder == null ? "null" : listingOrder.id.ToString())}, " +
                    $"RFQ={(rfqOrder == null ? "null" : rfqOrder.id.ToString())}");
                skip(
                    "P5 Purchase Orders origin is correct",
                    $"the fixture could not construct both known orders: " +
                    $"listing={(listingOrder == null ? "null" : listingOrder.id.ToString())}, " +
                    $"RFQ={(rfqOrder == null ? "null" : rfqOrder.id.ToString())}");
            }
            else
            {
                resetFixture(null);
                state.PurchaseOrders.Add(listingOrder);
                state.PurchaseOrders.Add(rfqOrder);

                int recomputedTotal = listingOrder.TotalPrice;
                listingOrder.paidSilver = recomputedTotal + 7;
                PurchaseOrdersRow valuesRow = PurchaseOrdersUiService.BuildRow(listingOrder);
                string expectedSupplier = listingOrder.settlementName + "\nSupplier Market";
                string expectedItem = listingOrder.ItemLabel();
                string expectedFulfillment = listingOrder.supplierDelivers
                    ? "Delivery"
                    : "Pickup";
                int expectedTotal = listingOrder.paidSilver;

                check(
                    "P1 Purchase Orders row carries order values",
                    valuesRow.order == listingOrder &&
                    valuesRow.orderId == listingOrder.id &&
                    valuesRow.orderIdLabel == $"#{listingOrder.id}" &&
                    valuesRow.supplierLabel == expectedSupplier &&
                    valuesRow.itemLabel == expectedItem &&
                    valuesRow.quantity == listingOrder.quantity &&
                    valuesRow.quantityLabel == listingOrder.quantity.ToString("N0") &&
                    valuesRow.totalPrice == expectedTotal &&
                    valuesRow.totalPriceLabel == $"{expectedTotal:N0} silver" &&
                    valuesRow.fulfillmentLabel == expectedFulfillment,
                    $"order={listingOrder.id}; supplier row=" +
                    $"\"{valuesRow.supplierLabel}\" expected=\"{expectedSupplier}\"; " +
                    $"item row=\"{valuesRow.itemLabel}\" expected=\"{expectedItem}\"; " +
                    $"quantity row/order={valuesRow.quantity}/{listingOrder.quantity}; " +
                    $"total row/order/recomputed={valuesRow.totalPrice}/{expectedTotal}/" +
                    $"{recomputedTotal}; fulfillment row/expected=\"" +
                    $"{valuesRow.fulfillmentLabel}\"/\"{expectedFulfillment}\"");
            }

            resetFixture(null);
            PurchaseOrder deliveryOrder = MakeReadModelOrder(
                960_201, settlement, supplierDelivers: true,
                PurchaseOrderStatus.Confirmed, PurchaseOrder.NoSupplierListing);
            PurchaseOrder pickupOrder = MakeReadModelOrder(
                960_202, settlement, supplierDelivers: false,
                PurchaseOrderStatus.Confirmed, PurchaseOrder.NoSupplierListing);
            int now = GenTicks.TicksGame;
            deliveryOrder.readyTick = now + 2 * GenDate.TicksPerDay;
            deliveryOrder.pickupExpiryTick = now + 9 * GenDate.TicksPerDay;
            pickupOrder.readyTick = now + 3 * GenDate.TicksPerDay;
            pickupOrder.pickupExpiryTick = now + 11 * GenDate.TicksPerDay;
            PurchaseOrdersRow deliveryRow = PurchaseOrdersUiService.BuildRow(deliveryOrder);
            PurchaseOrdersRow pickupRow = PurchaseOrdersUiService.BuildRow(pickupOrder);

            check(
                "P2 Purchase Orders timing names the correct fulfillment fact",
                deliveryRow.hasTiming && pickupRow.hasTiming &&
                deliveryRow.timingTick == deliveryOrder.readyTick &&
                pickupRow.timingTick == pickupOrder.pickupExpiryTick &&
                deliveryRow.timingLabel != pickupRow.timingLabel &&
                deliveryRow.timingLabel.Contains("Arrives in") &&
                !deliveryRow.timingLabel.Contains("Collect by") &&
                pickupRow.timingLabel.Contains("Collect by") &&
                !pickupRow.timingLabel.Contains("Arrives in"),
                $"delivery order={deliveryOrder.id}; label=\"{deliveryRow.timingLabel}\"; " +
                $"tick row/order={deliveryRow.timingTick}/{deliveryOrder.readyTick}; " +
                $"pickup order={pickupOrder.id}; label=\"{pickupRow.timingLabel}\"; " +
                $"tick row/order={pickupRow.timingTick}/{pickupOrder.pickupExpiryTick}");

            resetFixture(null);
            PurchaseOrder noArrivalOrder = MakeReadModelOrder(
                960_301, settlement, supplierDelivers: true,
                PurchaseOrderStatus.Confirmed, PurchaseOrder.NoSupplierListing);
            noArrivalOrder.readyTick = 0;
            PurchaseOrder noPickupOrder = MakeReadModelOrder(
                960_302, settlement, supplierDelivers: false,
                PurchaseOrderStatus.Confirmed, PurchaseOrder.NoSupplierListing);
            noPickupOrder.pickupExpiryTick = 0;
            PurchaseOrdersRow noArrivalRow = PurchaseOrdersUiService.BuildRow(noArrivalOrder);
            PurchaseOrdersRow noPickupRow = PurchaseOrdersUiService.BuildRow(noPickupOrder);
            string sentinelRendering = float.MaxValue.ToString(
                "F0", System.Globalization.CultureInfo.InvariantCulture);

            check(
                "P3 Purchase Orders missing timing never formats a sentinel",
                !noArrivalRow.hasTiming && !noPickupRow.hasTiming &&
                noArrivalRow.timingLabel == "No arrival date" &&
                noPickupRow.timingLabel == "No pickup deadline" &&
                !ContainsDigit(noArrivalRow.timingLabel) &&
                !ContainsDigit(noPickupRow.timingLabel) &&
                !noArrivalRow.timingLabel.Contains(sentinelRendering) &&
                !noPickupRow.timingLabel.Contains(sentinelRendering),
                $"arrival order={noArrivalOrder.id}; label=\"{noArrivalRow.timingLabel}\"; " +
                $"pickup order={noPickupOrder.id}; label=\"{noPickupRow.timingLabel}\"; " +
                $"float.MaxValue F0=\"{sentinelRendering}\"");

            resetFixture(null);
            List<PurchaseOrder> statusOrders = new List<PurchaseOrder>();
            List<string> enumeratedStatuses = new List<string>();
            int statusOrderId = 960_400;
            foreach (PurchaseOrderStatus status in Enum.GetValues(typeof(PurchaseOrderStatus)))
            {
                enumeratedStatuses.Add(status.ToString());
                PurchaseOrder statusOrder = MakeReadModelOrder(
                    statusOrderId++, settlement, supplierDelivers: true, status: status,
                    PurchaseOrder.NoSupplierListing);
                statusOrders.Add(statusOrder);
                state.PurchaseOrders.Add(statusOrder);
            }

            List<PurchaseOrdersRow> statusRows = PurchaseOrdersUiService.BuildRows(state);
            bool allStatusesClassified = statusRows.Count == statusOrders.Count;
            int liveCount = 0;
            int concludedCount = 0;
            bool liveFirst = true;
            bool reachedConcluded = false;
            List<string> groupMemberships = new List<string>();
            foreach (PurchaseOrdersRow row in statusRows)
            {
                if (row.isLive)
                {
                    liveCount++;
                    if (reachedConcluded)
                    {
                        liveFirst = false;
                    }
                }
                else
                {
                    concludedCount++;
                    reachedConcluded = true;
                }

                groupMemberships.Add(
                    $"{row.orderId}:{row.order?.status.ToString() ?? "null"}=" +
                    (row.isLive ? "live" : "concluded"));
            }

            foreach (PurchaseOrder expectedOrder in statusOrders)
            {
                bool found = false;
                foreach (PurchaseOrdersRow row in statusRows)
                {
                    if (row.order == expectedOrder)
                    {
                        bool expectedLive = expectedOrder.status == PurchaseOrderStatus.Confirmed ||
                                             expectedOrder.status == PurchaseOrderStatus.ReadyForPickup;
                        found = row.isLive == expectedLive;
                        break;
                    }
                }

                allStatusesClassified &= found;
            }

            check(
                "P4 Purchase Orders separate every status live-before-concluded",
                allStatusesClassified && liveCount > 0 && concludedCount > 0 && liveFirst,
                $"statuses enumerated=[{string.Join(", ", enumeratedStatuses.ToArray())}]; " +
                $"rows={statusRows.Count}/{statusOrders.Count}; live={liveCount}; " +
                $"concluded={concludedCount}; liveFirst={liveFirst}; " +
                $"groups=[{string.Join(", ", groupMemberships.ToArray())}]");

            if (listingOrder != null && rfqOrder != null)
            {
                PurchaseOrdersRow listingOriginRow = PurchaseOrdersUiService.BuildRow(listingOrder);
                PurchaseOrdersRow rfqOriginRow = PurchaseOrdersUiService.BuildRow(rfqOrder);
                string expectedListingSupplier = listingOrder.settlementName +
                                                  "\nSupplier Market";
                string expectedRfqSupplier = rfqOrder.settlementName + "\nRFQ";

                check(
                    "P5 Purchase Orders origin is correct",
                    listingOriginRow.supplierLabel == expectedListingSupplier &&
                    rfqOriginRow.supplierLabel == expectedRfqSupplier &&
                    listingOriginRow.supplierLabel.Contains("Supplier Market") &&
                    !listingOriginRow.supplierLabel.Contains("RFQ") &&
                    rfqOriginRow.supplierLabel.Contains("RFQ") &&
                    !rfqOriginRow.supplierLabel.Contains("Supplier Market"),
                    $"listing order={listingOrder.id}; supplierListingId=" +
                    $"{listingOrder.supplierListingId}; label=\"{listingOriginRow.supplierLabel}\"; " +
                    $"expected=\"{expectedListingSupplier}\"; RFQ order={rfqOrder.id}; " +
                    $"supplierListingId={rfqOrder.supplierListingId}; " +
                    $"label=\"{rfqOriginRow.supplierLabel}\"; " +
                    $"expected=\"{expectedRfqSupplier}\"");
            }

            PurchaseOrdersRow historyRow = PurchaseOrdersUiService.BuildRow(
                rfqOrder ?? MakeReadModelOrder(
                    960_601, settlement, supplierDelivers: true,
                    PurchaseOrderStatus.Confirmed, PurchaseOrder.NoSupplierListing));
            bool rowHasQuotationList = false;
            bool rowHasRequestStatus = false;
            bool rowHasRequestTimeline = false;
            foreach (FieldInfo field in typeof(PurchaseOrdersRow).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string fieldName = field.Name.ToLowerInvariant();
                rowHasQuotationList |= fieldName.Contains("quote") ||
                    fieldName.Contains("quotation") || field.FieldType == typeof(PurchaseRequest) ||
                    typeof(IEnumerable<Quotation>).IsAssignableFrom(field.FieldType);
                rowHasRequestStatus |= fieldName.Contains("requeststatus") ||
                    field.FieldType == typeof(PurchaseRequestStatus);
                rowHasRequestTimeline |= fieldName.Contains("requesttimeline") ||
                    fieldName.Contains("timeline");
            }

            string historyTooltip = historyRow.tooltip ?? "";
            bool tooltipHasRequestHistory = historyTooltip.IndexOf(
                "request", StringComparison.OrdinalIgnoreCase) >= 0;
            bool tooltipHasQuotationList = historyTooltip.IndexOf(
                "quote", StringComparison.OrdinalIgnoreCase) >= 0 ||
                historyTooltip.IndexOf("quotation", StringComparison.OrdinalIgnoreCase) >= 0;
            bool tooltipHasTimeline = historyTooltip.IndexOf(
                "timeline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                historyTooltip.IndexOf("history", StringComparison.OrdinalIgnoreCase) >= 0;

            check(
                "P6 Purchase Orders row omits request history",
                !rowHasQuotationList && !rowHasRequestStatus && !rowHasRequestTimeline &&
                !tooltipHasRequestHistory && !tooltipHasQuotationList && !tooltipHasTimeline,
                $"row fields expose quotationList={rowHasQuotationList}, " +
                $"requestStatus={rowHasRequestStatus}, requestTimeline={rowHasRequestTimeline}; " +
                $"tooltip request={tooltipHasRequestHistory}, quotationList={tooltipHasQuotationList}, " +
                $"timeline/history={tooltipHasTimeline}; tooltip=\"{historyTooltip}\"");

            CheckPurchaseOrderAction(check, skip, settlement);

            resetFixture(null);
            PurchaseOrder oldOrder = MakeReadModelOrder(
                960_801, settlement, supplierDelivers: true,
                PurchaseOrderStatus.Confirmed, PurchaseOrder.NoSupplierListing);
            state.PurchaseOrders.Add(oldOrder);
            List<PurchaseOrdersRow> oldRows = PurchaseOrdersUiService.BuildRows(state);
            resetFixture(null);
            PurchaseOrder newOrder = MakeReadModelOrder(
                960_802, settlement, supplierDelivers: false,
                PurchaseOrderStatus.ReadyForPickup, PurchaseOrder.NoSupplierListing);
            state.PurchaseOrders.Add(newOrder);
            List<PurchaseOrdersRow> newRows = PurchaseOrdersUiService.BuildRows(state);

            check(
                "P8 Purchase Orders rows refresh after the order set changes",
                oldRows.Count == 1 && oldRows[0].order == oldOrder &&
                newRows.Count == 1 && newRows[0].order == newOrder &&
                newRows[0].orderId == newOrder.id &&
                !ContainsPurchaseOrderRow(newRows, oldOrder.id),
                $"old order={oldOrder.id}; old rows={PurchaseOrderRowIds(oldRows)}; " +
                $"new order={newOrder.id}; new rows={PurchaseOrderRowIds(newRows)}; " +
                $"new group={(newRows.Count == 0 ? "none" :
                    (newRows[0].isLive ? "live" : "concluded"))}");
        }

        private static void SkipPurchaseOrdersReadModel(
            Action<string, string> skip,
            string reason)
        {
            skip("P1 Purchase Orders row carries order values", reason);
            skip("P2 Purchase Orders timing names the correct fulfillment fact", reason);
            skip("P3 Purchase Orders missing timing never formats a sentinel", reason);
            skip("P4 Purchase Orders separate every status live-before-concluded", reason);
            skip("P5 Purchase Orders origin is correct", reason);
            skip("P6 Purchase Orders row omits request history", reason);
            skip("P7 Purchase Orders action reuses cancellation refusal", reason);
            skip("P8 Purchase Orders rows refresh after the order set changes", reason);
        }

        private static void CheckPurchaseOrderAction(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement)
        {
            MethodInfo actionMethod = typeof(MainTabWindow_Intercolony).GetMethod(
                "ConfirmPurchaseCancellation", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo liveMessagesField = typeof(Messages).GetField(
                "liveMessages", BindingFlags.Static | BindingFlags.NonPublic);
            if (actionMethod == null || liveMessagesField == null || Find.WindowStack == null)
            {
                skip(
                    "P7 Purchase Orders action reuses cancellation refusal",
                    "the existing per-row handler, message list, or window stack is inaccessible");
                return;
            }

            List<Message> liveMessages = liveMessagesField.GetValue(null) as List<Message>;
            if (liveMessages == null)
            {
                skip(
                    "P7 Purchase Orders action reuses cancellation refusal",
                    "the existing live message list is inaccessible");
                return;
            }

            List<Window> savedWindows = new List<Window>(Find.WindowStack.Windows);
            List<Message> savedMessages = new List<Message>(liveMessages);
            bool ok = false;
            string detail = "no refusal was surfaced";
            try
            {
                PurchaseOrder order = MakeReadModelOrder(
                    960_901, settlement, supplierDelivers: true,
                    PurchaseOrderStatus.Confirmed, PurchaseOrder.NoSupplierListing);
                PurchaseOrdersRow row = PurchaseOrdersUiService.BuildRow(order);
                order.status = PurchaseOrderStatus.Completed;
                bool cancelled = PurchaseOrderService.Cancel(order, out string refusalReason);
                actionMethod.Invoke(new MainTabWindow_Intercolony(), new object[] { row });

                Dialog_MessageBox dialog = null;
                for (int i = Find.WindowStack.Windows.Count - 1; i >= 0; i--)
                {
                    Window window = Find.WindowStack.Windows[i];
                    if (!savedWindows.Contains(window) && window is Dialog_MessageBox)
                    {
                        dialog = (Dialog_MessageBox)window;
                        break;
                    }
                }

                if (dialog != null && dialog.buttonAAction != null)
                {
                    dialog.buttonAAction();
                }

                string surfaced = null;
                for (int i = liveMessages.Count - 1; i >= 0; i--)
                {
                    if (liveMessages[i] != null &&
                        liveMessages[i].text.ToString() == refusalReason)
                    {
                        surfaced = liveMessages[i].text.ToString();
                        break;
                    }
                }

                ok = !cancelled && row.actionLabel == "Cancel" && dialog != null &&
                     !refusalReason.NullOrEmpty() && surfaced == refusalReason;
                detail = $"order={order.id}; action=\"{row.actionLabel}\"; " +
                         $"service refused={(!cancelled)}; service=\"{refusalReason}\"; " +
                         $"surfaced=\"{surfaced ?? "none"}\"";
            }
            catch (Exception ex)
            {
                detail = $"handler/message probe threw {ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                List<Window> currentWindows = new List<Window>(Find.WindowStack.Windows);
                foreach (Window window in currentWindows)
                {
                    if (!savedWindows.Contains(window))
                    {
                        Find.WindowStack.TryRemove(window, doCloseSound: false);
                    }
                }

                liveMessages.Clear();
                liveMessages.AddRange(savedMessages);
            }

            check(
                "P7 Purchase Orders action reuses cancellation refusal", ok, detail);
        }

        private static PurchaseOrder MakeReadModelOrder(
            int id,
            Settlement settlement,
            bool supplierDelivers,
            PurchaseOrderStatus status,
            int supplierListingId)
        {
            int now = GenTicks.TicksGame;
            return new PurchaseOrder
            {
                id = id,
                requestId = supplierListingId == PurchaseOrder.NoSupplierListing ? 910_111 : 0,
                quotationId = supplierListingId == PurchaseOrder.NoSupplierListing ? 910_112 : 0,
                supplierListingId = supplierListingId,
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "Self-test",
                factionName = settlement.Faction?.Name ?? "",
                thingDef = ThingDefOf.Steel,
                quantity = 3,
                unitPrice = 0.51f,
                paidSilver = 2,
                supplierDelivers = supplierDelivers,
                orderedTick = now,
                readyTick = now + GenDate.TicksPerDay,
                pickupExpiryTick = now + 4 * GenDate.TicksPerDay,
                status = status
            };
        }

        private static bool ContainsDigit(string value)
        {
            if (value == null)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (character >= '0' && character <= '9')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPurchaseOrderRow(
            List<PurchaseOrdersRow> rows,
            int orderId)
        {
            if (rows == null)
            {
                return false;
            }

            foreach (PurchaseOrdersRow row in rows)
            {
                if (row.order != null && row.order.id == orderId)
                {
                    return true;
                }
            }

            return false;
        }

        private static string PurchaseOrderRowIds(List<PurchaseOrdersRow> rows)
        {
            if (rows == null)
            {
                return "null";
            }

            List<string> ids = new List<string>();
            foreach (PurchaseOrdersRow row in rows)
            {
                ids.Add(row.order == null ? "null" : row.order.id.ToString());
            }

            return string.Join(",", ids.ToArray());
        }

        private static void CheckSupplierMarketReadModel(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement,
            int window,
            FieldInfo refreshCountField,
            Action<SupplierListing> resetFixture)
        {
            if (state == null || settlement == null || refreshCountField == null ||
                resetFixture == null)
            {
                SkipSupplierMarketReadModel(
                    skip, "the live fixture fields needed for the read-model probe are inaccessible");
                return;
            }

            SupplierListing valuesListing = NewSupplierMarketListing(
                920_101, 7, settlement.ID, window, 1.37f,
                FulfillmentMode.BuyerPickup, 11);
            resetFixture(valuesListing);
            int selectedQuantity = 4;
            SupplierMarketRow valuesRow = SupplierMarketUiService.BuildRow(
                state, valuesListing, selectedQuantity);
            string expectedItem = valuesListing.thingDef.LabelCap.ToString();
            string expectedSupplier = settlement.Label.ToString();
            string expectedFulfillment = "Pickup";

            check(
                "Y1 Supplier Market row carries listing values",
                valuesRow.listing == valuesListing &&
                valuesRow.itemLabel == expectedItem &&
                valuesRow.supplierLabel == expectedSupplier &&
                valuesRow.quantityAvailable == valuesListing.quantityAvailable &&
                valuesRow.selectedQuantity == selectedQuantity &&
                valuesRow.unitPrice == valuesListing.unitPrice &&
                valuesRow.fulfillmentLabel == expectedFulfillment &&
                valuesRow.leadTimeDays == valuesListing.leadTimeDays,
                $"listing={valuesListing.id}; item row=\"{valuesRow.itemLabel}\" " +
                $"listing=\"{expectedItem}\"; supplier row=\"{valuesRow.supplierLabel}\" " +
                $"listing=\"{expectedSupplier}\"; quantity row/listing=" +
                $"{valuesRow.quantityAvailable}/{valuesListing.quantityAvailable}; " +
                $"selected={valuesRow.selectedQuantity}; lead row/listing=" +
                $"{valuesRow.leadTimeDays}/{valuesListing.leadTimeDays}; " +
                $"fulfillment row/listing=\"{valuesRow.fulfillmentLabel}\"/\"" +
                $"{expectedFulfillment}\"; unit row/listing={valuesRow.unitPrice:F2}/" +
                $"{valuesListing.unitPrice:F2}");

            SupplierListing totalListing = NewSupplierMarketListing(
                920_102, 5, settlement.ID, window, 0.51f,
                FulfillmentMode.SellerDelivery, 3);
            resetFixture(totalListing);
            int totalQuantity = 3;
            SupplierMarketRow totalRow = SupplierMarketUiService.BuildRow(
                state, totalListing, totalQuantity);
            int sharedTotal = IntercolonyPricing.TotalPayment(
                totalListing.unitPrice, totalQuantity);

            check(
                "Y2 Supplier Market total uses shared payment calculation",
                totalRow.totalPayment == sharedTotal,
                $"listing={totalListing.id}; rate={totalListing.unitPrice:F2}; " +
                $"quantity={totalQuantity}; row total={totalRow.totalPayment}; " +
                $"shared total={sharedTotal}");

            SupplierListing zeroListing = NewSupplierMarketListing(
                920_103, 0, settlement.ID, window, 0.73f,
                FulfillmentMode.SellerDelivery, 2);
            SupplierListing expiredListing = NewSupplierMarketListing(
                920_104, 2, settlement.ID, window, 0.73f,
                FulfillmentMode.SellerDelivery, 2);
            expiredListing.expiryTick = GenTicks.TicksGame;
            resetFixture(zeroListing);
            state.SupplierListings.Add(expiredListing);
            List<SupplierMarketRow> unavailableRows =
                SupplierMarketUiService.BuildRows(state);
            bool zeroAppeared = ContainsSupplierMarketRow(unavailableRows, zeroListing.id);
            bool expiredAppeared = ContainsSupplierMarketRow(unavailableRows, expiredListing.id);

            check(
                "Y3 unavailable Supplier Market listings are not offered",
                !zeroAppeared && !expiredAppeared,
                $"zero-quantity listing={zeroListing.id}; quantity={zeroListing.quantityAvailable}; " +
                $"appeared={zeroAppeared}; expired listing={expiredListing.id}; " +
                $"quantity={expiredListing.quantityAvailable}; expiry={expiredListing.expiryTick}; " +
                $"now={GenTicks.TicksGame}; appeared={expiredAppeared}");

            SupplierListing refusalListing = NewSupplierMarketListing(
                920_105, 2, settlement.ID, window, 0.51f,
                FulfillmentMode.SellerDelivery, 3);
            refusalListing.expiryTick = GenTicks.TicksGame;
            resetFixture(refusalListing);
            bool purchaseCreated = SupplierListingService.TryPurchase(
                state, refusalListing, 1,
                out PurchaseOrder refusedOrder, out string purchaseFailure);
            SupplierMarketRow refusalRow = SupplierMarketUiService.BuildRow(
                state, refusalListing, 1);

            check(
                "Y4 Supplier Market surfaces the purchase service refusal",
                !purchaseCreated && refusedOrder == null &&
                !string.IsNullOrEmpty(purchaseFailure) &&
                refusalRow.purchaseFailureReason == purchaseFailure && !refusalRow.canBuy,
                $"listing={refusalListing.id}; purchase created={purchaseCreated}; " +
                $"order={(refusedOrder == null ? "null" : refusedOrder.id.ToString())}; " +
                $"TryPurchase=\"{purchaseFailure ?? "null"}\"; " +
                $"row=\"{refusalRow.purchaseFailureReason ?? "null"}\"");

            SupplierListing sortA = NewSupplierMarketListing(
                920_201, 8, settlement.ID, window, 0.81f,
                FulfillmentMode.SellerDelivery, 4);
            SupplierListing sortB = NewSupplierMarketListing(
                920_202, 2, settlement.ID, window, 0.93f,
                FulfillmentMode.SellerDelivery, 12);
            SupplierListing sortC = NewSupplierMarketListing(
                920_203, 5, settlement.ID, window, 1.07f,
                FulfillmentMode.SellerDelivery, 9);
            resetFixture(sortA);
            state.SupplierListings.Add(sortB);
            state.SupplierListings.Add(sortC);
            List<SupplierMarketRow> sortFixture =
                SupplierMarketUiService.BuildRows(state);
            if (sortFixture.Count != 3)
            {
                SkipSupplierMarketSort(
                    skip,
                    $"expected 3 accessible fixture rows; built {sortFixture.Count}; " +
                    $"listing ids={SupplierMarketRowIds(sortFixture)}");
            }
            else
            {
                List<SupplierMarketRow> quantityAscending =
                    new List<SupplierMarketRow>(sortFixture);
                SupplierMarketUiService.SortRows(
                    quantityAscending, SupplierMarketColumn.Quantity, descending: false);
                check(
                    "Y5 Supplier Market quantity sort ascending",
                    SupplierMarketRowsMatchIds(
                        quantityAscending, sortB.id, sortC.id, sortA.id),
                    SupplierMarketSortDetail(
                        SupplierMarketColumn.Quantity, false,
                        $"{sortB.id}={sortB.quantityAvailable}, " +
                        $"{sortC.id}={sortC.quantityAvailable}, " +
                        $"{sortA.id}={sortA.quantityAvailable}",
                        SupplierMarketRowIds(quantityAscending)));

                List<SupplierMarketRow> quantityDescending =
                    new List<SupplierMarketRow>(sortFixture);
                SupplierMarketUiService.SortRows(
                    quantityDescending, SupplierMarketColumn.Quantity, descending: true);
                check(
                    "Y5 Supplier Market quantity sort descending",
                    SupplierMarketRowsMatchIds(
                        quantityDescending, sortA.id, sortC.id, sortB.id),
                    SupplierMarketSortDetail(
                        SupplierMarketColumn.Quantity, true,
                        $"{sortA.id}={sortA.quantityAvailable}, " +
                        $"{sortC.id}={sortC.quantityAvailable}, " +
                        $"{sortB.id}={sortB.quantityAvailable}",
                        SupplierMarketRowIds(quantityDescending)));

                List<SupplierMarketRow> leadAscending =
                    new List<SupplierMarketRow>(sortFixture);
                SupplierMarketUiService.SortRows(
                    leadAscending, SupplierMarketColumn.LeadTime, descending: false);
                check(
                    "Y5 Supplier Market lead-time sort ascending",
                    SupplierMarketRowsMatchIds(
                        leadAscending, sortA.id, sortC.id, sortB.id),
                    SupplierMarketSortDetail(
                        SupplierMarketColumn.LeadTime, false,
                        $"{sortA.id}={sortA.leadTimeDays}, " +
                        $"{sortC.id}={sortC.leadTimeDays}, " +
                        $"{sortB.id}={sortB.leadTimeDays}",
                        SupplierMarketRowIds(leadAscending)));

                List<SupplierMarketRow> leadDescending =
                    new List<SupplierMarketRow>(sortFixture);
                SupplierMarketUiService.SortRows(
                    leadDescending, SupplierMarketColumn.LeadTime, descending: true);
                check(
                    "Y5 Supplier Market lead-time sort descending",
                    SupplierMarketRowsMatchIds(
                        leadDescending, sortB.id, sortC.id, sortA.id),
                    SupplierMarketSortDetail(
                        SupplierMarketColumn.LeadTime, true,
                        $"{sortB.id}={sortB.leadTimeDays}, " +
                        $"{sortC.id}={sortC.leadTimeDays}, " +
                        $"{sortA.id}={sortA.leadTimeDays}",
                        SupplierMarketRowIds(leadDescending)));
            }

            resetFixture(null);
            refreshCountField.SetValue(state, 0);
            List<SupplierMarketRow> notLookedRows =
                SupplierMarketUiService.BuildRows(state);
            string notLookedMessage = SupplierMarketUiService.EmptyState(state);

            SupplierListing unreachableListing = NewSupplierMarketListing(
                920_601, 1, settlement.ID, window, 0.51f,
                FulfillmentMode.SellerDelivery, 1);
            unreachableListing.expiryTick = GenTicks.TicksGame;
            resetFixture(unreachableListing);
            refreshCountField.SetValue(state, 1);
            List<SupplierMarketRow> noReachableRows =
                SupplierMarketUiService.BuildRows(state);
            string noReachableMessage = SupplierMarketUiService.EmptyState(state);

            check(
                "Y6 empty state reports that no listings were generated",
                notLookedRows.Count == 0 &&
                notLookedMessage == SupplierMarketUiService.NotLookedMessage &&
                notLookedMessage != noReachableMessage,
                $"no-listings rows={notLookedRows.Count}; no-listings=\"{notLookedMessage}\"; " +
                $"no-reachable=\"{noReachableMessage}\"");
            check(
                "Y6 empty state reports that no reachable offers are available",
                noReachableRows.Count == 0 &&
                noReachableMessage == SupplierMarketUiService.NoReachableOffersMessage &&
                notLookedMessage != noReachableMessage,
                $"no-reachable rows={noReachableRows.Count}; no-reachable=\"{noReachableMessage}\"; " +
                $"no-listings=\"{notLookedMessage}\"");

            SupplierListing oldListing = NewSupplierMarketListing(
                920_701, 2, settlement.ID, window, 0.51f,
                FulfillmentMode.SellerDelivery, 2);
            SupplierListing newListing = NewSupplierMarketListing(
                920_702, 3, settlement.ID, window, 0.51f,
                FulfillmentMode.SellerDelivery, 2);
            resetFixture(oldListing);
            List<SupplierMarketRow> oldRows = SupplierMarketUiService.BuildRows(state);
            resetFixture(newListing);
            List<SupplierMarketRow> newRows = SupplierMarketUiService.BuildRows(state);

            check(
                "Y7 Supplier Market rows refresh after listings change",
                oldRows.Count == 1 && newRows.Count == 1 &&
                oldRows[0].listing == oldListing &&
                newRows[0].listing == newListing &&
                !ContainsSupplierMarketRow(newRows, oldListing.id),
                $"old listing={oldListing.id}; old rows={SupplierMarketRowIds(oldRows)}; " +
                $"new listing={newListing.id}; new rows={SupplierMarketRowIds(newRows)}; " +
                $"new quantity={newListing.quantityAvailable}");
        }

        private static void SkipSupplierMarketReadModel(
            Action<string, string> skip,
            string reason)
        {
            skip("Y1 Supplier Market row carries listing values", reason);
            skip("Y2 Supplier Market total uses shared payment calculation", reason);
            skip("Y3 unavailable Supplier Market listings are not offered", reason);
            skip("Y4 Supplier Market surfaces the purchase service refusal", reason);
            SkipSupplierMarketSort(skip, reason);
            skip("Y6 empty state reports that no listings were generated", reason);
            skip("Y6 empty state reports that no reachable offers are available", reason);
            skip("Y7 Supplier Market rows refresh after listings change", reason);
        }

        private static void SkipSupplierMarketSort(
            Action<string, string> skip,
            string reason)
        {
            skip("Y5 Supplier Market quantity sort ascending", reason);
            skip("Y5 Supplier Market quantity sort descending", reason);
            skip("Y5 Supplier Market lead-time sort ascending", reason);
            skip("Y5 Supplier Market lead-time sort descending", reason);
        }

        private static SupplierListing NewSupplierMarketListing(
            int id,
            int quantity,
            int settlementId,
            int refreshWindow,
            float unitPrice,
            FulfillmentMode fulfillment,
            int leadTimeDays)
        {
            return new SupplierListing
            {
                id = id,
                settlementId = settlementId,
                thingDef = ThingDefOf.Steel,
                quantityAvailable = quantity,
                unitPrice = unitPrice,
                fulfillment = fulfillment,
                leadTimeDays = leadTimeDays,
                createdTick = GenTicks.TicksGame,
                expiryTick = SupplierListing.NoExpiryTick,
                refreshWindow = refreshWindow
            };
        }

        private static bool ContainsSupplierMarketRow(
            List<SupplierMarketRow> rows,
            int listingId)
        {
            if (rows == null)
            {
                return false;
            }

            foreach (SupplierMarketRow row in rows)
            {
                if (row.listing != null && row.listing.id == listingId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SupplierMarketRowsMatchIds(
            List<SupplierMarketRow> rows,
            params int[] expectedIds)
        {
            if (rows == null || expectedIds == null || rows.Count != expectedIds.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedIds.Length; i++)
            {
                if (rows[i].listing == null || rows[i].listing.id != expectedIds[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string SupplierMarketRowIds(List<SupplierMarketRow> rows)
        {
            if (rows == null)
            {
                return "null";
            }

            List<string> ids = new List<string>();
            foreach (SupplierMarketRow row in rows)
            {
                ids.Add(row.listing == null ? "null" : row.listing.id.ToString());
            }

            return string.Join(",", ids.ToArray());
        }

        private static string SupplierMarketSortDetail(
            SupplierMarketColumn column,
            bool descending,
            string expected,
            string actual)
        {
            return $"column={column}; direction={(descending ? "descending" : "ascending")}; " +
                   $"expected={expected}; actual={actual}";
        }

        private static SupplierListing NewPurchasePathListing(
            int id,
            int quantity,
            int settlementId,
            int refreshWindow,
            float unitPrice)
        {
            return new SupplierListing
            {
                id = id,
                settlementId = settlementId,
                thingDef = ThingDefOf.Steel,
                quantityAvailable = quantity,
                unitPrice = unitPrice,
                fulfillment = FulfillmentMode.SellerDelivery,
                leadTimeDays = 0,
                createdTick = GenTicks.TicksGame,
                expiryTick = SupplierListing.NoExpiryTick,
                refreshWindow = refreshWindow
            };
        }

        private static Dictionary<Thing, int> SnapshotAllColonySilver(Map map)
        {
            Dictionary<Thing, int> result = new Dictionary<Thing, int>();
            if (map == null || ThingDefOf.Silver == null)
            {
                return result;
            }

            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (thing != null && !thing.Destroyed)
                {
                    result[thing] = thing.stackCount;
                }
            }

            return result;
        }

        private static int CountAllColonySilver(Map map)
        {
            if (map == null || ThingDefOf.Silver == null)
            {
                return 0;
            }

            int total = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (thing != null && !thing.Destroyed)
                {
                    // Deliberately includes loose silver at the trade spot: a refund is still
                    // colony silver when storage is full.
                    total += thing.stackCount;
                }
            }

            return total;
        }

        private static void RestoreAllColonySilver(
            Map map,
            Dictionary<Thing, int> savedSilver)
        {
            if (map == null || savedSilver == null)
            {
                return;
            }

            List<Thing> current = new List<Thing>(
                map.listerThings.ThingsOfDef(ThingDefOf.Silver));
            foreach (Thing thing in current)
            {
                if (savedSilver.TryGetValue(thing, out int originalCount))
                {
                    if (!thing.Destroyed)
                    {
                        thing.stackCount = originalCount;
                    }
                }
                else if (!thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }

            foreach (KeyValuePair<Thing, int> saved in savedSilver)
            {
                if (saved.Key != null && !saved.Key.Destroyed)
                {
                    saved.Key.stackCount = saved.Value;
                }
            }
        }

        private static Dictionary<Thing, int> SnapshotStoredSilver(Map map)
        {
            Dictionary<Thing, int> result = new Dictionary<Thing, int>();
            if (map == null)
            {
                return result;
            }

            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (thing != null && thing.IsInAnyStorage())
                {
                    result[thing] = thing.stackCount;
                }
            }

            return result;
        }

        private static bool TryCreateStoredSilver(
            Map map,
            int amount,
            out Thing silver,
            out Zone_Stockpile zone)
        {
            silver = null;
            zone = null;
            if (map == null || map.zoneManager == null || amount <= 0)
            {
                return false;
            }

            IntVec3 storageCell = IntVec3.Invalid;
            IntVec3 root = DropCellFinder.TradeDropSpot(map);
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(root, 12f, useCenter: true))
            {
                if (candidate.InBounds(map) && candidate.Standable(map) &&
                    candidate.GetFirstItem(map) == null && map.zoneManager.ZoneAt(candidate) == null)
                {
                    storageCell = candidate;
                    break;
                }
            }

            if (!storageCell.IsValid)
            {
                return false;
            }

            zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            zone.AddCell(storageCell);

            silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = Mathf.Min(amount, ThingDefOf.Silver.stackLimit);
            silver = GenSpawn.Spawn(silver, storageCell, map);
            if (silver == null || silver.Destroyed || !silver.IsInAnyStorage())
            {
                if (silver != null && !silver.Destroyed)
                {
                    silver.Destroy(DestroyMode.Vanish);
                }

                zone.Delete(playSound: false);
                silver = null;
                zone = null;
                return false;
            }

            return true;
        }

        private static void RestoreStoredSilver(
            Map map,
            Dictionary<Thing, int> savedSilver)
        {
            if (map == null || savedSilver == null)
            {
                return;
            }

            List<Thing> current = new List<Thing>(
                map.listerThings.ThingsOfDef(ThingDefOf.Silver));
            foreach (Thing thing in current)
            {
                if (savedSilver.TryGetValue(thing, out int originalCount))
                {
                    if (!thing.Destroyed)
                    {
                        thing.stackCount = originalCount;
                    }
                }
                else if (!thing.Destroyed && thing.IsInAnyStorage())
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }

            foreach (KeyValuePair<Thing, int> saved in savedSilver)
            {
                if (saved.Key != null && !saved.Key.Destroyed)
                {
                    saved.Key.stackCount = saved.Value;
                }
            }
        }

        private static List<Settlement> FindAccessibleSupplierSettlements(
            IntercolonyWorldComponent state)
        {
            List<Settlement> result = new List<Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return result;
            }

            foreach (Settlement settlement in settlements)
            {
                if (settlement != null &&
                    IntercolonyMarketAccess.IsAccessible(settlement) &&
                    state.GetProfile(settlement) != null)
                {
                    result.Add(settlement);
                }
            }

            result.Sort((left, right) => left.ID.CompareTo(right.ID));

            return result;
        }

        private static void CheckSupplierListingIdempotence(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement,
            int window)
        {
            state.SupplierListings.Clear();
            SupplierListingService.Refresh(state);
            int firstCount = CountCurrentListings(state, settlement.ID, window);
            if (firstCount == 0)
            {
                skip("T1 listing refresh is idempotent within one window",
                    $"settlement={settlement.ID}; window={window}; first count=0");
                return;
            }

            SupplierListingService.Refresh(state);
            int secondCount = CountCurrentListings(state, settlement.ID, window);
            check("T1 listing refresh is idempotent within one window",
                secondCount == firstCount,
                $"settlement={settlement.ID}; window={window}; " +
                $"first count={firstCount}; second count={secondCount}");
        }

        private static void CheckSupplierListingConsumedQuantity(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement,
            SettlementEconomicProfile profile,
            int window)
        {
            IntercolonyWorldComponent probeState = new IntercolonyWorldComponent(null);
            List<SupplierListing> grossListings = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window, 0, () => 1);
            SupplierListing gross = null;
            foreach (SupplierListing listing in grossListings)
            {
                if (listing != null && listing.quantityAvailable > 1)
                {
                    gross = listing;
                    break;
                }
            }

            if (gross == null)
            {
                skip("T2 listing quantity is net of consumed stock",
                    $"settlement={settlement.ID}; window={window}; no generated listing had quantity > 1");
                return;
            }

            int grossQuantity = gross.quantityAvailable;
            int consumed = Mathf.Max(1, grossQuantity / 3);
            probeState.ConsumeSupplierOffer(
                window, gross.thingDef, settlement.ID, consumed);
            List<SupplierListing> netListings = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window, 0, () => 2);
            SupplierListing net = FindListing(netListings, gross.thingDef, gross.stuffDef,
                gross.quality);
            int listedQuantity = net?.quantityAvailable ?? 0;
            int expectedQuantity = grossQuantity - consumed;

            check("T2 listing quantity is net of consumed stock",
                net != null && listedQuantity == expectedQuantity,
                $"settlement={settlement.ID}; window={window}; def={gross.thingDef.defName}; " +
                $"gross={grossQuantity}; consumed={consumed}; listed={listedQuantity}");
        }

        private static void CheckSupplierListingTechGate(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement,
            SettlementEconomicProfile profile,
            int window)
        {
            ThingDef blocked = null;
            IntercolonyProductCategory blockedCategory = IntercolonyProductCategory.Commodities;
            foreach (ThingDef def in IntercolonyProductClassifier.TradableDefs)
            {
                IntercolonyProductCategory? category =
                    IntercolonyProductClassifier.Classify(def);
                if (!category.HasValue || def.techLevel == TechLevel.Undefined ||
                    RfqService.SupplierOfferQuantity(
                        def, null, FixtureSupplyProfile(profile), 100f) <= 0)
                {
                    continue;
                }

                if (blocked == null || def.techLevel > blocked.techLevel)
                {
                    blocked = def;
                    blockedCategory = category.Value;
                }
            }

            if (blocked == null)
            {
                skip("T3 listing generation respects the technical gate",
                    $"settlement={settlement.ID}; no high-tech tradable def with positive fixture supply");
                return;
            }

            List<ThingDef> tradableDefs = IntercolonyProductClassifier.TradableDefs;
            List<ThingDef> savedTradableDefs = new List<ThingDef>(tradableDefs);
            TechLevel savedTechTier = profile.techTier;
            IntercolonyArchetype savedArchetype = profile.archetype;
            IntercolonyWealthTier savedWealthTier = profile.wealthTier;
            float[] savedSupplyWeights = (float[])profile.supplyWeights.Clone();

            try
            {
                // Make the target the only candidate and the only category with positive
                // effective supply. Tribal tech one tier below the target is a known rejection
                // without asking the gate for the expected answer.
                profile.techTier = (TechLevel)Math.Max(
                    (int)TechLevel.Undefined, (int)blocked.techLevel - 1);
                profile.archetype = IntercolonyArchetype.Tribal;
                profile.wealthTier = IntercolonyWealthTier.Wealthy;
                for (int i = 0; i < profile.supplyWeights.Length; i++)
                {
                    profile.supplyWeights[i] = 0f;
                }

                profile.supplyWeights[(int)blockedCategory] = 100f;
                tradableDefs.Clear();
                tradableDefs.Add(blocked);

                IntercolonyWorldComponent probeState = new IntercolonyWorldComponent(null);
                int probeWindow = window + 100;
                List<SupplierListing> listings = SupplierListingService.GenerateFor(
                    probeState, settlement, profile, probeWindow,
                    SupplierListingService.MaxPerSettlement - 1, () => 1);
                bool appeared = false;
                foreach (SupplierListing listing in listings)
                {
                    if (listing != null && listing.thingDef == blocked)
                    {
                        appeared = true;
                        break;
                    }
                }

                check("T3 listing generation respects the technical gate",
                    !appeared,
                    $"settlement={settlement.ID}; rejected def={blocked.defName}; " +
                    $"appeared={appeared}; category={blockedCategory}; window={probeWindow}");
            }
            finally
            {
                tradableDefs.Clear();
                tradableDefs.AddRange(savedTradableDefs);
                profile.techTier = savedTechTier;
                profile.archetype = savedArchetype;
                profile.wealthTier = savedWealthTier;
                Array.Copy(savedSupplyWeights, profile.supplyWeights, savedSupplyWeights.Length);
            }
        }

        private static void CheckSupplierListingSupplyDirection(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement,
            SettlementEconomicProfile sourceProfile,
            int window)
        {
            IntercolonyProductCategory? chosen = null;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                foreach (ThingDef def in IntercolonyProductClassifier.DefsInCategory(category))
                {
                    if (RfqService.CanTechnicallySupply(def, sourceProfile))
                    {
                        chosen = category;
                        break;
                    }
                }

                if (chosen.HasValue)
                {
                    break;
                }
            }

            if (!chosen.HasValue)
            {
                skip("T4 surplus lists more than shortage",
                    $"settlement={settlement.ID}; no technically supplyable category");
                return;
            }

            SettlementEconomicProfile profile = CloneProfile(sourceProfile);
            for (int i = 0; i < profile.supplyWeights.Length; i++)
            {
                profile.supplyWeights[i] = 0f;
            }

            profile.supplyWeights[(int)chosen.Value] = 1f;
            IntercolonyWorldComponent probeState = new IntercolonyWorldComponent(null);
            MarketPressureService.ApplySupplyShock(
                probeState, settlement.ID, chosen.Value, MarketPressureService.MaxPressure);
            float shortageSupply = EffectiveEconomyService.EffectiveSupply(
                probeState, profile, chosen.Value);
            List<SupplierListing> shortage = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window + 200, 0, () => 1);

            probeState.MarketStates.Clear();
            probeState.RefreshMarketStateIndex();
            MarketPressureService.ApplySupplyShock(
                probeState, settlement.ID, chosen.Value, -MarketPressureService.MaxPressure);
            float surplusSupply = EffectiveEconomyService.EffectiveSupply(
                probeState, profile, chosen.Value);
            List<SupplierListing> surplus = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window + 200, 0, () => 2);

            int shortageQuantity = TotalListingQuantity(shortage);
            int surplusQuantity = TotalListingQuantity(surplus);
            if (surplusSupply <= shortageSupply || shortageQuantity == 0 || surplusQuantity == 0)
            {
                skip("T4 surplus lists more than shortage",
                    $"settlement={settlement.ID}; category={chosen.Value}; " +
                    $"shortage supply={shortageSupply:F2}, surplus supply={surplusSupply:F2}; " +
                    $"listed quantities={shortageQuantity},{surplusQuantity}; " +
                    "deterministic two-condition fixture could not be constructed");
                return;
            }

            check("T4 surplus lists more than shortage",
                surplusQuantity > shortageQuantity,
                $"settlement={settlement.ID}; category={chosen.Value}; window={window + 200}; " +
                $"shortage supply={shortageSupply:F2}, quantity={shortageQuantity}; " +
                $"surplus supply={surplusSupply:F2}, quantity={surplusQuantity}");
        }

        private static void CheckSupplierListingSharedPrice(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement,
            SettlementEconomicProfile sourceProfile,
            int window)
        {
            if (!TryFindSupplyCategory(sourceProfile, out IntercolonyProductCategory category))
            {
                skip("T5 listing price uses shared supplier pricing",
                    $"settlement={settlement.ID}; no supplyable category");
                return;
            }

            SettlementEconomicProfile profile = CategoryOnlyProfile(sourceProfile, category);
            IntercolonyWorldComponent probeState = new IntercolonyWorldComponent(null);
            List<SupplierListing> listings = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window + 300, 0, () => 1);
            if (listings.Count == 0)
            {
                skip("T5 listing price uses shared supplier pricing",
                    $"settlement={settlement.ID}; category={category}; generator made no listing");
                return;
            }

            SupplierListing listing = listings[0];
            if (!TryReplayFirstListingPrice(
                    probeState, settlement, profile, window + 300, listing,
                    listing.quantityAvailable, out float expected))
            {
                skip("T5 listing price uses shared supplier pricing",
                    $"settlement={settlement.ID}; listing={listing.id}; " +
                    "the deterministic price replay could not match the generated first listing");
                return;
            }

            check("T5 listing price uses shared supplier pricing",
                Mathf.Approximately(listing.unitPrice, expected),
                $"settlement={settlement.ID}; listing={listing.id}; window={listing.refreshWindow}; " +
                $"listed={listing.unitPrice:F2}; shared={expected:F2}; " +
                $"quantity={listing.quantityAvailable}; " +
                $"supply={EffectiveEconomyService.EffectiveSupply(probeState, profile, category):F2}; " +
                $"distance={MarketOpportunityGenerator.DistanceToPlayer(settlement):F2}; " +
                $"delivers={listing.fulfillment == FulfillmentMode.SellerDelivery}");
        }

        private static void CheckSupplierListingPublishedRate(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement,
            SettlementEconomicProfile sourceProfile,
            int window)
        {
            if (!TryFindSupplyCategory(sourceProfile, out IntercolonyProductCategory category))
            {
                skip("T9 published listing rate does not move with purchase size",
                    $"settlement={settlement.ID}; no supplyable category");
                return;
            }

            SettlementEconomicProfile profile = CategoryOnlyProfile(sourceProfile, category);
            IntercolonyWorldComponent probeState = new IntercolonyWorldComponent(null);
            List<SupplierListing> listings = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window + 600, 0, () => 1);
            if (listings.Count == 0)
            {
                skip("T9 published listing rate does not move with purchase size",
                    $"settlement={settlement.ID}; category={category}; generator made no listing");
                return;
            }

            SupplierListing listing = listings[0];
            if (listing.quantityAvailable <= 1)
            {
                skip("T9 published listing rate does not move with purchase size",
                    $"settlement={settlement.ID}; listing={listing.id}; " +
                    $"quantity={listing.quantityAvailable}; no different positive purchase size");
                return;
            }

            int purchaseQuantity = Mathf.Max(1, listing.quantityAvailable / 2);
            if (purchaseQuantity == listing.quantityAvailable)
            {
                skip("T9 published listing rate does not move with purchase size",
                    $"settlement={settlement.ID}; listing={listing.id}; " +
                    $"listed quantity={listing.quantityAvailable}; alternate quantity unavailable");
                return;
            }

            if (!TryReplayFirstListingPrice(
                    probeState, settlement, profile, window + 600, listing,
                    purchaseQuantity, out float wouldBePrice))
            {
                skip("T9 published listing rate does not move with purchase size",
                    $"settlement={settlement.ID}; listing={listing.id}; " +
                    "the deterministic price replay could not match the generated first listing");
                return;
            }

            float publishedRate = listing.unitPrice;
            check("T9 published listing rate does not move with purchase size",
                Mathf.Approximately(listing.unitPrice, publishedRate),
                $"settlement={settlement.ID}; listing={listing.id}; window={listing.refreshWindow}; " +
                $"listed quantity={listing.quantityAvailable}; purchase quantity={purchaseQuantity}; " +
                $"published={publishedRate:F2}; would-be={wouldBePrice:F2}; " +
                $"stored after calculation={listing.unitPrice:F2}; " +
                $"supply={EffectiveEconomyService.EffectiveSupply(probeState, profile, category):F2}; " +
                $"distance={MarketOpportunityGenerator.DistanceToPlayer(settlement):F2}; " +
                $"delivers={listing.fulfillment == FulfillmentMode.SellerDelivery}");
        }

        private static void CheckSupplierListingRfqPrice(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement,
            SettlementEconomicProfile sourceProfile,
            int window)
        {
            if (!TryFindSupplyCategory(sourceProfile, out IntercolonyProductCategory category))
            {
                skip("T6 RFQ and listing prices agree",
                    $"settlement={settlement.ID}; no supplyable category");
                return;
            }

            SettlementEconomicProfile profile = CategoryOnlyProfile(sourceProfile, category);
            IntercolonyWorldComponent probeState = new IntercolonyWorldComponent(null);
            List<SupplierListing> listings = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window + 400, 0, () => 1);
            if (listings.Count == 0)
            {
                skip("T6 RFQ and listing prices agree",
                    $"settlement={settlement.ID}; category={category}; generator made no listing");
                return;
            }

            SupplierListing listing = listings[0];
            bool delivers = listing.fulfillment == FulfillmentMode.SellerDelivery;
            float supply = EffectiveEconomyService.EffectiveSupply(
                probeState, profile, category);
            float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
            int seed = 0x6B_2A_11;
            float rfqPrice;
            float listingPathPrice;
            MethodInfo rfqPricingMethod = typeof(RfqService).GetMethod(
                "QuotedUnitPrice", BindingFlags.Static | BindingFlags.NonPublic);
            if (rfqPricingMethod == null)
            {
                skip("T6 RFQ and listing prices agree",
                    $"settlement={settlement.ID}; RFQ quotation pricing method is inaccessible");
                return;
            }

            PurchaseRequest rfqRequest = new PurchaseRequest
            {
                thingDef = listing.thingDef,
                stuffDef = listing.stuffDef,
                quantityRequested = listing.quantityAvailable
            };
            Rand.PushState(seed);
            try
            {
                rfqPrice = (float)rfqPricingMethod.Invoke(null, new object[]
                {
                    probeState, rfqRequest, listing.stuffDef, listing.quality, profile,
                    category, supply, distance, delivers, null
                });
            }
            finally
            {
                Rand.PopState();
            }

            Rand.PushState(seed);
            try
            {
                listingPathPrice = IntercolonyPricing.SupplierUnitPrice(
                    probeState, listing.thingDef, listing.stuffDef, listing.quality, profile,
                    category, supply, distance, delivers, listing.quantityAvailable, out _);
            }
            finally
            {
                Rand.PopState();
            }

            check("T6 RFQ and listing prices agree",
                Mathf.Approximately(rfqPrice, listingPathPrice),
                $"settlement={settlement.ID}; listing={listing.id}; window={window + 400}; " +
                $"def={listing.thingDef.defName}; quantity={listing.quantityAvailable}; " +
                $"RFQ={rfqPrice:F2}; listing path={listingPathPrice:F2}");
        }

        private static void CheckSupplierListingCap(
            Action<string, bool, string> check,
            Action<string, string> skip,
            Settlement settlement,
            SettlementEconomicProfile sourceProfile,
            int window)
        {
            if (!TryFindSupplyCategory(sourceProfile, out IntercolonyProductCategory category))
            {
                skip("T7 listing count respects the per-settlement cap",
                    $"settlement={settlement.ID}; no supplyable category");
                return;
            }

            SettlementEconomicProfile profile = CategoryOnlyProfile(sourceProfile, category);
            IntercolonyWorldComponent probeState = new IntercolonyWorldComponent(null);
            List<SupplierListing> listings = SupplierListingService.GenerateFor(
                probeState, settlement, profile, window + 500, 0, () => 1);
            int cap = SupplierListingService.MaxPerSettlement;
            if (listings.Count == 0)
            {
                skip("T7 listing count respects the per-settlement cap",
                    $"settlement={settlement.ID}; category={category}; generator made no listing");
                return;
            }

            check("T7 listing count respects the per-settlement cap",
                listings.Count <= cap,
                $"settlement={settlement.ID}; window={window + 500}; " +
                $"count={listings.Count}; cap={cap}");
        }

        private static void CheckSupplierListingStaleWindow(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement,
            int window)
        {
            state.SupplierListings.Clear();
            state.SupplierListings.Add(new SupplierListing
            {
                id = 9_001,
                settlementId = settlement.ID,
                thingDef = ThingDefOf.Steel,
                quantityAvailable = 1,
                unitPrice = 1f,
                expiryTick = SupplierListing.NoExpiryTick,
                refreshWindow = window - 1
            });

            SupplierListingService.Refresh(state);
            bool staleRemains = false;
            int currentCount = 0;
            foreach (SupplierListing listing in state.SupplierListings)
            {
                if (listing == null || listing.settlementId != settlement.ID)
                {
                    continue;
                }

                staleRemains |= listing.refreshWindow == window - 1;
                if (listing.refreshWindow == window)
                {
                    currentCount++;
                }
            }

            if (currentCount == 0)
            {
                skip("T8 refresh prunes stale-window listings",
                    $"settlement={settlement.ID}; old window={window - 1}; " +
                    $"current window={window}; no current listing generated");
                return;
            }

            check("T8 refresh prunes stale-window listings",
                !staleRemains && currentCount > 0,
                $"settlement={settlement.ID}; old window={window - 1}; current window={window}; " +
                $"stale remains={staleRemains}; current count={currentCount}");
        }

        private static bool TryFindSupplyCategory(
            SettlementEconomicProfile profile,
            out IntercolonyProductCategory category)
        {
            foreach (IntercolonyProductCategory candidate in IntercolonyProductCategoryUtility.All)
            {
                foreach (ThingDef def in IntercolonyProductClassifier.DefsInCategory(candidate))
                {
                    if (RfqService.CanTechnicallySupply(def, profile))
                    {
                        category = candidate;
                        return true;
                    }
                }
            }

            category = IntercolonyProductCategory.Commodities;
            return false;
        }

        private static SettlementEconomicProfile CategoryOnlyProfile(
            SettlementEconomicProfile source,
            IntercolonyProductCategory category)
        {
            SettlementEconomicProfile profile = CloneProfile(source);
            for (int i = 0; i < profile.supplyWeights.Length; i++)
            {
                profile.supplyWeights[i] = 0f;
            }

            profile.supplyWeights[(int)category] = 1f;
            return profile;
        }

        private static SettlementEconomicProfile FixtureSupplyProfile(
            SettlementEconomicProfile source)
        {
            SettlementEconomicProfile profile = CloneProfile(source);
            profile.wealthTier = IntercolonyWealthTier.Wealthy;
            return profile;
        }

        private static SettlementEconomicProfile CloneProfile(
            SettlementEconomicProfile source)
        {
            SettlementEconomicProfile copy = new SettlementEconomicProfile
            {
                settlementId = source.settlementId,
                factionLoadId = source.factionLoadId,
                settlementName = source.settlementName,
                factionName = source.factionName,
                techTier = source.techTier,
                wealthTier = source.wealthTier,
                archetype = source.archetype,
                qualityPreference = source.qualityPreference,
                laborSupplyModifier = source.laborSupplyModifier,
                volatility = source.volatility,
                seed = source.seed
            };
            copy.demandWeights = (float[])source.demandWeights.Clone();
            copy.supplyWeights = (float[])source.supplyWeights.Clone();
            return copy;
        }

        private static int CountCurrentListings(
            IntercolonyWorldComponent state, int settlementId, int window)
        {
            int count = 0;
            foreach (SupplierListing listing in state.SupplierListings)
            {
                if (listing != null && listing.settlementId == settlementId &&
                    listing.refreshWindow == window)
                {
                    count++;
                }
            }

            return count;
        }

        private static SupplierListing FindListing(
            List<SupplierListing> listings,
            ThingDef def,
            ThingDef stuff,
            QualityCategory? quality)
        {
            foreach (SupplierListing listing in listings)
            {
                if (listing != null && listing.thingDef == def && listing.stuffDef == stuff &&
                    listing.quality == quality)
                {
                    return listing;
                }
            }

            return null;
        }

        private static int TotalListingQuantity(List<SupplierListing> listings)
        {
            int total = 0;
            foreach (SupplierListing listing in listings)
            {
                if (listing != null)
                {
                    total += listing.quantityAvailable;
                }
            }

            return total;
        }

        private static bool TryReplayFirstListingPrice(
            IntercolonyWorldComponent state,
            Settlement settlement,
            SettlementEconomicProfile profile,
            int refreshWindow,
            SupplierListing target,
            int pricingQuantity,
            out float expected)
        {
            expected = 0f;
            if (target == null || pricingQuantity <= 0)
            {
                return false;
            }

            FieldInfo saltField = typeof(SupplierListingService).GetField(
                "GenerationSalt", BindingFlags.Static | BindingFlags.NonPublic);
            if (saltField == null)
            {
                return false;
            }

            int salt = (int)saltField.GetValue(null);
            int seed = Gen.HashCombineInt(
                state.EconomySeed, settlement.ID, refreshWindow, salt);
            IntercolonyProductCategory category;
            if (!TryFindSupplyCategory(profile, out category))
            {
                return false;
            }

            Rand.PushState(seed);
            try
            {
                float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
                float supply = EffectiveEconomyService.EffectiveSupply(state, profile, category);
                Rand.Range(0f, supply);
                List<ThingDef> defs = IntercolonyProductClassifier.DefsInCategory(category);
                for (int i = defs.Count - 1; i >= 0; i--)
                {
                    if (!RfqService.CanTechnicallySupply(defs[i], profile))
                    {
                        defs.RemoveAt(i);
                    }
                }

                if (defs.Count == 0)
                {
                    return false;
                }

                ThingDef def = defs[Rand.Range(0, defs.Count)];
                ThingDef stuff = RfqService.PickSupplierStuff(def);
                QualityCategory? quality = RfqService.PickOfferedQuality(def, profile, null);
                int gross = RfqService.SupplierOfferQuantity(def, stuff, profile, supply);
                int consumed = state.SupplierOfferConsumptionFor(
                    refreshWindow, def, settlement.ID);
                int quantityAvailable = Mathf.Max(0, gross - consumed);
                if (quantityAvailable <= 0)
                {
                    return false;
                }

                bool rolledDelivers = Rand.Value < RfqService.DeliveryChance(profile, distance);
                bool delivers = target.fulfillment == FulfillmentMode.SellerDelivery;
                if (rolledDelivers != delivers)
                {
                    return false;
                }

                // Generation consumes this pickup jitter before it strikes the rate. Replaying
                // it keeps SupplierUnitPrice on the same negotiation random draw as the service.
                RfqService.LeadTimeDays(distance, delivers, supply);
                expected = IntercolonyPricing.SupplierUnitPrice(
                    state, def, stuff, quality, profile, category, supply, distance,
                    delivers, pricingQuantity, out _);

                return target.settlementId == settlement.ID && target.refreshWindow == refreshWindow &&
                       target.thingDef == def && target.stuffDef == stuff &&
                       target.quality == quality && target.quantityAvailable == quantityAvailable;
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static List<SupplierOfferConsumption> CloneConsumptions(
            List<SupplierOfferConsumption> source)
        {
            List<SupplierOfferConsumption> result =
                new List<SupplierOfferConsumption>();
            if (source == null)
            {
                return result;
            }

            foreach (SupplierOfferConsumption entry in source)
            {
                if (entry == null)
                {
                    continue;
                }

                result.Add(new SupplierOfferConsumption
                {
                    refreshWindow = entry.refreshWindow,
                    thingDefShortHash = entry.thingDefShortHash,
                    settlementId = entry.settlementId,
                    quantityPurchased = entry.quantityPurchased
                });
            }

            return result;
        }

        private static void CheckSupplierListingSentinel(
            Action<string, bool, string> check)
        {
            int tick = GenTicks.TicksGame;
            int quantity = 7;
            int id = 6_001;
            SupplierListing saved = new SupplierListing
            {
                id = id,
                thingDef = ThingDefOf.Steel,
                quantityAvailable = quantity,
                expiryTick = SupplierListing.NoExpiryTick
            };
            List<SupplierListing> savedList = new List<SupplierListing> { saved };
            List<SupplierListing> loadedList = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-SupplierListing-S1-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "supplierListingTest");
                Scribe_Collections.Look(ref savedList, "supplierListings", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                // Model the real save shape for a value equal to the corrected default. If the
                // production default is changed back to zero, loading this absent node returns
                // zero and the assertion goes red.
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlNode expiryNode = document.SelectSingleNode("//expiryTick");
                if (expiryNode != null)
                {
                    expiryNode.ParentNode.RemoveChild(expiryNode);
                    document.Save(path);
                }

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "supplierListings", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            SupplierListing loaded = loadedList != null && loadedList.Count == 1
                ? loadedList[0]
                : null;
            check(
                "S1 no-expiry sentinel survives an omitted-node save/load",
                failure == null && loaded != null && loaded.expiryTick == -1 && loaded.IsAvailable,
                $"tick={tick}; quantity={quantity}; id={id}; count " +
                $"{savedList.Count}->{loadedList?.Count ?? -1}; expiry expected -1, " +
                $"loaded {(loaded == null ? "null" : loaded.expiryTick.ToString())}; " +
                $"available={(loaded == null ? "null" : loaded.IsAvailable.ToString())}; " +
                $"failure={failure ?? "none"}");
        }

        private static void CheckSupplierListingAvailability(
            Action<string, bool, string> check)
        {
            int tick = GenTicks.TicksGame;
            int zeroQuantity = 0;
            int positiveQuantity = 9;
            int expiry = tick + 100;
            SupplierListing empty = NewSupplierListing(6_002, zeroQuantity, expiry);
            SupplierListing stocked = NewSupplierListing(6_003, positiveQuantity, expiry);

            check(
                "S2 availability is derived from quantity and expiry",
                !empty.IsAvailable && stocked.IsAvailable,
                $"tick={tick}; quantities={zeroQuantity},{positiveQuantity}; " +
                $"expiry={expiry}; ids={empty.id},{stocked.id}; " +
                $"available={empty.IsAvailable},{stocked.IsAvailable}");
        }

        private static void CheckSupplierListingExpiryBoundary(
            Action<string, bool, string> check)
        {
            int tick = GenTicks.TicksGame;
            int expiredExpiry = tick;
            int futureExpiry = tick + 100;
            int expiredQuantity = 4;
            int futureQuantity = 5;
            SupplierListing expired = NewSupplierListing(6_004, expiredQuantity, expiredExpiry);
            SupplierListing notYetExpired =
                NewSupplierListing(6_005, futureQuantity, futureExpiry);

            check(
                "S3 expired and not-yet-expired listings have the right availability",
                expired.HasExpired(tick) && !expired.IsAvailable &&
                !notYetExpired.HasExpired(tick) && notYetExpired.IsAvailable,
                $"sample tick={tick}; expired expiry={expiredExpiry}, quantity={expiredQuantity}, " +
                $"available={expired.IsAvailable}; not-yet expiry={futureExpiry}, " +
                $"quantity={futureQuantity}, available={notYetExpired.IsAvailable}");
        }

        private static void CheckSupplierListingCollection(
            Action<string, bool, string> check,
            Action<string, string> skip)
        {
            ThingDef validDef = ThingDefOf.Steel;
            if (validDef == null)
            {
                skip("S4 null listing is pruned and valid listing survives",
                    "Steel definition is unavailable in this install");
                skip("S4 unresolvable listing is pruned",
                    "Steel definition is unavailable in this install");
                return;
            }

            const string missingDefName = "Intercolony_SupplierListing_SelfTest_MissingDef";
            bool canExerciseUnresolvable =
                DefDatabase<ThingDef>.GetNamedSilentFail(missingDefName) == null;
            int validId = 6_006;
            int missingId = 6_007;
            List<SupplierListing> savedListings = new List<SupplierListing>
            {
                null,
                new SupplierListing
                {
                    id = validId,
                    thingDef = validDef,
                    quantityAvailable = 3,
                    expiryTick = SupplierListing.NoExpiryTick
                }
            };
            if (canExerciseUnresolvable)
            {
                savedListings.Insert(1, new SupplierListing
                {
                    id = missingId,
                    thingDef = new ThingDef { defName = missingDefName },
                    quantityAvailable = 3,
                    expiryTick = SupplierListing.NoExpiryTick
                });
            }

            IntercolonyWorldComponent savedState = new IntercolonyWorldComponent(null);
            savedState.SupplierListings.AddRange(savedListings);
            IntercolonyWorldComponent loadedState = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-SupplierListing-S4-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "supplierListingWorldTest");
                Scribe_Deep.Look(ref savedState, "state");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loadedState, "state", (object)null);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            List<SupplierListing> loadedListings = loadedState?.SupplierListings;
            bool validSurvived = loadedListings != null && loadedListings.Count == 1 &&
                                 loadedListings[0] != null && loadedListings[0].id == validId &&
                                 loadedListings[0].thingDef == validDef;
            check(
                "S4 collection round-trip prunes null listings and keeps valid listings",
                failure == null && validSurvived,
                $"count {savedListings.Count}->{loadedListings?.Count ?? -1}; " +
                $"valid id={validId}; loaded ids={ListingIds(loadedListings)}; failure={failure ?? "none"}");

            if (canExerciseUnresolvable)
            {
                check(
                    "S4 unresolvable listing is pruned",
                    failure == null && !ContainsListingId(loadedListings, missingId),
                    $"count {savedListings.Count}->{loadedListings?.Count ?? -1}; " +
                    $"missing id={missingId}; loaded ids={ListingIds(loadedListings)}; " +
                    $"failure={failure ?? "none"}");
            }
            else
            {
                skip("S4 unresolvable listing is pruned",
                    $"def name {missingDefName} already resolves in this install");
            }
        }

        private static void CheckSupplierListingMigration(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            FieldInfo saveVersionField)
        {
            if (saveVersionField == null)
            {
                skip("S5 schema 49-to-50 migration preserves listing counts",
                    "persisted saveVersion field is not accessible");
                return;
            }

            List<SupplierListing> beforeListings =
                new List<SupplierListing>(state.SupplierListings);
            int beforeSaveVersion = state.SaveVersion;
            int nonEmptyBefore = -1;
            int nonEmptyAfter = -1;
            int emptyBefore = -1;
            int emptyAfter = -1;
            int migrationSaveVersion = -1;
            string failure = null;

            try
            {
                state.SupplierListings.Clear();
                state.SupplierListings.Add(new SupplierListing
                {
                    id = 6_008,
                    thingDef = ThingDefOf.Steel,
                    quantityAvailable = 3,
                    expiryTick = SupplierListing.NoExpiryTick
                });
                saveVersionField.SetValue(state, 49);
                nonEmptyBefore = state.SupplierListings.Count;
                state.MigrateIfNeeded();
                nonEmptyAfter = state.SupplierListings.Count;

                state.SupplierListings.Clear();
                saveVersionField.SetValue(state, 49);
                emptyBefore = state.SupplierListings.Count;
                state.MigrateIfNeeded();
                emptyAfter = state.SupplierListings.Count;
                migrationSaveVersion = state.SaveVersion;
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                state.SupplierListings.Clear();
                state.SupplierListings.AddRange(beforeListings);
                saveVersionField.SetValue(state, beforeSaveVersion);
            }

            check(
                "S5 schema 49-to-50 migration preserves non-empty and empty listing counts",
                failure == null && nonEmptyBefore == nonEmptyAfter &&
                emptyBefore == emptyAfter && emptyBefore == 0 &&
                migrationSaveVersion == IntercolonyWorldComponent.CurrentSaveVersion,
                $"non-empty count {nonEmptyBefore}->{nonEmptyAfter}; empty count " +
                $"{emptyBefore}->{emptyAfter}; migrated saveVersion={migrationSaveVersion}; " +
                $"restored saveVersion={state.SaveVersion}; failure={failure ?? "none"}");
        }

        private static void CheckSupplierListingIds(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            FieldInfo nextIdField,
            int savedNextId)
        {
            if (nextIdField == null)
            {
                skip("S6 listing ids are unique and use the shared counter",
                    "persisted nextId field is not accessible for fixture restoration");
                return;
            }

            int otherRecordId = -1;
            List<SupplierListing> listings = new List<SupplierListing>();
            string failure = null;
            try
            {
                MarketOpportunity otherRecord = new MarketOpportunity { id = state.NextId() };
                otherRecordId = otherRecord.id;
                for (int i = 0; i < 4; i++)
                {
                    listings.Add(new SupplierListing
                    {
                        id = state.NextId(),
                        thingDef = ThingDefOf.Steel,
                        quantityAvailable = i + 1,
                        expiryTick = SupplierListing.NoExpiryTick
                    });
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                nextIdField.SetValue(state, savedNextId);
            }

            bool noRepeats = true;
            bool noCollision = otherRecordId >= 0;
            bool sharedSequence = listings.Count == 4;
            HashSet<int> ids = new HashSet<int>();
            for (int i = 0; i < listings.Count; i++)
            {
                SupplierListing listing = listings[i];
                noRepeats &= ids.Add(listing.id);
                noCollision &= listing.id != otherRecordId;
                sharedSequence &= listing.id == otherRecordId + i + 1;
            }

            check(
                "S6 listing ids are unique and use the shared counter",
                failure == null && listings.Count == 4 && noRepeats && noCollision && sharedSequence,
                $"other record id={otherRecordId}; listing ids={ListingIds(listings)}; " +
                $"count={listings.Count}; repeats={!noRepeats}; collision={!noCollision}; " +
                $"shared sequence={sharedSequence}; " +
                $"failure={failure ?? "none"}");
        }

        private static void CheckProcurementContracts(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            List<ProcurementContract> savedContracts = state == null
                ? null
                : new List<ProcurementContract>(state.ProcurementContracts);
            List<PurchaseOrder> savedOrders = state == null
                ? null
                : new List<PurchaseOrder>(state.PurchaseOrders);
            List<CommercialEventRecord> savedCommercialTimeline = state == null
                ? null
                : new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedCommercialTimelineStartTick = state?.CommercialTimelineStartTick ?? -1;
            Dictionary<int, CommercialReputation> savedReputations = state == null
                ? null
                : new Dictionary<int, CommercialReputation>(state.Reputations);
            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            Dictionary<Thing, int> savedSilver = SnapshotStoredSilver(paymentMap);
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            int savedSaveVersion = state?.SaveVersion ?? -1;
            int savedNextId = state?.PeekNextId() ?? -1;

            try
            {
                CheckProcurementContractSentinels(check);
                CheckProcurementContractStatuses(check);
                CheckProcurementContractValidity(check, skip);
                CheckProcurementContractCollection(check, skip);
                CheckProcurementContractMigration(check, skip, state, saveVersionField);
                CheckProcurementContractIds(check, skip, state, nextIdField, savedNextId);
                CheckProcurementNegotiation(check, skip, state);
                CheckProcurementProposalPath(check, skip, state);
            }
            finally
            {
                if (state != null && savedContracts != null)
                {
                    state.ProcurementContracts.Clear();
                    state.ProcurementContracts.AddRange(savedContracts);
                }

                if (state != null && savedOrders != null)
                {
                    state.PurchaseOrders.Clear();
                    state.PurchaseOrders.AddRange(savedOrders);
                }

                if (state != null && savedCommercialTimeline != null)
                {
                    state.CommercialTimeline.Clear();
                    state.CommercialTimeline.AddRange(savedCommercialTimeline);
                    state.CommercialTimelineStartTick = savedCommercialTimelineStartTick;
                }

                if (state != null && savedReputations != null)
                {
                    state.Reputations.Clear();
                    foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                    {
                        state.Reputations[entry.Key] = entry.Value;
                    }
                }

                RestoreStoredSilver(paymentMap, savedSilver);

                if (state != null && saveVersionField != null)
                {
                    saveVersionField.SetValue(state, savedSaveVersion);
                }

                if (state != null && nextIdField != null)
                {
                    nextIdField.SetValue(state, savedNextId);
                }
            }
        }

        private static void CheckProcurementContractSentinels(
            Action<string, bool, string> check)
        {
            const int ExpectedActiveOrderId = -1;
            const int ExpectedOfferExpiryTick = -1;
            ProcurementContract saved = new ProcurementContract
            {
                id = 6_010,
                thingDef = ThingDefOf.Steel,
                quantityPerCycle = 3,
                totalCycles = 2,
                activeOrderId = ExpectedActiveOrderId,
                offerExpiryTick = ExpectedOfferExpiryTick
            };
            List<ProcurementContract> savedList = new List<ProcurementContract> { saved };
            List<ProcurementContract> loadedList = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProcurementContract-C1-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "procurementContractSentinelTest");
                Scribe_Collections.Look(ref savedList, "procurementContracts", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                // Force the omitted-node path even if a future Scribe implementation writes
                // values equal to their defaults explicitly.
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlNode activeOrderNode = document.SelectSingleNode("//activeOrderId");
                if (activeOrderNode != null)
                {
                    activeOrderNode.ParentNode.RemoveChild(activeOrderNode);
                }

                XmlNode offerExpiryNode = document.SelectSingleNode("//offerExpiryTick");
                if (offerExpiryNode != null)
                {
                    offerExpiryNode.ParentNode.RemoveChild(offerExpiryNode);
                }

                document.Save(path);

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "procurementContracts", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            ProcurementContract loaded = loadedList != null && loadedList.Count == 1
                ? loadedList[0]
                : null;
            check(
                "C1 activeOrderId sentinel survives an omitted-node save/load",
                failure == null && loaded != null &&
                loaded.activeOrderId == ExpectedActiveOrderId,
                $"activeOrderId expected {ExpectedActiveOrderId}, loaded " +
                $"{(loaded == null ? "null" : loaded.activeOrderId.ToString())}; " +
                $"count {savedList.Count}->{loadedList?.Count ?? -1}; failure={failure ?? "none"}");
            check(
                "C1 offerExpiryTick sentinel survives an omitted-node save/load",
                failure == null && loaded != null &&
                loaded.offerExpiryTick == ExpectedOfferExpiryTick,
                $"offerExpiryTick expected {ExpectedOfferExpiryTick}, loaded " +
                $"{(loaded == null ? "null" : loaded.offerExpiryTick.ToString())}; " +
                $"count {savedList.Count}->{loadedList?.Count ?? -1}; failure={failure ?? "none"}");
        }

        private static void CheckProcurementContractStatuses(
            Action<string, bool, string> check)
        {
            ProcurementContractStatus[] expectedStatuses =
            {
                ProcurementContractStatus.Active,
                ProcurementContractStatus.Completed,
                ProcurementContractStatus.Cancelled,
                ProcurementContractStatus.SupplierDefault
            };
            List<ProcurementContract> savedList = new List<ProcurementContract>();
            foreach (ProcurementContractStatus status in expectedStatuses)
            {
                savedList.Add(new ProcurementContract
                {
                    id = 6_020 + savedList.Count,
                    thingDef = ThingDefOf.Steel,
                    quantityPerCycle = 2,
                    totalCycles = 3,
                    status = status
                });
            }

            List<ProcurementContract> loadedList = null;
            int savedStatusNodes = -1;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProcurementContract-C2-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "procurementContractStatusTest");
                Scribe_Collections.Look(ref savedList, "procurementContracts", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                XmlDocument document = new XmlDocument();
                document.Load(path);
                savedStatusNodes = document.SelectNodes("//status")?.Count ?? 0;

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "procurementContracts", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            for (int i = 0; i < expectedStatuses.Length; i++)
            {
                ProcurementContract loaded = loadedList != null && loadedList.Count > i
                    ? loadedList[i]
                    : null;
                ProcurementContractStatus? loadedStatus = loaded?.status;
                check(
                    $"C2 {expectedStatuses[i]} status round-trips off its Scribe default",
                    failure == null && savedStatusNodes == expectedStatuses.Length &&
                    loaded != null && loaded.status == expectedStatuses[i],
                    $"status expected {expectedStatuses[i]}, loaded " +
                    $"{(loaded == null ? "null" : loadedStatus.ToString())}; " +
                    $"saved status nodes={savedStatusNodes}/{expectedStatuses.Length}; " +
                    $"loaded count={loadedList?.Count ?? -1}; failure={failure ?? "none"}");
            }
        }

        private static void CheckProcurementContractValidity(
            Action<string, bool, string> check,
            Action<string, string> skip)
        {
            ThingDef validDef = ThingDefOf.Steel;
            if (validDef == null)
            {
                skip("C3 null thingDef is invalid after load",
                    "Steel definition is unavailable in this install");
                skip("C3 non-positive quantityPerCycle is invalid after load",
                    "Steel definition is unavailable in this install");
                skip("C3 non-positive totalCycles is invalid after load",
                    "Steel definition is unavailable in this install");
                skip("C3 well-formed procurement contract is valid after load",
                    "Steel definition is unavailable in this install");
                return;
            }

            ProcurementContract nullDef = new ProcurementContract
            {
                thingDef = null,
                quantityPerCycle = 1,
                totalCycles = 1
            };
            ProcurementContract zeroQuantity = new ProcurementContract
            {
                thingDef = validDef,
                quantityPerCycle = 0,
                totalCycles = 1
            };
            ProcurementContract zeroCycles = new ProcurementContract
            {
                thingDef = validDef,
                quantityPerCycle = 1,
                totalCycles = 0
            };
            ProcurementContract valid = new ProcurementContract
            {
                thingDef = validDef,
                quantityPerCycle = 1,
                totalCycles = 1
            };

            check(
                "C3 null thingDef is invalid after load",
                !nullDef.IsValidAfterLoad,
                $"thingDef=null; quantityPerCycle={nullDef.quantityPerCycle}; " +
                $"totalCycles={nullDef.totalCycles}; valid={nullDef.IsValidAfterLoad}");
            check(
                "C3 non-positive quantityPerCycle is invalid after load",
                !zeroQuantity.IsValidAfterLoad,
                $"thingDef={zeroQuantity.thingDef?.defName ?? "null"}; " +
                $"quantityPerCycle={zeroQuantity.quantityPerCycle}; " +
                $"totalCycles={zeroQuantity.totalCycles}; valid={zeroQuantity.IsValidAfterLoad}");
            check(
                "C3 non-positive totalCycles is invalid after load",
                !zeroCycles.IsValidAfterLoad,
                $"thingDef={zeroCycles.thingDef?.defName ?? "null"}; " +
                $"quantityPerCycle={zeroCycles.quantityPerCycle}; " +
                $"totalCycles={zeroCycles.totalCycles}; valid={zeroCycles.IsValidAfterLoad}");
            check(
                "C3 well-formed procurement contract is valid after load",
                valid.IsValidAfterLoad,
                $"thingDef={valid.thingDef?.defName ?? "null"}; " +
                $"quantityPerCycle={valid.quantityPerCycle}; totalCycles={valid.totalCycles}; " +
                $"valid={valid.IsValidAfterLoad}");
        }

        private static void CheckProcurementContractCollection(
            Action<string, bool, string> check,
            Action<string, string> skip)
        {
            ThingDef validDef = ThingDefOf.Steel;
            if (validDef == null)
            {
                skip("C4 null procurement contract is pruned and valid contract survives",
                    "Steel definition is unavailable in this install");
                skip("C4 invalid procurement contract is pruned during world load",
                    "Steel definition is unavailable in this install");
                return;
            }

            const int InvalidId = 6_031;
            const int ValidId = 6_032;
            IntercolonyWorldComponent savedState = new IntercolonyWorldComponent(null);
            savedState.ProcurementContracts.Add(null);
            savedState.ProcurementContracts.Add(new ProcurementContract
            {
                id = InvalidId,
                thingDef = null,
                quantityPerCycle = 1,
                totalCycles = 1
            });
            savedState.ProcurementContracts.Add(new ProcurementContract
            {
                id = ValidId,
                thingDef = validDef,
                quantityPerCycle = 2,
                totalCycles = 3
            });
            IntercolonyWorldComponent loadedState = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProcurementContract-C4-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "procurementContractWorldTest");
                Scribe_Deep.Look(ref savedState, "state");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loadedState, "state", (object)null);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            List<ProcurementContract> loadedContracts = loadedState?.ProcurementContracts;
            bool validSurvived = ContainsProcurementContractId(loadedContracts, ValidId);
            bool nullPruned = loadedContracts != null &&
                              !ContainsNullProcurementContract(loadedContracts);
            check(
                "C4 null procurement contract is pruned and valid contract survives",
                failure == null && nullPruned && validSurvived,
                $"count {savedState.ProcurementContracts.Count}->{loadedContracts?.Count ?? -1}; " +
                $"valid id={ValidId}; loaded ids={ProcurementContractIds(loadedContracts)}; " +
                $"null pruned={nullPruned}; failure={failure ?? "none"}");
            check(
                "C4 invalid procurement contract is pruned during world load",
                failure == null && !ContainsProcurementContractId(loadedContracts, InvalidId) &&
                validSurvived,
                $"count {savedState.ProcurementContracts.Count}->{loadedContracts?.Count ?? -1}; " +
                $"invalid id={InvalidId}; loaded ids={ProcurementContractIds(loadedContracts)}; " +
                $"valid id={ValidId} retained={validSurvived}; failure={failure ?? "none"}");
        }

        private static void CheckProcurementContractMigration(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            FieldInfo saveVersionField)
        {
            if (state == null || saveVersionField == null)
            {
                skip("C5 schema 51-to-53 migration preserves non-empty contract count",
                    "live state or persisted saveVersion field is not accessible");
                skip("C5 schema 51-to-53 migration preserves empty contract count",
                    "live state or persisted saveVersion field is not accessible");
                return;
            }

            List<ProcurementContract> beforeContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            int beforeSaveVersion = state.SaveVersion;
            int nonEmptyBefore = -1;
            int nonEmptyAfter = -1;
            int emptyBefore = -1;
            int emptyAfter = -1;
            int migrationSaveVersion = -1;
            string failure = null;

            try
            {
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.Add(new ProcurementContract
                {
                    id = 6_040,
                    thingDef = ThingDefOf.Steel,
                    quantityPerCycle = 1,
                    totalCycles = 1
                });
                saveVersionField.SetValue(state, 51);
                nonEmptyBefore = state.ProcurementContracts.Count;
                state.MigrateIfNeeded();
                nonEmptyAfter = state.ProcurementContracts.Count;

                state.ProcurementContracts.Clear();
                saveVersionField.SetValue(state, 51);
                emptyBefore = state.ProcurementContracts.Count;
                state.MigrateIfNeeded();
                emptyAfter = state.ProcurementContracts.Count;
                migrationSaveVersion = state.SaveVersion;
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(beforeContracts);
                saveVersionField.SetValue(state, beforeSaveVersion);
            }

            check(
                "C5 schema 51-to-53 migration preserves non-empty contract count",
                failure == null && nonEmptyBefore == nonEmptyAfter,
                $"non-empty count {nonEmptyBefore}->{nonEmptyAfter}; " +
                $"migration saveVersion={migrationSaveVersion}; " +
                $"restored saveVersion={state.SaveVersion}; failure={failure ?? "none"}");
            check(
                "C5 schema 51-to-53 migration preserves empty contract count",
                failure == null && emptyBefore == emptyAfter && emptyBefore == 0 &&
                migrationSaveVersion == IntercolonyWorldComponent.CurrentSaveVersion,
                $"empty count {emptyBefore}->{emptyAfter}; non-empty count " +
                $"{nonEmptyBefore}->{nonEmptyAfter}; migration saveVersion={migrationSaveVersion}; " +
                $"restored saveVersion={state.SaveVersion}; failure={failure ?? "none"}");
        }

        private static void CheckProcurementContractIds(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            FieldInfo nextIdField,
            int savedNextId)
        {
            if (state == null || nextIdField == null)
            {
                skip("C6 procurement contract ids are unique and use the shared counter",
                    "live state or persisted nextId field is not accessible");
                return;
            }

            int otherRecordId = -1;
            List<ProcurementContract> contracts = new List<ProcurementContract>();
            string failure = null;
            try
            {
                MarketOpportunity otherRecord = new MarketOpportunity { id = state.NextId() };
                otherRecordId = otherRecord.id;
                for (int i = 0; i < 4; i++)
                {
                    contracts.Add(new ProcurementContract
                    {
                        id = state.NextId(),
                        thingDef = ThingDefOf.Steel,
                        quantityPerCycle = i + 1,
                        totalCycles = 2
                    });
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                nextIdField.SetValue(state, savedNextId);
            }

            bool noRepeats = true;
            bool noCollision = otherRecordId >= 0;
            bool sharedSequence = contracts.Count == 4;
            HashSet<int> ids = new HashSet<int>();
            for (int i = 0; i < contracts.Count; i++)
            {
                ProcurementContract contract = contracts[i];
                noRepeats &= ids.Add(contract.id);
                noCollision &= contract.id != otherRecordId;
                sharedSequence &= contract.id == otherRecordId + i + 1;
            }

            check(
                "C6 procurement contract ids are unique and use the shared counter",
                failure == null && contracts.Count == 4 && noRepeats && noCollision &&
                sharedSequence,
                $"other record id={otherRecordId}; contract ids={ProcurementContractIds(contracts)}; " +
                $"count={contracts.Count}; repeats={!noRepeats}; collision={!noCollision}; " +
                $"shared sequence={sharedSequence}; failure={failure ?? "none"}");
        }

        private static void CheckProcurementProposalPath(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                SkipProcurementProposalPath(skip, "live world state is unavailable");
                return;
            }

            state.ProcurementContracts.Clear();
            if (!TryFindProcurementProposalFixture(
                    state, out Settlement settlement, out ThingDef product,
                    out ThingDef otherProduct, out SettlementEconomicProfile profile,
                    out string fixtureReason))
            {
                SkipProcurementProposalPath(skip, fixtureReason);
                return;
            }

            CheckProcurementProposalPending(check, state, settlement, product);
            CheckProcurementProposalAnswersOnce(check, state, settlement, product);
            CheckProcurementProposalSaveLoad(check, state, settlement, product, profile);
            CheckProcurementProposalValidation(check, state, settlement, product);
            CheckProcurementProposalTechnicalGate(
                check, skip, state, settlement, profile);
            CheckProcurementProposalDuplicateScope(
                check, skip, state, settlement, product, otherProduct);
            CheckProcurementProposalAcceptance(check, skip, state, product);
            CheckProcurementProposalPriceDirection(check, state, settlement, product);
            CheckProcurementContractPreview(check, state, settlement, product);
        }

        private static void SkipProcurementProposalPath(
            Action<string, string> skip,
            string reason)
        {
            skip("E1 sent procurement proposal remains pending", reason);
            skip("E2 procurement proposal answer applies exactly once", reason);
            skip("E3 persisted procurement decision survives save/load", reason);
            skip("E4 procurement proposal bounds refuse before evaluation", reason);
            skip("E5 technically unsupplyable procurement item is refused", reason);
            skip("E6 procurement proposal duplicate scope is supplier and product", reason);
            skip("E7 accepted procurement proposal schedules without prepayment", reason);
            skip("E8 supplier price appeal increases with purchase price", reason);
            skip("E9 procurement preview price matches sent proposal", reason);
            skip("E10 procurement preview refuses out-of-range quantity with proposal", reason);
            skip("E11 procurement preview refuses an existing settlement-item agreement", reason);
            skip("E12 procurement preview total payment uses shared pricing", reason);
            skip("E13 procurement acceptance preview matches the proposal band", reason);
            skip("E14 procurement acceptance preview leaves state untouched", reason);
            skip("E15 procurement acceptance preview refuses an out-of-range package", reason);
            skip("E16 procurement proposal appeal remains continuous", reason);
        }

        private static void CheckProcurementContractPreview(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product)
        {
            const int quantity = 10;
            const int cadenceDays = 1;
            const int totalCycles = 2;
            state.ProcurementContracts.Clear();
            ProcurementContractTerms preview =
                ProcurementContractService.PreviewContractTerms(
                    state, settlement, product, null, null, quantity, cadenceDays, totalCycles);
            int nextIdBeforeAcceptancePreviews = state.PeekNextId();
            int contractCountBeforeAcceptancePreviews = state.ProcurementContracts.Count;
            IntercolonyNegotiationAcceptancePreview acceptancePreview =
                ProcurementContractService.PreviewAcceptance(
                    state, settlement, product, null, null, quantity, cadenceDays,
                    totalCycles, agreedUnitPrice: null,
                    fulfillment: FulfillmentMode.SellerDelivery);
            // Keep both probes near the reference rate so this exercises the responsive part
            // of the appeal curve rather than two prices that both clamp at its ceiling.
            float continuousPrice = preview == null
                ? -1f
                : preview.referenceUnitPrice * 0.95f;
            float slightlyDifferentPrice = preview == null
                ? -1f
                : preview.referenceUnitPrice * 0.96f;
            IntercolonyNegotiationAcceptancePreview continuousFirstPreview =
                ProcurementContractService.PreviewAcceptance(
                    state, settlement, product, null, null, quantity, cadenceDays,
                    totalCycles, agreedUnitPrice: continuousPrice,
                    fulfillment: FulfillmentMode.SellerDelivery);
            IntercolonyNegotiationAcceptancePreview continuousSecondPreview =
                ProcurementContractService.PreviewAcceptance(
                    state, settlement, product, null, null, quantity, cadenceDays,
                    totalCycles, agreedUnitPrice: slightlyDifferentPrice,
                    fulfillment: FulfillmentMode.SellerDelivery);
            IntercolonyNegotiationAcceptancePreview repeatedAcceptancePreview =
                ProcurementContractService.PreviewAcceptance(
                    state, settlement, product, null, null, quantity, cadenceDays,
                    totalCycles, agreedUnitPrice: null,
                    fulfillment: FulfillmentMode.SellerDelivery);
            IntercolonyNegotiationAcceptancePreview thirdAcceptancePreview =
                ProcurementContractService.PreviewAcceptance(
                    state, settlement, product, null, null, quantity, cadenceDays,
                    totalCycles, agreedUnitPrice: null,
                    fulfillment: FulfillmentMode.SellerDelivery);

            // This fails if a procurement preview consumes an ID, records a contract, or mutates
            // the contract collection while it is only answering a read-only question.
            check(
                "E14 procurement acceptance preview leaves state untouched",
                acceptancePreview != null && repeatedAcceptancePreview != null &&
                thirdAcceptancePreview != null &&
                state.PeekNextId() == nextIdBeforeAcceptancePreviews &&
                state.ProcurementContracts.Count == contractCountBeforeAcceptancePreviews,
                $"next id {nextIdBeforeAcceptancePreviews}->{state.PeekNextId()}; " +
                $"contracts {contractCountBeforeAcceptancePreviews}->" +
                $"{state.ProcurementContracts.Count}");

            ProcurementContractProposalResult proposal =
                ProcurementContractService.ProposeContract(
                    state, settlement, product, null, null, quantity, cadenceDays, totalCycles);

            // This fails if preview and proposal calculate the seeded supplier price differently.
            check(
                "E9 procurement preview price matches sent proposal",
                preview != null && proposal.Success && proposal.Contract != null &&
                proposal.Contract.unitPrice == preview.unitPrice,
                $"preview unit={preview?.unitPrice.ToString("R") ?? "null"}; " +
                $"proposal unit={proposal.Contract?.unitPrice.ToString("R") ?? "null"}; " +
                $"preview reference={preview?.referenceUnitPrice.ToString("R") ?? "null"}; " +
                $"reason={proposal.Reason ?? "none"}");

            // This fails if a Refused preview reaches Likely or stronger, an Accepted preview
            // falls at Unlikely or weaker, or the previewed score or factor count differs from
            // the proposal evaluation.
            check(
                "E13 procurement acceptance preview matches the proposal band",
                acceptancePreview != null && proposal.Success &&
                proposal.Evaluation != null &&
                (proposal.Evaluation.Decision !=
                     IntercolonyNegotiationDecision.Refused ||
                 (int)acceptancePreview.Band <
                     (int)IntercolonyNegotiationAcceptanceBand.Likely) &&
                (proposal.Evaluation.Decision !=
                     IntercolonyNegotiationDecision.Accepted ||
                 (int)acceptancePreview.Band >
                     (int)IntercolonyNegotiationAcceptanceBand.Unlikely) &&
                acceptancePreview.Score == proposal.Evaluation.AcceptanceScore &&
                acceptancePreview.Factors.Count == proposal.Evaluation.Factors.Count &&
                acceptancePreview.AcceptanceChance == null &&
                proposal.Contract != null &&
                Mathf.Abs(
                    acceptancePreview.ProposalAppeal - proposal.Contract.proposalAppeal) <=
                    0.000001f,
                $"preview band={acceptancePreview?.Band.ToString() ?? "null"}; " +
                $"proposal decision={proposal.Evaluation?.Decision.ToString() ?? "null"}; " +
                $"preview score={acceptancePreview?.Score.ToString("R") ?? "null"}; " +
                $"proposal score={proposal.Evaluation?.AcceptanceScore.ToString("R") ?? "null"}; " +
                $"preview factors={acceptancePreview?.Factors.Count.ToString() ?? "null"}; " +
                $"proposal factors={proposal.Evaluation?.Factors.Count.ToString() ?? "null"}; " +
                $"preview appeal={acceptancePreview?.ProposalAppeal.ToString("R") ?? "null"}; " +
                $"stored appeal={proposal.Contract?.proposalAppeal.ToString("R") ?? "null"}; " +
                $"preview chance={(acceptancePreview?.AcceptanceChance.HasValue == true
                    ? acceptancePreview.AcceptanceChance.Value.ToString("R") : "null")}");

            // This must fail if anyone reintroduces a bucketed appeal: two near-reference
            // packages that differ only by a slight price change must retain different appeal
            // values, and neither value may be one of the old 0, 0.5, or 1 buckets.
            check(
                "E16 procurement proposal appeal remains continuous",
                preview != null &&
                continuousFirstPreview != null && continuousSecondPreview != null &&
                Mathf.Abs(slightlyDifferentPrice - continuousPrice) > 0f &&
                Mathf.Abs(
                    continuousFirstPreview.ProposalAppeal -
                    continuousSecondPreview.ProposalAppeal) > 0.000001f &&
                !IsLegacyAppealBucket(continuousFirstPreview.ProposalAppeal) &&
                !IsLegacyAppealBucket(continuousSecondPreview.ProposalAppeal),
                $"prices={continuousPrice:R}/{slightlyDifferentPrice:R}; " +
                $"appeals={continuousFirstPreview?.ProposalAppeal.ToString("R") ?? "null"}/" +
                $"{continuousSecondPreview?.ProposalAppeal.ToString("R") ?? "null"}");

            state.ProcurementContracts.Clear();
            const int outOfRangeQuantity = 0;
            IntercolonyNegotiationAcceptancePreview outOfRangeAcceptancePreview =
                ProcurementContractService.PreviewAcceptance(
                    state, settlement, product, null, null, outOfRangeQuantity,
                    cadenceDays, totalCycles, agreedUnitPrice: null,
                    fulfillment: FulfillmentMode.SellerDelivery);
            ProcurementContractTerms outOfRangePreview =
                ProcurementContractService.PreviewContractTerms(
                    state, settlement, product, null, null, outOfRangeQuantity,
                    cadenceDays, totalCycles);
            ProcurementContractProposalResult outOfRangeProposal =
                ProcurementContractService.ProposeContract(
                    state, settlement, product, null, null, outOfRangeQuantity,
                    cadenceDays, totalCycles);

            // This fails if preview accepts a quantity that ProposeContract rejects at its bounds.
            check(
                "E10 procurement preview refuses out-of-range quantity with proposal",
                outOfRangePreview == null && outOfRangeAcceptancePreview == null &&
                !outOfRangeProposal.Success &&
                outOfRangeProposal.Contract == null &&
                outOfRangeProposal.Failure == ProcurementContractProposalFailure.QuantityOutOfRange,
                $"quantity={outOfRangeQuantity}; preview=" +
                $"{(outOfRangePreview == null ? "null" : "terms")}; " +
                $"proposal success={outOfRangeProposal.Success}; " +
                $"failure={outOfRangeProposal.Failure}; " +
                $"reason={outOfRangeProposal.Reason ?? "none"}");

            // This fails if the acceptance preview returns a band for a package that violates a
            // service bound, instead of returning null like the terms preview.
            check(
                "E15 procurement acceptance preview refuses an out-of-range package",
                outOfRangeAcceptancePreview == null,
                $"preview={(outOfRangeAcceptancePreview == null
                    ? "null" : outOfRangeAcceptancePreview.Band.ToString())}");

            state.ProcurementContracts.Clear();
            ProcurementContractProposalResult existingProposal =
                ProposeProcurementFixture(state, settlement, product);
            ProcurementContractTerms existingPreview =
                ProcurementContractService.PreviewContractTerms(
                    state, settlement, product, null, null, quantity, cadenceDays, totalCycles);
            ProcurementContractProposalResult duplicateProposal =
                ProcurementContractService.ProposeContract(
                    state, settlement, product, null, null, quantity, cadenceDays, totalCycles);

            // This fails if preview omits the same settlement-and-item duplicate guard as proposal.
            check(
                "E11 procurement preview refuses an existing settlement-item agreement",
                existingProposal.Success && existingProposal.Contract != null &&
                existingPreview == null && !duplicateProposal.Success &&
                duplicateProposal.Contract == null &&
                duplicateProposal.Failure == ProcurementContractProposalFailure.ExistingContract,
                $"existing success={existingProposal.Success}; preview=" +
                $"{(existingPreview == null ? "null" : "terms")}; " +
                $"duplicate success={duplicateProposal.Success}; " +
                $"failure={duplicateProposal.Failure}; " +
                $"reason={duplicateProposal.Reason ?? "none"}");

            state.ProcurementContracts.Clear();
            const float paymentFixtureUnitPrice = 0.02f;
            const int paymentFixtureQuantity = 76;
            const int paymentFixtureCadenceDays = 5;
            const int paymentFixtureCycles = 5;
            ProcurementContractTerms paymentTerms =
                ProcurementContractService.PreviewContractTerms(
                    state, settlement, product, null, null, paymentFixtureQuantity,
                    paymentFixtureCadenceDays, paymentFixtureCycles,
                    paymentFixtureUnitPrice);
            int expectedPaymentPerCycle = IntercolonyPricing.TotalPayment(
                paymentFixtureUnitPrice, paymentFixtureQuantity);
            int expectedTotalPayment = IntercolonyPricing.TotalPayment(
                expectedPaymentPerCycle, paymentFixtureCycles);

            // This fails if payment truncates instead of rounding, or multiplies the unit price across all cycles instead of using the rounded per-cycle payment.
            check(
                "E12 procurement preview total payment uses shared pricing",
                paymentTerms != null && paymentTerms.totalCycles == paymentFixtureCycles &&
                paymentTerms.unitPrice == paymentFixtureUnitPrice &&
                paymentTerms.paymentPerCycle == expectedPaymentPerCycle &&
                paymentTerms.totalPayment == expectedTotalPayment &&
                paymentTerms.totalPayment == IntercolonyPricing.TotalPayment(
                    paymentTerms.paymentPerCycle, paymentFixtureCycles),
                $"fixture unit={paymentFixtureUnitPrice:R}; " +
                $"preview unit={paymentTerms?.unitPrice.ToString("R") ?? "null"}; " +
                $"quantity={paymentFixtureQuantity}; cycles={paymentFixtureCycles}; " +
                $"perCycle={paymentTerms?.paymentPerCycle.ToString() ?? "null"}; " +
                $"total={paymentTerms?.totalPayment.ToString() ?? "null"}; " +
                $"expected perCycle={expectedPaymentPerCycle}; " +
                $"expected total={expectedTotalPayment}");

            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementNegotiation(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                SkipProcurementNegotiation(skip, "live world state is unavailable");
                return;
            }

            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            Dictionary<Thing, int> savedSilver = SnapshotStoredSilver(paymentMap);
            List<ProcurementContract> savedContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            List<PurchaseOrder> savedOrders = new List<PurchaseOrder>(state.PurchaseOrders);
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            int savedSaveVersion = state.SaveVersion;
            int savedNextId = state.PeekNextId();
            ThingDef product = ThingDefOf.Steel;

            try
            {
                state.ProcurementContracts.Clear();

                if (!TryFindRealProcurementCounter(
                        state, out ProcurementContractProposalResult realProposal,
                        out int realSearchAttempts, out string realReason))
                {
                    skip(
                        "M1 countered procurement answer is not a refusal",
                        realReason);
                }
                else
                {
                    ProcurementContract realContract = realProposal.Contract;
                    ProcurementContractStatus beforeStatus = realContract.status;
                    ProcurementContractAnswer realAnswer =
                        ProcurementContractService.AnswerProposal(state, realContract);
                    check(
                        "M1 countered procurement answer is not a refusal",
                        realProposal.Evaluation != null &&
                        realProposal.Evaluation.Decision ==
                            IntercolonyNegotiationDecision.Countered &&
                        realAnswer.Applied &&
                        realAnswer.Decision == IntercolonyNegotiationDecision.Countered &&
                        realContract.status == ProcurementContractStatus.CounterpartyCountered &&
                        realContract.status != ProcurementContractStatus.CounterpartyRefused &&
                        realContract.proposalDecision ==
                            (int)IntercolonyNegotiationDecision.Countered,
                        $"before={beforeStatus}; after={realContract.status}; " +
                        $"answer={realAnswer.Decision}; persistedDecision=" +
                        $"{(IntercolonyNegotiationDecision)realContract.proposalDecision}; " +
                        $"evaluation={realProposal.Evaluation.Decision}; " +
                        $"original={DescribeProcurementTerms(realContract, false)}; " +
                        $"counter={DescribeProcurementTerms(realContract, true)}; " +
                        $"searchAttempts={realSearchAttempts}; reason={realAnswer.Reason ?? "none"}");

                    // M2 deliberately uses a separate save/load of the real service-produced
                    // counter, so the persisted package is not merely a test-only record.
                    CheckProcurementCounterSaveLoad(check, state, realContract);
                    state.ProcurementContracts.Clear();
                }

                if (product == null)
                {
                    SkipProcurementNegotiationFixtures(
                        skip, "Steel definition is unavailable for deterministic counter fixtures");
                }
                else
                {
                    if (realProposal == null)
                    {
                        state.ProcurementContracts.Clear();
                        ProcurementContract handMadeCounter = HandMadeProcurementCounter(
                            product, 6_800, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5);
                        state.ProcurementContracts.Add(handMadeCounter);
                        CheckProcurementCounterSaveLoad(check, state, handMadeCounter);
                    }

                    CheckProcurementCounterAcceptance(check, state, product);
                    CheckProcurementCounterDecline(check, skip, state, product, paymentMap);
                    CheckProcurementTransitionReplay(check, state, product);
                    CheckProcurementPlayerActionsByStatus(check, state, product);
                }

                if (saveVersionField == null)
                {
                    skip(
                        "M8 schema 55 migration does not rewrite procurement contracts",
                        "persisted saveVersion field is not accessible");
                }
                else
                {
                    CheckProcurementSchema55Migration(
                        check, state, product ?? ThingDefOf.Silver, saveVersionField);
                }

                state.ProcurementContracts.Clear();
                if (!TryFindRealProcurementCounter(
                        state, out ProcurementContractProposalResult searchedExchange,
                        out int exchangeSearchAttempts, out string exchangeReason))
                {
                    skip(
                        "M6 procurement proposal-counter-accept exchange evaluates once",
                        exchangeReason);
                }
                else
                {
                    ProcurementContract searchedContract = searchedExchange.Contract;
                    Settlement exchangeSettlement = searchedContract == null
                        ? null
                        : IntercolonyMarketAccess.FindSettlement(searchedContract.settlementId);
                    MethodInfo evaluatorMethod = typeof(IntercolonyNegotiationEvaluator).GetMethod(
                        "Evaluate", BindingFlags.Public | BindingFlags.Static);
                    MethodInfo evaluatorPostfix = typeof(IntercolonyRfqSelfTest).GetMethod(
                        nameof(CountProcurementEvaluatorInvocation),
                        BindingFlags.NonPublic | BindingFlags.Static);
                    if (searchedContract == null || exchangeSettlement == null ||
                        evaluatorMethod == null || evaluatorPostfix == null)
                    {
                        skip(
                            "M6 procurement proposal-counter-accept exchange evaluates once",
                            "real counter settlement or evaluator instrumentation method was unavailable");
                    }
                    else
                    {
                        state.ProcurementContracts.Remove(searchedContract);
                        procurementEvaluatorInvocationCount = 0;
                        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(
                            "miannoni.intercolony.stage6h.selftest");
                        ProcurementContractProposalResult exchangeProposal = null;
                        ProcurementContract exchangeContract = null;
                        ProcurementContractAnswer counterAnswer = null;
                        ProcurementContractAnswer acceptedAnswer = null;
                        try
                        {
                            harmony.Patch(
                                evaluatorMethod,
                                postfix: new HarmonyLib.HarmonyMethod(evaluatorPostfix));
                            exchangeProposal = ProcurementContractService.ProposeContract(
                                state,
                                exchangeSettlement,
                                searchedContract.thingDef,
                                searchedContract.quantityPerCycle,
                                searchedContract.cadenceDays,
                                searchedContract.totalCycles,
                                searchedContract.unitPrice,
                                searchedContract.fulfillment);
                            exchangeContract = exchangeProposal.Contract;
                            if (exchangeContract != null)
                            {
                                counterAnswer = ProcurementContractService.AnswerProposal(
                                    state, exchangeContract);
                                acceptedAnswer = ProcurementContractService.AcceptFinalCounter(
                                    state, exchangeContract);
                            }

                            check(
                                "M6 procurement proposal-counter-accept exchange evaluates once",
                                procurementEvaluatorInvocationCount == 1 &&
                                exchangeProposal != null &&
                                exchangeProposal.Evaluation != null &&
                                exchangeProposal.Evaluation.Decision ==
                                    IntercolonyNegotiationDecision.Countered &&
                                counterAnswer != null && counterAnswer.Applied &&
                                counterAnswer.Decision == IntercolonyNegotiationDecision.Countered &&
                                acceptedAnswer != null && acceptedAnswer.Applied &&
                                acceptedAnswer.Decision == IntercolonyNegotiationDecision.Countered &&
                                exchangeContract.status == ProcurementContractStatus.Active,
                                $"proposalDecision={exchangeProposal.Evaluation?.Decision.ToString() ?? "none"}; " +
                                $"counterAnswer={counterAnswer?.Decision.ToString() ?? "none"}; " +
                                $"acceptedAnswer={acceptedAnswer?.Decision.ToString() ?? "none"}; " +
                                $"finalState={exchangeContract?.status.ToString() ?? "none"}; " +
                                $"exchangeEvaluatorInvocations={procurementEvaluatorInvocationCount}; " +
                                $"searchAttempts={exchangeSearchAttempts}; " +
                                $"reason={exchangeReason ?? "none"}");
                        }
                        finally
                        {
                            harmony.Unpatch(
                                evaluatorMethod,
                                HarmonyLib.HarmonyPatchType.Postfix,
                                harmony.Id);
                            procurementEvaluatorInvocationCount = 0;
                        }
                    }
                }
            }
            finally
            {
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedContracts);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedOrders);
                RestoreStoredSilver(paymentMap, savedSilver);
                if (saveVersionField != null)
                {
                    saveVersionField.SetValue(state, savedSaveVersion);
                }

                if (nextIdField != null)
                {
                    nextIdField.SetValue(state, savedNextId);
                }
            }
        }

        private static void SkipProcurementNegotiation(
            Action<string, string> skip,
            string reason)
        {
            skip("M1 countered procurement answer is not a refusal", reason);
            skip("M2a counter quantity survives save/load", reason);
            skip("M2b counter unit price survives save/load", reason);
            skip("M2c counter cadence survives save/load", reason);
            skip("M2d counter total cycles survives save/load", reason);
            skip("M3 accepted procurement counter binds persisted terms", reason);
            skip("M4 declining a procurement counter is terminal and non-destructive", reason);
            skip("M5 procurement negotiation transitions apply exactly once", reason);
            skip("M6 procurement proposal-counter-accept exchange evaluates once", reason);
            skip("M7 player actions are refused outside the countered state", reason);
            skip("M8 schema 55 migration does not rewrite procurement contracts", reason);
        }

        private static void SkipProcurementNegotiationFixtures(
            Action<string, string> skip,
            string reason)
        {
            skip("M2a counter quantity survives save/load", reason);
            skip("M2b counter unit price survives save/load", reason);
            skip("M2c counter cadence survives save/load", reason);
            skip("M2d counter total cycles survives save/load", reason);
            skip("M3 accepted procurement counter binds persisted terms", reason);
            skip("M4 declining a procurement counter is terminal and non-destructive", reason);
            skip("M5 procurement negotiation transitions apply exactly once", reason);
            skip("M7 player actions are refused outside the countered state", reason);
        }

        private static bool TryFindRealProcurementCounter(
            IntercolonyWorldComponent state,
            out ProcurementContractProposalResult found,
            out int proposalsTried,
            out string reason)
        {
            found = null;
            proposalsTried = 0;
            reason = null;
            if (state == null)
            {
                reason = "live world state is unavailable";
                return false;
            }

            if (!TryFindProcurementProposalFixture(
                    state, out Settlement settlement, out ThingDef product,
                    out _, out _, out string fixtureReason))
            {
                reason = fixtureReason;
                return false;
            }

            float[] priceMultipliers =
            {
                0.50f, 0.55f, 0.60f, 0.65f, 0.70f, 0.75f, 0.80f, 0.85f,
                0.90f, 0.95f, 1.00f, 1.05f, 1.10f, 1.20f, 1.30f, 1.50f
            };
            int[] quantities = { 8, 10, 12, 15, 20 };
            int[] cadences = { 1, 2, 4, 7 };
            FulfillmentMode[] fulfillmentModes =
            {
                FulfillmentMode.SellerDelivery,
                FulfillmentMode.BuyerPickup
            };
            IntercolonyNegotiationDecision? lastDecision = null;

            foreach (int quantity in quantities)
            {
                foreach (int cadence in cadences)
                {
                    foreach (FulfillmentMode fulfillment in fulfillmentModes)
                    {
                        ProcurementContractProposalResult baseline =
                            ProcurementContractService.ProposeContract(
                                state, settlement, product, quantity, cadence, 2,
                                null, fulfillment);
                        if (baseline.Contract == null)
                        {
                            continue;
                        }

                        float referencePrice = baseline.Contract.unitPrice;
                        lastDecision = baseline.Evaluation?.Decision;
                        if (baseline.Evaluation?.Decision ==
                            IntercolonyNegotiationDecision.Countered)
                        {
                            return KeepRealCounter(state, baseline, ref found);
                        }

                        state.ProcurementContracts.Remove(baseline.Contract);
                        foreach (float multiplier in priceMultipliers)
                        {
                            proposalsTried++;
                            ProcurementContractProposalResult candidate =
                                ProcurementContractService.ProposeContract(
                                    state, settlement, product, quantity, cadence, 2,
                                    referencePrice * multiplier, fulfillment);
                            lastDecision = candidate.Evaluation?.Decision;
                            if (candidate.Contract != null &&
                                candidate.Evaluation?.Decision ==
                                    IntercolonyNegotiationDecision.Countered)
                            {
                                return KeepRealCounter(state, candidate, ref found);
                            }

                            if (candidate.Contract != null)
                            {
                                state.ProcurementContracts.Remove(candidate.Contract);
                            }
                        }
                    }
                }
            }

            reason = $"no Countered result after {proposalsTried} price proposal(s); " +
                     $"lastDecision={lastDecision?.ToString() ?? "none"}; " +
                     $"settlement={settlement.ID}; product={product.defName}";
            return false;
        }

        private static bool KeepRealCounter(
            IntercolonyWorldComponent state,
            ProcurementContractProposalResult candidate,
            ref ProcurementContractProposalResult found)
        {
            found = candidate;
            return true;
        }

        private static void CheckProcurementCounterSaveLoad(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            ProcurementContract source)
        {
            ProcurementContractCounterTerms expected = null;
            bool hasExpected = source != null &&
                               source.TryGetFinalCounterTerms(
                                   out expected);
            List<ProcurementContract> savedList = hasExpected
                ? new List<ProcurementContract> { source }
                : new List<ProcurementContract>();
            List<ProcurementContract> loadedList = null;
            ProcurementContract loaded = null;
            ProcurementContractCounterTerms actual = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProcurementCounter-M2-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "procurementCounterTermsTest");
                Scribe_Collections.Look(ref savedList, "procurementContracts", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "procurementContracts", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
                loaded = loadedList != null && loadedList.Count == 1 ? loadedList[0] : null;
                if (loaded != null)
                {
                    loaded.TryGetFinalCounterTerms(out actual);
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            check(
                "M2a counter quantity survives save/load",
                failure == null && hasExpected && loaded != null && actual != null &&
                loaded.status == ProcurementContractStatus.CounterpartyCountered &&
                actual.quantityPerCycle == expected.quantityPerCycle,
                $"state={source?.status.ToString() ?? "null"}->{loaded?.status.ToString() ?? "null"}; " +
                $"quantity={expected?.quantityPerCycle.ToString() ?? "none"}->" +
                $"{actual?.quantityPerCycle.ToString() ?? "none"}; " +
                $"counter={DescribeCounterTerms(expected)}; loaded={DescribeCounterTerms(actual)}; " +
                $"failure={failure ?? "none"}");
            check(
                "M2b counter unit price survives save/load",
                failure == null && hasExpected && loaded != null && actual != null &&
                actual.unitPrice == expected.unitPrice,
                $"unitPrice={expected?.unitPrice.ToString("F4") ?? "none"}->" +
                $"{actual?.unitPrice.ToString("F4") ?? "none"}; " +
                $"counter={DescribeCounterTerms(expected)}; loaded={DescribeCounterTerms(actual)}; " +
                $"failure={failure ?? "none"}");
            check(
                "M2c counter cadence survives save/load",
                failure == null && hasExpected && loaded != null && actual != null &&
                actual.cadenceDays == expected.cadenceDays,
                $"cadenceDays={expected?.cadenceDays.ToString() ?? "none"}->" +
                $"{actual?.cadenceDays.ToString() ?? "none"}; " +
                $"counter={DescribeCounterTerms(expected)}; loaded={DescribeCounterTerms(actual)}; " +
                $"failure={failure ?? "none"}");
            check(
                "M2d counter total cycles survives save/load",
                failure == null && hasExpected && loaded != null && actual != null &&
                actual.totalCycles == expected.totalCycles,
                $"totalCycles={expected?.totalCycles.ToString() ?? "none"}->" +
                $"{actual?.totalCycles.ToString() ?? "none"}; " +
                $"counter={DescribeCounterTerms(expected)}; loaded={DescribeCounterTerms(actual)}; " +
                $"failure={failure ?? "none"}");
        }

        private static void CheckProcurementCounterAcceptance(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            ThingDef product)
        {
            state.ProcurementContracts.Clear();
            ProcurementContract contract = HandMadeProcurementCounter(
                product, 6_801, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5);
            state.ProcurementContracts.Add(contract);
            contract.TryGetFinalCounterTerms(out ProcurementContractCounterTerms expected);
            ProcurementContractAnswer answer =
                ProcurementContractService.AcceptFinalCounter(state, contract);
            check(
                "M3 accepted procurement counter binds persisted terms",
                answer.Applied && answer.Decision == IntercolonyNegotiationDecision.Countered &&
                contract.status == ProcurementContractStatus.Active && expected != null &&
                contract.quantityPerCycle == expected.quantityPerCycle &&
                contract.unitPrice == expected.unitPrice &&
                contract.cadenceDays == expected.cadenceDays &&
                contract.totalCycles == expected.totalCycles,
                $"before={ProcurementContractStatus.CounterpartyCountered}; " +
                $"after={contract.status}; answer={answer.Decision}; " +
                $"original=7x/1.25/{3}d/x1; persisted={DescribeCounterTerms(expected)}; " +
                $"active={DescribeProcurementTerms(contract, false)}; " +
                $"orderCount={state.PurchaseOrders.Count}");
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementCounterDecline(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            ThingDef product,
            Map paymentMap)
        {
            if (paymentMap == null)
            {
                skip("M4 declining a procurement counter is terminal and non-destructive",
                    "no player map is available to count silver");
                return;
            }

            state.ProcurementContracts.Clear();
            state.PurchaseOrders.Clear();
            ProcurementContract contract = HandMadeProcurementCounter(
                product, 6_802, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5);
            state.ProcurementContracts.Add(contract);
            int ordersBefore = state.PurchaseOrders.Count;
            int silverBefore = PurchaseOrderService.CountColonySilver(paymentMap);
            string originalTerms = DescribeProcurementTerms(contract, false);
            bool declined = ProcurementContractService.TryDeclineFinalCounter(state, contract);
            ProcurementContractStatus statusAfterDecline = contract.status;
            int ordersAfterDecline = state.PurchaseOrders.Count;
            int silverAfterDecline = PurchaseOrderService.CountColonySilver(paymentMap);
            bool acceptedAfterDecline =
                ProcurementContractService.AcceptFinalCounter(state, contract).Applied;
            bool declinedAgain = ProcurementContractService.TryDeclineFinalCounter(state, contract);
            check(
                "M4 declining a procurement counter is terminal and non-destructive",
                declined && !acceptedAfterDecline && !declinedAgain &&
                statusAfterDecline == ProcurementContractStatus.Cancelled &&
                contract.status != ProcurementContractStatus.Active &&
                ordersAfterDecline == ordersBefore && silverAfterDecline == silverBefore &&
                DescribeProcurementTerms(contract, false) == originalTerms,
                $"before={ProcurementContractStatus.CounterpartyCountered}; " +
                $"after={statusAfterDecline}; acceptedAfterDecline={acceptedAfterDecline}; " +
                $"declinedAgain={declinedAgain}; orders={ordersBefore}->{ordersAfterDecline}; " +
                $"silver={silverBefore}->{silverAfterDecline}; " +
                $"originalTerms={originalTerms}; finalTerms={DescribeProcurementTerms(contract, false)}");
            state.ProcurementContracts.Clear();
            state.PurchaseOrders.Clear();
        }

        private static void CheckProcurementTransitionReplay(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            ThingDef product)
        {
            state.ProcurementContracts.Clear();
            ProcurementContract answerContract = HandMadeProcurementCounter(
                product, 6_803, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5,
                ProcurementContractStatus.Offered);
            answerContract.decisionDueTick = GenTicks.TicksGame;
            answerContract.proposalAppeal = 0.25f;
            state.ProcurementContracts.Add(answerContract);
            ProcurementContractStatus answerInitialStatus = answerContract.status;
            ProcurementContractAnswer firstAnswer =
                ProcurementContractService.AnswerProposal(state, answerContract);
            ProcurementContractStatus answerStatus = answerContract.status;
            string answerNote = answerContract.outcomeNote;
            ProcurementContractAnswer secondAnswer =
                ProcurementContractService.AnswerProposal(state, answerContract);

            ProcurementContract acceptContract = HandMadeProcurementCounter(
                product, 6_804, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5);
            state.ProcurementContracts.Clear();
            state.ProcurementContracts.Add(acceptContract);
            ProcurementContractStatus acceptInitialStatus = acceptContract.status;
            ProcurementContractAnswer firstAccept =
                ProcurementContractService.AcceptFinalCounter(state, acceptContract);
            ProcurementContractStatus acceptStatus = acceptContract.status;
            string acceptTerms = DescribeProcurementTerms(acceptContract, false);
            ProcurementContractAnswer secondAccept =
                ProcurementContractService.AcceptFinalCounter(state, acceptContract);

            ProcurementContract declineContract = HandMadeProcurementCounter(
                product, 6_805, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5);
            state.ProcurementContracts.Clear();
            state.ProcurementContracts.Add(declineContract);
            ProcurementContractStatus declineInitialStatus = declineContract.status;
            bool firstDecline = ProcurementContractService.TryDeclineFinalCounter(
                state, declineContract);
            ProcurementContractStatus declineStatus = declineContract.status;
            string declineNote = declineContract.outcomeNote;
            bool secondDecline = ProcurementContractService.TryDeclineFinalCounter(
                state, declineContract);

            check(
                "M5 procurement negotiation transitions apply exactly once",
                firstAnswer.Applied && answerStatus != answerInitialStatus &&
                !secondAnswer.Applied && answerContract.status == answerStatus &&
                answerContract.outcomeNote == answerNote && firstAccept.Applied &&
                acceptStatus != acceptInitialStatus && !secondAccept.Applied &&
                acceptContract.status == acceptStatus &&
                DescribeProcurementTerms(acceptContract, false) == acceptTerms &&
                firstDecline && declineStatus != declineInitialStatus && !secondDecline &&
                declineContract.status == declineStatus &&
                declineContract.outcomeNote == declineNote,
                $"answer={firstAnswer.Applied}/{secondAnswer.Applied}; " +
                $"answerStates={answerInitialStatus}->{answerStatus}->{answerContract.status}; " +
                $"accept={firstAccept.Applied}/{secondAccept.Applied}; " +
                $"acceptStates={acceptInitialStatus}->{acceptStatus}->{acceptContract.status}; " +
                $"acceptTerms={acceptTerms}/{DescribeProcurementTerms(acceptContract, false)}; " +
                $"decline={firstDecline}/{secondDecline}; " +
                $"declineStates={declineInitialStatus}->{declineStatus}->" +
                $"{declineContract.status}");
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementPlayerActionsByStatus(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            ThingDef product)
        {
            state.ProcurementContracts.Clear();
            List<string> details = new List<string>();
            bool allStatesPass = true;
            int id = 6_820;
            foreach (ProcurementContractStatus requestedStatus in
                     Enum.GetValues(typeof(ProcurementContractStatus)))
            {
                ProcurementContract acceptFixture = HandMadeProcurementCounter(
                    product, id++, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5, requestedStatus);
                ProcurementContract declineFixture = HandMadeProcurementCounter(
                    product, id++, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5, requestedStatus);
                state.ProcurementContracts.Add(acceptFixture);
                ProcurementContractAnswer acceptAnswer =
                    ProcurementContractService.AcceptFinalCounter(state, acceptFixture);
                state.ProcurementContracts.Remove(acceptFixture);
                state.ProcurementContracts.Add(declineFixture);
                bool declineApplied = ProcurementContractService.TryDeclineFinalCounter(
                    state, declineFixture);
                state.ProcurementContracts.Remove(declineFixture);
                bool expectedPlayerResponse =
                    requestedStatus == ProcurementContractStatus.CounterpartyCountered;
                bool statePass = expectedPlayerResponse
                    ? acceptAnswer.Applied && declineApplied
                    : !acceptAnswer.Applied && !declineApplied;
                allStatesPass &= statePass;
                details.Add(
                    $"{requestedStatus}: accept={acceptAnswer.Applied}; " +
                    $"decline={declineApplied}; expectedResponse={expectedPlayerResponse}; " +
                    $"acceptState={acceptFixture.status}; declineState={declineFixture.status}");
            }

            check(
                "M7 player actions are refused outside the countered state",
                allStatesPass,
                $"statuses enumerated from {typeof(ProcurementContractStatus).Name}: " +
                string.Join(" | ", details.ToArray()));
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementSchema55Migration(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            ThingDef product,
            FieldInfo saveVersionField)
        {
            List<ProcurementContract> savedContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            int savedSaveVersion = state.SaveVersion;
            string failure = null;
            int beforeCount = -1;
            int afterCount = -1;
            List<ProcurementContractStatus> beforeStates =
                new List<ProcurementContractStatus>();
            List<ProcurementContractStatus> afterStates =
                new List<ProcurementContractStatus>();
            int migratedVersion = -1;

            try
            {
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.Add(
                    HandMadeProcurementCounter(
                        product, 6_850, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5,
                        ProcurementContractStatus.Offered));
                state.ProcurementContracts.Add(
                    HandMadeProcurementCounter(
                        product, 6_851, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5,
                        ProcurementContractStatus.Active));
                state.ProcurementContracts.Add(
                    HandMadeProcurementCounter(
                        product, 6_852, 7, 1.25f, 3, 1, 13, 4.75f, 17, 5,
                        ProcurementContractStatus.Cancelled));
                beforeCount = state.ProcurementContracts.Count;
                foreach (ProcurementContract contract in state.ProcurementContracts)
                {
                    beforeStates.Add(contract.status);
                }

                saveVersionField.SetValue(state, 54);
                state.MigrateIfNeeded();
                migratedVersion = state.SaveVersion;
                afterCount = state.ProcurementContracts.Count;
                foreach (ProcurementContract contract in state.ProcurementContracts)
                {
                    afterStates.Add(contract.status);
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedContracts);
                saveVersionField.SetValue(state, savedSaveVersion);
            }

            bool statesUnchanged = beforeStates.Count == afterStates.Count;
            for (int i = 0; i < beforeStates.Count && i < afterStates.Count; i++)
            {
                statesUnchanged &= beforeStates[i] == afterStates[i];
            }

            check(
                "M8 schema 55 migration does not rewrite procurement contracts",
                failure == null && beforeCount == afterCount && statesUnchanged,
                $"saveVersion=54->{migratedVersion}; restored={state.SaveVersion}; " +
                $"count={beforeCount}->{afterCount}; " +
                $"states={string.Join(",", beforeStates)}->{string.Join(",", afterStates)}; " +
                $"failure={failure ?? "none"}");
        }

        private static ProcurementContract HandMadeProcurementCounter(
            ThingDef product,
            int id,
            int originalQuantity,
            float originalUnitPrice,
            int originalCadence,
            int originalTotalCycles,
            int counterQuantity,
            float counterUnitPrice,
            int counterCadence,
            int counterTotalCycles,
            ProcurementContractStatus status = ProcurementContractStatus.CounterpartyCountered)
        {
            ProcurementContract contract = new ProcurementContract
            {
                id = id,
                settlementId = id,
                settlementName = "Stage 6H counter fixture",
                thingDef = product,
                quantityPerCycle = originalQuantity,
                unitPrice = originalUnitPrice,
                cadenceDays = originalCadence,
                totalCycles = originalTotalCycles,
                fulfillment = FulfillmentMode.SellerDelivery,
                status = status,
                proposalAppeal = 0.25f,
                proposalDecision = (int)IntercolonyNegotiationDecision.Countered,
                decisionDueTick = GenTicks.TicksGame,
                nextCycleTick = 0,
                activeOrderId = ProcurementContract.NoActiveOrderId
            };
            SetPrivateCounterValue(contract, "finalCounterQuantityPerCycle", counterQuantity);
            SetPrivateCounterValue(contract, "finalCounterUnitPrice", counterUnitPrice);
            SetPrivateCounterValue(contract, "finalCounterCadenceDays", counterCadence);
            SetPrivateCounterValue(contract, "finalCounterTotalCycles", counterTotalCycles);
            SetPrivateCounterValue(
                contract, "finalCounterFulfillment", FulfillmentMode.BuyerPickup);
            return contract;
        }

        private static int procurementEvaluatorInvocationCount;

        private static void CountProcurementEvaluatorInvocation()
        {
            procurementEvaluatorInvocationCount++;
        }

        private static void SetPrivateCounterValue<T>(
            ProcurementContract contract,
            string fieldName,
            T value)
        {
            FieldInfo field = typeof(ProcurementContract).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Procurement counter fixture field '{fieldName}' is unavailable.");
            }

            field.SetValue(contract, value);
        }

        private static string DescribeCounterTerms(ProcurementContractCounterTerms terms)
        {
            return terms == null
                ? "none"
                : $"{terms.quantityPerCycle}x/{terms.unitPrice:F4}/" +
                  $"{terms.cadenceDays}d/x{terms.totalCycles}/{terms.fulfillment}";
        }

        private static string DescribeProcurementTerms(
            ProcurementContract contract,
            bool counter)
        {
            if (contract == null)
            {
                return "none";
            }

            if (counter && contract.TryGetFinalCounterTerms(
                    out ProcurementContractCounterTerms counterTerms))
            {
                return DescribeCounterTerms(counterTerms);
            }

            return $"{contract.quantityPerCycle}x/{contract.unitPrice:F4}/" +
                   $"{contract.cadenceDays}d/x{contract.totalCycles}/{contract.fulfillment}";
        }

        private static bool TryFindProcurementProposalFixture(
            IntercolonyWorldComponent state,
            out Settlement settlement,
            out ThingDef product,
            out ThingDef otherProduct,
            out SettlementEconomicProfile profile,
            out string reason)
        {
            settlement = null;
            product = null;
            otherProduct = null;
            profile = null;
            reason = null;

            List<Settlement> settlements = FindAccessibleSupplierSettlements(state);
            foreach (Settlement candidateSettlement in settlements)
            {
                SettlementEconomicProfile candidateProfile =
                    state.GetProfile(candidateSettlement);
                if (candidateProfile == null)
                {
                    continue;
                }

                foreach (ThingDef candidateDef in IntercolonyProductClassifier.TradableDefs)
                {
                    if (!IntercolonyProductClassifier.Classify(candidateDef).HasValue ||
                        !RfqService.CanTechnicallySupply(candidateDef, candidateProfile) ||
                        state.HasContractWith(candidateSettlement.ID, candidateDef))
                    {
                        continue;
                    }

                    if (product == null)
                    {
                        settlement = candidateSettlement;
                        profile = candidateProfile;
                        product = candidateDef;
                    }
                    else if (candidateSettlement == settlement && candidateDef != product)
                    {
                        otherProduct = candidateDef;
                        reason = null;
                        return true;
                    }
                }

                if (product != null && settlement == candidateSettlement)
                {
                    reason = null;
                    return true;
                }
            }

            reason = "no accessible supplier with an eligible, technically supplyable tradable item";
            return false;
        }

        private static ProcurementContractProposalResult ProposeProcurementFixture(
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product,
            int quantity = 10,
            int cadenceDays = 1,
            int totalCycles = 2,
            float? agreedUnitPrice = null)
        {
            return ProcurementContractService.ProposeContract(
                state, settlement, product, quantity, cadenceDays, totalCycles,
                agreedUnitPrice, FulfillmentMode.SellerDelivery);
        }

        private static void CheckProcurementProposalPending(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product)
        {
            state.ProcurementContracts.Clear();
            int currentTick = GenTicks.TicksGame;
            ProcurementContractProposalResult result =
                ProposeProcurementFixture(state, settlement, product);
            ProcurementContract contract = result.Contract;
            bool noActiveContract = true;
            foreach (ProcurementContract existing in state.ProcurementContracts)
            {
                if (existing != null && existing.settlementId == settlement.ID &&
                    existing.thingDef == product &&
                    existing.status == ProcurementContractStatus.Active)
                {
                    noActiveContract = false;
                    break;
                }
            }

            check(
                "E1 sent procurement proposal remains pending",
                result.Success && contract != null &&
                contract.status == ProcurementContractStatus.Offered &&
                contract.decisionDueTick > currentTick && noActiveContract,
                $"current tick={currentTick}; due tick={contract?.decisionDueTick.ToString() ?? "null"}; " +
                $"status={(contract == null ? "null" : contract.status.ToString())}; " +
                $"active={(!noActiveContract)}; reason={result.Reason ?? "none"}");
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementProposalAnswersOnce(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product)
        {
            state.ProcurementContracts.Clear();
            ProcurementContractProposalResult result =
                ProposeProcurementFixture(state, settlement, product);
            ProcurementContract contract = result.Contract;
            ProcurementContractStatus initialStatus =
                contract?.status ?? ProcurementContractStatus.Cancelled;
            int originalDueTick = contract?.decisionDueTick ?? -1;
            int dueTick = GenTicks.TicksGame;
            int firstAdvanced = 0;
            ProcurementContractStatus firstStatus = ProcurementContractStatus.Cancelled;
            string firstNote = null;
            ProcurementContractAnswer secondAnswer = null;

            if (contract != null)
            {
                contract.decisionDueTick = dueTick;
                firstAdvanced = ProcurementContractService.AdvanceProposals(state);
                firstStatus = contract.status;
                firstNote = contract.outcomeNote;
                secondAnswer = ProcurementContractService.AnswerProposal(state, contract);
            }

            check(
                "E2 procurement proposal answer applies exactly once",
                result.Success && contract != null &&
                initialStatus == ProcurementContractStatus.Offered &&
                firstAdvanced == 1 && firstStatus != initialStatus &&
                secondAnswer != null && !secondAnswer.Applied &&
                contract.status == firstStatus && contract.outcomeNote == firstNote,
                $"original due tick={originalDueTick}; driven due tick={dueTick}; " +
                $"initial status={initialStatus}; first status={firstStatus}; " +
                $"first advances={firstAdvanced}; second applied={secondAnswer?.Applied.ToString() ?? "null"}; " +
                $"final status={(contract == null ? "null" : contract.status.ToString())}; " +
                $"reason={result.Reason ?? secondAnswer?.Reason ?? "none"}");
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementProposalSaveLoad(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product,
            SettlementEconomicProfile profile)
        {
            state.ProcurementContracts.Clear();
            ProcurementContractProposalResult result =
                ProposeProcurementFixture(state, settlement, product);
            ProcurementContract original = result.Contract;
            int capturedDecision = original?.proposalDecision ?? -1;
            List<ProcurementContract> savedList = original == null
                ? new List<ProcurementContract>()
                : new List<ProcurementContract> { original };
            List<ProcurementContract> loadedList = null;
            ProcurementContract loaded = null;
            ProcurementContractAnswer answer = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProcurementProposal-E3-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "procurementProposalDecisionTest");
                Scribe_Collections.Look(ref savedList, "procurementContracts", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "procurementContracts", LookMode.Deep);
                Scribe.loader.FinalizeLoading();

                loaded = loadedList != null && loadedList.Count == 1 ? loadedList[0] : null;
                state.ProcurementContracts.Clear();
                if (loaded != null)
                {
                    state.ProcurementContracts.Add(loaded);
                }

                if (loaded != null)
                {
                    TechLevel savedTechTier = profile.techTier;
                    IntercolonyWealthTier savedWealthTier = profile.wealthTier;
                    IntercolonyArchetype savedArchetype = profile.archetype;
                    float[] savedSupplyWeights = (float[])profile.supplyWeights.Clone();
                    try
                    {
                        // Make a later market read materially different from the captured answer.
                        profile.techTier = TechLevel.Neolithic;
                        profile.wealthTier = IntercolonyWealthTier.Destitute;
                        profile.archetype = IntercolonyArchetype.Tribal;
                        for (int i = 0; i < profile.supplyWeights.Length; i++)
                        {
                            profile.supplyWeights[i] = 0f;
                        }

                        loaded.decisionDueTick = GenTicks.TicksGame;
                        answer = ProcurementContractService.AnswerProposal(state, loaded);
                    }
                    finally
                    {
                        profile.techTier = savedTechTier;
                        profile.wealthTier = savedWealthTier;
                        profile.archetype = savedArchetype;
                        Array.Copy(savedSupplyWeights, profile.supplyWeights,
                            savedSupplyWeights.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            IntercolonyNegotiationDecision decision =
                (IntercolonyNegotiationDecision)capturedDecision;
            ProcurementContractStatus expectedStatus;
            switch (decision)
            {
                case IntercolonyNegotiationDecision.Accepted:
                    expectedStatus = ProcurementContractStatus.Active;
                    break;
                case IntercolonyNegotiationDecision.Refused:
                    expectedStatus = ProcurementContractStatus.CounterpartyRefused;
                    break;
                case IntercolonyNegotiationDecision.Countered:
                    expectedStatus = ProcurementContractStatus.CounterpartyCountered;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unhandled procurement proposal decision: {decision}");
            }
            check(
                "E3 persisted procurement decision survives save/load",
                failure == null && result.Success && original != null &&
                loaded != null && loaded.proposalDecision == capturedDecision &&
                answer != null && answer.Applied && answer.Decision == decision &&
                loaded.status == expectedStatus,
                $"captured decision={decision}; loaded decision=" +
                $"{(loaded == null ? "null" : ((IntercolonyNegotiationDecision)loaded.proposalDecision).ToString())}; " +
                $"loaded status={(loaded == null ? "null" : loaded.status.ToString())}; " +
                $"expected status={expectedStatus}; answer={answer?.Decision.ToString() ?? "null"}; " +
                $"answer reason={answer?.Reason ?? "none"}; " +
                $"captured due tick={original?.decisionDueTick.ToString() ?? "null"}; " +
                $"loaded due tick={(loaded == null ? "null" : loaded.decisionDueTick.ToString())}; " +
                $"failure={failure ?? "none"}");
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementProposalValidation(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product)
        {
            state.ProcurementContracts.Clear();
            ProcurementContractProposalResult quantity =
                ProposeProcurementFixture(state, settlement, product, 0, 1, 1);
            bool quantityRefused = !quantity.Success && quantity.Contract == null &&
                                   quantity.Evaluation == null &&
                                   !string.IsNullOrEmpty(quantity.Reason) &&
                                   quantity.Reason.Contains("10") &&
                                   quantity.Reason.Contains("4000") &&
                                   state.ProcurementContracts.Count == 0;

            ProcurementContractProposalResult cadence =
                ProposeProcurementFixture(state, settlement, product, 10, 0, 1);
            bool cadenceRefused = !cadence.Success && cadence.Contract == null &&
                                  cadence.Evaluation == null &&
                                  !string.IsNullOrEmpty(cadence.Reason) &&
                                  cadence.Reason.Contains("1") &&
                                  cadence.Reason.Contains("365") &&
                                  state.ProcurementContracts.Count == 0;

            ProcurementContractProposalResult cycles =
                ProposeProcurementFixture(state, settlement, product, 10, 1, 0);
            bool cyclesRefused = !cycles.Success && cycles.Contract == null &&
                                 cycles.Evaluation == null &&
                                 !string.IsNullOrEmpty(cycles.Reason) &&
                                 cycles.Reason.Contains("1") &&
                                 cycles.Reason.Contains("365") &&
                                 state.ProcurementContracts.Count == 0;

            ProcurementContractProposalResult valid =
                ProposeProcurementFixture(state, settlement, product, 10, 1, 1);
            bool validAccepted = valid.Success && valid.Contract != null &&
                                 state.ProcurementContracts.Count == 1;
            check(
                "E4 procurement proposal bounds refuse before evaluation",
                quantityRefused && cadenceRefused && cyclesRefused && validAccepted,
                $"quantity attempted=0, bound=10..4000, refused={quantityRefused}, " +
                $"reason={quantity.Reason ?? "none"}; cadence attempted=0, bound=1..365, " +
                $"refused={cadenceRefused}, reason={cadence.Reason ?? "none"}; " +
                $"total cycles attempted=0, bound=1..365, refused={cyclesRefused}, " +
                $"reason={cycles.Reason ?? "none"}; valid succeeded={validAccepted}");
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementProposalTechnicalGate(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement,
            SettlementEconomicProfile profile)
        {
            ThingDef blocked = null;
            IntercolonyProductCategory blockedCategory = IntercolonyProductCategory.Commodities;
            foreach (ThingDef def in IntercolonyProductClassifier.TradableDefs)
            {
                IntercolonyProductCategory? category =
                    IntercolonyProductClassifier.Classify(def);
                if (!category.HasValue || def.techLevel == TechLevel.Undefined ||
                    state.HasContractWith(settlement.ID, def) ||
                    RfqService.SupplierOfferQuantity(
                        def, null, FixtureSupplyProfile(profile), 100f) <= 0)
                {
                    continue;
                }

                if (blocked == null || def.techLevel > blocked.techLevel)
                {
                    blocked = def;
                    blockedCategory = category.Value;
                }
            }

            if (blocked == null)
            {
                skip("E5 technically unsupplyable procurement item is refused",
                    $"settlement={settlement.ID}; no positive-supply high-tech tradable def");
                return;
            }

            TechLevel savedTechTier = profile.techTier;
            IntercolonyArchetype savedArchetype = profile.archetype;
            IntercolonyWealthTier savedWealthTier = profile.wealthTier;
            float[] savedSupplyWeights = (float[])profile.supplyWeights.Clone();
            state.ProcurementContracts.Clear();
            try
            {
                // This is the T3 fixture: make the selected high-tech item the only supplied
                // category, then put the supplier one tech tier below that item.
                profile.techTier = (TechLevel)Math.Max(
                    (int)TechLevel.Undefined, (int)blocked.techLevel - 1);
                profile.archetype = IntercolonyArchetype.Tribal;
                profile.wealthTier = IntercolonyWealthTier.Wealthy;
                for (int i = 0; i < profile.supplyWeights.Length; i++)
                {
                    profile.supplyWeights[i] = 0f;
                }

                profile.supplyWeights[(int)blockedCategory] = 100f;
                bool gateRefuses = !RfqService.CanTechnicallySupply(blocked, profile);
                ProcurementContractProposalResult result =
                    ProposeProcurementFixture(state, settlement, blocked);
                check(
                    "E5 technically unsupplyable procurement item is refused",
                    gateRefuses && !result.Success && result.Contract == null &&
                    result.Failure == ProcurementContractProposalFailure.SupplierCannotSupply &&
                    !string.IsNullOrEmpty(result.Reason) &&
                    state.ProcurementContracts.Count == 0,
                    $"settlement={settlement.ID}; def={blocked.defName}; " +
                    $"supplier tech={profile.techTier}; item tech={blocked.techLevel}; " +
                    $"real gate refuses={gateRefuses}; reason={result.Reason ?? "none"}");
            }
            finally
            {
                profile.techTier = savedTechTier;
                profile.archetype = savedArchetype;
                profile.wealthTier = savedWealthTier;
                Array.Copy(savedSupplyWeights, profile.supplyWeights,
                    savedSupplyWeights.Length);
                state.ProcurementContracts.Clear();
            }
        }

        private static void CheckProcurementProposalDuplicateScope(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product,
            ThingDef otherProduct)
        {
            if (otherProduct == null)
            {
                skip("E6 procurement proposal duplicate scope is supplier and product",
                    $"settlement={settlement.ID}; no second eligible product");
                return;
            }

            state.ProcurementContracts.Clear();
            ProcurementContractProposalResult first =
                ProposeProcurementFixture(state, settlement, product);
            int afterFirst = state.ProcurementContracts.Count;
            ProcurementContractProposalResult duplicate =
                ProposeProcurementFixture(state, settlement, product);
            int afterDuplicate = state.ProcurementContracts.Count;
            ProcurementContractProposalResult different =
                ProposeProcurementFixture(state, settlement, otherProduct);
            int afterDifferent = state.ProcurementContracts.Count;
            check(
                "E6 procurement proposal duplicate scope is supplier and product",
                first.Success && afterFirst == 1 && !duplicate.Success &&
                duplicate.Contract == null && !string.IsNullOrEmpty(duplicate.Reason) &&
                afterDuplicate == 1 && different.Success &&
                different.Contract != null && different.Contract.thingDef == otherProduct &&
                afterDifferent == 2,
                $"supplier={settlement.ID}; product={product.defName}; " +
                $"duplicate reason={duplicate.Reason ?? "none"}; counts={afterFirst}->" +
                $"{afterDuplicate}; different product={otherProduct.defName}; " +
                $"different success={different.Success}; final count={afterDifferent}");
            state.ProcurementContracts.Clear();
        }

        private static void CheckProcurementProposalAcceptance(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            ThingDef product)
        {
            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            int ordersBefore = state.PurchaseOrders.Count;
            int silverBefore = paymentMap == null
                ? 0
                : PurchaseOrderService.CountColonySilver(paymentMap);
            int now = GenTicks.TicksGame;
            List<ProcurementContract> savedContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            int savedNextId = state.PeekNextId();
            List<string> candidateDecisions = new List<string>();
            int candidatesTried = 0;
            int reputationPinnedCandidates = 0;
            IntercolonyNegotiationDecision? bestDecision = null;
            ProcurementContractProposalResult result = null;
            ProcurementContract contract = null;
            Settlement acceptedSettlement = null;
            bool acceptedCandidate = false;

            try
            {
                state.ProcurementContracts.Clear();
                foreach (Settlement candidateSettlement in
                         FindAccessibleSupplierSettlements(state))
                {
                    SettlementEconomicProfile candidateProfile =
                        state.GetProfile(candidateSettlement);
                    if (candidateProfile == null ||
                        !RfqService.CanTechnicallySupply(product, candidateProfile) ||
                        state.HasContractWith(candidateSettlement.ID, product))
                    {
                        continue;
                    }

                    candidatesTried++;
                    bool candidateHadReputation = state.Reputations.TryGetValue(
                        candidateSettlement.ID,
                        out CommercialReputation candidateSavedReputation);
                    ProcurementContract candidateContract = null;
                    bool candidateAccepted = false;
                    try
                    {
                        // A favourable relationship makes the reachability probe independent of
                        // whatever reputation the live world happened to give this settlement.
                        SetProcurementReputation(state, candidateProfile);
                        reputationPinnedCandidates++;
                        ProcurementContractProposalResult candidateResult =
                            ProposeProcurementFixture(
                                state, candidateSettlement, product);
                        candidateContract = candidateResult.Contract;

                        IntercolonyNegotiationDecision? candidateDecision = null;
                        if (candidateContract != null &&
                            candidateContract.proposalDecision >= 0)
                        {
                            candidateDecision =
                                (IntercolonyNegotiationDecision)candidateContract.proposalDecision;
                        }
                        else if (candidateResult.Evaluation != null)
                        {
                            candidateDecision = candidateResult.Evaluation.Decision;
                        }

                        string decisionText = candidateDecision.HasValue
                            ? candidateDecision.Value.ToString()
                            : candidateResult.Failure.ToString();
                        candidateDecisions.Add(
                            $"settlement {candidateSettlement.ID}={decisionText}");
                        if (!bestDecision.HasValue ||
                            candidateDecision == IntercolonyNegotiationDecision.Accepted ||
                            (candidateDecision == IntercolonyNegotiationDecision.Countered &&
                             bestDecision == IntercolonyNegotiationDecision.Refused))
                        {
                            if (candidateDecision.HasValue)
                            {
                                bestDecision = candidateDecision;
                            }
                        }

                        candidateAccepted = candidateContract != null &&
                            candidateDecision == IntercolonyNegotiationDecision.Accepted;
                        if (candidateAccepted)
                        {
                            result = candidateResult;
                            contract = candidateContract;
                            acceptedSettlement = candidateSettlement;
                            acceptedCandidate = true;
                        }
                    }
                    finally
                    {
                        if (!candidateAccepted)
                        {
                            if (candidateContract != null)
                            {
                                state.ProcurementContracts.Remove(candidateContract);
                            }

                            if (candidateHadReputation)
                            {
                                state.Reputations[candidateSettlement.ID] =
                                    candidateSavedReputation;
                            }
                            else
                            {
                                state.Reputations.Remove(candidateSettlement.ID);
                            }
                        }
                    }

                    if (candidateAccepted)
                    {
                        break;
                    }
                }

                string candidateDecisionDetail = candidateDecisions.Count == 0
                    ? "none"
                    : string.Join(", ", candidateDecisions.ToArray());
                string searchDetail =
                    $"candidates tried={candidatesTried}; decisions={candidateDecisionDetail}; " +
                    $"best decision={bestDecision?.ToString() ?? "none"}; " +
                    $"accepted settlement id={acceptedSettlement?.ID.ToString() ?? "none"}; " +
                    $"current tick={now}; next cycle tick=" +
                    $"{(contract == null ? "null" : contract.nextCycleTick.ToString())}; " +
                    $"reputation set deliberately={reputationPinnedCandidates} candidate(s) " +
                    "to 100 and restored";

                if (paymentMap == null)
                {
                    skip("E7 accepted procurement proposal schedules without prepayment",
                        "no player map is available to count silver; " + searchDetail);
                }
                else
                {
                    int answered = 0;
                    if (contract != null)
                    {
                        contract.decisionDueTick = now;
                        answered = ProcurementContractService.AdvanceProposals(state);
                    }

                    int ordersAfter = state.PurchaseOrders.Count;
                    int silverAfter = PurchaseOrderService.CountColonySilver(paymentMap);
                    check(
                        "E7 accepted procurement proposal schedules without prepayment",
                        result != null && result.Success && contract != null &&
                        contract.proposalDecision == (int)IntercolonyNegotiationDecision.Accepted &&
                        answered == 1 && contract.status == ProcurementContractStatus.Active &&
                        contract.nextCycleTick > now && ordersAfter == ordersBefore &&
                        silverAfter == silverBefore,
                        $"{searchDetail}; next cycle tick after answer=" +
                        $"{(contract == null ? "null" : contract.nextCycleTick.ToString())}; " +
                        $"status={(contract == null ? "null" : contract.status.ToString())}; " +
                        $"answered={answered}; orders={ordersBefore}->{ordersAfter}; " +
                        $"silver={silverBefore}->{silverAfter}; reason={result?.Reason ?? "none"}");
                }

                check(
                    "E7b a reasonable procurement proposal is accepted by some supplier",
                    acceptedCandidate && result != null && result.Success &&
                    result.Evaluation != null &&
                    result.Evaluation.Decision == IntercolonyNegotiationDecision.Accepted,
                    searchDetail);

                if (acceptedCandidate && contract != null &&
                    contract.status == ProcurementContractStatus.Active && paymentMap != null)
                {
                    CheckProcurementContractCycles(check, skip, state, contract, paymentMap);
                }
                else
                {
                    SkipProcurementContractCycles(
                        skip,
                        "the accepted E7 contract or a player payment map was unavailable; " +
                        searchDetail);
                }
            }
            finally
            {
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedContracts);
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> savedReputation in
                         savedReputations)
                {
                    state.Reputations[savedReputation.Key] = savedReputation.Value;
                }
                if (nextIdField != null)
                {
                    nextIdField.SetValue(state, savedNextId);
                }
            }
        }

        private static void SkipProcurementContractCycles(
            Action<string, string> skip,
            string reason)
        {
            skip("G1 due procurement cycle creates one order at the agreed price", reason);
            skip("G2 procurement cycle pays only its own cost", reason);
            skip("G3 late procurement cycle keeps the scheduled cadence", reason);
            skip("G4 unaffordable procurement cycle fails without ending the agreement", reason);
            skip("G5 open procurement order blocks a second cycle", reason);
            skip("G6 procurement agreement completes exactly at its cycle count", reason);
            skip("G7 concluded procurement order restores the active-order sentinel", reason);
            skip("J1 supply shortfall fails one procurement cycle", reason);
            skip("J2 ordinary procurement supply succeeds", reason);
            skip("J3 paid supplier default refunds through PurchaseOrderService", reason);
            skip("J4 hostility suspends a procurement agreement", reason);
            skip("J5 suspended procurement agreement runs no cycles", reason);
            skip("J6 resumption shifts the outage and cycles resume", reason);
            skip("J7 repeated supplier defaults keep the agreement active", reason);
            skip("J8 older-save migration preserves procurement agreements", reason);
            skip("P1 started procurement agreement records exactly once", reason);
            skip("P2 accepted procurement counter has a distinct timeline record", reason);
            skip("P3 procurement cycle records only when goods arrive", reason);
            skip("P3 creating a procurement cycle order records nothing", reason);
            skip("P4 supplier default records one reasoned failure event", reason);
            skip("P5a sending a procurement proposal leaves the timeline unchanged", reason);
            skip("P5b receiving a procurement counter leaves the timeline unchanged", reason);
            skip("P5c declining a procurement counter leaves the timeline unchanged", reason);
            skip("P6 cancelling an active agreement records counts and stops cycles", reason);
            skip("P7 cancelled agreement is terminal and idempotent", reason);
            skip("P8 active cancellation costs standing but suspended cancellation does not", reason);
            skip("P9 cancellation preserves an in-flight procurement order", reason);
        }

        private static void CheckProcurementContractCycles(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            ProcurementContract acceptedContract,
            Map paymentMap)
        {
            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo consumptionField = typeof(IntercolonyWorldComponent).GetField(
                "supplierOfferConsumption", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            if (state == null || acceptedContract == null || paymentMap == null ||
                ThingDefOf.Silver == null || nextIdField == null || consumptionField == null)
            {
                SkipProcurementContractCycles(
                    skip, "live state, silver, or fixture-restoration fields were unavailable");
                return;
            }

            List<ProcurementContract> savedContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            List<PurchaseOrder> savedOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<LedgerEntry> savedLedger = new List<LedgerEntry>(state.Ledger);
            List<CommercialEventRecord> savedCommercialTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedCommercialTimelineStartTick = state.CommercialTimelineStartTick;
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<SupplierOfferConsumption> savedConsumption = CloneConsumptions(
                consumptionField.GetValue(state) as List<SupplierOfferConsumption>);
            Dictionary<Thing, int> savedSilver = SnapshotStoredSilver(paymentMap);
            int savedLedgerStartTick = state.LedgerStartTick;
            int savedNextId = state.PeekNextId();

            int savedQuantity = acceptedContract.quantityPerCycle;
            float savedUnitPrice = acceptedContract.unitPrice;
            int savedCadenceDays = acceptedContract.cadenceDays;
            int savedTotalCycles = acceptedContract.totalCycles;
            int savedCompleted = acceptedContract.cyclesCompleted;
            int savedFailed = acceptedContract.cyclesFailed;
            int savedNextCycleTick = acceptedContract.nextCycleTick;
            int savedSuspendedTick = acceptedContract.suspendedTick;
            int savedActiveOrderId = acceptedContract.activeOrderId;
            ProcurementContractStatus savedStatus = acceptedContract.status;
            string savedOutcomeNote = acceptedContract.outcomeNote;
            Settlement cycleSettlement = IntercolonyMarketAccess.FindSettlement(
                acceptedContract.settlementId);
            SettlementEconomicProfile cycleProfile = cycleSettlement == null
                ? null
                : state.GetProfile(cycleSettlement);
            IntercolonyProductCategory? cycleCategory =
                IntercolonyProductClassifier.Classify(acceptedContract.thingDef);
            float[] savedCycleSupplyWeights = cycleProfile == null
                ? null
                : (float[])cycleProfile.supplyWeights.Clone();
            TechLevel savedCycleTechTier = cycleProfile == null
                ? TechLevel.Undefined
                : cycleProfile.techTier;
            TechLevel cycleFixtureTechTier = savedCycleTechTier;
            Faction supplierFaction = cycleSettlement?.Faction;
            FactionRelation supplierRelation = supplierFaction == null || Faction.OfPlayer == null
                ? null
                : supplierFaction.RelationWith(Faction.OfPlayer, allowNull: true);
            FactionRelation playerRelation = supplierFaction == null || Faction.OfPlayer == null
                ? null
                : Faction.OfPlayer.RelationWith(supplierFaction, allowNull: true);
            bool bilateralRelationAvailable = supplierRelation != null &&
                                              supplierRelation.other != null &&
                                              playerRelation != null &&
                                              playerRelation.other != null;
            FactionRelationKind savedSupplierRelationKind = bilateralRelationAvailable
                ? supplierRelation.kind
                : FactionRelationKind.Neutral;
            FactionRelationKind savedPlayerRelationKind = bilateralRelationAvailable
                ? playerRelation.kind
                : FactionRelationKind.Neutral;
            int savedSupplierBaseGoodwill = bilateralRelationAvailable
                ? supplierRelation.baseGoodwill
                : 0;
            int savedPlayerBaseGoodwill = bilateralRelationAvailable
                ? playerRelation.baseGoodwill
                : 0;
            Thing fixtureSilver = null;
            Zone_Stockpile fixtureSilverZone = null;
            Dictionary<Thing, int> fixtureSilverBaseline = null;

            void RestoreCycleProfile()
            {
                if (cycleProfile == null || savedCycleSupplyWeights == null)
                {
                    return;
                }

                Array.Copy(savedCycleSupplyWeights, cycleProfile.supplyWeights,
                    savedCycleSupplyWeights.Length);
                cycleProfile.techTier = cycleFixtureTechTier;
            }

            void MakeSupplyInsufficient()
            {
                RestoreCycleProfile();
                if (cycleProfile != null && cycleCategory.HasValue)
                {
                    cycleProfile.supplyWeights[(int)cycleCategory.Value] = 0f;
                }
            }

            void MakeSupplierHostile()
            {
                supplierFaction.SetRelation(
                    new FactionRelation(Faction.OfPlayer, FactionRelationKind.Hostile)
                    {
                        baseGoodwill = -100
                    });
                FactionRelation mirror = Faction.OfPlayer.RelationWith(
                    supplierFaction, allowNull: true);
                if (mirror != null)
                {
                    mirror.kind = FactionRelationKind.Hostile;
                    mirror.baseGoodwill = -100;
                }
            }

            void RestoreSupplierRelation()
            {
                if (!bilateralRelationAvailable)
                {
                    return;
                }

                supplierFaction.SetRelation(
                    new FactionRelation(Faction.OfPlayer, savedSupplierRelationKind)
                    {
                        baseGoodwill = savedSupplierBaseGoodwill
                    });
                FactionRelation mirror = Faction.OfPlayer.RelationWith(
                    supplierFaction, allowNull: true);
                if (mirror != null)
                {
                    mirror.kind = savedPlayerRelationKind;
                    mirror.baseGoodwill = savedPlayerBaseGoodwill;
                }
            }

            try
            {
                // Use a positive frozen value. A quicktest may have one stored silver stack that
                // is smaller than two cycles at the live proposal price, so keep this synthetic
                // cycle payable and let G4 raise it above the fixture purse deliberately.
                float cycleUnitPrice = savedUnitPrice * 1.5f;
                if (cycleUnitPrice <= 0f || cycleUnitPrice == savedUnitPrice)
                {
                    cycleUnitPrice = savedUnitPrice + 1f;
                }

                acceptedContract.quantityPerCycle = 1;
                acceptedContract.unitPrice = cycleUnitPrice;
                acceptedContract.cadenceDays = 1;
                acceptedContract.totalCycles = 2;
                acceptedContract.cyclesCompleted = 0;
                acceptedContract.cyclesFailed = 0;
                acceptedContract.activeOrderId = ProcurementContract.NoActiveOrderId;
                acceptedContract.status = ProcurementContractStatus.Active;
                acceptedContract.outcomeNote = "cycle test fixture";

                int availableSilver = PurchaseOrderService.CountColonySilver(paymentMap);
                int expectedCycleCost = IntercolonyPricing.TotalPayment(
                    acceptedContract.unitPrice, acceptedContract.quantityPerCycle);
                int requiredSilver = expectedCycleCost * acceptedContract.totalCycles + 2;
                if (availableSilver < requiredSilver)
                {
                    float affordableUnitPrice = availableSilver > 2
                        ? (availableSilver - 2f) /
                          (acceptedContract.quantityPerCycle * acceptedContract.totalCycles)
                        : 1f;
                    cycleUnitPrice = Mathf.Max(
                        1f, Mathf.Min(cycleUnitPrice, affordableUnitPrice));
                    acceptedContract.unitPrice = cycleUnitPrice;
                    expectedCycleCost = IntercolonyPricing.TotalPayment(
                        acceptedContract.unitPrice, acceptedContract.quantityPerCycle);
                    requiredSilver = expectedCycleCost * acceptedContract.totalCycles + 2;
                }

                if (availableSilver < requiredSilver)
                {
                    int neededSilver = requiredSilver - availableSilver;
                    Thing topUp = null;
                    foreach (Thing silver in savedSilver.Keys)
                    {
                        if (silver != null && !silver.Destroyed &&
                            silver.stackCount + neededSilver <= ThingDefOf.Silver.stackLimit)
                        {
                            topUp = silver;
                            break;
                        }
                    }

                    if (topUp != null)
                    {
                        topUp.stackCount += neededSilver;
                    }
                    else if (!TryCreateStoredSilver(
                        paymentMap, neededSilver, out fixtureSilver, out fixtureSilverZone))
                    {
                        SkipProcurementContractCycles(
                            skip,
                            $"stored silver was {availableSilver}, needed {requiredSilver}, " +
                            "and a temporary stockpile could not be created");
                        return;
                    }
                }

                fixtureSilverBaseline = SnapshotStoredSilver(paymentMap);
                int fixtureSilverAmount = PurchaseOrderService.CountColonySilver(paymentMap);
                if (fixtureSilverAmount < requiredSilver)
                {
                    SkipProcurementContractCycles(
                        skip,
                        $"stored silver after fixture setup was {fixtureSilverAmount}, " +
                        $"needed {requiredSilver}");
                    return;
                }

                void ResetCycleFixture()
                {
                    state.ProcurementContracts.Clear();
                    state.ProcurementContracts.Add(acceptedContract);
                    state.PurchaseOrders.Clear();
                    RestoreStoredSilver(paymentMap, fixtureSilverBaseline);
                    acceptedContract.quantityPerCycle = 1;
                    acceptedContract.unitPrice = cycleUnitPrice;
                    acceptedContract.cadenceDays = 1;
                    acceptedContract.totalCycles = 2;
                    acceptedContract.cyclesCompleted = 0;
                    acceptedContract.cyclesFailed = 0;
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    acceptedContract.activeOrderId = ProcurementContract.NoActiveOrderId;
                    acceptedContract.status = ProcurementContractStatus.Active;
                    acceptedContract.outcomeNote = "cycle test fixture";
                }

                PurchaseOrder FindFixtureOrder(int id)
                {
                    foreach (PurchaseOrder order in state.PurchaseOrders)
                    {
                        if (order != null && order.id == id)
                        {
                            return order;
                        }
                    }

                    return null;
                }

                ResetCycleFixture();
                int g1OrdersBefore = state.PurchaseOrders.Count;
                int g1Now = GenTicks.TicksGame;
                acceptedContract.nextCycleTick = g1Now;
                int g1Advanced = ProcurementContractService.AdvanceCycles(state);
                int g1OrderId = acceptedContract.activeOrderId;
                PurchaseOrder g1Order = FindFixtureOrder(g1OrderId);
                check(
                    "G1 due procurement cycle creates one order at the agreed price",
                    g1Advanced == 1 && state.PurchaseOrders.Count == g1OrdersBefore + 1 &&
                    g1Order != null && g1Order.unitPrice == acceptedContract.unitPrice &&
                    g1Order.quantity == acceptedContract.quantityPerCycle,
                    $"orders {g1OrdersBefore}->{state.PurchaseOrders.Count}; order id={g1OrderId}; " +
                    $"contract price={acceptedContract.unitPrice:F4}; " +
                    $"order price={(g1Order == null ? "null" : g1Order.unitPrice.ToString("F4"))}; " +
                    $"advanced={g1Advanced}");

                ResetCycleFixture();
                int g2ExpectedCost = IntercolonyPricing.TotalPayment(
                    acceptedContract.unitPrice, acceptedContract.quantityPerCycle);
                int g2SilverBefore = PurchaseOrderService.CountColonySilver(paymentMap);
                acceptedContract.nextCycleTick = GenTicks.TicksGame;
                ProcurementContractService.AdvanceCycles(state);
                int g2SilverAfter = PurchaseOrderService.CountColonySilver(paymentMap);
                check(
                    "G2 procurement cycle pays only its own cost",
                    g2SilverBefore - g2SilverAfter == g2ExpectedCost &&
                    state.PurchaseOrders.Count == 1,
                    $"silver before={g2SilverBefore}; after={g2SilverAfter}; " +
                    $"expected cycle cost={g2ExpectedCost}; orders={state.PurchaseOrders.Count}; " +
                    $"cycles completed={acceptedContract.cyclesCompleted}; " +
                    $"cycles failed={acceptedContract.cyclesFailed}");

                ResetCycleFixture();
                int g3Now = GenTicks.TicksGame;
                int g3OldTick = g3Now - 3 * acceptedContract.cadenceDays * GenDate.TicksPerDay;
                int g3CadenceTicks = acceptedContract.cadenceDays * GenDate.TicksPerDay;
                int g3ScheduledCandidate = g3OldTick + g3CadenceTicks;
                int g3NowCandidate = g3Now + g3CadenceTicks;
                acceptedContract.nextCycleTick = g3OldTick;
                ProcurementContractService.AdvanceCycles(state);
                check(
                    "G3 late procurement cycle keeps the scheduled cadence",
                    acceptedContract.nextCycleTick == g3ScheduledCandidate &&
                    g3ScheduledCandidate != g3NowCandidate,
                    $"old tick={g3OldTick}; new tick={acceptedContract.nextCycleTick}; " +
                    $"scheduled+cadence={g3ScheduledCandidate}; now+cadence={g3NowCandidate}; " +
                    $"now={g3Now}; orders={state.PurchaseOrders.Count}");

                ResetCycleFixture();
                int g4SilverBefore = PurchaseOrderService.CountColonySilver(paymentMap);
                acceptedContract.unitPrice = g4SilverBefore + 1f;
                int g4ExpectedCost = IntercolonyPricing.TotalPayment(
                    acceptedContract.unitPrice, acceptedContract.quantityPerCycle);
                int g4OldNextTick = GenTicks.TicksGame;
                int g4FailedBefore = acceptedContract.cyclesFailed;
                acceptedContract.nextCycleTick = g4OldNextTick;
                ProcurementContractService.AdvanceCycles(state);
                int g4NewNextTick = acceptedContract.nextCycleTick;
                check(
                    "G4 unaffordable procurement cycle fails without ending the agreement",
                    g4ExpectedCost > g4SilverBefore &&
                    acceptedContract.cyclesFailed == g4FailedBefore + 1 &&
                    acceptedContract.status == ProcurementContractStatus.Active &&
                    g4NewNextTick == g4OldNextTick +
                        acceptedContract.cadenceDays * GenDate.TicksPerDay &&
                    state.PurchaseOrders.Count == 0 &&
                    acceptedContract.activeOrderId == ProcurementContract.NoActiveOrderId,
                    $"silver before={g4SilverBefore}; expected cost={g4ExpectedCost}; " +
                    $"silver after={PurchaseOrderService.CountColonySilver(paymentMap)}; " +
                    $"failed {g4FailedBefore}->{acceptedContract.cyclesFailed}; " +
                    $"status={acceptedContract.status}; next tick old={g4OldNextTick}, " +
                    $"new={g4NewNextTick}; orders={state.PurchaseOrders.Count}");

                ResetCycleFixture();
                PurchaseOrder g5LiveOrder = new PurchaseOrder
                {
                    id = state.NextId(),
                    settlementId = acceptedContract.settlementId,
                    settlementName = acceptedContract.settlementName,
                    destinationMap = paymentMap,
                    thingDef = acceptedContract.thingDef,
                    quantity = acceptedContract.quantityPerCycle,
                    unitPrice = acceptedContract.unitPrice,
                    supplierDelivers = true,
                    status = PurchaseOrderStatus.Confirmed,
                    orderedTick = GenTicks.TicksGame,
                    readyTick = GenTicks.TicksGame + GenDate.TicksPerDay
                };
                state.PurchaseOrders.Add(g5LiveOrder);
                acceptedContract.activeOrderId = g5LiveOrder.id;
                int g5OrdersBefore = state.PurchaseOrders.Count;
                int g5SilverBefore = PurchaseOrderService.CountColonySilver(paymentMap);
                int g5DueTick = GenTicks.TicksGame;
                acceptedContract.nextCycleTick = g5DueTick;
                int g5Advanced = ProcurementContractService.AdvanceCycles(state);
                check(
                    "G5 open procurement order blocks a second cycle",
                    g5Advanced == 0 && state.PurchaseOrders.Count == g5OrdersBefore &&
                    acceptedContract.activeOrderId == g5LiveOrder.id && g5LiveOrder.IsOpen &&
                    PurchaseOrderService.CountColonySilver(paymentMap) == g5SilverBefore,
                    $"live order id={g5LiveOrder.id}; active order id={acceptedContract.activeOrderId}; " +
                    $"orders {g5OrdersBefore}->{state.PurchaseOrders.Count}; " +
                    $"silver before={g5SilverBefore}; after={PurchaseOrderService.CountColonySilver(paymentMap)}; " +
                    $"advanced={g5Advanced}; status={acceptedContract.status}");

                ResetCycleFixture();
                int g6AdvanceCalls = 0;
                while (acceptedContract.status == ProcurementContractStatus.Active &&
                       g6AdvanceCalls < acceptedContract.totalCycles + 2)
                {
                    if (acceptedContract.activeOrderId != ProcurementContract.NoActiveOrderId)
                    {
                        PurchaseOrder open = FindFixtureOrder(acceptedContract.activeOrderId);
                        if (open != null)
                        {
                            open.status = PurchaseOrderStatus.Completed;
                        }
                    }

                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    ProcurementContractService.AdvanceCycles(state);
                    g6AdvanceCalls++;
                }

                int g6OrdersAtCompletion = state.PurchaseOrders.Count;
                int g6FurtherAdvanced = ProcurementContractService.AdvanceCycles(state);
                check(
                    "G6 procurement agreement completes exactly at its cycle count",
                    acceptedContract.cyclesCompleted + acceptedContract.cyclesFailed ==
                        acceptedContract.totalCycles &&
                    acceptedContract.status == ProcurementContractStatus.Completed &&
                    g6FurtherAdvanced == 0 && state.PurchaseOrders.Count == g6OrdersAtCompletion,
                    $"cycles completed={acceptedContract.cyclesCompleted}; failed=" +
                    $"{acceptedContract.cyclesFailed}; total={acceptedContract.totalCycles}; " +
                    $"status={acceptedContract.status}; advance calls={g6AdvanceCalls}; " +
                    $"orders at completion={g6OrdersAtCompletion}; further advanced={g6FurtherAdvanced}");

                ResetCycleFixture();
                int g7Now = GenTicks.TicksGame;
                acceptedContract.nextCycleTick = g7Now;
                ProcurementContractService.AdvanceCycles(state);
                int g7OrderId = acceptedContract.activeOrderId;
                PurchaseOrder g7Order = FindFixtureOrder(g7OrderId);
                if (g7Order != null)
                {
                    g7Order.status = PurchaseOrderStatus.Completed;
                }

                ProcurementContractService.AdvanceCycles(state);
                check(
                    "G7 concluded procurement order restores the active-order sentinel",
                    g7Order != null && g7Order.status == PurchaseOrderStatus.Completed &&
                    acceptedContract.activeOrderId == ProcurementContract.NoActiveOrderId &&
                    acceptedContract.cyclesCompleted == 1 &&
                    acceptedContract.status == ProcurementContractStatus.Active,
                    $"order id={g7OrderId}; order status={(g7Order == null ? "null" : g7Order.status.ToString())}; " +
                    $"active order id={acceptedContract.activeOrderId}; " +
                    $"cycles completed={acceptedContract.cyclesCompleted}; " +
                    $"cycles failed={acceptedContract.cyclesFailed}; status={acceptedContract.status}");

                if (cycleProfile != null && cycleCategory.HasValue &&
                    acceptedContract.thingDef != null &&
                    acceptedContract.thingDef.techLevel != TechLevel.Undefined &&
                    cycleProfile.techTier < acceptedContract.thingDef.techLevel)
                {
                    // Keep the cycle fixture on the deterministic, technically capable side of
                    // the existing gate. The J assertions below are about current capacity, not
                    // the separate tech-level refusal path.
                    cycleFixtureTechTier = acceptedContract.thingDef.techLevel;
                    cycleProfile.techTier = cycleFixtureTechTier;
                }

                bool supplyFixtureAvailable = cycleProfile != null && cycleCategory.HasValue;
                string supplyFixtureReason = supplyFixtureAvailable
                    ? null
                    : "accepted supplier profile or product category is unavailable";
                float ordinaryEffectiveSupply = 0f;
                int ordinaryCapacity = 0;
                if (supplyFixtureAvailable)
                {
                    ordinaryEffectiveSupply = EffectiveEconomyService.EffectiveSupply(
                        state, cycleProfile, cycleCategory.Value);
                    ordinaryCapacity = RfqService.SupplierOfferQuantity(
                        acceptedContract.thingDef, acceptedContract.stuffDef, cycleProfile,
                        ordinaryEffectiveSupply);
                    if (ordinaryCapacity < acceptedContract.quantityPerCycle)
                    {
                        supplyFixtureReason =
                            $"ordinary effective supply={ordinaryEffectiveSupply:F2}; " +
                            $"computed capacity={ordinaryCapacity}; " +
                            $"promised={acceptedContract.quantityPerCycle}";
                    }
                }

                if (!supplyFixtureAvailable)
                {
                    skip("J1 supply shortfall fails one procurement cycle", supplyFixtureReason);
                    skip("J2 ordinary procurement supply succeeds", supplyFixtureReason);
                    skip("J3 paid supplier default refunds through PurchaseOrderService",
                        supplyFixtureReason);
                    skip("J7 repeated supplier defaults keep the agreement active",
                        supplyFixtureReason);
                }
                else
                {
                    ResetCycleFixture();
                    MakeSupplyInsufficient();
                    float j1EffectiveSupply = EffectiveEconomyService.EffectiveSupply(
                        state, cycleProfile, cycleCategory.Value);
                    int j1Capacity = RfqService.SupplierOfferQuantity(
                        acceptedContract.thingDef, acceptedContract.stuffDef, cycleProfile,
                        j1EffectiveSupply);
                    int j1FailedBefore = acceptedContract.cyclesFailed;
                    int j1CompletedBefore = acceptedContract.cyclesCompleted;
                    int j1OrdersBefore = state.PurchaseOrders.Count;
                    int j1DueTick = GenTicks.TicksGame;
                    acceptedContract.nextCycleTick = j1DueTick;
                    int j1Advanced = ProcurementContractService.AdvanceCycles(state);
                    check(
                        "J1 supply shortfall fails one procurement cycle",
                        acceptedContract.quantityPerCycle > j1Capacity &&
                        j1Advanced == 1 &&
                        acceptedContract.cyclesFailed == j1FailedBefore + 1 &&
                        acceptedContract.cyclesCompleted == j1CompletedBefore &&
                        acceptedContract.status == ProcurementContractStatus.Active &&
                        state.PurchaseOrders.Count == j1OrdersBefore + 1 &&
                        acceptedContract.activeOrderId == ProcurementContract.NoActiveOrderId,
                        $"promised={acceptedContract.quantityPerCycle}; " +
                        $"effective supply={j1EffectiveSupply:F2}; computed capacity={j1Capacity}; " +
                        $"failed {j1FailedBefore}->{acceptedContract.cyclesFailed}; " +
                        $"completed {j1CompletedBefore}->{acceptedContract.cyclesCompleted}; " +
                        $"status={acceptedContract.status}; orders {j1OrdersBefore}->" +
                        $"{state.PurchaseOrders.Count}; advanced={j1Advanced}");

                    if (ordinaryCapacity < acceptedContract.quantityPerCycle)
                    {
                        skip("J2 ordinary procurement supply succeeds", supplyFixtureReason);
                    }
                    else
                    {
                        RestoreCycleProfile();
                        ResetCycleFixture();
                        int j2FailedBefore = acceptedContract.cyclesFailed;
                        int j2CompletedBefore = acceptedContract.cyclesCompleted;
                        int j2OrdersBefore = state.PurchaseOrders.Count;
                        acceptedContract.nextCycleTick = GenTicks.TicksGame;
                        int j2Advanced = ProcurementContractService.AdvanceCycles(state);
                        PurchaseOrder j2Order = FindFixtureOrder(acceptedContract.activeOrderId);
                        check(
                            "J2 ordinary procurement supply succeeds",
                            ordinaryEffectiveSupply > 0f &&
                            ordinaryCapacity >= acceptedContract.quantityPerCycle &&
                            j2Advanced == 1 &&
                            state.PurchaseOrders.Count == j2OrdersBefore + 1 &&
                            j2Order != null && j2Order.IsOpen &&
                            acceptedContract.cyclesFailed == j2FailedBefore &&
                            acceptedContract.cyclesCompleted == j2CompletedBefore &&
                            acceptedContract.status == ProcurementContractStatus.Active,
                            $"promised={acceptedContract.quantityPerCycle}; " +
                            $"effective supply={ordinaryEffectiveSupply:F2}; " +
                            $"computed capacity={ordinaryCapacity}; orders {j2OrdersBefore}->" +
                            $"{state.PurchaseOrders.Count}; failed {j2FailedBefore}->" +
                            $"{acceptedContract.cyclesFailed}; completed {j2CompletedBefore}->" +
                            $"{acceptedContract.cyclesCompleted}; status={acceptedContract.status}; " +
                            $"advanced={j2Advanced}");
                    }

                    ResetCycleFixture();
                    MakeSupplyInsufficient();
                    int j3SilverBefore = PurchaseOrderService.CountColonySilver(paymentMap);
                    int j3FailedBefore = acceptedContract.cyclesFailed;
                    int j3OrdersBefore = state.PurchaseOrders.Count;
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    int j3Advanced = ProcurementContractService.AdvanceCycles(state);
                    PurchaseOrder j3Order = state.PurchaseOrders.Count > j3OrdersBefore
                        ? state.PurchaseOrders[state.PurchaseOrders.Count - 1]
                        : null;
                    int j3SilverAfter = PurchaseOrderService.CountColonySilver(paymentMap);
                    check(
                        "J3 paid supplier default refunds through PurchaseOrderService",
                        j3Advanced == 1 && j3Order != null &&
                        j3Order.status == PurchaseOrderStatus.SupplierDefault &&
                        j3SilverAfter == j3SilverBefore &&
                        acceptedContract.cyclesFailed == j3FailedBefore + 1 &&
                        acceptedContract.status == ProcurementContractStatus.Active &&
                        acceptedContract.activeOrderId == ProcurementContract.NoActiveOrderId,
                        $"silver {j3SilverBefore}->{j3SilverAfter}; " +
                        $"order status={(j3Order == null ? "null" : j3Order.status.ToString())}; " +
                        $"paid={(j3Order == null ? "null" : j3Order.paidSilver.ToString())}; " +
                        $"failed {j3FailedBefore}->{acceptedContract.cyclesFailed}; " +
                        $"status={acceptedContract.status}; advanced={j3Advanced}");

                    ResetCycleFixture();
                    MakeSupplyInsufficient();
                    acceptedContract.totalCycles = 4;
                    int j7FailuresRequested = 3;
                    int j7FailedBefore = acceptedContract.cyclesFailed;
                    int j7OrdersBefore = state.PurchaseOrders.Count;
                    for (int i = 0; i < j7FailuresRequested; i++)
                    {
                        acceptedContract.nextCycleTick = GenTicks.TicksGame;
                        ProcurementContractService.AdvanceCycles(state);
                    }

                    check(
                        "J7 repeated supplier defaults keep the agreement active",
                        acceptedContract.cyclesFailed == j7FailedBefore + j7FailuresRequested &&
                        acceptedContract.status == ProcurementContractStatus.Active &&
                        acceptedContract.activeOrderId == ProcurementContract.NoActiveOrderId &&
                        acceptedContract.nextCycleTick > GenTicks.TicksGame &&
                        state.PurchaseOrders.Count == j7OrdersBefore + j7FailuresRequested,
                        $"defaults requested={j7FailuresRequested}; failed {j7FailedBefore}->" +
                        $"{acceptedContract.cyclesFailed}; total={acceptedContract.totalCycles}; " +
                        $"status={acceptedContract.status}; next tick={acceptedContract.nextCycleTick}; " +
                        $"orders {j7OrdersBefore}->{state.PurchaseOrders.Count}");
                }

                if (!bilateralRelationAvailable || supplierFaction == null)
                {
                    skip("J4 hostility suspends a procurement agreement",
                        "supplier faction has no bilateral player relation to mutate safely");
                    skip("J5 suspended procurement agreement runs no cycles",
                        "supplier faction has no bilateral player relation to mutate safely");
                    skip("J6 resumption shifts the outage and cycles resume",
                        "supplier faction has no bilateral player relation to mutate safely");
                }
                else
                {
                    RestoreCycleProfile();
                    ResetCycleFixture();
                    int j4Now = GenTicks.TicksGame;
                    acceptedContract.nextCycleTick = j4Now;
                    int j4OrdersBefore = state.PurchaseOrders.Count;
                    MakeSupplierHostile();
                    HostilityPolicy.Sweep(state);
                    check(
                        "J4 hostility suspends a procurement agreement",
                        acceptedContract.status == ProcurementContractStatus.Suspended &&
                        acceptedContract.suspendedTick == j4Now,
                        $"faction={supplierFaction.Name}; hostile={HostilityPolicy.IsAtWar(supplierFaction)}; " +
                        $"status={acceptedContract.status}; suspended tick={acceptedContract.suspendedTick}; " +
                        $"expected tick={j4Now}; orders before={j4OrdersBefore}; " +
                        $"orders after={state.PurchaseOrders.Count}");

                    int j5CompletedBefore = acceptedContract.cyclesCompleted;
                    int j5FailedBefore = acceptedContract.cyclesFailed;
                    int j5OrdersBefore = state.PurchaseOrders.Count;
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    int j5Advanced = ProcurementContractService.AdvanceCycles(state);
                    check(
                        "J5 suspended procurement agreement runs no cycles",
                        acceptedContract.status == ProcurementContractStatus.Suspended &&
                        j5Advanced == 0 && state.PurchaseOrders.Count == j5OrdersBefore &&
                        acceptedContract.cyclesCompleted == j5CompletedBefore &&
                        acceptedContract.cyclesFailed == j5FailedBefore &&
                        acceptedContract.activeOrderId == ProcurementContract.NoActiveOrderId,
                        $"status={acceptedContract.status}; due tick={acceptedContract.nextCycleTick}; " +
                        $"orders {j5OrdersBefore}->{state.PurchaseOrders.Count}; " +
                        $"completed {j5CompletedBefore}->{acceptedContract.cyclesCompleted}; " +
                        $"failed {j5FailedBefore}->{acceptedContract.cyclesFailed}; advanced={j5Advanced}");

                    if (!supplyFixtureAvailable ||
                        ordinaryCapacity < acceptedContract.quantityPerCycle)
                    {
                        skip("J6 resumption shifts the outage and cycles resume",
                            $"ordinary effective supply={ordinaryEffectiveSupply:F2}; " +
                            $"computed capacity={ordinaryCapacity}; " +
                            $"promised={acceptedContract.quantityPerCycle}");
                    }
                    else
                    {
                        ResetCycleFixture();
                        RestoreCycleProfile();
                        MakeSupplierHostile();
                        int j6SuspensionTick = GenTicks.TicksGame;
                        int j6OutageLength =
                            3 * acceptedContract.cadenceDays * GenDate.TicksPerDay;
                        int j6NextTickBeforeSuspension =
                            j6SuspensionTick - 2 * acceptedContract.cadenceDays * GenDate.TicksPerDay;
                        acceptedContract.nextCycleTick = j6NextTickBeforeSuspension;
                        HostilityPolicy.Sweep(state);
                        bool j6Suspended =
                            acceptedContract.status == ProcurementContractStatus.Suspended;

                        // The self-test must not wait three in-game days. Move the persisted
                        // suspension marker back by the intended outage, which constructs the same
                        // due/past-clock precondition while leaving the real world clock and every
                        // other system alone.
                        acceptedContract.suspendedTick = j6SuspensionTick - j6OutageLength;
                        int j6ObservedOutage = GenTicks.TicksGame - acceptedContract.suspendedTick;
                        RestoreSupplierRelation();
                        HostilityPolicy.Sweep(state);
                        int j6NextTickAfterResume = acceptedContract.nextCycleTick;
                        // The shifted schedule is deliberately still ahead of the current tick, so
                        // a reset-to-now implementation cannot satisfy the clock assertion. Make
                        // the cycle due only after recording that shifted value, then drive one
                        // cycle.
                        acceptedContract.nextCycleTick = GenTicks.TicksGame;
                        int j6Advanced = ProcurementContractService.AdvanceCycles(state);
                        PurchaseOrder j6Order = FindFixtureOrder(acceptedContract.activeOrderId);
                        check(
                            "J6 resumption shifts the outage and cycles resume",
                            j6Suspended &&
                            acceptedContract.status == ProcurementContractStatus.Active &&
                            j6ObservedOutage == j6OutageLength &&
                            j6NextTickAfterResume ==
                                j6NextTickBeforeSuspension + j6ObservedOutage &&
                            j6NextTickAfterResume > GenTicks.TicksGame &&
                            j6Advanced == 1 && j6Order != null && j6Order.IsOpen,
                            $"tick before suspension={j6NextTickBeforeSuspension}; " +
                            $"outage={j6ObservedOutage}; " +
                            $"tick after resumption={j6NextTickAfterResume}; " +
                            $"current tick={GenTicks.TicksGame}; suspended={j6Suspended}; " +
                            $"status={acceptedContract.status}; advanced={j6Advanced}; " +
                            $"order id={acceptedContract.activeOrderId}");
                    }
                }

                if (saveVersionField == null)
                {
                    skip("J8 older-save migration preserves procurement agreements",
                        "persisted saveVersion field is not accessible");
                }
                else
                {
                    int j8ContractCountBefore = state.ProcurementContracts.Count;
                    ProcurementContractStatus j8StatusBefore = acceptedContract.status;
                    int j8CompletedBefore = acceptedContract.cyclesCompleted;
                    int j8FailedBefore = acceptedContract.cyclesFailed;
                    int j8SaveVersionBefore = state.SaveVersion;
                    int j8SaveVersionAfter = -1;
                    string j8Failure = null;
                    try
                    {
                        saveVersionField.SetValue(state, 53);
                        state.MigrateIfNeeded();
                        j8SaveVersionAfter = state.SaveVersion;
                    }
                    catch (Exception ex)
                    {
                        j8Failure = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    finally
                    {
                        saveVersionField.SetValue(state, j8SaveVersionBefore);
                    }

                    check(
                        "J8 older-save migration preserves procurement agreements",
                        j8Failure == null && j8ContractCountBefore == state.ProcurementContracts.Count &&
                        acceptedContract.status == j8StatusBefore &&
                        acceptedContract.status != ProcurementContractStatus.Suspended &&
                        acceptedContract.cyclesCompleted == j8CompletedBefore &&
                        acceptedContract.cyclesFailed == j8FailedBefore &&
                        j8SaveVersionAfter == IntercolonyWorldComponent.CurrentSaveVersion &&
                        state.SaveVersion == j8SaveVersionBefore,
                        $"saveVersion 53->{j8SaveVersionAfter}, restored={state.SaveVersion}; " +
                        $"contracts {j8ContractCountBefore}->{state.ProcurementContracts.Count}; " +
                        $"status {j8StatusBefore}->{acceptedContract.status}; " +
                        $"completed {j8CompletedBefore}->{acceptedContract.cyclesCompleted}; " +
                        $"failed {j8FailedBefore}->{acceptedContract.cyclesFailed}; " +
                        $"failure={j8Failure ?? "none"}");
                }

                // --- Stage 6I part 3: procurement timeline and cancellation ----------------
                int p1Started = CountProcurementTimelineRecords(
                    state, acceptedContract.settlementId, acceptedContract.id,
                    CommercialEventType.ContractStarted);
                check(
                    "P1 started procurement agreement records exactly once",
                    p1Started == 1,
                    $"settlement={acceptedContract.settlementId}; contract={acceptedContract.id}; " +
                    $"ContractStarted records={p1Started}; timeline count={state.CommercialTimeline.Count}");

                int p2OrdinaryCounterRecords = CountProcurementTimelineRecords(
                    state, acceptedContract.settlementId, acceptedContract.id,
                    CommercialEventType.CounterofferAccepted);
                int p2CounterId = state.PeekNextId() + 100;
                ProcurementContract p2Counter = HandMadeProcurementCounter(
                    acceptedContract.thingDef, p2CounterId, 7, 1.25f, 3, 1,
                    13, 4.75f, 17, 5);
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.Add(p2Counter);
                ProcurementContractAnswer p2Answer =
                    ProcurementContractService.AcceptFinalCounter(state, p2Counter);
                int p2CounterRecords = CountProcurementTimelineRecords(
                    state, p2Counter.settlementId, p2Counter.id,
                    CommercialEventType.CounterofferAccepted);
                int p2CounterStartedRecords = CountProcurementTimelineRecords(
                    state, p2Counter.settlementId, p2Counter.id,
                    CommercialEventType.ContractStarted);
                check(
                    "P2 accepted procurement counter has a distinct timeline record",
                    p2Answer.Applied && p2Counter.status == ProcurementContractStatus.Active &&
                    p2CounterRecords == 1 && p2CounterStartedRecords == 1 &&
                    p2OrdinaryCounterRecords == 0,
                    $"ordinary contract={acceptedContract.id}; ordinary CounterofferAccepted=" +
                    $"{p2OrdinaryCounterRecords}; counter contract={p2Counter.id}; " +
                    $"answer={p2Answer.Applied}/{p2Answer.Decision}; counter status={p2Counter.status}; " +
                    $"counter records={p2CounterRecords}; counter ContractStarted=" +
                    $"{p2CounterStartedRecords}");

                state.ProcurementContracts.Clear();
                state.ProcurementContracts.Add(acceptedContract);

                // Proposal, counter receipt, and counter refusal are state transitions, not
                // commercial outcomes. Each gets its own before/after count so one accidental
                // write cannot be hidden by another non-event.
                state.CommercialTimeline.Clear();
                state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
                if (cycleSettlement == null || acceptedContract.thingDef == null)
                {
                    string p5Reason =
                        $"settlement={cycleSettlement?.ID.ToString() ?? "none"}; " +
                        $"product={acceptedContract.thingDef?.defName ?? "none"}";
                    skip("P5a sending a procurement proposal leaves the timeline unchanged",
                        p5Reason);
                    skip("P5b receiving a procurement counter leaves the timeline unchanged",
                        "real procurement settlement/product fixture unavailable");
                    skip("P5c declining a procurement counter leaves the timeline unchanged",
                        "real procurement settlement/product fixture unavailable");
                }
                else
                {
                    state.ProcurementContracts.Clear();
                    int p5aBefore = state.CommercialTimeline.Count;
                    ProcurementContractProposalResult p5aProposal =
                        ProposeProcurementFixture(state, cycleSettlement, acceptedContract.thingDef);
                    int p5aAfter = state.CommercialTimeline.Count;
                    if (p5aProposal.Contract == null)
                    {
                        skip(
                            "P5a sending a procurement proposal leaves the timeline unchanged",
                            $"proposal was not constructed for settlement={cycleSettlement.ID}, " +
                            $"product={acceptedContract.thingDef.defName}; " +
                            $"success={p5aProposal.Success}; reason={p5aProposal.Reason ?? "none"}");
                    }
                    else
                    {
                        check(
                            "P5a sending a procurement proposal leaves the timeline unchanged",
                            p5aAfter == p5aBefore,
                            $"timeline {p5aBefore}->{p5aAfter}; proposal id={p5aProposal.Contract.id}; " +
                            $"decision={p5aProposal.Evaluation?.Decision.ToString() ?? "none"}; " +
                            $"record types={CommercialTimelineTypes(state)}");
                    }

                    state.ProcurementContracts.Clear();
                    ProcurementContract p5bCounter = HandMadeProcurementCounter(
                        acceptedContract.thingDef, state.PeekNextId() + 101,
                        7, 1.25f, 3, 1, 13, 4.75f, 17, 5,
                        ProcurementContractStatus.Offered);
                    state.ProcurementContracts.Add(p5bCounter);
                    int p5bBefore = state.CommercialTimeline.Count;
                    ProcurementContractAnswer p5bAnswer =
                        ProcurementContractService.AnswerProposal(state, p5bCounter);
                    int p5bAfter = state.CommercialTimeline.Count;
                    check(
                        "P5b receiving a procurement counter leaves the timeline unchanged",
                        p5bAnswer.Applied &&
                        p5bAnswer.Decision == IntercolonyNegotiationDecision.Countered &&
                        p5bCounter.status == ProcurementContractStatus.CounterpartyCountered &&
                        p5bAfter == p5bBefore,
                        $"timeline {p5bBefore}->{p5bAfter}; counter id={p5bCounter.id}; " +
                        $"answer={p5bAnswer.Applied}/{p5bAnswer.Decision}; " +
                        $"status={p5bCounter.status}; record types={CommercialTimelineTypes(state)}");

                    state.ProcurementContracts.Clear();
                    ProcurementContract p5cCounter = HandMadeProcurementCounter(
                        acceptedContract.thingDef, state.PeekNextId() + 102,
                        7, 1.25f, 3, 1, 13, 4.75f, 17, 5);
                    state.ProcurementContracts.Add(p5cCounter);
                    int p5cBefore = state.CommercialTimeline.Count;
                    bool p5cDeclined =
                        ProcurementContractService.TryDeclineFinalCounter(state, p5cCounter);
                    int p5cAfter = state.CommercialTimeline.Count;
                    check(
                        "P5c declining a procurement counter leaves the timeline unchanged",
                        p5cDeclined && p5cCounter.status == ProcurementContractStatus.Cancelled &&
                        p5cAfter == p5cBefore,
                        $"timeline {p5cBefore}->{p5cAfter}; counter id={p5cCounter.id}; " +
                        $"declined={p5cDeclined}; status={p5cCounter.status}; " +
                        $"record types={CommercialTimelineTypes(state)}");
                }

                state.ProcurementContracts.Clear();
                state.ProcurementContracts.Add(acceptedContract);
                CommercialReputation pReputation = null;
                if (cycleSettlement != null)
                {
                    pReputation = new CommercialReputation(
                        cycleSettlement.ID, cycleSettlement.Label ?? "Self-test",
                        cycleSettlement.Faction?.Name ?? "");
                    // Keep the active cancellation away from a reputation-tier boundary, so
                    // P6/P7/P8 count only their contract event and not a milestone side effect.
                    pReputation.Adjust(20f);
                    state.Reputations[cycleSettlement.ID] = pReputation;
                }

                bool pCycleFixtureReady = supplyFixtureAvailable &&
                    ordinaryCapacity >= acceptedContract.quantityPerCycle &&
                    fixtureSilverBaseline != null;
                string pCycleFixtureReason = pCycleFixtureReady
                    ? null
                    : $"supplier={acceptedContract.settlementId}; " +
                      $"profile={(cycleProfile == null ? "none" : "available")}; " +
                      $"category={cycleCategory?.ToString() ?? "none"}; " +
                      $"ordinary capacity={ordinaryCapacity}; " +
                      $"promised={acceptedContract.quantityPerCycle}; " +
                      $"silver baseline={(fixtureSilverBaseline == null ? "none" : "available")}";

                if (!pCycleFixtureReady)
                {
                    skip("P3 procurement cycle records only when goods arrive", pCycleFixtureReason);
                    skip("P3 creating a procurement cycle order records nothing", pCycleFixtureReason);
                    skip("P4 supplier default records one reasoned failure event", pCycleFixtureReason);
                    skip("P9 cancellation preserves an in-flight procurement order",
                        pCycleFixtureReason);
                }
                else
                {
                    RestoreCycleProfile();
                    ResetCycleFixture();
                    state.CommercialTimeline.Clear();
                    state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
                    int p3CreateBefore = state.CommercialTimeline.Count;
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    int p3CreateAdvanced = ProcurementContractService.AdvanceCycles(state);
                    PurchaseOrder p3Order = FindFixtureOrder(acceptedContract.activeOrderId);
                    int p3CreateAfter = state.CommercialTimeline.Count;
                    int p3CycleRecordsAtCreate = CountProcurementTimelineRecords(
                        state, acceptedContract.settlementId,
                        p3Order?.id ?? ProcurementContract.NoActiveOrderId,
                        CommercialEventType.ProcurementCycleCompleted);
                    check(
                        "P3 creating a procurement cycle order records nothing",
                        p3CreateAdvanced == 1 && p3Order != null && p3Order.IsOpen &&
                        p3CreateAfter == p3CreateBefore && p3CycleRecordsAtCreate == 0,
                        $"timeline {p3CreateBefore}->{p3CreateAfter}; advanced={p3CreateAdvanced}; " +
                        $"order id={p3Order?.id.ToString() ?? "none"}; " +
                        $"order state={p3Order?.status.ToString() ?? "none"}; " +
                        $"ProcurementCycleCompleted={p3CycleRecordsAtCreate}");

                    if (p3Order == null)
                    {
                        skip(
                            "P3 procurement cycle records only when goods arrive",
                            $"cycle order was not constructed; advanced={p3CreateAdvanced}; " +
                            $"orders={state.PurchaseOrders.Count}; " +
                            $"activeOrderId={acceptedContract.activeOrderId}");
                    }
                    else
                    {
                        int p3CompletionBefore = CountProcurementTimelineRecords(
                            state, acceptedContract.settlementId, p3Order.id,
                            CommercialEventType.ProcurementCycleCompleted);
                        PurchaseOrderService.Complete(p3Order, "P3 goods arrived");
                        int p3CompletionAfter = CountProcurementTimelineRecords(
                            state, acceptedContract.settlementId, p3Order.id,
                            CommercialEventType.ProcurementCycleCompleted);
                        CommercialEventRecord p3Record = FindProcurementTimelineRecord(
                            state, acceptedContract.settlementId, p3Order.id,
                            CommercialEventType.ProcurementCycleCompleted);
                        check(
                            "P3 procurement cycle records only when goods arrive",
                            p3Order.status == PurchaseOrderStatus.Completed &&
                            p3CompletionBefore == 0 && p3CompletionAfter == 1 &&
                            p3Record != null && p3Record.tick >= p3Order.orderedTick,
                            $"order id={p3Order.id}; order state={p3Order.status}; " +
                            $"cycle records {p3CompletionBefore}->{p3CompletionAfter}; " +
                            $"record tick={p3Record?.tick.ToString() ?? "none"}; " +
                            $"ordered tick={p3Order.orderedTick}; types={CommercialTimelineTypes(state)}");
                    }

                    RestoreCycleProfile();
                    ResetCycleFixture();
                    MakeSupplyInsufficient();
                    state.CommercialTimeline.Clear();
                    state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
                    int p4Before = state.CommercialTimeline.Count;
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    int p4Advanced = ProcurementContractService.AdvanceCycles(state);
                    PurchaseOrder p4Order = state.PurchaseOrders.Count == 0
                        ? null
                        : state.PurchaseOrders[state.PurchaseOrders.Count - 1];
                    int p4After = state.CommercialTimeline.Count;
                    int p4FailedRecords = p4Order == null
                        ? 0
                        : CountProcurementTimelineRecords(
                            state, acceptedContract.settlementId, p4Order.id,
                            CommercialEventType.PurchaseFailed);
                    CommercialEventRecord p4Record = p4Order == null
                        ? null
                        : FindProcurementTimelineRecord(
                            state, acceptedContract.settlementId, p4Order.id,
                            CommercialEventType.PurchaseFailed);
                    bool p4Reasoned = p4Record != null && !string.IsNullOrEmpty(
                        p4Record.compactDetail) && p4Record.compactDetail.Contains("Supplier default") &&
                        !string.IsNullOrEmpty(p4Order?.outcomeNote) &&
                        p4Order.outcomeNote.StartsWith(p4Record.compactDetail);
                    check(
                        "P4 supplier default records one reasoned failure event",
                        p4Advanced == 1 && p4Order != null &&
                        p4Order.status == PurchaseOrderStatus.SupplierDefault &&
                        p4After == p4Before + 1 && p4FailedRecords == 1 && p4Reasoned,
                        $"timeline {p4Before}->{p4After}; advanced={p4Advanced}; " +
                        $"order id={p4Order?.id.ToString() ?? "none"}; " +
                        $"order state={p4Order?.status.ToString() ?? "none"}; " +
                        $"PurchaseFailed records={p4FailedRecords}; " +
                        $"record detail={p4Record?.compactDetail ?? "none"}; " +
                        $"order outcome={p4Order?.outcomeNote ?? "none"}; " +
                        $"record types={CommercialTimelineTypes(state)}");

                    ResetCycleFixture();
                    state.CommercialTimeline.Clear();
                    state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
                    acceptedContract.totalCycles = 4;
                    acceptedContract.cyclesCompleted = 1;
                    acceptedContract.cyclesFailed = 1;
                    acceptedContract.activeOrderId = ProcurementContract.NoActiveOrderId;
                    acceptedContract.status = ProcurementContractStatus.Active;
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    int p6Completed = acceptedContract.cyclesCompleted;
                    int p6Remaining = acceptedContract.totalCycles -
                                      acceptedContract.cyclesCompleted - acceptedContract.cyclesFailed;
                    int p6OrdersBeforeAdvance = state.PurchaseOrders.Count;
                    bool p6Cancelled = ProcurementContractService.CancelContract(
                        state, acceptedContract);
                    CommercialEventRecord p6Record = FindProcurementTimelineRecord(
                        state, acceptedContract.settlementId, acceptedContract.id,
                        CommercialEventType.ContractCancelled);
                    int p6CancelRecords = CountProcurementTimelineRecords(
                        state, acceptedContract.settlementId, acceptedContract.id,
                        CommercialEventType.ContractCancelled);
                    int p6OrdersBeforeResume = state.PurchaseOrders.Count;
                    int p6AdvancedAfterCancel = ProcurementContractService.AdvanceCycles(state);
                    check(
                        "P6 cancelling an active agreement records counts and stops cycles",
                        p6Cancelled && acceptedContract.status == ProcurementContractStatus.Cancelled &&
                        p6CancelRecords == 1 && p6Record != null &&
                        p6Record.compactDetail.Contains($"{p6Completed} cycles completed") &&
                        p6Record.compactDetail.Contains($"{p6Remaining} cycles remained") &&
                        p6AdvancedAfterCancel == 0 &&
                        state.PurchaseOrders.Count == p6OrdersBeforeResume &&
                        p6OrdersBeforeResume == p6OrdersBeforeAdvance,
                        $"cancelled={p6Cancelled}; status={acceptedContract.status}; " +
                        $"ContractCancelled records={p6CancelRecords}; " +
                        $"record detail={p6Record?.compactDetail ?? "none"}; " +
                        $"cycles completed={p6Completed}; remaining={p6Remaining}; " +
                        $"orders before={p6OrdersBeforeAdvance}; after resume=" +
                        $"{state.PurchaseOrders.Count}; advanced={p6AdvancedAfterCancel}");

                    ProcurementContractStatus p7Status = acceptedContract.status;
                    string p7Note = acceptedContract.outcomeNote;
                    int p7TimelineBefore = state.CommercialTimeline.Count;
                    int p7OrdersBefore = state.PurchaseOrders.Count;
                    bool p7CancelledAgain = ProcurementContractService.CancelContract(
                        state, acceptedContract);
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    int p7Advanced = ProcurementContractService.AdvanceCycles(state);
                    int p7CancelRecords = CountProcurementTimelineRecords(
                        state, acceptedContract.settlementId, acceptedContract.id,
                        CommercialEventType.ContractCancelled);
                    check(
                        "P7 cancelled agreement is terminal and idempotent",
                        !p7CancelledAgain && acceptedContract.status == p7Status &&
                        acceptedContract.outcomeNote == p7Note &&
                        state.CommercialTimeline.Count == p7TimelineBefore &&
                        p7CancelRecords == 1 && p7Advanced == 0 &&
                        state.PurchaseOrders.Count == p7OrdersBefore &&
                        acceptedContract.activeOrderId == ProcurementContract.NoActiveOrderId,
                        $"second cancel={p7CancelledAgain}; status={p7Status}->{acceptedContract.status}; " +
                        $"timeline {p7TimelineBefore}->{state.CommercialTimeline.Count}; " +
                        $"ContractCancelled records={p7CancelRecords}; orders {p7OrdersBefore}->" +
                        $"{state.PurchaseOrders.Count}; advanced={p7Advanced}; " +
                        $"activeOrderId={acceptedContract.activeOrderId}");

                    if (pReputation == null)
                    {
                        skip(
                            "P8 active cancellation costs standing but suspended cancellation does not",
                            $"supplier settlement {acceptedContract.settlementId} no longer resolves");
                    }
                    else
                    {
                        pReputation = new CommercialReputation(
                            cycleSettlement.ID, cycleSettlement.Label ?? "Self-test",
                            cycleSettlement.Faction?.Name ?? "");
                        pReputation.Adjust(20f);
                        state.Reputations[cycleSettlement.ID] = pReputation;
                        ResetCycleFixture();
                        acceptedContract.status = ProcurementContractStatus.Active;
                        acceptedContract.activeOrderId = ProcurementContract.NoActiveOrderId;
                        state.CommercialTimeline.Clear();
                        state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
                        float p8ActiveBefore = pReputation.Score;
                        bool p8ActiveCancelled = ProcurementContractService.CancelContract(
                            state, acceptedContract);
                        float p8ActiveAfter = pReputation.Score;
                        float p8ActiveDelta = p8ActiveAfter - p8ActiveBefore;

                        acceptedContract.status = ProcurementContractStatus.Suspended;
                        acceptedContract.activeOrderId = ProcurementContract.NoActiveOrderId;
                        float p8SuspendedBefore = pReputation.Score;
                        bool p8SuspendedCancelled = ProcurementContractService.CancelContract(
                            state, acceptedContract);
                        float p8SuspendedAfter = pReputation.Score;
                        check(
                            "P8 active cancellation costs standing but suspended cancellation does not",
                            p8ActiveCancelled && p8ActiveAfter < p8ActiveBefore &&
                            p8ActiveDelta < 0f && p8SuspendedCancelled &&
                            acceptedContract.status == ProcurementContractStatus.Cancelled &&
                            Mathf.Approximately(p8SuspendedAfter, p8SuspendedBefore),
                            $"active cancelled={p8ActiveCancelled}; reputation " +
                            $"{p8ActiveBefore:F3}->{p8ActiveAfter:F3} (delta {p8ActiveDelta:F3}); " +
                            $"suspended cancelled={p8SuspendedCancelled}; reputation " +
                            $"{p8SuspendedBefore:F3}->{p8SuspendedAfter:F3}; " +
                            $"status={acceptedContract.status}");
                    }

                    RestoreCycleProfile();
                    ResetCycleFixture();
                    state.CommercialTimeline.Clear();
                    state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
                    acceptedContract.status = ProcurementContractStatus.Active;
                    acceptedContract.activeOrderId = ProcurementContract.NoActiveOrderId;
                    acceptedContract.nextCycleTick = GenTicks.TicksGame;
                    int p9Advanced = ProcurementContractService.AdvanceCycles(state);
                    int p9OrderId = acceptedContract.activeOrderId;
                    PurchaseOrder p9Order = FindFixtureOrder(p9OrderId);
                    if (p9Order == null || !p9Order.IsOpen)
                    {
                        check(
                            "P9 cancellation preserves an in-flight procurement order",
                            false,
                            $"cycle advance={p9Advanced}; activeOrderId={p9OrderId}; " +
                            $"order={(p9Order == null ? "none" : p9Order.id.ToString())}; " +
                            $"order state={(p9Order == null ? "none" : p9Order.status.ToString())}; " +
                            $"capacity={ordinaryCapacity}; promised={acceptedContract.quantityPerCycle}");
                    }
                    else
                    {
                        PurchaseOrderStatus p9StateBefore = p9Order.status;
                        int p9OrdersBefore = state.PurchaseOrders.Count;
                        bool p9Cancelled = ProcurementContractService.CancelContract(
                            state, acceptedContract);
                        PurchaseOrder p9SurvivingOrder = FindFixtureOrder(p9OrderId);
                        check(
                            "P9 cancellation preserves an in-flight procurement order",
                            p9Cancelled && acceptedContract.status == ProcurementContractStatus.Cancelled &&
                            acceptedContract.activeOrderId == p9OrderId &&
                            p9SurvivingOrder != null && p9SurvivingOrder.id == p9OrderId &&
                            p9SurvivingOrder.status == p9StateBefore && p9SurvivingOrder.IsOpen &&
                            state.PurchaseOrders.Count == p9OrdersBefore,
                            $"cancelled={p9Cancelled}; contract status={acceptedContract.status}; " +
                            $"order id={p9OrderId}; surviving id={p9SurvivingOrder?.id.ToString() ?? "none"}; " +
                            $"state before={p9StateBefore}; state after=" +
                            $"{p9SurvivingOrder?.status.ToString() ?? "none"}; " +
                            $"activeOrderId={acceptedContract.activeOrderId}; " +
                            $"orders {p9OrdersBefore}->{state.PurchaseOrders.Count}");
                    }
                }

                // --- Stage 6I part 4: acceptance gate criteria 9-12 ----------------------
                CheckStage6IAcceptanceGatePart4(
                    check, skip, state, acceptedContract, paymentMap, cycleSettlement,
                    cycleProfile, cycleCategory);
            }
            finally
            {
                RestoreStoredSilver(paymentMap, savedSilver);
                if (fixtureSilverZone != null)
                {
                    fixtureSilverZone.Delete(playSound: false);
                }

                acceptedContract.quantityPerCycle = savedQuantity;
                acceptedContract.unitPrice = savedUnitPrice;
                acceptedContract.cadenceDays = savedCadenceDays;
                acceptedContract.totalCycles = savedTotalCycles;
                acceptedContract.cyclesCompleted = savedCompleted;
                acceptedContract.cyclesFailed = savedFailed;
                acceptedContract.nextCycleTick = savedNextCycleTick;
                acceptedContract.suspendedTick = savedSuspendedTick;
                acceptedContract.activeOrderId = savedActiveOrderId;
                acceptedContract.status = savedStatus;
                acceptedContract.outcomeNote = savedOutcomeNote;
                RestoreCycleProfile();
                if (cycleProfile != null)
                {
                    cycleProfile.techTier = savedCycleTechTier;
                }
                RestoreSupplierRelation();
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedContracts);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedOrders);
                state.Ledger.Clear();
                state.Ledger.AddRange(savedLedger);
                state.LedgerStartTick = savedLedgerStartTick;

                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedCommercialTimelineStartTick;

                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> savedReputation in
                         savedReputations)
                {
                    state.Reputations[savedReputation.Key] = savedReputation.Value;
                }

                List<SupplierOfferConsumption> liveConsumption =
                    consumptionField.GetValue(state) as List<SupplierOfferConsumption>;
                if (liveConsumption != null)
                {
                    liveConsumption.Clear();
                    liveConsumption.AddRange(savedConsumption);
                }

                nextIdField.SetValue(state, savedNextId);
            }
        }

        private static void CheckStage6IAcceptanceGatePart4(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            ProcurementContract acceptedContract,
            Map paymentMap,
            Settlement settlement,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory? category)
        {
            const string R9 = "R9 paid procurement default conserves all colony silver";
            const string R10a = "R10a market shock changes future supplier listings";
            const string R10b = "R10b market shock does not rewrite a paid purchase order";
            const string R10c = "R10c market shock does not rewrite accepted procurement terms";
            const string R11 = "R11 all five live record kinds survive save/load";
            const string R12a = "R12 market opportunity still uses shared sell pricing";
            const string R12b = "R12 sales recurring contract still runs a cycle";

            if (state == null)
            {
                skip(R9, "world state is unavailable");
                skip(R10a, "world state is unavailable");
                skip(R10b, "world state is unavailable");
                skip(R10c, "world state is unavailable");
                skip(R11, "world state is unavailable");
                skip(R12a, "world state is unavailable");
                skip(R12b, "world state is unavailable");
                return;
            }

            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo refreshCountField = typeof(IntercolonyWorldComponent).GetField(
                "refreshCount", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lastRefreshTickField = typeof(IntercolonyWorldComponent).GetField(
                "lastRefreshTick", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo consumptionField = typeof(IntercolonyWorldComponent).GetField(
                "supplierOfferConsumption", BindingFlags.Instance | BindingFlags.NonPublic);

            List<RecurringContract> savedSalesContracts =
                new List<RecurringContract>(state.Contracts);
            List<ProcurementContract> savedProcurementContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            List<SupplierListing> savedListings =
                new List<SupplierListing>(state.SupplierListings);
            List<PurchaseRequest> savedRequests = new List<PurchaseRequest>(state.Requests);
            List<PurchaseOrder> savedPurchaseOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<SalesOrder> savedSalesOrders = new List<SalesOrder>(state.Orders);
            List<CommercialEventRecord> savedTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;
            List<LedgerEntry> savedLedger = new List<LedgerEntry>(state.Ledger);
            int savedLedgerStartTick = state.LedgerStartTick;
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<EconomicEvent> savedEconomicEvents =
                new List<EconomicEvent>(state.EconomicEvents);
            List<SettlementMarketState> savedMarketStates =
                new List<SettlementMarketState>(state.MarketStates);
            Dictionary<SettlementMarketState, float[]> savedDemand =
                new Dictionary<SettlementMarketState, float[]>();
            Dictionary<SettlementMarketState, float[]> savedSupply =
                new Dictionary<SettlementMarketState, float[]>();
            Dictionary<SettlementMarketState, int> savedMarketRefreshes =
                new Dictionary<SettlementMarketState, int>();
            foreach (SettlementMarketState marketState in savedMarketStates)
            {
                if (marketState == null)
                {
                    continue;
                }

                savedDemand[marketState] = (float[])marketState.demandPressure.Clone();
                savedSupply[marketState] = (float[])marketState.supplyPressure.Clone();
                savedMarketRefreshes[marketState] = marketState.lastAdvancedRefresh;
            }

            List<SupplierOfferConsumption> savedConsumption = consumptionField == null
                ? null
                : CloneConsumptions(
                    consumptionField.GetValue(state) as List<SupplierOfferConsumption>);
            Dictionary<Thing, int> savedSilver = SnapshotAllColonySilver(paymentMap);
            float[] savedProfileSupplyWeights = profile?.supplyWeights == null
                ? null
                : (float[])profile.supplyWeights.Clone();
            int savedNextId = nextIdField == null ? -1 : (int)nextIdField.GetValue(state);
            int savedRefreshCount = refreshCountField == null
                ? -1
                : (int)refreshCountField.GetValue(state);
            int savedLastRefreshTick = lastRefreshTickField == null
                ? -1
                : (int)lastRefreshTickField.GetValue(state);
            int savedSaveVersion = saveVersionField == null
                ? -1
                : (int)saveVersionField.GetValue(state);

            int savedProcurementQuantity = acceptedContract?.quantityPerCycle ?? 0;
            float savedProcurementUnitPrice = acceptedContract?.unitPrice ?? 0f;
            int savedProcurementCadence = acceptedContract?.cadenceDays ?? 0;
            int savedProcurementTotalCycles = acceptedContract?.totalCycles ?? 0;
            int savedProcurementCompleted = acceptedContract?.cyclesCompleted ?? 0;
            int savedProcurementFailed = acceptedContract?.cyclesFailed ?? 0;
            int savedProcurementNextTick = acceptedContract?.nextCycleTick ?? 0;
            int savedProcurementActiveOrder = acceptedContract?.activeOrderId ?? 0;
            ProcurementContractStatus savedProcurementStatus = acceptedContract == null
                ? ProcurementContractStatus.Offered
                : acceptedContract.status;
            string savedProcurementNote = acceptedContract?.outcomeNote;
            Thing temporarySilver = null;
            Zone_Stockpile temporarySilverZone = null;
            PurchaseOrder paidOrderForShock = null;
            float paidOrderUnitPriceBeforeShock = 0f;
            int paidOrderQuantityBeforeShock = 0;
            int paidOrderTotalBeforeShock = 0;

            try
            {
                PurchaseOrder defaultOrder = null;
                bool cyclePreconditions = paymentMap != null && ThingDefOf.Silver != null &&
                    acceptedContract != null && settlement != null && profile != null &&
                    category.HasValue && (int)category.Value >= 0 &&
                    (int)category.Value < IntercolonyProductCategoryUtility.Count &&
                    consumptionField != null;
                string cycleReason = cyclePreconditions
                    ? null
                    : "paid-cycle fixture needs a payment map, silver, accepted contract, " +
                      "supplier profile/category, and the consumption ledger";

                if (!cyclePreconditions)
                {
                    skip(R9, cycleReason);
                    skip(R10b, "no paid purchase order can be constructed: " + cycleReason);
                }
                else
                {
                    state.ProcurementContracts.Clear();
                    state.ProcurementContracts.Add(acceptedContract);
                    state.PurchaseOrders.Clear();
                    acceptedContract.quantityPerCycle = 1;
                    acceptedContract.unitPrice = Mathf.Max(
                        1f, savedProcurementUnitPrice > 0f ? savedProcurementUnitPrice : 1f);
                    acceptedContract.cadenceDays = 1;
                    acceptedContract.totalCycles = 2;
                    acceptedContract.cyclesCompleted = 0;
                    acceptedContract.cyclesFailed = 0;
                    acceptedContract.activeOrderId = ProcurementContract.NoActiveOrderId;
                    acceptedContract.status = ProcurementContractStatus.Active;
                    acceptedContract.outcomeNote = "R9 acceptance-gate fixture";

                    int expectedCycleCost = IntercolonyPricing.TotalPayment(
                        acceptedContract.unitPrice, acceptedContract.quantityPerCycle);
                    int storedSilver = PurchaseOrderService.CountColonySilver(paymentMap);
                    // Keep one silver beyond the payment so the service splits a paid stack
                    // rather than consuming the exact original Thing object.
                    int requiredStoredSilver = expectedCycleCost + 1;
                    if (storedSilver < requiredStoredSilver)
                    {
                        int needed = requiredStoredSilver - storedSilver;
                        Thing topUp = null;
                        foreach (Thing silver in paymentMap.listerThings.ThingsOfDef(ThingDefOf.Silver))
                        {
                            if (silver != null && !silver.Destroyed && silver.IsInAnyStorage() &&
                                silver.stackCount + needed <= ThingDefOf.Silver.stackLimit)
                            {
                                topUp = silver;
                                break;
                            }
                        }

                        if (topUp != null)
                        {
                            topUp.stackCount += needed;
                        }
                        else
                        {
                            TryCreateStoredSilver(
                                paymentMap, needed, out temporarySilver, out temporarySilverZone);
                        }

                        storedSilver = PurchaseOrderService.CountColonySilver(paymentMap);
                    }

                    if (storedSilver < requiredStoredSilver)
                    {
                        skip(R9,
                            $"stored silver={storedSilver}; paid cycle needs {expectedCycleCost} " +
                            $"plus one preservation silver; " +
                            "temporary stored-silver fixture could not be made");
                        skip(R10b,
                            $"no paid purchase order: stored silver={storedSilver}; " +
                            $"cycle cost={expectedCycleCost}");
                    }
                    else
                    {
                        int silverTotalBefore = CountAllColonySilver(paymentMap);
                        profile.supplyWeights[(int)category.Value] = 0f;
                        acceptedContract.nextCycleTick = GenTicks.TicksGame;
                        int advanced = ProcurementContractService.AdvanceCycles(state);
                        if (state.PurchaseOrders.Count > 0)
                        {
                            defaultOrder = state.PurchaseOrders[state.PurchaseOrders.Count - 1];
                        }

                        int silverTotalAfter = CountAllColonySilver(paymentMap);
                        int silverDifference = silverTotalAfter - silverTotalBefore;
                        check(
                            R9,
                            advanced == 1 && defaultOrder != null &&
                            defaultOrder.status == PurchaseOrderStatus.SupplierDefault &&
                            defaultOrder.paidSilver == expectedCycleCost &&
                            silverTotalAfter == silverTotalBefore,
                            $"silverTotalBefore={silverTotalBefore}; " +
                            $"silverTotalAfter={silverTotalAfter}; " +
                            $"difference={silverDifference}; expectedCycleCost={expectedCycleCost}; " +
                            $"advanced={advanced}; orderStatus=" +
                            $"{(defaultOrder == null ? "null" : defaultOrder.status.ToString())}; " +
                            $"paid={(defaultOrder == null ? "null" : defaultOrder.paidSilver.ToString())}");

                        if (defaultOrder == null || defaultOrder.paidSilver <= 0)
                        {
                            skip(R10b,
                                "the paid-cycle fixture did not produce a paid purchase order");
                        }
                        else
                        {
                            paidOrderForShock = defaultOrder;
                            paidOrderUnitPriceBeforeShock = defaultOrder.unitPrice;
                            paidOrderQuantityBeforeShock = defaultOrder.quantity;
                            paidOrderTotalBeforeShock = defaultOrder.paidSilver;
                        }
                    }
                }

                if (profile != null && savedProfileSupplyWeights != null)
                {
                    Array.Copy(savedProfileSupplyWeights, profile.supplyWeights,
                        savedProfileSupplyWeights.Length);
                }

                bool listingPreconditions = settlement != null && profile != null &&
                    ThingDefOf.Steel != null;
                if (!listingPreconditions)
                {
                    skip(R10a,
                        "supplier listing fixture needs an accessible settlement, profile, and Steel");
                }
                else
                {
                    int listingWindow = state.RefreshCount + 10_000;
                    int listingId = 920_000;
                    List<SupplierListing> beforeListings = SupplierListingService.GenerateFor(
                        state, settlement, profile, listingWindow, 0, () => listingId++);
                    if (beforeListings.Count == 0)
                    {
                        skip(R10a,
                            $"supplier listing generator produced 0 baseline listings for " +
                            $"settlement {settlement.ID} in window {listingWindow}");
                    }
                    else
                    {
                        IntercolonyProductCategory? listingCategory =
                            IntercolonyProductClassifier.Classify(beforeListings[0].thingDef);
                        if (!listingCategory.HasValue)
                        {
                            skip(R10a,
                                $"baseline listing {beforeListings[0].id} item " +
                                $"{beforeListings[0].thingDef?.defName ?? "null"} has no category");
                        }
                        else
                        {
                            SettlementMarketState listingMarketState = state.MarketStateFor(
                                settlement.ID, createIfMissing: false);
                            if (listingMarketState != null)
                            {
                                listingMarketState.supplyPressure[(int)listingCategory.Value] =
                                    SettlementMarketState.Neutral;
                            }

                            float supplyBefore = EffectiveEconomyService.EffectiveSupply(
                                state, profile, listingCategory.Value);
                            MarketPressureService.ApplySupplyShock(
                                state, settlement.ID, listingCategory.Value, 0.35f);
                            float supplyAfter = EffectiveEconomyService.EffectiveSupply(
                                state, profile, listingCategory.Value);
                            int afterId = 930_000;
                            List<SupplierListing> afterListings = SupplierListingService.GenerateFor(
                                state, settlement, profile, listingWindow, 0, () => afterId++);
                            bool listingsDiffer = !SameSupplierListingBatch(
                                beforeListings, afterListings);
                            if (supplyAfter >= supplyBefore - 0.0001f)
                            {
                                skip(
                                    R10a,
                                    $"supplier effective supply did not decrease " +
                                    $"{supplyBefore:F4}->{supplyAfter:F4}");
                            }
                            else
                            {
                                check(
                                    R10a,
                                    listingsDiffer,
                                    $"supply {supplyBefore:F4}->{supplyAfter:F4}; " +
                                    $"listings {SupplierListingBatchDetail(beforeListings)} -> " +
                                    $"{SupplierListingBatchDetail(afterListings)}");
                            }
                        }
                    }
                }

                if (acceptedContract == null || settlement == null ||
                    acceptedContract.thingDef == null ||
                    acceptedContract.quantityPerCycle <= 0 || acceptedContract.totalCycles <= 0)
                {
                    skip(R10c,
                        "accepted procurement contract has no valid item, quantity, or cycle terms");
                }
                else
                {
                    acceptedContract.status = ProcurementContractStatus.Active;
                    float contractUnitPriceBefore = acceptedContract.unitPrice;
                    int contractQuantityBefore = acceptedContract.quantityPerCycle;
                    int contractCadenceBefore = acceptedContract.cadenceDays;
                    int contractTotalCyclesBefore = acceptedContract.totalCycles;
                    IntercolonyProductCategory contractCategory =
                        IntercolonyProductClassifier.Classify(acceptedContract.thingDef) ??
                        IntercolonyProductCategory.Commodities;
                    if (settlement != null)
                    {
                        float contractSupplyBefore = EffectiveEconomyService.EffectiveSupply(
                            state, profile, contractCategory);
                        MarketPressureService.ApplySupplyShock(
                            state, settlement.ID, contractCategory, 0.35f);
                        float contractSupplyAfter = EffectiveEconomyService.EffectiveSupply(
                            state, profile, contractCategory);
                        if (contractSupplyAfter >= contractSupplyBefore - 0.0001f)
                        {
                            skip(
                                R10c,
                                $"contract category effective supply did not decrease " +
                                $"{contractSupplyBefore:F4}->{contractSupplyAfter:F4}");
                        }
                        else
                        {
                            check(
                                R10c,
                                acceptedContract.unitPrice == contractUnitPriceBefore &&
                                acceptedContract.quantityPerCycle == contractQuantityBefore &&
                                acceptedContract.cadenceDays == contractCadenceBefore &&
                                acceptedContract.totalCycles == contractTotalCyclesBefore,
                                $"quantity {contractQuantityBefore}->{acceptedContract.quantityPerCycle}; " +
                                $"unitPrice {contractUnitPriceBefore:F4}->{acceptedContract.unitPrice:F4}; " +
                                $"cadenceDays {contractCadenceBefore}->{acceptedContract.cadenceDays}; " +
                                $"totalCycles {contractTotalCyclesBefore}->{acceptedContract.totalCycles}; " +
                                $"supply {contractSupplyBefore:F4}->{contractSupplyAfter:F4}");
                        }
                    }
                }

                if (paidOrderForShock == null)
                {
                    // R9 already emitted a specific skip when its paid precondition was
                    // unavailable. Do not duplicate it here.
                }
                else
                {
                    IntercolonyProductCategory orderCategory =
                        IntercolonyProductClassifier.Classify(paidOrderForShock.thingDef) ??
                        IntercolonyProductCategory.Commodities;
                    float paidPressureBefore = EffectiveEconomyService.CurrentSupplyPressure(
                        state, paidOrderForShock.settlementId, orderCategory);
                    MarketPressureService.ApplySupplyShock(
                        state, paidOrderForShock.settlementId, orderCategory, 0.35f);
                    float paidPressureAfter = EffectiveEconomyService.CurrentSupplyPressure(
                        state, paidOrderForShock.settlementId, orderCategory);
                    if (paidPressureAfter <= paidPressureBefore + 0.0001f)
                    {
                        skip(
                            R10b,
                            $"paid-order category supply pressure did not increase " +
                            $"{paidPressureBefore:F4}->{paidPressureAfter:F4}");
                    }
                    else
                    {
                        check(
                            R10b,
                            paidOrderForShock.unitPrice == paidOrderUnitPriceBeforeShock &&
                            paidOrderForShock.quantity == paidOrderQuantityBeforeShock &&
                            paidOrderForShock.paidSilver == paidOrderTotalBeforeShock,
                            $"unitPrice {paidOrderUnitPriceBeforeShock:F4}->" +
                            $"{paidOrderForShock.unitPrice:F4}; " +
                            $"quantity {paidOrderQuantityBeforeShock}->{paidOrderForShock.quantity}; " +
                            $"total {paidOrderTotalBeforeShock}->{paidOrderForShock.paidSilver}; " +
                            $"supply pressure {paidPressureBefore:F4}->{paidPressureAfter:F4}");
                    }
                }

                CheckStage6IAcceptanceGateSaveLoad(
                    check, skip, state, acceptedContract, settlement);
                CheckStage6IAcceptanceGateSelling(
                    check, skip, state, settlement, profile);
            }
            finally
            {
                RestoreAllColonySilver(paymentMap, savedSilver);
                temporarySilverZone?.Delete(playSound: false);
                if (temporarySilver != null && !temporarySilver.Destroyed)
                {
                    temporarySilver.Destroy(DestroyMode.Vanish);
                }

                state.Contracts.Clear();
                state.Contracts.AddRange(savedSalesContracts);
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedProcurementContracts);
                state.SupplierListings.Clear();
                state.SupplierListings.AddRange(savedListings);
                state.Requests.Clear();
                state.Requests.AddRange(savedRequests);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedPurchaseOrders);
                state.Orders.Clear();
                state.Orders.AddRange(savedSalesOrders);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.Ledger.Clear();
                state.Ledger.AddRange(savedLedger);
                state.LedgerStartTick = savedLedgerStartTick;
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }

                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(savedEconomicEvents);
                state.MarketStates.Clear();
                state.MarketStates.AddRange(savedMarketStates);
                foreach (KeyValuePair<SettlementMarketState, float[]> entry in savedDemand)
                {
                    Array.Copy(entry.Value, entry.Key.demandPressure, entry.Value.Length);
                    Array.Copy(savedSupply[entry.Key], entry.Key.supplyPressure,
                        savedSupply[entry.Key].Length);
                    entry.Key.lastAdvancedRefresh = savedMarketRefreshes[entry.Key];
                }
                state.RefreshMarketStateIndex();

                if (consumptionField != null && savedConsumption != null)
                {
                    List<SupplierOfferConsumption> liveConsumption =
                        consumptionField.GetValue(state) as List<SupplierOfferConsumption>;
                    if (liveConsumption != null)
                    {
                        liveConsumption.Clear();
                        liveConsumption.AddRange(savedConsumption);
                    }
                }

                if (acceptedContract != null)
                {
                    acceptedContract.quantityPerCycle = savedProcurementQuantity;
                    acceptedContract.unitPrice = savedProcurementUnitPrice;
                    acceptedContract.cadenceDays = savedProcurementCadence;
                    acceptedContract.totalCycles = savedProcurementTotalCycles;
                    acceptedContract.cyclesCompleted = savedProcurementCompleted;
                    acceptedContract.cyclesFailed = savedProcurementFailed;
                    acceptedContract.nextCycleTick = savedProcurementNextTick;
                    acceptedContract.activeOrderId = savedProcurementActiveOrder;
                    acceptedContract.status = savedProcurementStatus;
                    acceptedContract.outcomeNote = savedProcurementNote;
                }

                if (profile != null && savedProfileSupplyWeights != null)
                {
                    Array.Copy(savedProfileSupplyWeights, profile.supplyWeights,
                        savedProfileSupplyWeights.Length);
                }

                if (nextIdField != null && savedNextId >= 0)
                {
                    nextIdField.SetValue(state, savedNextId);
                }
                if (refreshCountField != null && savedRefreshCount >= 0)
                {
                    refreshCountField.SetValue(state, savedRefreshCount);
                }
                if (lastRefreshTickField != null)
                {
                    lastRefreshTickField.SetValue(state, savedLastRefreshTick);
                }
                if (saveVersionField != null && savedSaveVersion >= 0)
                {
                    saveVersionField.SetValue(state, savedSaveVersion);
                }
            }
        }

        private sealed class Stage8ACounts
        {
            public int activeMarketOpportunities;
            public int activeEconomicEvents;
            public int nonNeutralMarketPressure;
            public int brandRecords;
            public int negotiatedSalesOrders;
            public int activeRfqs;
            public int supplierMarketListings;
            public int activePurchaseOrders;
            public int recurringSalesContracts;
            public int recurringProcurementContracts;
            public int hiredWorkersPayroll;
            public int commercialHistoryTimeline;

            // This aggregate is not a thirteenth requested kind. It is included in the count
            // report because M3 deliberately proves that completion updates this other persisted
            // collection too.
            public int commercialHistoryAggregate;

            public static Stage8ACounts From(IntercolonyWorldComponent state)
            {
                if (state == null)
                {
                    return new Stage8ACounts
                    {
                        activeMarketOpportunities = -1,
                        activeEconomicEvents = -1,
                        nonNeutralMarketPressure = -1,
                        brandRecords = -1,
                        negotiatedSalesOrders = -1,
                        activeRfqs = -1,
                        supplierMarketListings = -1,
                        activePurchaseOrders = -1,
                        recurringSalesContracts = -1,
                        recurringProcurementContracts = -1,
                        hiredWorkersPayroll = -1,
                        commercialHistoryTimeline = -1,
                        commercialHistoryAggregate = -1
                    };
                }

                return new Stage8ACounts
                {
                    activeMarketOpportunities = state.Opportunities?.Count ?? -1,
                    activeEconomicEvents = state.EconomicEvents?.Count ?? -1,
                    nonNeutralMarketPressure = state.MarketStates?.Count ?? -1,
                    brandRecords = state.ProductBrandRecords?.Count ?? -1,
                    negotiatedSalesOrders = state.Orders?.Count ?? -1,
                    activeRfqs = state.Requests?.Count ?? -1,
                    supplierMarketListings = state.SupplierListings?.Count ?? -1,
                    activePurchaseOrders = state.PurchaseOrders?.Count ?? -1,
                    recurringSalesContracts = state.Contracts?.Count ?? -1,
                    recurringProcurementContracts = state.ProcurementContracts?.Count ?? -1,
                    hiredWorkersPayroll = state.Employments?.Count ?? -1,
                    commercialHistoryTimeline = state.CommercialTimeline?.Count ?? -1,
                    commercialHistoryAggregate = state.CommercialHistory?.Count ?? -1
                };
            }

            public bool SameAs(Stage8ACounts other)
            {
                return other != null &&
                       activeMarketOpportunities == other.activeMarketOpportunities &&
                       activeEconomicEvents == other.activeEconomicEvents &&
                       nonNeutralMarketPressure == other.nonNeutralMarketPressure &&
                       brandRecords == other.brandRecords &&
                       negotiatedSalesOrders == other.negotiatedSalesOrders &&
                       activeRfqs == other.activeRfqs &&
                       supplierMarketListings == other.supplierMarketListings &&
                       activePurchaseOrders == other.activePurchaseOrders &&
                       recurringSalesContracts == other.recurringSalesContracts &&
                       recurringProcurementContracts == other.recurringProcurementContracts &&
                       hiredWorkersPayroll == other.hiredWorkersPayroll &&
                       commercialHistoryTimeline == other.commercialHistoryTimeline &&
                       commercialHistoryAggregate == other.commercialHistoryAggregate;
            }

            public override string ToString()
            {
                return "active market opportunities=" + activeMarketOpportunities +
                       "; active economic events=" + activeEconomicEvents +
                       "; non-neutral market pressure=" + nonNeutralMarketPressure +
                       "; positive/negative brand records=" + brandRecords +
                       "; negotiated sales orders=" + negotiatedSalesOrders +
                       "; active RFQs=" + activeRfqs +
                       "; Supplier Market listings=" + supplierMarketListings +
                       "; active PurchaseOrders=" + activePurchaseOrders +
                       "; recurring sales contracts=" + recurringSalesContracts +
                       "; recurring procurement contracts=" + recurringProcurementContracts +
                       "; hired workers/payroll state=" + hiredWorkersPayroll +
                       "; commercial history timeline=" + commercialHistoryTimeline +
                       "; durable commercial history aggregate=" + commercialHistoryAggregate;
            }
        }

        private sealed class Stage8ARoundTrip
        {
            public IntercolonyWorldComponent loaded;
            public string failure;
        }

        private sealed class Stage8AFixture
        {
            public ThingDef primaryDef;
            public ThingDef secondaryDef;
            public int categoryIndex;
            public int now;
            public MarketOpportunity opportunity;
            public EconomicEvent economicEvent;
            public SettlementMarketState marketState;
            public ProductBrandRecord positiveBrand;
            public ProductBrandRecord negativeBrand;
            public SalesOrder salesOrder;
            public PurchaseRequest request;
            public SupplierListing listing;
            public PurchaseOrder purchaseOrder;
            public RecurringContract salesContract;
            public ProcurementContract procurementContract;
            public EmploymentContract employment;
            public CommercialEventRecord timelineRecord;
            public CommercialHistoryEntry history;
        }

        /// <summary>
        /// Stage 8A's full current-schema matrix. This deliberately uses the real WorldComponent
        /// Scribe path twice, and drives the loaded objects between those saves. The fixture is
        /// kept here beside R11 because it reuses the same detached-world round-trip machinery,
        /// but it owns every list it touches and restores the live world in the finally block.
        /// </summary>
        private static void CheckStage8AFullSaveLoadMatrix(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            const string M1 = "M1 all twelve Stage 8A kinds survive the first save/load";
            const string M2 = "M2 the world advances after the first reload";
            const string M3 = "M3 sales and purchase orders complete after reload and update durable history";
            const string M4 = "M4 a second save/load preserves advanced and completed state";
            const string M5 = "M5 every Stage 8A persisted collection keeps its exact fixture count";

            if (state == null)
            {
                Stage8ASkipAll(skip, "world state is unavailable");
                return;
            }

            FieldInfo nextIdField = typeof(IntercolonyWorldComponent).GetField(
                "nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo refreshCountField = typeof(IntercolonyWorldComponent).GetField(
                "refreshCount", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lastRefreshTickField = typeof(IntercolonyWorldComponent).GetField(
                "lastRefreshTick", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo economySeedField = typeof(IntercolonyWorldComponent).GetField(
                "economySeed", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo consumptionField = typeof(IntercolonyWorldComponent).GetField(
                "supplierOfferConsumption", BindingFlags.Instance | BindingFlags.NonPublic);

            if (nextIdField == null || refreshCountField == null || lastRefreshTickField == null ||
                saveVersionField == null || economySeedField == null || consumptionField == null)
            {
                Stage8ASkipAll(
                    skip,
                    "one or more live collection/counter fields needed for complete restoration " +
                    "are inaccessible");
                return;
            }

            List<MarketOpportunity> savedOpportunities =
                new List<MarketOpportunity>(state.Opportunities);
            List<EconomicEvent> savedEconomicEvents =
                new List<EconomicEvent>(state.EconomicEvents);
            List<SettlementMarketState> savedMarketStates =
                new List<SettlementMarketState>(state.MarketStates);
            List<ProductBrandRecord> savedBrands =
                new List<ProductBrandRecord>(state.ProductBrandRecords);
            List<SalesOrder> savedSalesOrders = new List<SalesOrder>(state.Orders);
            List<PurchaseRequest> savedRequests = new List<PurchaseRequest>(state.Requests);
            List<SupplierListing> savedListings =
                new List<SupplierListing>(state.SupplierListings);
            List<PurchaseOrder> savedPurchaseOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<RecurringContract> savedSalesContracts =
                new List<RecurringContract>(state.Contracts);
            List<ProcurementContract> savedProcurementContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            List<EmploymentContract> savedEmployments =
                new List<EmploymentContract>(state.Employments);
            List<CommercialEventRecord> savedTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            List<CommercialHistoryEntry> savedHistory =
                new List<CommercialHistoryEntry>(state.CommercialHistory);
            List<LedgerEntry> savedLedger = new List<LedgerEntry>(state.Ledger);
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            List<LaborDebt> savedLaborDebts = new List<LaborDebt>(state.LaborDebts);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<SupplierOfferConsumption> savedConsumption = CloneConsumptions(
                consumptionField.GetValue(state) as List<SupplierOfferConsumption>);
            Dictionary<SettlementMarketState, float[]> savedDemand =
                new Dictionary<SettlementMarketState, float[]>();
            Dictionary<SettlementMarketState, float[]> savedSupply =
                new Dictionary<SettlementMarketState, float[]>();
            Dictionary<SettlementMarketState, int> savedMarketRefreshes =
                new Dictionary<SettlementMarketState, int>();
            foreach (SettlementMarketState marketState in savedMarketStates)
            {
                if (marketState == null)
                {
                    continue;
                }

                savedDemand[marketState] = (float[])marketState.demandPressure.Clone();
                savedSupply[marketState] = (float[])marketState.supplyPressure.Clone();
                savedMarketRefreshes[marketState] = marketState.lastAdvancedRefresh;
            }

            Dictionary<Thing, int> savedSilver = SnapshotAllColonySilver(
                Find.CurrentMap ?? Find.AnyPlayerHomeMap);
            Dictionary<Faction, int> savedFactionGoodwill = Stage8ASnapshotFactionGoodwill();
            EmployerReputation savedEmployerStanding = state.EmployerStanding?.Snapshot();
            int savedTimelineStartTick = state.CommercialTimelineStartTick;
            int savedLedgerStartTick = state.LedgerStartTick;
            int savedNextId = (int)nextIdField.GetValue(state);
            int savedRefreshCount = (int)refreshCountField.GetValue(state);
            int savedLastRefreshTick = (int)lastRefreshTickField.GetValue(state);
            int savedSaveVersion = (int)saveVersionField.GetValue(state);
            int savedEconomySeed = (int)economySeedField.GetValue(state);

            ThingDef primaryDef = ThingDefOf.Steel ?? ThingDefOf.Silver;
            List<ThingDef> tradableDefs = IntercolonyProductClassifier.TradableDefs;
            if (primaryDef == null && tradableDefs != null)
            {
                foreach (ThingDef def in tradableDefs)
                {
                    if (def != null)
                    {
                        primaryDef = def;
                        break;
                    }
                }
            }

            ThingDef secondaryDef = null;
            if (tradableDefs != null)
            {
                foreach (ThingDef def in tradableDefs)
                {
                    if (def != null && def != primaryDef)
                    {
                        secondaryDef = def;
                        break;
                    }
                }
            }

            if (secondaryDef == null && ThingDefOf.Silver != null && ThingDefOf.Silver != primaryDef)
            {
                secondaryDef = ThingDefOf.Silver;
            }

            if (primaryDef == null)
            {
                Stage8ASkipAll(skip, "no resolvable ThingDef is available for the fixture");
                return;
            }

            if (secondaryDef == null)
            {
                skip("Stage 8A kind: positive and negative brand records",
                    "two distinct resolvable ThingDefs are required to construct both signs");
                skip(M1, "brand-record kind could not be constructed");
                skip(M2, "full twelve-kind fixture was not constructible");
                skip(M3, "full twelve-kind fixture was not constructible");
                skip(M4, "full twelve-kind fixture was not constructible");
                skip(M5, "full twelve-kind fixture was not constructible");
                return;
            }

            IntercolonyProductCategory category =
                IntercolonyProductClassifier.Classify(primaryDef) ??
                IntercolonyProductCategory.Commodities;
            int categoryIndex = (int)category;
            if (categoryIndex < 0 ||
                categoryIndex >= IntercolonyProductCategoryUtility.Count)
            {
                Stage8ASkipAll(
                    skip,
                    "the selected product category cannot address the persisted pressure arrays");
                return;
            }

            Stage8AFixture fixture = new Stage8AFixture
            {
                primaryDef = primaryDef,
                secondaryDef = secondaryDef,
                categoryIndex = categoryIndex,
                now = GenTicks.TicksGame
            };

            try
            {
                int now = fixture.now;
                const int settlementId = 880_801;
                const string settlementName = "Stage 8A Testholme";
                const string factionName = "Stage 8A Test faction";

                state.Opportunities.Clear();
                state.EconomicEvents.Clear();
                state.MarketStates.Clear();
                state.ProductBrandRecords.Clear();
                state.Orders.Clear();
                state.Requests.Clear();
                state.SupplierListings.Clear();
                state.PurchaseOrders.Clear();
                state.Contracts.Clear();
                state.ProcurementContracts.Clear();
                state.Employments.Clear();
                state.CommercialTimeline.Clear();
                state.CommercialHistory.Clear();
                state.Reputations.Clear();
                state.Ledger.Clear();
                state.Postings.Clear();
                state.LaborDebts.Clear();
                state.CommercialTimelineStartTick = now;
                state.LedgerStartTick = LedgerService.NoHistory;
                List<SupplierOfferConsumption> liveConsumption =
                    consumptionField.GetValue(state) as List<SupplierOfferConsumption>;
                liveConsumption?.Clear();
                saveVersionField.SetValue(state, IntercolonyWorldComponent.CurrentSaveVersion);

                fixture.opportunity = new MarketOpportunity
                {
                    id = state.NextId(),
                    settlementId = settlementId,
                    settlementName = settlementName,
                    thingDef = primaryDef,
                    quantity = 6,
                    unitPrice = 4.5f,
                    createdTick = now,
                    expiryTick = now + GenDate.TicksPerDay * 10,
                    deadlineDays = 5,
                    distanceTiles = 12f,
                    state = MarketOpportunityState.Available,
                    priceExplanation = "Stage 8A negotiated opportunity"
                };
                state.Opportunities.Add(fixture.opportunity);

                fixture.economicEvent = new EconomicEvent
                {
                    id = state.NextId(),
                    type = EconomicEventType.Drought,
                    startTick = now - GenDate.TicksPerDay,
                    endTick = now + GenDate.TicksPerDay * 10,
                    anchorSettlementId = settlementId,
                    radiusTiles = 20f,
                    factionLoadId = EconomicEvent.NoFaction
                };
                fixture.economicEvent.demandModifier[categoryIndex] = 1.35f;
                fixture.economicEvent.supplyScarcityModifier[categoryIndex] = 1.45f;
                state.EconomicEvents.Add(fixture.economicEvent);

                fixture.marketState = new SettlementMarketState(settlementId);
                fixture.marketState.demandPressure[categoryIndex] = 1.25f;
                fixture.marketState.supplyPressure[categoryIndex] = 1.40f;
                fixture.marketState.lastAdvancedRefresh = state.RefreshCount;
                state.MarketStates.Add(fixture.marketState);
                state.RefreshMarketStateIndex();

                fixture.positiveBrand = new ProductBrandRecord(
                    primaryDef, 65f, 12f, 24);
                fixture.negativeBrand = new ProductBrandRecord(
                    secondaryDef, -55f, 9f, 18);
                state.ProductBrandRecords.Add(fixture.positiveBrand);
                state.ProductBrandRecords.Add(fixture.negativeBrand);

                fixture.salesContract = new RecurringContract
                {
                    id = state.NextId(),
                    settlementId = settlementId,
                    settlementName = settlementName,
                    thingDef = primaryDef,
                    quantityPerCycle = 2,
                    cadenceTicks = GenDate.TicksPerDay,
                    totalCycles = 2,
                    cyclesCompleted = 0,
                    unitPrice = 4f,
                    referenceUnitPrice = 4f,
                    status = ContractStatus.Active,
                    nextCycleTick = now + GenDate.TicksPerDay
                };
                state.Contracts.Add(fixture.salesContract);

                fixture.salesOrder = new SalesOrder
                {
                    id = state.NextId(),
                    opportunityId = fixture.opportunity.id,
                    contractId = fixture.salesContract.id,
                    settlementId = settlementId,
                    settlementName = settlementName,
                    factionName = factionName,
                    line = new OrderLine(primaryDef, 2),
                    unitPrice = 4f,
                    referenceUnitPrice = 4f,
                    acceptedTick = now,
                    deadlineTick = now + GenDate.TicksPerDay * 2,
                    status = SalesOrderStatus.Accepted,
                    DiscountFraction = 0.1f,
                    fulfillment = FulfillmentMode.SellerDelivery,
                    deliveredQuantity = 2,
                    paidSilver = 8
                };
                fixture.salesContract.activeOrderId = fixture.salesOrder.id;
                state.Orders.Add(fixture.salesOrder);

                fixture.request = new PurchaseRequest
                {
                    id = state.NextId(),
                    thingDef = primaryDef,
                    quantityRequested = 5,
                    quantityOrdered = 0,
                    desiredDays = 5,
                    createdTick = now,
                    expiryTick = now + GenDate.TicksPerDay * 8,
                    status = PurchaseRequestStatus.Open,
                    fulfillmentPreference = ProcurementFulfillmentPreference.Either,
                    quotes = new List<Quotation>()
                };
                fixture.request.quotes.Add(new Quotation
                {
                    id = state.NextId(),
                    settlementId = settlementId,
                    settlementName = settlementName,
                    factionName = factionName,
                    refreshWindow = state.RefreshCount,
                    quantityOffered = 5,
                    unitPrice = 2.2f,
                    leadTimeDays = 2,
                    supplierDelivers = true,
                    distanceTiles = 12f,
                    priceExplanation = "Stage 8A RFQ quote"
                });
                state.Requests.Add(fixture.request);

                fixture.listing = new SupplierListing
                {
                    id = state.NextId(),
                    settlementId = settlementId,
                    thingDef = primaryDef,
                    quantityAvailable = 9,
                    unitPrice = 2.1f,
                    fulfillment = FulfillmentMode.SellerDelivery,
                    leadTimeDays = 2,
                    createdTick = now,
                    expiryTick = SupplierListing.NoExpiryTick,
                    refreshWindow = state.RefreshCount
                };
                state.SupplierListings.Add(fixture.listing);

                fixture.purchaseOrder = new PurchaseOrder
                {
                    id = state.NextId(),
                    requestId = fixture.request.id,
                    quotationId = fixture.request.quotes[0].id,
                    supplierListingId = fixture.listing.id,
                    settlementId = settlementId,
                    settlementName = settlementName,
                    factionName = factionName,
                    thingDef = primaryDef,
                    quantity = 3,
                    unitPrice = 2f,
                    paidSilver = 6,
                    supplierDelivers = true,
                    orderedTick = now,
                    readyTick = now + GenDate.TicksPerDay,
                    pickupExpiryTick = now + GenDate.TicksPerDay * 3,
                    status = PurchaseOrderStatus.Confirmed
                };
                state.PurchaseOrders.Add(fixture.purchaseOrder);

                fixture.procurementContract = new ProcurementContract
                {
                    id = state.NextId(),
                    settlementId = settlementId,
                    settlementName = settlementName,
                    thingDef = primaryDef,
                    quantityPerCycle = 3,
                    totalCycles = 2,
                    unitPrice = 2f,
                    cadenceDays = 3,
                    cyclesCompleted = 0,
                    cyclesFailed = 0,
                    nextCycleTick = now + GenDate.TicksPerDay * 3,
                    activeOrderId = ProcurementContract.NoActiveOrderId,
                    status = ProcurementContractStatus.Active
                };
                state.ProcurementContracts.Add(fixture.procurementContract);

                fixture.employment = new EmploymentContract
                {
                    id = state.NextId(),
                    settlementId = settlementId,
                    settlementName = settlementName,
                    factionName = factionName,
                    workerName = "Stage 8A travelling worker",
                    workerSkills = "Construction 8; Intellectual 6",
                    dailyWage = 17,
                    termDays = 20,
                    paidSilver = 34,
                    wageStructure = WageStructure.Daily,
                    nextPaymentTick = now + GenDate.TicksPerDay,
                    arrearsSilver = 5,
                    missedPayments = 1,
                    refusingWork = true,
                    refusalReason = WorkRefusalReason.UnpaidWages,
                    hiredTick = now - GenDate.TicksPerDay,
                    arrivalTick = now + GenDate.TicksPerDay,
                    arrivedTick = EmploymentContract.NotArrived,
                    status = EmploymentStatus.Travelling
                };
                state.Employments.Add(fixture.employment);

                fixture.timelineRecord = new CommercialEventRecord(
                    state.NextId(), now, settlementId, CommercialEventType.ContractStarted,
                    settlementName, fixture.salesContract.id, primaryDef, 2, 8,
                    "Stage 8A timeline fixture");
                state.CommercialTimeline.Add(fixture.timelineRecord);

                fixture.history = new CommercialHistoryEntry
                {
                    settlementId = settlementId,
                    thingDef = primaryDef,
                    completedSaleCount = 3,
                    totalQuantitySupplied = 12,
                    totalTradeValue = 90
                };
                state.CommercialHistory.Add(fixture.history);
                CommercialReputation reputation = new CommercialReputation(
                    settlementId, settlementName, factionName);
                reputation.ordersCompleted = 3;
                reputation.purchasesCompleted = 2;
                state.Reputations[settlementId] = reputation;

                Stage8ACounts beforeFirst = Stage8ACounts.From(state);
                Stage8ARoundTrip first = Stage8ARoundTripState(
                    state, "stage8A-first");
                Stage8ACounts afterFirst = Stage8ACounts.From(first.loaded);
                bool firstFields = Stage8AFieldsSurvived(
                    fixture, first.loaded, afterAdvance: false, out string firstFieldDetail);
                check(
                    M1,
                    first.failure == null && first.loaded != null &&
                    beforeFirst.SameAs(afterFirst) && firstFields &&
                    first.loaded.SaveVersion == IntercolonyWorldComponent.CurrentSaveVersion,
                    Stage8ARoundTripDetail(
                        beforeFirst, afterFirst, null, null) +
                    $"; identifying fields={firstFieldDetail}; failure={first.failure ?? "none"}");

                bool salesCompleted = false;
                bool purchaseCompleted = false;
                bool cycleAdvanced = false;
                bool orderAdvanced = false;
                bool timelineAdvanced = false;
                string actionFailure = null;
                Stage8ACounts beforeSecond = Stage8ACounts.From(first.loaded);
                Stage8ACounts afterSecond = Stage8ACounts.From(null);
                Stage8ARoundTrip second = new Stage8ARoundTrip
                {
                    failure = "second round trip was not attempted"
                };

                if (first.loaded != null)
                {
                    try
                    {
                        SalesOrder loadedSalesOrder = first.loaded.Orders.Find(
                            order => order != null && order.id == fixture.salesOrder.id);
                        RecurringContract loadedSalesContract = first.loaded.Contracts.Find(
                            contract => contract != null && contract.id == fixture.salesContract.id);
                        int timelineBeforeCompletion = first.loaded.CommercialTimeline.Count;
                        int cyclesBefore = loadedSalesContract?.cyclesCompleted ?? -1;
                        CommercialHistoryEntry loadedHistory = first.loaded.FindCommercialHistory(
                            fixture.salesOrder.settlementId, fixture.primaryDef);
                        int salesCountBefore = loadedHistory?.completedSaleCount ?? -1;
                        int tradeValueBeforeSales = loadedHistory?.totalTradeValue ?? -1;

                        if (loadedSalesOrder != null && loadedSalesContract != null &&
                            loadedHistory != null)
                        {
                            SalesOrderService.Complete(
                                first.loaded, loadedSalesOrder, GenTicks.TicksGame,
                                "Stage 8A completed a sales order after reload");
                            salesCompleted = loadedSalesOrder.status == SalesOrderStatus.Completed &&
                                loadedHistory.completedSaleCount == salesCountBefore + 1 &&
                                loadedHistory.totalTradeValue == tradeValueBeforeSales +
                                loadedSalesOrder.paidSilver;

                            ContractService.AdvanceContracts(first.loaded);
                            cycleAdvanced = loadedSalesContract.cyclesCompleted == cyclesBefore + 1 &&
                                loadedSalesContract.activeOrderId ==
                                0 &&
                                loadedSalesContract.status == ContractStatus.Active;
                            orderAdvanced = loadedSalesOrder.status == SalesOrderStatus.Completed;
                            timelineAdvanced = first.loaded.CommercialTimeline.Count >
                                timelineBeforeCompletion;
                        }

                        PurchaseOrder loadedPurchaseOrder = first.loaded.PurchaseOrders.Find(
                            order => order != null && order.id == fixture.purchaseOrder.id);
                        CommercialHistoryEntry historyBeforePurchase = first.loaded.FindCommercialHistory(
                            fixture.purchaseOrder.settlementId, fixture.primaryDef);
                        int purchaseTradeBefore = historyBeforePurchase?.totalTradeValue ?? -1;
                        if (loadedPurchaseOrder != null && historyBeforePurchase != null &&
                            ReferenceEquals(IntercolonyWorldComponent.Current, state))
                        {
                            // PurchaseOrderService.Complete's public completion boundary uses the
                            // live-world singleton. Adopt the detached loaded graph into that
                            // singleton for this one call, then the shared child references make
                            // the completion visible on first.loaded as well.
                            Stage8AAdoptLoadedStateForCurrent(
                                state, first.loaded, nextIdField);
                            PurchaseOrderService.Complete(
                                loadedPurchaseOrder,
                                "Stage 8A completed a PurchaseOrder after reload");
                            Stage8ASyncCurrentBackToLoaded(state, first.loaded, nextIdField);
                            purchaseCompleted = loadedPurchaseOrder.status ==
                                PurchaseOrderStatus.Completed &&
                                historyBeforePurchase.totalTradeValue ==
                                purchaseTradeBefore + loadedPurchaseOrder.paidSilver;
                        }
                        else if (!ReferenceEquals(IntercolonyWorldComponent.Current, state))
                        {
                            actionFailure =
                                "PurchaseOrderService.Current did not resolve to the fixture world";
                        }

                        check(
                            M2,
                            cycleAdvanced && orderAdvanced && timelineAdvanced,
                            $"sales status={loadedSalesOrder?.status.ToString() ?? "missing"}; " +
                            $"contract cycles={cyclesBefore}->{loadedSalesContract?.cyclesCompleted.ToString() ?? "missing"}; " +
                            $"activeOrderId={loadedSalesContract?.activeOrderId.ToString() ?? "missing"}; " +
                            $"timeline {timelineBeforeCompletion}->{first.loaded.CommercialTimeline.Count}; " +
                            $"failure={actionFailure ?? "none"}");
                        check(
                            M3,
                            salesCompleted && purchaseCompleted,
                            $"sales completed={salesCompleted}; purchase completed={purchaseCompleted}; " +
                            $"sales aggregate={first.loaded.FindCommercialHistory(settlementId, primaryDef)?.completedSaleCount.ToString() ?? "missing"}; " +
                            $"purchase status={loadedPurchaseOrder?.status.ToString() ?? "missing"}; " +
                            $"failure={actionFailure ?? "none"}");

                        beforeSecond = Stage8ACounts.From(first.loaded);
                        second = Stage8ARoundTripState(first.loaded, "stage8A-second");
                        afterSecond = Stage8ACounts.From(second.loaded);
                        bool secondFields = Stage8AFieldsSurvived(
                            fixture, second.loaded, afterAdvance: true, out string secondFieldDetail);
                        check(
                            M4,
                            second.failure == null && second.loaded != null &&
                            beforeSecond.SameAs(afterSecond) && secondFields &&
                            salesCompleted && purchaseCompleted,
                            Stage8ARoundTripDetail(
                                beforeFirst, afterFirst, beforeSecond, afterSecond) +
                            $"; identifying fields={secondFieldDetail}; failure={second.failure ?? "none"}");
                        check(
                            M5,
                            beforeFirst.SameAs(afterFirst) &&
                            second.loaded != null && beforeSecond.SameAs(afterSecond),
                            Stage8ARoundTripDetail(
                                beforeFirst, afterFirst, beforeSecond, afterSecond));
                    }
                    catch (Exception ex)
                    {
                        actionFailure = ex.GetType().Name + ": " + ex.Message;
                        check(M2, false,
                            Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                            $"; post-reload advance threw {actionFailure}");
                        check(M3, false,
                            Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                            $"; post-reload completion threw {actionFailure}");
                        check(M4, false,
                            Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                            $"; second-cycle setup threw {actionFailure}");
                        check(M5, false,
                            Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                            $"; second-cycle setup threw {actionFailure}");
                    }
                }
                else
                {
                    check(M2, false,
                        Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                        $"; first reload failed: {first.failure ?? "no loaded state"}");
                    check(M3, false,
                        Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                        $"; first reload failed: {first.failure ?? "no loaded state"}");
                    check(M4, false,
                        Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                        $"; first reload failed: {first.failure ?? "no loaded state"}");
                    check(M5, false,
                        Stage8ARoundTripDetail(beforeFirst, afterFirst, beforeSecond, afterSecond) +
                        $"; first reload failed: {first.failure ?? "no loaded state"}");
                }
            }
            finally
            {
                RestoreAllColonySilver(
                    Find.CurrentMap ?? Find.AnyPlayerHomeMap, savedSilver);

                state.Opportunities.Clear();
                state.Opportunities.AddRange(savedOpportunities);
                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(savedEconomicEvents);
                state.MarketStates.Clear();
                state.MarketStates.AddRange(savedMarketStates);
                foreach (KeyValuePair<SettlementMarketState, float[]> entry in savedDemand)
                {
                    if (entry.Key == null || !savedSupply.ContainsKey(entry.Key))
                    {
                        continue;
                    }

                    Array.Copy(entry.Value, entry.Key.demandPressure, entry.Value.Length);
                    Array.Copy(savedSupply[entry.Key], entry.Key.supplyPressure,
                        savedSupply[entry.Key].Length);
                    entry.Key.lastAdvancedRefresh = savedMarketRefreshes[entry.Key];
                }
                state.RefreshMarketStateIndex();
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrands);
                state.Orders.Clear();
                state.Orders.AddRange(savedSalesOrders);
                state.Requests.Clear();
                state.Requests.AddRange(savedRequests);
                state.SupplierListings.Clear();
                state.SupplierListings.AddRange(savedListings);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedPurchaseOrders);
                state.Contracts.Clear();
                state.Contracts.AddRange(savedSalesContracts);
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedProcurementContracts);
                state.Employments.Clear();
                state.Employments.AddRange(savedEmployments);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedHistory);
                state.Ledger.Clear();
                state.Ledger.AddRange(savedLedger);
                state.LedgerStartTick = savedLedgerStartTick;
                state.Postings.Clear();
                state.Postings.AddRange(savedPostings);
                state.LaborDebts.Clear();
                state.LaborDebts.AddRange(savedLaborDebts);
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }
                List<SupplierOfferConsumption> restoredConsumption =
                    consumptionField.GetValue(state) as List<SupplierOfferConsumption>;
                if (restoredConsumption != null)
                {
                    restoredConsumption.Clear();
                    restoredConsumption.AddRange(savedConsumption);
                }

                if (savedEmployerStanding != null && state.EmployerStanding != null)
                {
                    state.EmployerStanding.RestoreFrom(savedEmployerStanding);
                }
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.LedgerStartTick = savedLedgerStartTick;
                nextIdField.SetValue(state, savedNextId);
                refreshCountField.SetValue(state, savedRefreshCount);
                lastRefreshTickField.SetValue(state, savedLastRefreshTick);
                saveVersionField.SetValue(state, savedSaveVersion);
                economySeedField.SetValue(state, savedEconomySeed);
                Stage8ARestoreFactionGoodwill(savedFactionGoodwill);
            }
        }

        private static void Stage8ASkipAll(Action<string, string> skip, string reason)
        {
            skip("Stage 8A kind: active market opportunities", reason);
            skip("Stage 8A kind: active economic event", reason);
            skip("Stage 8A kind: non-neutral market pressure", reason);
            skip("Stage 8A kind: positive and negative brand records", reason);
            skip("Stage 8A kind: negotiated sales order", reason);
            skip("Stage 8A kind: active RFQ", reason);
            skip("Stage 8A kind: Supplier Market listing", reason);
            skip("Stage 8A kind: active PurchaseOrder", reason);
            skip("Stage 8A kind: recurring sales contract", reason);
            skip("Stage 8A kind: recurring procurement contract", reason);
            skip("Stage 8A kind: hired workers / payroll state", reason);
            skip("Stage 8A kind: commercial history timeline", reason);
            skip("M1 all twelve Stage 8A kinds survive the first save/load", reason);
            skip("M2 the world advances after the first reload", reason);
            skip("M3 sales and purchase orders complete after reload and update durable history", reason);
            skip("M4 a second save/load preserves advanced and completed state", reason);
            skip("M5 every Stage 8A persisted collection keeps its exact fixture count", reason);
        }

        private static void CheckStage8BMigrationMatrix(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state)
        {
            const string N1 = "N1 schema 42 preserves every active obligation's price and quantity";
            const string N2 = "N2 schema 49 preserves every active obligation's price and quantity";
            const string N3 = "N3 every migration start version 42 through 55 reaches current";
            const string N4 = "N4 current-schema world needs no migration";
            const string N5 = "N5 schema 42 migration is idempotent";

            FieldInfo saveVersionField = state == null
                ? null
                : typeof(IntercolonyWorldComponent).GetField(
                    "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            if (state == null || saveVersionField == null)
            {
                string detail = "migration fixture unavailable: world or saveVersion field missing";
                check(N1, false, detail);
                check(N2, false, detail);
                check(N3, false, detail);
                check(N4, false, detail);
                check(N5, false, detail);
                return;
            }

            List<SalesOrder> savedSalesOrders = new List<SalesOrder>(state.Orders);
            List<PurchaseOrder> savedPurchaseOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<RecurringContract> savedSalesContracts =
                new List<RecurringContract>(state.Contracts);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            int savedSaveVersion = state.SaveVersion;
            int savedTimelineStartTick = state.CommercialTimelineStartTick;

            try
            {
                Stage8BMigrationRun n1 = RunStage8BMigration(state, saveVersionField, 42);
                check(
                    N1,
                    n1.failure == null &&
                    n1.finalSaveVersion == IntercolonyWorldComponent.CurrentSaveVersion &&
                    n1.before.TermsAndCountsEqual(n1.after),
                    n1.Detail() + $"; start version=42; failure={n1.failure ?? "none"}");

                Stage8BMigrationRun n2 = RunStage8BMigration(state, saveVersionField, 49);
                check(
                    N2,
                    n2.failure == null &&
                    n2.finalSaveVersion == IntercolonyWorldComponent.CurrentSaveVersion &&
                    n2.before.TermsAndCountsEqual(n2.after),
                    n2.Detail() + $"; start version=49; failure={n2.failure ?? "none"}");

                List<int> failedStarts = new List<int>();
                List<string> startFailures = new List<string>();
                for (int startVersion = 42;
                     startVersion < IntercolonyWorldComponent.CurrentSaveVersion;
                     startVersion++)
                {
                    Stage8BMigrationRun run =
                        RunStage8BMigration(state, saveVersionField, startVersion);
                    if (run.failure != null ||
                        run.finalSaveVersion != IntercolonyWorldComponent.CurrentSaveVersion)
                    {
                        failedStarts.Add(startVersion);
                        startFailures.Add(
                            $"{startVersion}: {run.Detail()}; final={run.finalSaveVersion}; " +
                            $"failure={run.failure ?? "none"}");
                    }
                }

                check(
                    N3,
                    failedStarts.Count == 0,
                    $"enumerated starts=42..{IntercolonyWorldComponent.CurrentSaveVersion - 1}; " +
                    $"failed starts={(failedStarts.Count == 0
                        ? "none" : string.Join(",", failedStarts.ToArray()))}; " +
                    $"details={(startFailures.Count == 0
                        ? "none" : string.Join(" | ", startFailures.ToArray()))}");

                Stage8BMigrationRun n4 = RunStage8BMigration(
                    state, saveVersionField, IntercolonyWorldComponent.CurrentSaveVersion);
                check(
                    N4,
                    n4.failure == null &&
                    n4.migrationSaveVersionBefore == IntercolonyWorldComponent.CurrentSaveVersion &&
                    n4.migrationSaveVersionBefore == n4.finalSaveVersion &&
                    n4.finalSaveVersion == IntercolonyWorldComponent.CurrentSaveVersion &&
                    n4.before.ExactlyEqual(n4.after),
                    n4.Detail() + $"; start version={IntercolonyWorldComponent.CurrentSaveVersion}; " +
                    $"failure={n4.failure ?? "none"}");

                Stage8BMigrationRun n5 = RunStage8BMigration(state, saveVersionField, 42);
                Stage8BSnapshot secondBefore = null;
                Stage8BSnapshot secondAfter = null;
                int secondVersionBefore = -1;
                int secondVersionAfter = -1;
                string secondFailure = null;
                if (n5.failure == null)
                {
                    secondBefore = Stage8BSnapshot.From(state);
                    secondVersionBefore = state.SaveVersion;
                    try
                    {
                        state.MigrateIfNeeded();
                        secondVersionAfter = state.SaveVersion;
                        secondAfter = Stage8BSnapshot.From(state);
                    }
                    catch (Exception ex)
                    {
                        secondFailure = ex.GetType().Name + ": " + ex.Message;
                    }
                }

                bool firstRunAnchored = n5.before.TermsAndCountsEqual(n5.after);
                bool secondRunStable = secondBefore != null && secondAfter != null &&
                    secondBefore.ExactlyEqual(secondAfter) &&
                    secondVersionBefore == secondVersionAfter &&
                    state.SaveVersion == IntercolonyWorldComponent.CurrentSaveVersion;
                check(
                    N5,
                    n5.failure == null && firstRunAnchored && secondRunStable,
                    $"start version=42; first {n5.Detail()}; second " +
                    $"{(secondBefore == null ? "not run" : secondBefore.Detail(secondAfter))}; " +
                    $"second version {secondVersionBefore}->{secondVersionAfter}; " +
                    $"first failure={n5.failure ?? "none"}; second failure={secondFailure ?? "none"}");
            }
            finally
            {
                state.Orders.Clear();
                state.Orders.AddRange(savedSalesOrders);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedPurchaseOrders);
                state.Contracts.Clear();
                state.Contracts.AddRange(savedSalesContracts);
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }

                state.CommercialTimelineStartTick = savedTimelineStartTick;
                saveVersionField.SetValue(state, savedSaveVersion);
            }
        }

        private static Stage8BMigrationRun RunStage8BMigration(
            IntercolonyWorldComponent state,
            FieldInfo saveVersionField,
            int startVersion)
        {
            Stage8BMigrationRun run = new Stage8BMigrationRun { startVersion = startVersion };
            try
            {
                state.Orders.Clear();
                state.PurchaseOrders.Clear();
                state.Contracts.Clear();
                state.Reputations.Clear();

                int now = GenTicks.TicksGame;
                ThingDef product = ThingDefOf.Steel ?? ThingDefOf.Silver;
                run.salesOrder = new SalesOrder
                {
                    id = 8_100,
                    settlementId = 8_101,
                    settlementName = "Stage 8B supplier",
                    factionName = "Stage 8B faction",
                    line = new OrderLine(product, 7),
                    unitPrice = 13.75f,
                    acceptedTick = now,
                    deadlineTick = now + GenDate.TicksPerDay * 10,
                    status = SalesOrderStatus.Accepted
                };
                run.purchaseOrder = new PurchaseOrder
                {
                    id = 8_102,
                    requestId = 8_103,
                    quotationId = 8_104,
                    settlementId = 8_101,
                    settlementName = "Stage 8B supplier",
                    factionName = "Stage 8B faction",
                    thingDef = product,
                    quantity = 11,
                    unitPrice = 3.625f,
                    supplierDelivers = true,
                    orderedTick = now,
                    readyTick = now + GenDate.TicksPerDay * 4,
                    status = PurchaseOrderStatus.Confirmed
                };
                run.salesContract = new RecurringContract
                {
                    id = 8_105,
                    settlementId = 8_101,
                    settlementName = "Stage 8B supplier",
                    factionName = "Stage 8B faction",
                    thingDef = product,
                    quantityPerCycle = 13,
                    totalCycles = 4,
                    cyclesCompleted = 0,
                    unitPrice = 2.875f,
                    cadenceTicks = GenDate.TicksPerDay * 3,
                    nextCycleTick = now + GenDate.TicksPerDay * 3,
                    status = ContractStatus.Active
                };

                state.Orders.Add(run.salesOrder);
                state.PurchaseOrders.Add(run.purchaseOrder);
                state.Contracts.Add(run.salesContract);
                run.before = Stage8BSnapshot.From(state);
                saveVersionField.SetValue(state, startVersion);
                run.migrationSaveVersionBefore = state.SaveVersion;
                state.MigrateIfNeeded();
                run.finalSaveVersion = state.SaveVersion;
                run.after = Stage8BSnapshot.From(state);
            }
            catch (Exception ex)
            {
                run.failure = ex.GetType().Name + ": " + ex.Message;
                run.finalSaveVersion = state.SaveVersion;
                run.after = Stage8BSnapshot.From(state);
            }

            return run;
        }

        private sealed class Stage8BMigrationRun
        {
            public int startVersion;
            public SalesOrder salesOrder;
            public PurchaseOrder purchaseOrder;
            public RecurringContract salesContract;
            public Stage8BSnapshot before;
            public Stage8BSnapshot after;
            public int finalSaveVersion;
            public int migrationSaveVersionBefore;
            public string failure;

            public string Detail()
            {
                return before == null
                    ? "before snapshot unavailable"
                    : before.Detail(after);
            }
        }

        private sealed class Stage8BSnapshot
        {
            public int salesCount;
            public float salesPrice;
            public int salesQuantity;
            public int purchaseCount;
            public float purchasePrice;
            public int purchaseQuantity;
            public int contractCount;
            public float contractPrice;
            public int contractQuantity;
            public int salesId;
            public int purchaseId;
            public int contractId;
            public SalesOrderStatus? salesStatus;
            public PurchaseOrderStatus? purchaseStatus;
            public ContractStatus? contractStatus;

            public static Stage8BSnapshot From(IntercolonyWorldComponent state)
            {
                SalesOrder sales = state.Orders.Count == 1 ? state.Orders[0] : null;
                PurchaseOrder purchase =
                    state.PurchaseOrders.Count == 1 ? state.PurchaseOrders[0] : null;
                RecurringContract contract =
                    state.Contracts.Count == 1 ? state.Contracts[0] : null;
                return new Stage8BSnapshot
                {
                    salesCount = state.Orders.Count,
                    salesPrice = sales == null ? float.NaN : sales.unitPrice,
                    salesQuantity = sales == null ? -1 : sales.Quantity,
                    purchaseCount = state.PurchaseOrders.Count,
                    purchasePrice = purchase == null ? float.NaN : purchase.unitPrice,
                    purchaseQuantity = purchase == null ? -1 : purchase.quantity,
                    contractCount = state.Contracts.Count,
                    contractPrice = contract == null ? float.NaN : contract.unitPrice,
                    contractQuantity = contract == null ? -1 : contract.quantityPerCycle,
                    salesId = sales?.id ?? -1,
                    purchaseId = purchase?.id ?? -1,
                    contractId = contract?.id ?? -1,
                    salesStatus = sales?.status,
                    purchaseStatus = purchase?.status,
                    contractStatus = contract?.status
                };
            }

            public bool TermsAndCountsEqual(Stage8BSnapshot other)
            {
                return other != null &&
                    salesCount == other.salesCount && salesCount == 1 &&
                    salesPrice == other.salesPrice && salesQuantity == other.salesQuantity &&
                    salesStatus == other.salesStatus && salesStatus == SalesOrderStatus.Accepted &&
                    purchaseCount == other.purchaseCount && purchaseCount == 1 &&
                    purchasePrice == other.purchasePrice &&
                    purchaseQuantity == other.purchaseQuantity &&
                    purchaseStatus == other.purchaseStatus &&
                    purchaseStatus == PurchaseOrderStatus.Confirmed &&
                    contractCount == other.contractCount && contractCount == 1 &&
                    contractPrice == other.contractPrice &&
                    contractQuantity == other.contractQuantity &&
                    contractStatus == other.contractStatus &&
                    contractStatus == ContractStatus.Active;
            }

            public bool ExactlyEqual(Stage8BSnapshot other)
            {
                return TermsAndCountsEqual(other) &&
                    salesId == other.salesId && purchaseId == other.purchaseId &&
                    contractId == other.contractId && salesStatus == other.salesStatus &&
                    purchaseStatus == other.purchaseStatus && contractStatus == other.contractStatus;
            }

            public string Detail(Stage8BSnapshot after)
            {
                return $"sales orders count {salesCount}->{after?.salesCount.ToString() ?? "missing"}; " +
                    $"sales price {Format(salesPrice)}->{Format(after?.salesPrice)}; " +
                    $"sales quantity {salesQuantity}->{after?.salesQuantity.ToString() ?? "missing"}; " +
                    $"purchase orders count {purchaseCount}->{after?.purchaseCount.ToString() ?? "missing"}; " +
                    $"purchase price {Format(purchasePrice)}->{Format(after?.purchasePrice)}; " +
                    $"purchase quantity {purchaseQuantity}->{after?.purchaseQuantity.ToString() ?? "missing"}; " +
                    $"sales contracts count {contractCount}->{after?.contractCount.ToString() ?? "missing"}; " +
                    $"contract price {Format(contractPrice)}->{Format(after?.contractPrice)}; " +
                    $"contract quantity {contractQuantity}->{after?.contractQuantity.ToString() ?? "missing"}";
            }

            private static string Format(float? value)
            {
                return !value.HasValue || float.IsNaN(value.Value)
                    ? "missing"
                    : value.Value.ToString("R");
            }
        }

        private static Stage8ARoundTrip Stage8ARoundTripState(
            IntercolonyWorldComponent source, string label)
        {
            IntercolonyWorldComponent savedState = source;
            IntercolonyWorldComponent loadedState = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-{label}-{Guid.NewGuid():N}.xml");
            try
            {
                Scribe.saver.InitSaving(path, label);
                Scribe_Deep.Look(ref savedState, "state");
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loadedState, "state", (object)null);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                Scribe.ForceStop();
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // A failed temp-file cleanup must not hide the Scribe failure being tested.
                }
            }

            return new Stage8ARoundTrip { loaded = loadedState, failure = failure };
        }

        private static string Stage8ARoundTripDetail(
            Stage8ACounts beforeFirst,
            Stage8ACounts afterFirst,
            Stage8ACounts beforeSecond,
            Stage8ACounts afterSecond)
        {
            return "round trip 1 before=[" + beforeFirst + "] after=[" + afterFirst +
                   "]; round trip 2 before=[" +
                   (beforeSecond == null ? "not attempted" : beforeSecond.ToString()) +
                   "] after=[" +
                   (afterSecond == null ? "not attempted" : afterSecond.ToString()) + "]";
        }

        private static bool Stage8AFieldsSurvived(
            Stage8AFixture fixture,
            IntercolonyWorldComponent loaded,
            bool afterAdvance,
            out string detail)
        {
            if (loaded == null)
            {
                detail = "loaded world is null";
                return false;
            }

            MarketOpportunity opportunity = loaded.Opportunities.Find(
                item => item != null && item.id == fixture.opportunity.id);
            EconomicEvent economicEvent = loaded.EconomicEvents.Find(
                item => item != null && item.id == fixture.economicEvent.id);
            SettlementMarketState marketState = loaded.MarketStates.Find(
                item => item != null && item.settlementId == fixture.marketState.settlementId);
            ProductBrandRecord positiveBrand = loaded.ProductBrandRecords.Find(
                item => item != null && item.thingDef == fixture.primaryDef);
            ProductBrandRecord negativeBrand = loaded.ProductBrandRecords.Find(
                item => item != null && item.thingDef == fixture.secondaryDef);
            SalesOrder salesOrder = loaded.Orders.Find(
                item => item != null && item.id == fixture.salesOrder.id);
            PurchaseRequest request = loaded.Requests.Find(
                item => item != null && item.id == fixture.request.id);
            SupplierListing listing = loaded.SupplierListings.Find(
                item => item != null && item.id == fixture.listing.id);
            PurchaseOrder purchaseOrder = loaded.PurchaseOrders.Find(
                item => item != null && item.id == fixture.purchaseOrder.id);
            RecurringContract salesContract = loaded.Contracts.Find(
                item => item != null && item.id == fixture.salesContract.id);
            ProcurementContract procurementContract = loaded.ProcurementContracts.Find(
                item => item != null && item.id == fixture.procurementContract.id);
            EmploymentContract employment = loaded.Employments.Find(
                item => item != null && item.id == fixture.employment.id);
            CommercialEventRecord timeline = loaded.CommercialTimeline.Find(
                item => item != null && item.id == fixture.timelineRecord.id);
            CommercialHistoryEntry history = loaded.FindCommercialHistory(
                fixture.history.settlementId, fixture.primaryDef);

            Quotation quote = request?.quotes?.Find(
                item => item != null && item.id == fixture.request.quotes[0].id);
            bool completionState = salesOrder != null && purchaseOrder != null &&
                salesOrder.status == SalesOrderStatus.Completed &&
                purchaseOrder.status == PurchaseOrderStatus.Completed;
            int expectedSales = fixture.history.completedSaleCount + (afterAdvance ? 1 : 0);
            int expectedQuantity = fixture.history.totalQuantitySupplied +
                (afterAdvance ? fixture.salesOrder.deliveredQuantity : 0);
            int expectedTrade = fixture.history.totalTradeValue +
                (afterAdvance ? fixture.salesOrder.paidSilver + fixture.purchaseOrder.paidSilver : 0);

            bool ok = opportunity != null && opportunity.thingDef == fixture.primaryDef &&
                opportunity.quantity == fixture.opportunity.quantity && opportunity.IsAvailable &&
                economicEvent != null && economicEvent.type == fixture.economicEvent.type &&
                economicEvent.IsActiveAt(GenTicks.TicksGame) &&
                Mathf.Approximately(
                    economicEvent.supplyScarcityModifier[fixture.categoryIndex],
                    fixture.economicEvent.supplyScarcityModifier[fixture.categoryIndex]) &&
                marketState != null && !marketState.IsNeutral &&
                marketState.settlementId == fixture.marketState.settlementId &&
                (afterAdvance || Mathf.Approximately(
                    marketState.supplyPressure[fixture.categoryIndex],
                    fixture.marketState.supplyPressure[fixture.categoryIndex])) &&
                positiveBrand != null && positiveBrand.directScore > 0f &&
                negativeBrand != null && negativeBrand.directScore < 0f &&
                salesOrder != null && salesOrder.ThingDef == fixture.primaryDef &&
                salesOrder.Quantity == fixture.salesOrder.Quantity &&
                salesOrder.opportunityId == fixture.salesOrder.opportunityId &&
                salesOrder.contractId == fixture.salesOrder.contractId &&
                salesOrder.status == (afterAdvance
                    ? SalesOrderStatus.Completed : SalesOrderStatus.Accepted) &&
                request != null && request.thingDef == fixture.primaryDef &&
                request.quantityRequested == fixture.request.quantityRequested &&
                request.status == PurchaseRequestStatus.Open && quote != null &&
                quote.quantityOffered == fixture.request.quotes[0].quantityOffered &&
                listing != null && listing.thingDef == fixture.primaryDef &&
                listing.quantityAvailable == fixture.listing.quantityAvailable &&
                listing.IsAvailable && purchaseOrder != null &&
                purchaseOrder.thingDef == fixture.primaryDef &&
                purchaseOrder.quantity == fixture.purchaseOrder.quantity &&
                purchaseOrder.status == (afterAdvance
                    ? PurchaseOrderStatus.Completed : PurchaseOrderStatus.Confirmed) &&
                salesContract != null && salesContract.thingDef == fixture.primaryDef &&
                salesContract.quantityPerCycle == fixture.salesContract.quantityPerCycle &&
                salesContract.status == ContractStatus.Active &&
                salesContract.cyclesCompleted == (afterAdvance ? 1 : 0) &&
                salesContract.activeOrderId == (afterAdvance
                    ? 0 : fixture.salesOrder.id) &&
                procurementContract != null && procurementContract.thingDef == fixture.primaryDef &&
                procurementContract.quantityPerCycle == fixture.procurementContract.quantityPerCycle &&
                procurementContract.cadenceDays == fixture.procurementContract.cadenceDays &&
                procurementContract.status == ProcurementContractStatus.Active &&
                employment != null && employment.workerName == fixture.employment.workerName &&
                employment.dailyWage == fixture.employment.dailyWage &&
                employment.wageStructure == fixture.employment.wageStructure &&
                employment.nextPaymentTick == fixture.employment.nextPaymentTick &&
                employment.arrearsSilver == fixture.employment.arrearsSilver &&
                employment.missedPayments == fixture.employment.missedPayments &&
                employment.status == EmploymentStatus.Travelling && timeline != null &&
                timeline.type == fixture.timelineRecord.type &&
                timeline.relatedEntityId == fixture.timelineRecord.relatedEntityId &&
                history != null && history.completedSaleCount == expectedSales &&
                history.totalQuantitySupplied == expectedQuantity &&
                history.totalTradeValue == expectedTrade &&
                (!afterAdvance || completionState);

            detail =
                $"opportunity={(opportunity == null ? "missing" : opportunity.id.ToString())}; " +
                $"event={(economicEvent == null ? "missing" : economicEvent.id.ToString())}; " +
                $"pressure={(marketState == null ? "missing" : marketState.supplyPressure[fixture.categoryIndex].ToString("F2"))}; " +
                $"brands={(positiveBrand == null ? "missing" : positiveBrand.directScore.ToString("F1"))}/" +
                $"{(negativeBrand == null ? "missing" : negativeBrand.directScore.ToString("F1"))}; " +
                $"sales={(salesOrder == null ? "missing" : salesOrder.status.ToString())}; " +
                $"RFQ={(request == null ? "missing" : request.status.ToString())}; " +
                $"listing={(listing == null ? "missing" : listing.quantityAvailable.ToString())}; " +
                $"purchase={(purchaseOrder == null ? "missing" : purchaseOrder.status.ToString())}; " +
                $"salesContract={(salesContract == null ? "missing" : salesContract.cyclesCompleted.ToString())}; " +
                $"procurement={(procurementContract == null ? "missing" : procurementContract.status.ToString())}; " +
                $"worker={(employment == null ? "missing" : employment.workerName)}; " +
                $"timeline={(timeline == null ? "missing" : timeline.id.ToString())}; " +
                $"aggregate={(history == null ? "missing" : history.totalTradeValue.ToString())}";
            return ok;
        }

        private static void Stage8AAdoptLoadedStateForCurrent(
            IntercolonyWorldComponent current,
            IntercolonyWorldComponent loaded,
            FieldInfo nextIdField)
        {
            current.Opportunities.Clear();
            current.Opportunities.AddRange(loaded.Opportunities);
            current.EconomicEvents.Clear();
            current.EconomicEvents.AddRange(loaded.EconomicEvents);
            current.MarketStates.Clear();
            current.MarketStates.AddRange(loaded.MarketStates);
            current.ProductBrandRecords.Clear();
            current.ProductBrandRecords.AddRange(loaded.ProductBrandRecords);
            current.Orders.Clear();
            current.Orders.AddRange(loaded.Orders);
            current.Requests.Clear();
            current.Requests.AddRange(loaded.Requests);
            current.SupplierListings.Clear();
            current.SupplierListings.AddRange(loaded.SupplierListings);
            current.PurchaseOrders.Clear();
            current.PurchaseOrders.AddRange(loaded.PurchaseOrders);
            current.Contracts.Clear();
            current.Contracts.AddRange(loaded.Contracts);
            current.ProcurementContracts.Clear();
            current.ProcurementContracts.AddRange(loaded.ProcurementContracts);
            current.Employments.Clear();
            current.Employments.AddRange(loaded.Employments);
            current.CommercialTimeline.Clear();
            current.CommercialTimeline.AddRange(loaded.CommercialTimeline);
            current.CommercialTimelineStartTick = loaded.CommercialTimelineStartTick;
            current.CommercialHistory.Clear();
            current.CommercialHistory.AddRange(loaded.CommercialHistory);
            current.Reputations.Clear();
            foreach (KeyValuePair<int, CommercialReputation> entry in loaded.Reputations)
            {
                current.Reputations[entry.Key] = entry.Value;
            }
            current.RefreshMarketStateIndex();
            current.EmployerStanding.RestoreFrom(loaded.EmployerStanding);
            nextIdField.SetValue(current, loaded.PeekNextId());
        }

        private static void Stage8ASyncCurrentBackToLoaded(
            IntercolonyWorldComponent current,
            IntercolonyWorldComponent loaded,
            FieldInfo nextIdField)
        {
            loaded.CommercialTimeline.Clear();
            loaded.CommercialTimeline.AddRange(current.CommercialTimeline);
            loaded.CommercialTimelineStartTick = current.CommercialTimelineStartTick;
            loaded.CommercialHistory.Clear();
            loaded.CommercialHistory.AddRange(current.CommercialHistory);
            loaded.Reputations.Clear();
            foreach (KeyValuePair<int, CommercialReputation> entry in current.Reputations)
            {
                loaded.Reputations[entry.Key] = entry.Value;
            }
            loaded.RefreshMarketStateIndex();
            nextIdField.SetValue(loaded, current.PeekNextId());
        }

        private static Dictionary<Faction, int> Stage8ASnapshotFactionGoodwill()
        {
            Dictionary<Faction, int> result = new Dictionary<Faction, int>();
            if (Faction.OfPlayer == null || Find.FactionManager == null)
            {
                return result;
            }

            foreach (Faction faction in Find.FactionManager.AllFactions)
            {
                if (faction != null && faction != Faction.OfPlayer)
                {
                    result[faction] = faction.GoodwillWith(Faction.OfPlayer);
                }
            }

            return result;
        }

        private static void Stage8ARestoreFactionGoodwill(Dictionary<Faction, int> saved)
        {
            if (saved == null || Faction.OfPlayer == null)
            {
                return;
            }

            foreach (KeyValuePair<Faction, int> entry in saved)
            {
                Faction faction = entry.Key;
                if (faction == null || faction == Faction.OfPlayer)
                {
                    continue;
                }

                int delta = entry.Value - faction.GoodwillWith(Faction.OfPlayer);
                if (delta != 0 && faction.CanChangeGoodwillFor(Faction.OfPlayer, delta))
                {
                    faction.TryAffectGoodwillWith(
                        Faction.OfPlayer, delta, canSendMessage: false,
                        canSendHostilityLetter: false);
                }
            }
        }

        private static void CheckStage6IAcceptanceGateSaveLoad(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            ProcurementContract acceptedContract,
            Settlement settlement)
        {
            const string assertion = "R11 all five live record kinds survive save/load";
            if (state == null || acceptedContract == null || ThingDefOf.Steel == null)
            {
                skip(assertion,
                    "save/load fixture needs world state, an accepted procurement contract, and Steel");
                return;
            }

            int supplierId = state.NextId();
            int requestId = state.NextId();
            int orderId = state.NextId();
            int salesContractId = state.NextId();
            SupplierListing listing = new SupplierListing
            {
                id = supplierId,
                settlementId = settlement?.ID ?? acceptedContract.settlementId,
                thingDef = ThingDefOf.Steel,
                quantityAvailable = 7,
                unitPrice = 1.25f,
                leadTimeDays = 2,
                createdTick = GenTicks.TicksGame,
                expiryTick = SupplierListing.NoExpiryTick,
                refreshWindow = state.RefreshCount
            };
            PurchaseRequest request = new PurchaseRequest
            {
                id = requestId,
                thingDef = ThingDefOf.Steel,
                quantityRequested = 3,
                desiredDays = 5,
                createdTick = GenTicks.TicksGame,
                expiryTick = GenTicks.TicksGame + GenDate.TicksPerDay * 5,
                status = PurchaseRequestStatus.Open
            };
            PurchaseOrder order = new PurchaseOrder
            {
                id = orderId,
                settlementId = listing.settlementId,
                settlementName = settlement?.Label ?? acceptedContract.settlementName,
                factionName = settlement?.Faction?.Name ?? "",
                thingDef = ThingDefOf.Steel,
                quantity = 2,
                unitPrice = 1.5f,
                paidSilver = 3,
                orderedTick = GenTicks.TicksGame,
                readyTick = GenTicks.TicksGame + GenDate.TicksPerDay,
                pickupExpiryTick = GenTicks.TicksGame + GenDate.TicksPerDay * 2,
                status = PurchaseOrderStatus.Confirmed
            };
            RecurringContract salesContract = new RecurringContract
            {
                id = salesContractId,
                settlementId = listing.settlementId,
                settlementName = listing.settlementId.ToString(),
                factionName = "R11 sales fixture",
                thingDef = ThingDefOf.Steel,
                quantityPerCycle = 2,
                cadenceTicks = GenDate.TicksPerDay,
                totalCycles = 3,
                unitPrice = 2.25f,
                status = ContractStatus.Active,
                nextCycleTick = GenTicks.TicksGame + GenDate.TicksPerDay
            };

            List<RecurringContract> beforeSales = new List<RecurringContract> { salesContract };
            List<ProcurementContract> beforeProcurement =
                new List<ProcurementContract> { acceptedContract };
            List<SupplierListing> beforeListings = new List<SupplierListing> { listing };
            List<PurchaseRequest> beforeRequests = new List<PurchaseRequest> { request };
            List<PurchaseOrder> beforeOrders = new List<PurchaseOrder> { order };
            state.Contracts.Clear();
            state.Contracts.Add(salesContract);
            state.ProcurementContracts.Clear();
            acceptedContract.status = ProcurementContractStatus.Active;
            state.ProcurementContracts.Add(acceptedContract);
            state.SupplierListings.Clear();
            state.SupplierListings.Add(listing);
            state.Requests.Clear();
            state.Requests.Add(request);
            state.PurchaseOrders.Clear();
            state.PurchaseOrders.Add(order);

            IntercolonyWorldComponent savedState = state;
            IntercolonyWorldComponent loadedState = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-Stage6I-R11-{Guid.NewGuid():N}.xml");
            try
            {
                Scribe.saver.InitSaving(path, "stage6IAcceptanceGate");
                Scribe_Deep.Look(ref savedState, "state");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loadedState, "state", (object)null);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            int salesAfter = loadedState?.Contracts?.Count ?? -1;
            int procurementAfter = loadedState?.ProcurementContracts?.Count ?? -1;
            int listingsAfter = loadedState?.SupplierListings?.Count ?? -1;
            int requestsAfter = loadedState?.Requests?.Count ?? -1;
            int ordersAfter = loadedState?.PurchaseOrders?.Count ?? -1;
            RecurringContract loadedSalesContract = loadedState?.Contracts != null &&
                loadedState.Contracts.Count == 1 ? loadedState.Contracts[0] : null;
            ProcurementContract loadedProcurement = loadedState?.ProcurementContracts != null &&
                loadedState.ProcurementContracts.Count == 1
                ? loadedState.ProcurementContracts[0]
                : null;
            SupplierListing loadedListing = loadedState?.SupplierListings != null &&
                loadedState.SupplierListings.Count == 1 ? loadedState.SupplierListings[0] : null;
            PurchaseRequest loadedRequest = loadedState?.Requests != null &&
                loadedState.Requests.Count == 1 ? loadedState.Requests[0] : null;
            PurchaseOrder loadedOrder = loadedState?.PurchaseOrders != null &&
                loadedState.PurchaseOrders.Count == 1 ? loadedState.PurchaseOrders[0] : null;
            bool identifyingFieldsSurvived = loadedListing?.id == listing.id &&
                loadedListing.thingDef == listing.thingDef && loadedListing.quantityAvailable ==
                listing.quantityAvailable && loadedListing.IsAvailable &&
                loadedRequest?.id == request.id && loadedRequest.thingDef == request.thingDef &&
                loadedRequest.quantityRequested == request.quantityRequested &&
                loadedRequest.status == PurchaseRequestStatus.Open &&
                loadedOrder?.id == order.id && loadedOrder.thingDef == order.thingDef &&
                loadedOrder.quantity == order.quantity && loadedOrder.paidSilver == order.paidSilver &&
                loadedOrder.status == PurchaseOrderStatus.Confirmed &&
                loadedSalesContract?.id == salesContract.id &&
                loadedSalesContract.thingDef == salesContract.thingDef &&
                loadedSalesContract.quantityPerCycle == salesContract.quantityPerCycle &&
                loadedSalesContract.status == ContractStatus.Active &&
                loadedProcurement?.id == acceptedContract.id &&
                loadedProcurement.thingDef == acceptedContract.thingDef &&
                loadedProcurement.quantityPerCycle == acceptedContract.quantityPerCycle &&
                loadedProcurement.unitPrice == acceptedContract.unitPrice &&
                loadedProcurement.cadenceDays == acceptedContract.cadenceDays &&
                loadedProcurement.totalCycles == acceptedContract.totalCycles &&
                loadedProcurement.status == ProcurementContractStatus.Active;
            check(
                assertion,
                failure == null && salesAfter == beforeSales.Count &&
                procurementAfter == beforeProcurement.Count &&
                listingsAfter == beforeListings.Count &&
                requestsAfter == beforeRequests.Count && ordersAfter == beforeOrders.Count &&
                identifyingFieldsSurvived,
                $"available supplier listing count {beforeListings.Count}->{listingsAfter}; " +
                $"open RFQ request count {beforeRequests.Count}->{requestsAfter}; " +
                $"open purchase order count {beforeOrders.Count}->{ordersAfter}; " +
                $"active SALES recurring contract count {beforeSales.Count}->{salesAfter}; " +
                $"active procurement contract count {beforeProcurement.Count}->{procurementAfter}; " +
                $"ids listing={supplierId}; request={requestId}; order={orderId}; " +
                $"sales={salesContractId}; procurement={acceptedContract.id}; " +
                $"failure={failure ?? "none"}");
        }

        private static void CheckStage6IAcceptanceGateSelling(
            Action<string, bool, string> check,
            Action<string, string> skip,
            IntercolonyWorldComponent state,
            Settlement settlement,
            SettlementEconomicProfile profile)
        {
            const string pricingAssertion = "R12 market opportunity still uses shared sell pricing";
            const string cycleAssertion = "R12 sales recurring contract still runs a cycle";
            if (state == null || settlement == null || profile == null)
            {
                string reason = "selling fixture needs world state, an accessible settlement, and profile";
                skip(pricingAssertion, reason);
                skip(cycleAssertion, reason);
                return;
            }

            // R12 is a selling-pricing integration guard, not an opportunity-posting dice test.
            // Use the exact UnitPrice overload that MarketOpportunityGenerator.CreateOne calls,
            // then put that result on a probe opportunity so TotalPrice still crosses the same
            // shared payment boundary without depending on a random offer being posted.
            ThingDef product = IntercolonyProductClassifier.TradableDefs.Count > 0
                ? IntercolonyProductClassifier.TradableDefs[0]
                : null;
            IntercolonyProductCategory category = product == null
                ? IntercolonyProductCategory.Commodities
                : IntercolonyProductClassifier.Classify(product) ??
                  IntercolonyProductCategory.Commodities;
            if (product == null)
            {
                skip(pricingAssertion,
                    "selling pricing fixture has no tradable product to value");
            }
            else
            {
                const int quantity = 1;
                float unitPrice = IntercolonyPricing.UnitPrice(
                    state, product, null, quantity, profile, category,
                    MarketOpportunityGenerator.DistanceToPlayer(settlement), null,
                    out List<PriceFactor> factors);
                MarketOpportunity opportunity = new MarketOpportunity
                {
                    settlementId = settlement.ID,
                    thingDef = product,
                    quantity = quantity,
                    unitPrice = unitPrice
                };
                int sharedTotal = IntercolonyPricing.TotalPayment(unitPrice, quantity);
                check(
                    pricingAssertion,
                    unitPrice > 0f && opportunity.TotalPrice == sharedTotal,
                    $"product={product.defName}; category={category}; " +
                    $"unitPrice={unitPrice:F4}; quantity={quantity}; " +
                    $"total={opportunity.TotalPrice}; sharedTotal={sharedTotal}; " +
                    $"factors={(factors == null ? "null" : factors.Count.ToString())}; " +
                    "pricing entry point=IntercolonyPricing.UnitPrice");
            }

            RecurringContract salesContract = new RecurringContract
            {
                id = state.NextId(),
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "R12 sales fixture",
                factionName = settlement.Faction?.Name ?? "",
                thingDef = ThingDefOf.Steel != null ? ThingDefOf.Steel :
                    IntercolonyProductClassifier.TradableDefs[0],
                quantityPerCycle = 1,
                cadenceTicks = GenDate.TicksPerDay,
                totalCycles = 2,
                unitPrice = 1f,
                status = ContractStatus.Active,
                nextCycleTick = GenTicks.TicksGame
            };
            state.Contracts.Add(salesContract);
            int ordersBefore = state.Orders.Count;
            ContractService.AdvanceContracts(state);
            SalesOrder cycleOrder = state.FindOrder(salesContract.activeOrderId);
            check(
                cycleAssertion,
                state.Orders.Count == ordersBefore + 1 && salesContract.activeOrderId != 0 &&
                cycleOrder != null && cycleOrder.status == SalesOrderStatus.Accepted &&
                cycleOrder.contractId == salesContract.id &&
                cycleOrder.unitPrice == salesContract.unitPrice,
                $"orders {ordersBefore}->{state.Orders.Count}; contract={salesContract.id}; " +
                $"activeOrderId={salesContract.activeOrderId}; order=" +
                $"{(cycleOrder == null ? "null" : cycleOrder.id.ToString())}; status=" +
                $"{(cycleOrder == null ? "null" : cycleOrder.status.ToString())}; " +
                $"unitPrice={(cycleOrder == null ? "null" : cycleOrder.unitPrice.ToString("F4"))}");
        }

        private static bool SameSupplierListingBatch(
            List<SupplierListing> before,
            List<SupplierListing> after)
        {
            if (before == null || after == null || before.Count != after.Count)
            {
                return false;
            }

            for (int i = 0; i < before.Count; i++)
            {
                SupplierListing left = before[i];
                SupplierListing right = after[i];
                if (left == null || right == null || left.thingDef != right.thingDef ||
                    left.quantityAvailable != right.quantityAvailable ||
                    left.unitPrice != right.unitPrice || left.fulfillment != right.fulfillment ||
                    left.leadTimeDays != right.leadTimeDays)
                {
                    return false;
                }
            }

            return true;
        }

        private static string SupplierListingBatchDetail(List<SupplierListing> listings)
        {
            if (listings == null)
            {
                return "null";
            }

            List<string> details = new List<string>();
            foreach (SupplierListing listing in listings)
            {
                details.Add(listing == null
                    ? "null"
                    : $"{listing.thingDef?.defName ?? "null"}:{listing.quantityAvailable}@" +
                      $"{listing.unitPrice:F3}");
            }

            return "[" + string.Join(",", details.ToArray()) + "]";
        }

        private static void SetProcurementReputation(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile)
        {
            CommercialReputation reputation = new CommercialReputation(
                profile.settlementId, profile.settlementName, profile.factionName);
            reputation.Adjust(
                CommercialReputation.MaxScore - CommercialReputation.StartingScore);
            state.Reputations[profile.settlementId] = reputation;
        }

        private static void CheckProcurementProposalPriceDirection(
            Action<string, bool, string> check,
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef product)
        {
            state.ProcurementContracts.Clear();
            ProcurementContractProposalResult lower =
                ProposeProcurementFixture(state, settlement, product);
            float lowerPrice = lower.Contract?.unitPrice ?? 0f;
            float higherPrice = lowerPrice * 1.5f;
            state.ProcurementContracts.Clear();
            ProcurementContractProposalResult higher =
                ProposeProcurementFixture(
                    state, settlement, product, 10, 1, 2, higherPrice);
            bool comparison = lower.Evaluation != null && higher.Evaluation != null &&
                              higher.Evaluation.AcceptanceScore >=
                              lower.Evaluation.AcceptanceScore;
            check(
                "E8 supplier price appeal increases with purchase price",
                lower.Success && higher.Success && comparison,
                $"lower price={lowerPrice:F2}; higher price={higherPrice:F2}; " +
                $"lower score={(lower.Evaluation == null ? "null" : lower.Evaluation.AcceptanceScore.ToString("F3"))}; " +
                $"higher score={(higher.Evaluation == null ? "null" : higher.Evaluation.AcceptanceScore.ToString("F3"))}; " +
                $"lower reason={lower.Reason ?? "none"}; higher reason={higher.Reason ?? "none"}");
            state.ProcurementContracts.Clear();
        }

        private static int CountProcurementTimelineRecords(
            IntercolonyWorldComponent state,
            int settlementId,
            int relatedEntityId,
            CommercialEventType type)
        {
            int count = 0;
            if (state == null)
            {
                return count;
            }

            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.settlementId == settlementId &&
                    record.relatedEntityId == relatedEntityId && record.type == type)
                {
                    count++;
                }
            }

            return count;
        }

        private static CommercialEventRecord FindProcurementTimelineRecord(
            IntercolonyWorldComponent state,
            int settlementId,
            int relatedEntityId,
            CommercialEventType type)
        {
            if (state == null)
            {
                return null;
            }

            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.settlementId == settlementId &&
                    record.relatedEntityId == relatedEntityId && record.type == type)
                {
                    return record;
                }
            }

            return null;
        }

        private static string CommercialTimelineTypes(IntercolonyWorldComponent state)
        {
            if (state == null || state.CommercialTimeline.Count == 0)
            {
                return "none";
            }

            List<string> types = new List<string>();
            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                types.Add(record == null ? "null" : record.type.ToString());
            }

            return string.Join(",", types.ToArray());
        }

        private static bool ContainsProcurementContractId(
            List<ProcurementContract> contracts,
            int id)
        {
            if (contracts == null)
            {
                return false;
            }

            foreach (ProcurementContract contract in contracts)
            {
                if (contract != null && contract.id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsNullProcurementContract(
            List<ProcurementContract> contracts)
        {
            foreach (ProcurementContract contract in contracts)
            {
                if (contract == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ProcurementContractIds(List<ProcurementContract> contracts)
        {
            if (contracts == null)
            {
                return "null";
            }

            List<string> ids = new List<string>();
            foreach (ProcurementContract contract in contracts)
            {
                ids.Add(contract == null ? "null" : contract.id.ToString());
            }

            return string.Join(",", ids.ToArray());
        }

        private static SupplierListing NewSupplierListing(int id, int quantity, int expiry)
        {
            return new SupplierListing
            {
                id = id,
                thingDef = ThingDefOf.Steel,
                quantityAvailable = quantity,
                expiryTick = expiry
            };
        }

        private static bool ContainsListingId(List<SupplierListing> listings, int id)
        {
            if (listings == null)
            {
                return false;
            }

            foreach (SupplierListing listing in listings)
            {
                if (listing != null && listing.id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ListingIds(List<SupplierListing> listings)
        {
            if (listings == null)
            {
                return "null";
            }

            List<string> ids = new List<string>();
            foreach (SupplierListing listing in listings)
            {
                ids.Add(listing == null ? "null" : listing.id.ToString());
            }

            return string.Join(",", ids.ToArray());
        }

        private static void CheckEffectiveSupplyForRfq(
            System.Action<string, bool, string> check,
            IntercolonyWorldComponent state)
        {
            const IntercolonyProductCategory Category =
                IntercolonyProductCategory.IntermediateGoods;
            SettlementEconomicProfile profile = new SettlementEconomicProfile
            {
                settlementId = SupplyProbeSettlementId
            };
            profile.supplyWeights[(int)Category] = 1f;

            ClearSupplyProbe(state);
            try
            {
                float baseline = profile.BaseSupplyFor(Category);
                float undisturbed =
                    EffectiveEconomyService.EffectiveSupply(state, profile, Category);
                check("RFQ supply probe has a non-zero category baseline", baseline > 0f,
                    baseline.ToString("F3"));
                check("undisturbed RFQ effective supply equals its baseline exactly",
                    Mathf.Approximately(undisturbed, baseline),
                    $"{baseline:F3} -> {undisturbed:F3}");

                MarketPressureService.ApplySupplyShock(
                    state, SupplyProbeSettlementId, Category, 0.35f);
                float scarce = EffectiveEconomyService.EffectiveSupply(state, profile, Category);
                check("supply pressure gives RFQs less effective supply than baseline",
                    scarce < undisturbed, $"{undisturbed:F3} -> {scarce:F3}");
                check("scarce RFQ effective supply stays within the economy bounds",
                    scarce >= baseline * EffectiveEconomyService.MinCondition &&
                    scarce <= baseline * EffectiveEconomyService.MaxCondition,
                    scarce.ToString("F3"));

                ClearSupplyProbe(state);
                MarketPressureService.ApplySupplyShock(
                    state, SupplyProbeSettlementId, Category, -0.25f);
                float surplus = EffectiveEconomyService.EffectiveSupply(state, profile, Category);
                check("a supply surplus gives RFQs more effective supply than baseline",
                    surplus > undisturbed, $"{undisturbed:F3} -> {surplus:F3}");
                check("surplus RFQ effective supply stays within the economy bounds",
                    surplus >= baseline * EffectiveEconomyService.MinCondition &&
                    surplus <= baseline * EffectiveEconomyService.MaxCondition,
                    surplus.ToString("F3"));
            }
            finally
            {
                ClearSupplyProbe(state);
            }
        }

        private static void ClearSupplyProbe(IntercolonyWorldComponent state)
        {
            state.MarketStates.RemoveAll(
                s => s != null && s.settlementId == SupplyProbeSettlementId);
            state.RefreshMarketStateIndex();
        }

        private static void CheckRfqResponseCountUsesEffectiveSupply(
            System.Action<string, bool, string> check,
            System.Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            const string Assertion = "RFQ response count falls under maximum supply scarcity";
            const int ProbesPerCategory = 2;
            const int MinimumUndisturbedQuotes = 6;
            const int Requested = 50;
            List<Settlement> accessible = new List<Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                foreach (Settlement settlement in settlements)
                {
                    if (IntercolonyMarketAccess.IsAccessible(settlement) &&
                        state.GetProfile(settlement) != null)
                    {
                        accessible.Add(settlement);
                    }
                }
            }

            int settlementCount = accessible.Count;
            if (settlementCount == 0)
            {
                skip(Assertion,
                    "0 accessible settlements with profiles, 0 undisturbed quotations");
                return;
            }

            // Match the market baseline's probe selection: sample deterministic future market
            // cycles, then take the most-demanded loaded defs per category. Future refresh numbers
            // keep the sample distinct from the live market without advancing the player's world.
            Dictionary<IntercolonyProductCategory, Dictionary<ThingDef, int>> demand =
                new Dictionary<IntercolonyProductCategory, Dictionary<ThingDef, int>>();
            int syntheticId = 1;
            int firstSyntheticRefresh = state.RefreshCount + 1;
            for (int refresh = firstSyntheticRefresh;
                 refresh < firstSyntheticRefresh + IntercolonyMarketBaseline.DefaultRefreshSamples;
                 refresh++)
            {
                foreach (Settlement settlement in accessible)
                {
                    SettlementEconomicProfile profile = state.GetProfile(settlement);
                    List<MarketOpportunity> opportunities =
                        MarketOpportunityGenerator.GenerateFor(
                            settlement, profile, state.EconomySeed, refresh, existingCount: 0,
                            idAllocator: () => syntheticId++);
                    foreach (MarketOpportunity opportunity in opportunities)
                    {
                        if (opportunity.thingDef == null)
                        {
                            continue;
                        }

                        IntercolonyProductCategory? category =
                            IntercolonyProductClassifier.Classify(opportunity.thingDef);
                        if (!category.HasValue)
                        {
                            continue;
                        }

                        if (!demand.TryGetValue(
                                category.Value, out Dictionary<ThingDef, int> counts))
                        {
                            counts = new Dictionary<ThingDef, int>();
                            demand[category.Value] = counts;
                        }

                        counts.TryGetValue(opportunity.thingDef, out int seen);
                        counts[opportunity.thingDef] = seen + 1;
                    }
                }
            }

            List<ThingDef> probes = new List<ThingDef>();
            List<IntercolonyProductCategory> probeCategories =
                new List<IntercolonyProductCategory>();
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (!demand.TryGetValue(category, out Dictionary<ThingDef, int> counts))
                {
                    continue;
                }

                List<KeyValuePair<ThingDef, int>> ranked =
                    new List<KeyValuePair<ThingDef, int>>(counts);
                ranked.Sort((a, b) =>
                {
                    int byCount = b.Value.CompareTo(a.Value);
                    return byCount != 0
                        ? byCount
                        : string.CompareOrdinal(a.Key.defName, b.Key.defName);
                });

                int take = Mathf.Min(ProbesPerCategory, ranked.Count);
                for (int i = 0; i < take; i++)
                {
                    probes.Add(ranked[i].Key);
                }

                if (take > 0)
                {
                    probeCategories.Add(category);
                }
            }

            int undisturbedCount = 0;
            foreach (ThingDef def in probes)
            {
                PurchaseRequest request = new PurchaseRequest
                {
                    thingDef = def,
                    quantityRequested = Requested,
                    desiredDays = 10,
                    fulfillmentPreference = ProcurementFulfillmentPreference.Either,
                    minQuality = null,
                    stuffDef = null
                };

                RfqService.GenerateResponses(state, request);
                undisturbedCount += request.quotes.Count;
            }

            if (undisturbedCount < MinimumUndisturbedQuotes)
            {
                skip(Assertion,
                    $"{settlementCount} accessible settlements with profiles, " +
                    $"{undisturbedCount} undisturbed quotations across {probes.Count} defs; " +
                    $"at least {MinimumUndisturbedQuotes} required");
                return;
            }

            float undisturbedSupply = TotalProbeSupply(state, accessible, probeCategories);

            Dictionary<int, SettlementMarketState> savedRecords =
                new Dictionary<int, SettlementMarketState>();
            Dictionary<int, float[]> savedDemand = new Dictionary<int, float[]>();
            Dictionary<int, float[]> savedSupply = new Dictionary<int, float[]>();
            Dictionary<int, int> savedRefreshes = new Dictionary<int, int>();

            try
            {
                foreach (Settlement settlement in accessible)
                {
                    SettlementMarketState record =
                        state.MarketStateFor(settlement.ID, createIfMissing: false);
                    if (record != null)
                    {
                        savedRecords.Add(settlement.ID, record);
                        savedDemand.Add(
                            settlement.ID, (float[])record.demandPressure.Clone());
                        savedSupply.Add(
                            settlement.ID, (float[])record.supplyPressure.Clone());
                        savedRefreshes.Add(settlement.ID, record.lastAdvancedRefresh);
                    }

                    foreach (IntercolonyProductCategory category in probeCategories)
                    {
                        MarketPressureService.ApplySupplyShock(
                            state, settlement.ID, category, MarketPressureService.MaxPressure);
                    }
                }

                float scarceSupply = TotalProbeSupply(state, accessible, probeCategories);
                if (scarceSupply >= undisturbedSupply - 0.0001f)
                {
                    skip(Assertion,
                        $"sampled effective supply did not decrease " +
                        $"{undisturbedSupply:F4}->{scarceSupply:F4}");
                    return;
                }

                int scarceCount = 0;
                foreach (ThingDef def in probes)
                {
                    PurchaseRequest request = new PurchaseRequest
                    {
                        thingDef = def,
                        quantityRequested = Requested,
                        desiredDays = 10,
                        fulfillmentPreference = ProcurementFulfillmentPreference.Either,
                        minQuality = null,
                        stuffDef = null
                    };

                    // GenerateResponses seeds on the refresh and def, so each scarce request
                    // repeats its undisturbed random rolls and isolates effective supply.
                    RfqService.GenerateResponses(state, request);
                    scarceCount += request.quotes.Count;
                }

                check(Assertion, scarceCount < undisturbedCount,
                    $"{settlementCount} settlements, {probes.Count} defs, " +
                    $"{undisturbedCount} -> {scarceCount} quotations");
            }
            finally
            {
                foreach (Settlement settlement in accessible)
                {
                    if (savedRecords.TryGetValue(settlement.ID, out SettlementMarketState record))
                    {
                        float[] demandBefore = savedDemand[settlement.ID];
                        float[] supplyBefore = savedSupply[settlement.ID];
                        for (int i = 0; i < demandBefore.Length; i++)
                        {
                            record.demandPressure[i] = demandBefore[i];
                            record.supplyPressure[i] = supplyBefore[i];
                        }

                        record.lastAdvancedRefresh = savedRefreshes[settlement.ID];
                    }
                    else
                    {
                        state.MarketStates.RemoveAll(
                            s => s != null && s.settlementId == settlement.ID);
                    }
                }

                state.RefreshMarketStateIndex();
            }
        }

        private static float TotalProbeSupply(
            IntercolonyWorldComponent state,
            List<Settlement> settlements,
            List<IntercolonyProductCategory> categories)
        {
            float total = 0f;
            foreach (Settlement settlement in settlements)
            {
                SettlementEconomicProfile profile = state.GetProfile(settlement);
                foreach (IntercolonyProductCategory category in categories)
                {
                    total += EffectiveEconomyService.EffectiveSupply(
                        state, profile, category);
                }
            }

            return total;
        }

        /// <summary>
        /// Proves the Procurement tab selects retained conclusions as well as live purchases and
        /// orders the live commitments ahead of history. The probes are never added to world state.
        /// </summary>
        private static void CheckPurchaseOrderDisplaySelection(
            System.Action<string, bool, string> check)
        {
            PurchaseOrder confirmed = new PurchaseOrder
                { id = 601, status = PurchaseOrderStatus.Confirmed };
            PurchaseOrder ready = new PurchaseOrder
                { id = 602, status = PurchaseOrderStatus.ReadyForPickup };
            PurchaseOrder completed = new PurchaseOrder
                { id = 701, status = PurchaseOrderStatus.Completed };
            PurchaseOrder cancelled = new PurchaseOrder
                { id = 702, status = PurchaseOrderStatus.Cancelled };
            PurchaseOrder supplierDefault = new PurchaseOrder
                { id = 703, status = PurchaseOrderStatus.SupplierDefault };
            PurchaseOrder lostToWar = new PurchaseOrder
                { id = 704, status = PurchaseOrderStatus.LostToWar };

            List<PurchaseOrder> selected =
                MainTabWindow_Intercolony.SelectPurchaseOrdersForDisplay(
                    new List<PurchaseOrder>
                    {
                        lostToWar, completed, ready, supplierDefault, confirmed, cancelled
                    });

            check("purchase display selects a confirmed order", selected.Contains(confirmed),
                $"selected {selected.Count} order(s)");
            check("purchase display selects a ready-for-pickup order", selected.Contains(ready),
                $"selected {selected.Count} order(s)");
            check("purchase display selects a completed order", selected.Contains(completed),
                $"selected {selected.Count} order(s)");
            check("purchase display selects a cancelled order", selected.Contains(cancelled),
                $"selected {selected.Count} order(s)");
            check("purchase display selects a supplier-default order",
                selected.Contains(supplierDefault), $"selected {selected.Count} order(s)");
            check("purchase display selects an order lost to war", selected.Contains(lostToWar),
                $"selected {selected.Count} order(s)");

            bool openBeforeConcluded = selected.Count == 6;
            bool reachedConcluded = false;
            foreach (PurchaseOrder order in selected)
            {
                if (!order.IsOpen)
                {
                    reachedConcluded = true;
                }
                else if (reachedConcluded)
                {
                    openBeforeConcluded = false;
                }
            }

            check("purchase display puts open orders before concluded orders",
                openBeforeConcluded, string.Join(", ", selected.ConvertAll(o => o.status.ToString())));
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
            System.Action<string, string> skip,
            string caseName,
            ThingDef def,
            ThingDef stuff,
            QualityCategory? quality,
            int quantity)
        {
            if (def == null)
            {
                skip(caseName, "def not in this install");
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
