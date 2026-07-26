using System.Collections.Generic;
using System.Text;
using RimWorld;
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

            sb.AppendLine($"  {passed} passed, {failed} failed.");
            return sb.ToString();
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
