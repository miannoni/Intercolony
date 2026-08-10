using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public class IntercolonyMod : Mod
    {
        private static readonly IntercolonySettings Defaults = new IntercolonySettings();
        private static IntercolonySettings settings;
        private Vector2 settingsScrollPosition;

        public static IntercolonySettings Settings => settings ?? Defaults;

        public IntercolonyMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<IntercolonySettings>();
            IntercolonyLog.Message("loaded.");
        }

        public override string SettingsCategory()
        {
            return "Intercolony";
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            BuyOnlyTradeUnlock.ApplyEnabledCategories(
                Settings.enabledBuyOnlyTradeCategoryKeys);
            Dialog_CreateRequest.InvalidateAnimalDiscovery();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, inRect.width, MeasureSettings(inRect.width));
            Widgets.AdjustRectsForScrollView(inRect, ref outRect, ref viewRect);
            outRect.width = Mathf.Max(1f, outRect.width);
            outRect.height = Mathf.Max(1f, outRect.height);
            viewRect.width = Mathf.Max(1f, viewRect.width);
            viewRect.height = Mathf.Max(outRect.height, MeasureSettings(viewRect.width));

            Widgets.BeginScrollView(outRect, ref settingsScrollPosition, viewRect);
            try
            {
                DrawSettings(viewRect.width);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static float MeasureSettings(float width)
        {
            return LayoutSettings(Mathf.Max(1f, width), false);
        }

        private static void DrawSettings(float width)
        {
            LayoutSettings(Mathf.Max(1f, width), true);
        }

        private static float LayoutSettings(float width, bool draw)
        {
            float y = 0f;

            SectionTitle("Letter volume", width, ref y, draw);
            Paragraph(
                "Choose which Intercolony letters interrupt play. Letters that do not appear " +
                "are still written to the log.", width, ref y, draw);
            RadioOption(
                "Everything — every update gets a letter",
                IntercolonyLetterVolume.Everything, width, ref y, draw);
            RadioOption(
                "Important only — decisions and notable outcomes",
                IntercolonyLetterVolume.ImportantOnly, width, ref y, draw);
            RadioOption(
                "Minimal — only deadlines, money at risk, debts, breaches, deaths, and war",
                IntercolonyLetterVolume.Minimal, width, ref y, draw);

            SectionGap(ref y);
            SectionTitle("Market pacing", width, ref y, draw);
            Paragraph(
                "Choose how often the market changes and how many open opportunities it can " +
                "hold. Lowering the limit does not remove anything already listed: excess " +
                "listings expire normally, and no new ones appear until there is room.",
                width, ref y, draw);

            float refreshDays = Settings.refreshDays;
            string refreshValue = RefreshDaysLabel(refreshDays);
            Slider(
                refreshValue,
                TallestTextHeight(
                    width, IntercolonySettings.MinRefreshDays,
                    IntercolonySettings.MaxRefreshDays, 0.25f, RefreshDaysLabel),
                ref refreshDays, IntercolonySettings.MinRefreshDays,
                IntercolonySettings.MaxRefreshDays, 0.25f, width, ref y, draw);
            if (draw)
            {
                Settings.refreshDays = refreshDays;
            }

            float active = Settings.activeOpportunities;
            Slider(
                ActiveOpportunitiesLabel(active),
                TallestTextHeight(
                    width, IntercolonySettings.MinActiveOpportunities,
                    IntercolonySettings.MaxActiveOpportunities, 1f,
                    ActiveOpportunitiesLabel),
                ref active,
                IntercolonySettings.MinActiveOpportunities,
                IntercolonySettings.MaxActiveOpportunities, 1f, width, ref y, draw);
            if (draw)
            {
                Settings.activeOpportunities = Mathf.RoundToInt(active);
            }

            SectionGap(ref y);
            SectionTitle("Economy difficulty", width, ref y, draw);
            float difficulty = Settings.economyDifficulty;
            Paragraph(
                EconomyDifficultyDescription(difficulty), width, ref y, draw,
                TallestTextHeight(
                    width, IntercolonySettings.MinEconomyDifficulty,
                    IntercolonySettings.MaxEconomyDifficulty, 0.01f,
                    EconomyDifficultyDescription));
            Slider(
                EconomyDifficultyLabel(difficulty),
                TallestTextHeight(
                    width, IntercolonySettings.MinEconomyDifficulty,
                    IntercolonySettings.MaxEconomyDifficulty, 0.05f,
                    EconomyDifficultyLabel),
                ref difficulty,
                IntercolonySettings.MinEconomyDifficulty,
                IntercolonySettings.MaxEconomyDifficulty, 0.05f, width, ref y, draw);
            if (draw)
            {
                Settings.economyDifficulty = difficulty;
            }

            SectionGap(ref y);
            SectionTitle("Buy-only items", width, ref y, draw);
            IReadOnlyList<BuyOnlyTradeCategoryGroup> buyOnlyGroups = BuyOnlyTradeUnlock.Groups;
            if (buyOnlyGroups.Count == 0)
            {
                Paragraph(
                    "No buy-only item categories are available with the current mod list.",
                    width, ref y, draw);
            }
            else
            {
                Paragraph(
                    "RimWorld disallows the player from selling these items by default. Enabling " +
                    "a category changes the items themselves, so they become sellable to every " +
                    "trader in the game, not only through Intercolony. Other mods are affected too. " +
                    "In vanilla, this rule covers only stone blocks and cooked meals.",
                    width, ref y, draw);

                foreach (BuyOnlyTradeCategoryGroup group in buyOnlyGroups)
                {
                    BuyOnlyCategoryOption(group, width, ref y, draw);
                }
            }

            SectionGap(ref y);
            SectionTitle("Animals no trader sells", width, ref y, draw);
            Paragraph(
                "A few animals can be sold to traders but never bought from them — the thrumbo " +
                "is the one most players know. RimWorld withholds them on purpose, so " +
                "Intercolony does not offer them either.\n\n" +
                "Enabling this lets you order those animals from other colonies. It changes " +
                "nothing outside Intercolony: ordinary traders still will not sell you one. " +
                "You pay the full market price, so this is a matter of whether such an animal " +
                "should be purchasable at all, not of getting one cheaply.",
                width, ref y, draw);

            bool allowUnsold = Settings.allowBuyingUnsoldAnimals;
            float unsoldHeight = Mathf.Max(
                24f, Text.CalcHeight(AllowUnsoldAnimalsLabel, Mathf.Max(1f, width - 28f)));
            if (draw)
            {
                bool wasAllowed = allowUnsold;
                Widgets.CheckboxLabeled(
                    new Rect(0f, y, width, unsoldHeight), AllowUnsoldAnimalsLabel, ref allowUnsold);
                Settings.allowBuyingUnsoldAnimals = allowUnsold;
                if (allowUnsold != wasAllowed)
                {
                    // The offerable list is cached, so it must be rebuilt or the change would
                    // not appear until something else happened to invalidate it.
                    Dialog_CreateRequest.InvalidateAnimalDiscovery();
                }
            }

            y += unsoldHeight + 2f;

            return y;
        }

        private const string AllowUnsoldAnimalsLabel =
            "Allow ordering animals that no trader sells";

        private static string RefreshDaysLabel(float refreshDays)
        {
            return refreshDays == 1f
                ? "New market activity: every 1 day"
                : $"New market activity: every {refreshDays:0.##} days";
        }

        private static string ActiveOpportunitiesLabel(float activeOpportunities)
        {
            return $"Open opportunities kept active: {Mathf.RoundToInt(activeOpportunities)}";
        }

        private static string EconomyDifficultyLabel(float difficulty)
        {
            return $"Difficulty on new trades: {Mathf.RoundToInt(difficulty * 100f)}%";
        }

        private static string EconomyDifficultyDescription(float difficulty)
        {
            const string existingTerms =
                " Existing orders, quotations, and supply agreements keep the amounts already agreed.";
            int difference = Mathf.RoundToInt(
                Mathf.Abs(difficulty - IntercolonySettings.DefaultEconomyDifficulty) * 100f);
            // Branch on what the player sees so rounding cannot produce a misleading 0% change.
            if (difference == 0)
            {
                return "Prices are unchanged. Higher is harder." + existingTerms;
            }

            if (difficulty > IntercolonySettings.DefaultEconomyDifficulty)
            {
                return $"Buyers pay {difference}% less and suppliers charge {difference}% more " +
                       "than they would otherwise. Higher is harder." + existingTerms;
            }

            return $"Buyers pay {difference}% more and suppliers charge {difference}% less " +
                   "than they would otherwise. Higher is harder." + existingTerms;
        }

        private static float TallestTextHeight(
            float width, float min, float max, float step,
            System.Func<float, string> textForValue)
        {
            Text.Font = GameFont.Small;
            float height = 0f;
            int stepCount = Mathf.RoundToInt((max - min) / step);
            for (int i = 0; i <= stepCount; i++)
            {
                height = Mathf.Max(height, Text.CalcHeight(textForValue(min + i * step), width));
            }

            return height;
        }

        private static void SectionTitle(string text, float width, ref float y, bool draw)
        {
            Text.Font = GameFont.Medium;
            float height = Text.CalcHeight(text, width);
            if (draw)
            {
                Widgets.Label(new Rect(0f, y, width, height), text);
            }

            y += height + 4f;
            Text.Font = GameFont.Small;
        }

        private static void Paragraph(
            string text, float width, ref float y, bool draw, float reservedHeight = 0f)
        {
            Text.Font = GameFont.Small;
            float textHeight = Text.CalcHeight(text, width);
            float height = Mathf.Max(textHeight, reservedHeight);
            if (draw)
            {
                Widgets.Label(new Rect(0f, y + height - textHeight, width, textHeight), text);
            }

            y += height + 8f;
        }

        private static void RadioOption(
            string text, IntercolonyLetterVolume value, float width, ref float y, bool draw)
        {
            float height = Mathf.Max(24f, Text.CalcHeight(text, Mathf.Max(1f, width - 28f)));
            if (draw && Widgets.RadioButtonLabeled(
                    new Rect(0f, y, width, height), text, Settings.letterVolume == value))
            {
                Settings.letterVolume = value;
            }

            y += height + 2f;
        }

        private static void BuyOnlyCategoryOption(
            BuyOnlyTradeCategoryGroup group, float width, ref float y, bool draw)
        {
            string itemWord = group.Defs.Count == 1 ? "item" : "items";
            string text = $"{group.Label} ({group.Defs.Count} {itemWord})";
            float height = Mathf.Max(24f, Text.CalcHeight(text, Mathf.Max(1f, width - 28f)));
            Rect row = new Rect(0f, y, width, height);
            if (draw)
            {
                bool enabled = Settings.enabledBuyOnlyTradeCategoryKeys.Contains(group.Key);
                bool wasEnabled = enabled;
                Widgets.CheckboxLabeled(row, text, ref enabled);
                if (enabled)
                {
                    Settings.enabledBuyOnlyTradeCategoryKeys.Add(group.Key);
                }
                else
                {
                    Settings.enabledBuyOnlyTradeCategoryKeys.Remove(group.Key);
                }

                if (enabled != wasEnabled)
                {
                    BuyOnlyTradeUnlock.ApplyEnabledCategories(
                        Settings.enabledBuyOnlyTradeCategoryKeys);
                }

                TooltipHandler.TipRegion(row, group.ItemLabelsTooltip);
            }

            y += height + 2f;
        }

        private static void Slider(
            string label, float reservedLabelHeight, ref float value,
            float min, float max, float roundTo,
            float width, ref float y, bool draw)
        {
            float textHeight = Text.CalcHeight(label, width);
            float labelHeight = Mathf.Max(textHeight, reservedLabelHeight);
            if (draw)
            {
                Widgets.Label(new Rect(0f, y + labelHeight - textHeight, width, textHeight), label);
            }

            // Measure every generated variant rather than predicting line counts, but reserve the
            // maximum: text changed by a control must never move that control's rect mid-drag.
            y += labelHeight;
            float sliderHeight = Mathf.Max(22f, Text.LineHeight);
            if (draw)
            {
                value = Widgets.HorizontalSlider(
                    new Rect(0f, y, width, sliderHeight), value, min, max,
                    middleAlignment: true, roundTo: roundTo);
            }

            y += sliderHeight + 8f;
        }

        private static void SectionGap(ref float y)
        {
            y += 16f;
        }
    }
}
