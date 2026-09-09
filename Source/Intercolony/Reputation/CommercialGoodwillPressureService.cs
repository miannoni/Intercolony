using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;

namespace Intercolony
{
    /// <summary>
    /// One positive goodwill change selected by the commercial-standing decision. This is only a
    /// decision result; the application unit owns calling vanilla's goodwill API later.
    /// </summary>
    public readonly struct CommercialGoodwillPressure
    {
        public readonly Faction Faction;
        public readonly int Delta;

        public CommercialGoodwillPressure(Faction faction, int delta)
        {
            Faction = faction;
            Delta = delta;
        }
    }

    public enum CommercialGoodwillPressureStatus
    {
        Unavailable,
        BelowPreferred,
        Hostile,
        AtCeiling,
        GoodwillRestricted,
        Earning
    }

    public readonly struct CommercialGoodwillPressureEvaluation
    {
        public readonly CommercialGoodwillPressureStatus Status;
        public readonly Faction Faction;
        public readonly int Delta;

        public CommercialGoodwillPressureEvaluation(
            CommercialGoodwillPressureStatus status,
            Faction faction,
            int delta)
        {
            Status = status;
            Faction = faction;
            Delta = delta;
        }

        public bool IsEarning => Status == CommercialGoodwillPressureStatus.Earning;
    }

    /// <summary>
    /// Selects the bounded goodwill pressure created by very strong commercial standing.
    ///
    /// Evaluate remains pure; Apply consumes its results through the existing goodwill writer.
    /// This service does not write persisted state or keep a last-run field, so cadence remains an
    /// absolute-tick concern of the world component.
    /// </summary>
    public static class CommercialGoodwillPressureService
    {
        /// <summary>Goodwill gained by each qualifying faction per application.</summary>
        public static int GoodwillPressureDelta =>
            IntercolonyMod.Settings.commercialGoodwillPerInterval;

        /// <summary>
        /// Pressure stops at this live-configured BASE goodwill. The setting is capped below
        /// vanilla's Ally threshold so commerce cannot itself reach alliance.
        /// </summary>
        public static int GoodwillBaseCeiling =>
            IntercolonyMod.Settings.commercialGoodwillCeiling;

        /// <summary>Vanilla's goodwill threshold for changing a relation to Ally.</summary>
        public const int VanillaAllyThreshold = 75;

        /// <summary>
        /// Returns one configured pressure result for each faction with at least one qualifying
        /// settlement.
        /// Results are sorted by the faction's stable load ID, never by dictionary enumeration
        /// order, so the same save produces the same application order after reload.
        /// </summary>
        public static List<CommercialGoodwillPressure> Evaluate(IntercolonyWorldComponent state)
        {
            List<CommercialGoodwillPressure> result =
                new List<CommercialGoodwillPressure>();

            if (state == null || state.Reputations == null)
            {
                return result;
            }

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
            {
                return result;
            }

            Dictionary<Faction, CommercialGoodwillPressure> byFaction =
                new Dictionary<Faction, CommercialGoodwillPressure>();

            foreach (KeyValuePair<int, CommercialReputation> entry in state.Reputations)
            {
                CommercialGoodwillPressureEvaluation evaluation = EvaluateStatus(
                    state,
                    entry.Key,
                    playerFaction);
                if (!evaluation.IsEarning || evaluation.Faction == null ||
                    byFaction.ContainsKey(evaluation.Faction))
                {
                    continue;
                }

                // One faction receives one result even when several of its settlements are
                // Preferred.
                byFaction.Add(
                    evaluation.Faction,
                    new CommercialGoodwillPressure(evaluation.Faction, evaluation.Delta));
            }

            foreach (CommercialGoodwillPressure pressure in byFaction.Values)
            {
                result.Add(pressure);
            }

            result.Sort(CompareByFactionLoadId);
            return result;
        }

        /// <summary>
        /// Returns the same live decision that the scheduled tick uses for one settlement. The
        /// faction is resolved from the live settlement, while the persisted faction name remains
        /// display-only history.
        /// </summary>
        public static CommercialGoodwillPressureEvaluation EvaluateStatus(
            IntercolonyWorldComponent state,
            int settlementId)
        {
            return EvaluateStatus(state, settlementId, Faction.OfPlayer);
        }

        /// <summary>Applies the current commercial-standing decision to vanilla goodwill.</summary>
        public static void Apply(IntercolonyWorldComponent state)
        {
            foreach (CommercialGoodwillPressure pressure in Evaluate(state))
            {
                if (pressure.Delta <= 0)
                {
                    continue;
                }

                EmployerReputationService.AffectGoodwill(
                    pressure.Faction,
                    pressure.Delta,
                    "commercial standing pressure");
            }
        }

        private static CommercialGoodwillPressureEvaluation EvaluateStatus(
            IntercolonyWorldComponent state,
            int settlementId,
            Faction playerFaction)
        {
            if (state == null || state.Reputations == null || playerFaction == null)
            {
                return new CommercialGoodwillPressureEvaluation(
                    CommercialGoodwillPressureStatus.Unavailable,
                    null,
                    0);
            }

            CommercialReputation reputation = state.FindReputation(settlementId);
            if (reputation == null)
            {
                return new CommercialGoodwillPressureEvaluation(
                    CommercialGoodwillPressureStatus.Unavailable,
                    null,
                    0);
            }

            if (reputation.Score < IntercolonyMod.Settings.commercialReputationRequired)
            {
                return new CommercialGoodwillPressureEvaluation(
                    CommercialGoodwillPressureStatus.BelowPreferred,
                    null,
                    0);
            }

            // The record's factionName is only a historical/display snapshot. Ownership is
            // resolved from the live settlement so a change of hands cannot credit the old
            // faction.
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(settlementId);
            Faction faction = settlement?.Faction;
            if (faction == null || faction.IsPlayer || faction == playerFaction ||
                faction.Hidden || faction.defeated)
            {
                return new CommercialGoodwillPressureEvaluation(
                    CommercialGoodwillPressureStatus.Unavailable,
                    faction,
                    0);
            }

            // HostilityPolicy is the mod's single live definition of being at war; it includes
            // both the hostile relation kind and HostileTo, and reads the current faction.
            if (HostilityPolicy.IsAtWar(faction))
            {
                return new CommercialGoodwillPressureEvaluation(
                    CommercialGoodwillPressureStatus.Hostile,
                    faction,
                    0);
            }

            int baseGoodwill = faction.BaseGoodwillWith(playerFaction);
            if (baseGoodwill >= GoodwillBaseCeiling)
            {
                return new CommercialGoodwillPressureEvaluation(
                    CommercialGoodwillPressureStatus.AtCeiling,
                    faction,
                    0);
            }

            if (!CanReceivePressure(faction, playerFaction, baseGoodwill))
            {
                return new CommercialGoodwillPressureEvaluation(
                    CommercialGoodwillPressureStatus.GoodwillRestricted,
                    faction,
                    0);
            }

            int remainingHeadroom = Math.Max(0, GoodwillBaseCeiling - baseGoodwill);
            int appliedDelta = Math.Max(0, Math.Min(GoodwillPressureDelta, remainingHeadroom));
            return new CommercialGoodwillPressureEvaluation(
                CommercialGoodwillPressureStatus.Earning,
                faction,
                appliedDelta);
        }

        private static bool CanReceivePressure(
            Faction faction,
            Faction playerFaction,
            int baseGoodwill)
        {
            // BaseGoodwillWith is the value TryAffectGoodwillWith writes. The ceiling must be
            // checked against it, not against the possibly capped effective value.
            if (baseGoodwill >= GoodwillBaseCeiling)
            {
                return false;
            }

            // GoodwillWith applies GoodwillSituationManager's current maximum. If that effective
            // value is below base goodwill, the diplomatic restriction is active; do not build
            // invisible credit that would appear when the restriction expires.
            return faction.GoodwillWith(playerFaction) >= baseGoodwill;
        }

        private static int CompareByFactionLoadId(
            CommercialGoodwillPressure left,
            CommercialGoodwillPressure right)
        {
            return left.Faction.loadID.CompareTo(right.Faction.loadID);
        }
    }
}
