using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Decides which settlements the player can currently do business with
    /// (DESIGN.md §51 "Market access and contact").
    ///
    /// Kept separate from <see cref="SettlementProfileGenerator.IsEligible"/> on purpose.
    /// Eligibility is structural and stable — it decides whether a settlement *has* an
    /// economy at all, and must not flicker as goodwill drifts, or profiles would be
    /// regenerated constantly. Access is volatile and answers a different question: can the
    /// player trade with them *right now*.
    ///
    /// §51 asks for "the simplest intuitive rule" and warns against overcomplicating
    /// communications before commerce works, so this checks hostility only. Discovery,
    /// comms consoles, caravan contact, and prior-trade requirements are all listed in §51 as
    /// options and remain available to layer on later.
    /// </summary>
    public static class IntercolonyMarketAccess
    {
        /// <summary>
        /// Whether the player can trade with this settlement now. <paramref name="reason"/>
        /// explains a refusal, for debug output and eventually player-facing UI.
        /// </summary>
        public static bool IsAccessible(Settlement settlement, out string reason)
        {
            if (!SettlementProfileGenerator.IsEligible(settlement))
            {
                reason = "not an economic participant";
                return false;
            }

            Faction faction = settlement.Faction;
            if (faction == null)
            {
                reason = "no faction";
                return false;
            }

            // People shooting at you do not post purchase orders.
            if (faction.HostileTo(Faction.OfPlayer))
            {
                reason = $"{faction.Name} is hostile";
                return false;
            }

            if (faction.PlayerRelationKind == FactionRelationKind.Hostile)
            {
                reason = $"{faction.Name} relations are hostile";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool IsAccessible(Settlement settlement)
        {
            return IsAccessible(settlement, out _);
        }

        /// <summary>
        /// Whether an already-listed opportunity is still valid. Used on refresh to drop
        /// listings from factions that have since turned hostile.
        ///
        /// Opportunities are non-binding (§7.2), so dropping them is the correct response and
        /// costs the player nothing. Binding contracts caught by a war need the deliberate
        /// policy in §88, which is a later phase.
        /// </summary>
        public static bool IsStillValid(MarketOpportunity opportunity)
        {
            if (opportunity == null)
            {
                return false;
            }

            Settlement settlement = FindSettlement(opportunity.settlementId);
            if (settlement == null)
            {
                // The buyer no longer exists (§87).
                return false;
            }

            return IsAccessible(settlement);
        }

        public static Settlement FindSettlement(int settlementId)
        {
            if (Find.WorldObjects == null)
            {
                return null;
            }

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.ID == settlementId)
                {
                    return settlement;
                }
            }

            return null;
        }
    }
}
