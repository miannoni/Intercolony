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
