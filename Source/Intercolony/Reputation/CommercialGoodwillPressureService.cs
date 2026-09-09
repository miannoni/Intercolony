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

    /// <summary>
    /// Selects the bounded goodwill pressure created by very strong commercial standing.
    ///
    /// Evaluate remains pure; Apply consumes its results through the existing goodwill writer.
    /// This service does not write persisted state or keep a last-run field, so cadence remains an
    /// absolute-tick concern of the world component.
    /// </summary>
    public static class CommercialGoodwillPressureService
    {
        /// <summary>One positive goodwill point per qualifying faction per application.</summary>
        public const int GoodwillPressureDelta = 1;

        /// <summary>
        /// Pressure stops at this BASE goodwill. Vanilla changes a faction to Ally at 75 goodwill;
        /// stopping at 60 leaves a hard 15-point gap so commerce cannot itself reach alliance.
        /// </summary>
        public const int GoodwillBaseCeiling = 60;

        /// <summary>Vanilla's goodwill threshold for changing a relation to Ally.</summary>
        public const int VanillaAllyThreshold = 75;

        /// <summary>
        /// Returns one +1 pressure result for each faction with at least one qualifying settlement.
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
                CommercialReputation reputation = entry.Value;
                if (reputation == null || reputation.Tier != ReputationTier.Preferred)
                {
                    continue;
                }

                // The record's factionName is only a historical/display snapshot. Ownership is
                // resolved from the live settlement so a change of hands cannot credit the old
                // faction.
                Settlement settlement = IntercolonyMarketAccess.FindSettlement(entry.Key);
                if (!IsEligibleSettlement(settlement, playerFaction))
                {
                    continue;
                }

                Faction faction = settlement.Faction;
                if (!CanReceivePressure(faction, playerFaction) || byFaction.ContainsKey(faction))
                {
                    continue;
                }

                // One faction receives one result even when several of its settlements are
                // Preferred.
                byFaction.Add(
                    faction,
                    new CommercialGoodwillPressure(faction, GoodwillPressureDelta));
            }

            foreach (CommercialGoodwillPressure pressure in byFaction.Values)
            {
                result.Add(pressure);
            }

            result.Sort(CompareByFactionLoadId);
            return result;
        }

        /// <summary>Applies the current commercial-standing decision to vanilla goodwill.</summary>
        public static void Apply(IntercolonyWorldComponent state)
        {
            foreach (CommercialGoodwillPressure pressure in Evaluate(state))
            {
                EmployerReputationService.AffectGoodwill(
                    pressure.Faction,
                    pressure.Delta,
                    "commercial standing pressure");
            }
        }

        private static bool IsEligibleSettlement(Settlement settlement, Faction playerFaction)
        {
            if (settlement == null)
            {
                return false;
            }

            Faction faction = settlement.Faction;
            if (faction == null || faction.IsPlayer || faction == playerFaction ||
                faction.Hidden || faction.defeated)
            {
                return false;
            }

            // HostilityPolicy is the mod's single live definition of being at war; it includes
            // both the hostile relation kind and HostileTo, and reads the current faction.
            return !HostilityPolicy.IsAtWar(faction);
        }

        private static bool CanReceivePressure(Faction faction, Faction playerFaction)
        {
            // BaseGoodwillWith is the value TryAffectGoodwillWith writes. The ceiling must be
            // checked against it, not against the possibly capped effective value.
            int baseGoodwill = faction.BaseGoodwillWith(playerFaction);
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
