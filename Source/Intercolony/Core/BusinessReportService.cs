using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Turns the mod's state into the two screens §117 asks for, and answers §45's questions.
    ///
    /// §45 calls this "the heart of the finished product" and lists what a player should be able to
    /// work out: is this contract profitable, should I hire or train, should I buy inputs or produce
    /// them, should I deliver myself. Each of those is a comparison, so each figure here exists to
    /// sit next to another one rather than to be impressive on its own.
    ///
    /// §117's warning governs the whole file: *"Use estimates carefully."* Everything backward-looking
    /// comes from the ledger and is fact. Everything forward-looking is an estimate, is labelled as
    /// one, and is built from numbers the mod would really charge — not from a model of the player's
    /// production, which the mod cannot see and should not pretend to.
    /// </summary>
    public static class BusinessReportService
    {
        /// <summary>A quadrum. §117's report window.</summary>
        public const int QuadrumDays = GenDate.DaysPerQuadrum;

        /// <summary>A year, the longer view. Matches the ledger's retention exactly.</summary>
        public const int YearDays = GenDate.DaysPerYear;

        // --- Forward-looking: is this contract worth it? (§45) -----------------------------

        /// <summary>
        /// One recurring agreement's economics per cycle, in §45's exact shape.
        /// </summary>
        public class ContractEstimate
        {
            public RecurringContract contract;

            /// <summary>Agreed, not estimated — the price is locked for the contract's life.</summary>
            public int revenue;

            /// <summary>What procuring the same goods would cost. Negative.</summary>
            public int inputsIfBought;

            /// <summary>Share of the wage bill this cycle would cover. Negative.</summary>
            public int payroll;

            /// <summary>The delivery premium, shown as what hauling it yourself is worth. Negative.</summary>
            public int transport;

            public int Margin => revenue + inputsIfBought + payroll + transport;

            /// <summary>What making the goods rather than buying them is worth, per cycle.</summary>
            public int MakingSaves => -inputsIfBought;

            public float CadenceDays => contract?.CadenceDays ?? 1f;

            public float MarginPerDay => CadenceDays <= 0f ? 0f : Margin / CadenceDays;
        }

        /// <summary>
        /// Estimates a contract's per-cycle economics.
        ///
        /// **Inputs are priced as "what buying it instead would cost", and that choice is the
        /// answer to §45's question rather than a shortcut around it.** The mod cannot see what a
        /// player's rice costs to grow — soil, work, a stove, a season — and any number it invented
        /// for that would be fiction with a decimal point. What it *can* say precisely is what the
        /// same goods would cost through procurement, which is exactly the alternative the player is
        /// weighing. The line reads "if you bought it" for that reason.
        /// </summary>
        public static ContractEstimate Estimate(IntercolonyWorldComponent state, RecurringContract contract)
        {
            ContractEstimate estimate = new ContractEstimate { contract = contract };
            if (contract == null)
            {
                return estimate;
            }

            estimate.revenue = contract.DiscountedCyclePayment;

            // Base value plus what a supplier marks up, using procurement's own constant so the
            // dashboard cannot recommend buying at a price procurement would not offer.
            float unit = IntercolonyPricing.BaseValue(contract.thingDef, contract.stuffDef) *
                         RfqService.SupplierMargin;
            estimate.inputsIfBought = -Mathf.RoundToInt(unit * contract.quantityPerCycle);

            estimate.payroll = -PayrollForPeriod(state, contract.CadenceDays);

            // §45's "should I deliver myself?". A recurring agreement is always seller-delivery, and
            // the price already carries a premium for that — so the transport line is that premium,
            // shown as the cost of the caravan trips that earn it. A quadrum of pickup orders would
            // show nothing here, which is the honest answer: nothing was hauled.
            float deliver = IntercolonyPricing.LogisticsFactor(FulfillmentMode.SellerDelivery).multiplier;
            float collect = IntercolonyPricing.LogisticsFactor(FulfillmentMode.BuyerPickup).multiplier;
            float premiumShare = deliver <= 0f ? 0f : (deliver - collect) / deliver;
            estimate.transport = -Mathf.RoundToInt(estimate.revenue * premiumShare);

            return estimate;
        }

        /// <summary>
        /// The colony's wage bill over a stretch of days.
        ///
        /// Every active employee, not a share apportioned to one contract. Apportioning would need
        /// the mod to know who works on what, which it does not — and a made-up allocation is worse
        /// than an honest total, because it looks precise. The dashboard labels it as the whole
        /// wage bill so the comparison the player makes is the true one: does this agreement cover
        /// what the workforce costs.
        /// </summary>
        public static int PayrollForPeriod(IntercolonyWorldComponent state, float days)
        {
            if (state == null || days <= 0f)
            {
                return 0;
            }

            int daily = 0;
            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract.status == EmploymentStatus.Active)
                {
                    daily += contract.dailyWage;
                }
            }

            return Mathf.RoundToInt(daily * days);
        }

        /// <summary>Every live agreement, estimated, best margin first.</summary>
        public static List<ContractEstimate> ActiveEstimates(IntercolonyWorldComponent state)
        {
            List<ContractEstimate> estimates = new List<ContractEstimate>();
            if (state == null)
            {
                return estimates;
            }

            foreach (RecurringContract contract in state.Contracts)
            {
                if (contract.IsActive || contract.status == ContractStatus.Suspended)
                {
                    estimates.Add(Estimate(state, contract));
                }
            }

            estimates.Sort((a, b) => b.Margin.CompareTo(a.Margin));
            return estimates;
        }

        // --- Backward-looking helpers (§75, §117) ------------------------------------------

        /// <summary>
        /// The colony's current daily wage commitment, for the "should I hire?" question.
        /// </summary>
        public static int DailyWageBill(IntercolonyWorldComponent state)
        {
            return PayrollForPeriod(state, 1f);
        }

        /// <summary>
        /// Silver on hand across every player map, so the report can say how long the wage bill is
        /// covered for. That runway figure is the single most useful thing the dashboard can tell a
        /// player who is deciding whether to take on another worker.
        /// </summary>
        public static int SilverOnHand()
        {
            int total = 0;
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    total += PurchaseOrderService.CountColonySilver(map);
                }
            }

            return total;
        }

        /// <summary>
        /// Days the colony can meet payroll for out of what it holds, or -1 when nothing is owed.
        /// </summary>
        public static float PayrollRunwayDays(IntercolonyWorldComponent state)
        {
            int daily = DailyWageBill(state);
            return daily <= 0 ? -1f : SilverOnHand() / (float)daily;
        }
    }
}
