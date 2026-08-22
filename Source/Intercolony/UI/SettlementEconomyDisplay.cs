using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Intercolony
{
    internal static class SettlementEconomyDisplay
    {
        /// <summary>
        /// A settlement's standing economic character, as labelled rows (§6: the face of a control
        /// shows what you get, the tooltip explains it).
        ///
        /// Stage 1.4 of the 1.0 program. Deliberately reuses tooltips the player already opens
        /// rather than adding a screen: this appears on a Market listing, which is where a buyer is
        /// actually chosen, and on a Relations row. The Relations tab alone would not have been
        /// enough — it only lists settlements already traded with, and the question "what is this
        /// place good for?" is one the player asks *before* the first trade.
        ///
        /// Returns an empty string when no profile resolves, so callers can append unconditionally.
        /// </summary>
        internal static string SettlementEconomicSummary(int settlementId)
        {
            SettlementEconomicProfile profile =
                IntercolonyWorldComponent.Current?.GetProfile(
                    IntercolonyMarketAccess.FindSettlement(settlementId));
            if (profile == null)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Economy: {profile.archetype} / {profile.wealthTier}");
            sb.AppendLine($"Usually supplies: {LeadingCategories(profile, supply: true)}");
            sb.AppendLine($"Usually demands: {LeadingCategories(profile, supply: false)}");
            sb.AppendLine($"Quality preference: {QualityPreferenceLabel(profile.qualityPreference)}");
            return sb.ToString();
        }

        /// <summary>
        /// The categories a settlement leans toward, strongest first. Only weights above parity
        /// count: listing every category in weight order would read as a ranking of six things a
        /// settlement is equally involved in, which is the opposite of an identity.
        /// </summary>
        internal static string LeadingCategories(
            SettlementEconomicProfile profile,
            bool supply)
        {
            List<KeyValuePair<IntercolonyProductCategory, float>> weighted =
                new List<KeyValuePair<IntercolonyProductCategory, float>>();
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                // These are deliberately the baseline values. Stage 1 established what a
                // settlement is; current pressure describes what it is going through, and folding
                // the latter into these rows would erase exactly that distinction.
                float weight = supply
                    ? profile.BaseSupplyFor(category)
                    : profile.BaseDemandFor(category);
                if (weight >= 1f)
                {
                    weighted.Add(
                        new KeyValuePair<IntercolonyProductCategory, float>(category, weight));
                }
            }

            if (weighted.Count == 0)
            {
                return supply ? "little to spare" : "nothing in particular";
            }

            weighted.Sort((a, b) => b.Value.CompareTo(a.Value));

            List<string> labels = new List<string>();
            int take = Mathf.Min(3, weighted.Count);
            for (int i = 0; i < take; i++)
            {
                labels.Add(weighted[i].Key.Label());
            }

            return string.Join(", ", labels.ToArray());
        }

        internal static string QualityPreferenceLabel(float preference)
        {
            if (preference < 0.34f)
            {
                return "indifferent";
            }

            return preference < 0.67f ? "moderate" : "particular";
        }
    }
}
