using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public class WITab_Economy : WITab
    {
        private const float TabWidth = 440f;
        private const float Margin = 10f;
        private const float KeyWidth = 130f;
        private const float RowGap = 4f;
        private const float SectionGap = 12f;
        private const float MinimumHeight = 48f;

        internal readonly struct DisplayRow
        {
            internal readonly string key;
            internal readonly string value;
            internal readonly bool startsSection;

            internal DisplayRow(string key, string value, bool startsSection = false)
            {
                this.key = key;
                this.value = value;
                this.startsSection = startsSection;
            }
        }

        public WITab_Economy()
        {
            size = new Vector2(TabWidth, MinimumHeight);
            labelKey = "IntercolonyTabEconomy";
        }

        protected override void UpdateSize()
        {
            List<DisplayRow> rows = BuildRows();
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            size = new Vector2(
                TabWidth,
                Mathf.Max(MinimumHeight, Margin * 2f + MeasureRows(rows)));
            Text.Font = previousFont;
        }

        protected override void FillTab()
        {
            Settlement settlement = SelObject as Settlement;
            if (settlement == null)
            {
                return;
            }

            List<DisplayRow> rows = BuildRows(IntercolonyWorldComponent.Current, settlement);
            Text.Font = GameFont.Small;
            float y = Margin;
            float valueWidth = TabWidth - Margin * 2f - KeyWidth;
            for (int i = 0; i < rows.Count; i++)
            {
                DisplayRow row = rows[i];
                if (row.startsSection)
                {
                    y += SectionGap;
                }

                float keyHeight = Text.CalcHeight(row.key, KeyWidth);
                float valueHeight = Text.CalcHeight(row.value, valueWidth);
                float rowHeight = Mathf.Max(keyHeight, valueHeight);
                Widgets.Label(new Rect(Margin, y, KeyWidth, keyHeight), row.key);
                Widgets.Label(
                    new Rect(Margin + KeyWidth, y, valueWidth, valueHeight), row.value);
                y += rowHeight + RowGap;
            }
        }

        private List<DisplayRow> BuildRows()
        {
            return BuildRows(IntercolonyWorldComponent.Current, SelObject as Settlement);
        }

        /// <summary>
        /// Builds the same measured rows used by the tab. This is internal so the self-test can
        /// inspect the production row builder; rebuilding the event filter in a test would allow
        /// the tab to lose its explanation while the test continued to pass.
        /// </summary>
        internal static List<DisplayRow> BuildRows(
            IntercolonyWorldComponent state, Settlement settlement)
        {
            List<DisplayRow> rows = new List<DisplayRow>();
            if (settlement == null)
            {
                return rows;
            }

            SettlementEconomicProfile profile = state?.GetProfile(settlement);
            if (profile == null)
            {
                string factionName = settlement.Faction?.Name ?? "none";
                rows.Add(new DisplayRow(
                    "Economy:", $"Not an economic participant (faction: {factionName})."));
                return rows;
            }

            rows.Add(new DisplayRow("Economy:", $"{profile.archetype} / {profile.wealthTier}"));
            rows.Add(new DisplayRow(
                "Usually supplies:",
                SettlementEconomyDisplay.LeadingCategories(profile, supply: true)));
            rows.Add(new DisplayRow(
                "Usually demands:",
                SettlementEconomyDisplay.LeadingCategories(profile, supply: false)));
            rows.Add(new DisplayRow(
                "Quality preference:",
                SettlementEconomyDisplay.QualityPreferenceLabel(profile.qualityPreference)));

            bool firstCondition = true;
            List<EconomicEvent> activeEvents = EconomicEventService.ActiveEventsAffecting(
                state, settlement);
            for (int i = 0; i < activeEvents.Count; i++)
            {
                EconomicEvent economicEvent = activeEvents[i];
                rows.Add(new DisplayRow(
                    "Right now:",
                    $"{economicEvent.type.Label()}, " +
                    EconomicEventService.RemainingDurationLabel(
                        economicEvent, GenTicks.TicksGame),
                    firstCondition));
                firstCondition = false;
            }

            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                float demand = EffectiveEconomyService.CurrentDemandPressure(
                    state, settlement.ID, category);
                float supply = EffectiveEconomyService.CurrentSupplyPressure(
                    state, settlement.ID, category);
                bool demandDisturbed = !Mathf.Approximately(demand, SettlementMarketState.Neutral);
                bool supplyDisturbed = !Mathf.Approximately(supply, SettlementMarketState.Neutral);
                if (!demandDisturbed && !supplyDisturbed)
                {
                    continue;
                }

                string condition = CurrentConditionLabel(
                    category, demand, demandDisturbed, supply, supplyDisturbed);
                rows.Add(new DisplayRow("Right now:", condition, firstCondition));
                firstCondition = false;
            }

            return rows;
        }

        private static string CurrentConditionLabel(
            IntercolonyProductCategory category,
            float demand,
            bool demandDisturbed,
            float supply,
            bool supplyDisturbed)
        {
            string categoryLabel = category.Label();
            if (demandDisturbed && supplyDisturbed)
            {
                string demandLabel = demand > SettlementMarketState.Neutral ? "shortage" : "surplus";
                string supplyLabel = supply > SettlementMarketState.Neutral ? "shortage" : "surplus";
                return demandLabel == supplyLabel
                    ? $"{categoryLabel} {demandLabel}"
                    : $"{categoryLabel} demand {demandLabel} / supply {supplyLabel}";
            }

            float pressure = demandDisturbed ? demand : supply;
            return $"{categoryLabel} {(pressure > 1f ? "shortage" : "surplus")}";
        }

        private static float MeasureRows(List<DisplayRow> rows)
        {
            float height = 0f;
            float valueWidth = TabWidth - Margin * 2f - KeyWidth;
            for (int i = 0; i < rows.Count; i++)
            {
                DisplayRow row = rows[i];
                if (row.startsSection)
                {
                    height += SectionGap;
                }

                height += Mathf.Max(
                    Text.CalcHeight(row.key, KeyWidth),
                    Text.CalcHeight(row.value, valueWidth));
                height += RowGap;
            }

            return height;
        }
    }
}
