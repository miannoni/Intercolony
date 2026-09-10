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
            string modVersion = Content.ModMetaData?.ModVersion;
            string versionLabel = string.IsNullOrEmpty(modVersion) ? "unknown" : modVersion;
            IntercolonyLog.Message($"loaded, version {versionLabel}.");
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
            SectionTitle("Commercial relationships", width, ref y, draw);
            float commercialGoodwillIntervalDays = Settings.commercialGoodwillIntervalDays;
            Slider(
                CommercialGoodwillIntervalLabel(commercialGoodwillIntervalDays),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinCommercialGoodwillIntervalDays,
                    IntercolonySettings.MaxCommercialGoodwillIntervalDays,
                    1f,
                    CommercialGoodwillIntervalLabel),
                ref commercialGoodwillIntervalDays,
                IntercolonySettings.MinCommercialGoodwillIntervalDays,
                IntercolonySettings.MaxCommercialGoodwillIntervalDays,
                1f,
                width,
                ref y,
                draw,
                CommercialGoodwillIntervalTooltip);
            if (draw)
            {
                Settings.commercialGoodwillIntervalDays =
                    Mathf.RoundToInt(commercialGoodwillIntervalDays);
            }

            float commercialGoodwillPerInterval = Settings.commercialGoodwillPerInterval;
            Slider(
                CommercialGoodwillPerIntervalLabel(commercialGoodwillPerInterval),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinCommercialGoodwillPerInterval,
                    IntercolonySettings.MaxCommercialGoodwillPerInterval,
                    1f,
                    CommercialGoodwillPerIntervalLabel),
                ref commercialGoodwillPerInterval,
                IntercolonySettings.MinCommercialGoodwillPerInterval,
                IntercolonySettings.MaxCommercialGoodwillPerInterval,
                1f,
                width,
                ref y,
                draw,
                CommercialGoodwillPerIntervalTooltip);
            if (draw)
            {
                Settings.commercialGoodwillPerInterval =
                    Mathf.RoundToInt(commercialGoodwillPerInterval);
            }

            float commercialGoodwillCeiling = Settings.commercialGoodwillCeiling;
            Slider(
                CommercialGoodwillCeilingLabel(commercialGoodwillCeiling),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinCommercialGoodwillCeiling,
                    IntercolonySettings.MaxCommercialGoodwillCeiling,
                    1f,
                    CommercialGoodwillCeilingLabel),
                ref commercialGoodwillCeiling,
                IntercolonySettings.MinCommercialGoodwillCeiling,
                IntercolonySettings.MaxCommercialGoodwillCeiling,
                1f,
                width,
                ref y,
                draw,
                CommercialGoodwillCeilingTooltip);
            if (draw)
            {
                Settings.commercialGoodwillCeiling = Mathf.RoundToInt(commercialGoodwillCeiling);
            }

            float commercialReputationRequired = Settings.commercialReputationRequired;
            Slider(
                CommercialReputationRequiredLabel(commercialReputationRequired),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinCommercialReputationRequired,
                    IntercolonySettings.MaxCommercialReputationRequired,
                    1f,
                    CommercialReputationRequiredLabel),
                ref commercialReputationRequired,
                IntercolonySettings.MinCommercialReputationRequired,
                IntercolonySettings.MaxCommercialReputationRequired,
                1f,
                width,
                ref y,
                draw,
                CommercialReputationRequiredTooltip);
            if (draw)
            {
                Settings.commercialReputationRequired =
                    Mathf.RoundToInt(commercialReputationRequired);
            }

            SectionGap(ref y);
            SectionTitle("Employment experience", width, ref y, draw);
            float minimumEmploymentDaysForGoodwill =
                Settings.minimumEmploymentDaysForGoodwill;
            Slider(
                MinimumEmploymentDaysLabel(minimumEmploymentDaysForGoodwill),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinMinimumEmploymentDaysForGoodwill,
                    IntercolonySettings.MaxMinimumEmploymentDaysForGoodwill,
                    1f,
                    MinimumEmploymentDaysLabel),
                ref minimumEmploymentDaysForGoodwill,
                IntercolonySettings.MinMinimumEmploymentDaysForGoodwill,
                IntercolonySettings.MaxMinimumEmploymentDaysForGoodwill,
                1f,
                width,
                ref y,
                draw,
                MinimumEmploymentDaysTooltip);
            if (draw)
            {
                Settings.minimumEmploymentDaysForGoodwill =
                    Mathf.RoundToInt(minimumEmploymentDaysForGoodwill);
            }

            float positiveExperienceThreshold = Settings.positiveExperienceThreshold;
            Slider(
                PositiveExperienceThresholdLabel(positiveExperienceThreshold),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinPositiveExperienceThreshold,
                    IntercolonySettings.MaxPositiveExperienceThreshold,
                    0.01f,
                    PositiveExperienceThresholdLabel),
                ref positiveExperienceThreshold,
                IntercolonySettings.MinPositiveExperienceThreshold,
                IntercolonySettings.MaxPositiveExperienceThreshold,
                0.01f,
                width,
                ref y,
                draw,
                PositiveExperienceThresholdTooltip);
            if (draw)
            {
                Settings.positiveExperienceThreshold = Mathf.Clamp(
                    positiveExperienceThreshold,
                    IntercolonySettings.MinPositiveExperienceThreshold,
                    IntercolonySettings.MaxPositiveExperienceThreshold);
                if (Settings.positiveExperienceThreshold <=
                    Settings.negativeExperienceThreshold)
                {
                    Settings.positiveExperienceThreshold =
                        Settings.negativeExperienceThreshold + 0.01f;
                }
            }

            float negativeExperienceThreshold = Settings.negativeExperienceThreshold;
            Slider(
                NegativeExperienceThresholdLabel(negativeExperienceThreshold),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinNegativeExperienceThreshold,
                    IntercolonySettings.MaxNegativeExperienceThreshold,
                    0.01f,
                    NegativeExperienceThresholdLabel),
                ref negativeExperienceThreshold,
                IntercolonySettings.MinNegativeExperienceThreshold,
                IntercolonySettings.MaxNegativeExperienceThreshold,
                0.01f,
                width,
                ref y,
                draw,
                NegativeExperienceThresholdTooltip);
            if (draw)
            {
                Settings.negativeExperienceThreshold = Mathf.Clamp(
                    negativeExperienceThreshold,
                    IntercolonySettings.MinNegativeExperienceThreshold,
                    IntercolonySettings.MaxNegativeExperienceThreshold);
                if (Settings.negativeExperienceThreshold >=
                    Settings.positiveExperienceThreshold)
                {
                    Settings.negativeExperienceThreshold =
                        Settings.positiveExperienceThreshold - 0.01f;
                }
            }

            float employmentGoodwillImpact = Settings.employmentGoodwillImpact;
            Slider(
                EmploymentGoodwillImpactLabel(employmentGoodwillImpact),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinEmploymentGoodwillImpact,
                    IntercolonySettings.MaxEmploymentGoodwillImpact,
                    1f,
                    EmploymentGoodwillImpactLabel),
                ref employmentGoodwillImpact,
                IntercolonySettings.MinEmploymentGoodwillImpact,
                IntercolonySettings.MaxEmploymentGoodwillImpact,
                1f,
                width,
                ref y,
                draw,
                EmploymentGoodwillImpactTooltip);
            if (draw)
            {
                Settings.employmentGoodwillImpact = Mathf.RoundToInt(employmentGoodwillImpact);
            }

            SectionGap(ref y);
            SectionTitle("RFQ response speed", width, ref y, draw);
            float rfqResponseSpeed = Settings.rfqResponseSpeed;
            Slider(
                RfqResponseSpeedLabel(rfqResponseSpeed),
                TallestTextHeight(
                    width,
                    IntercolonySettings.MinRfqResponseSpeed,
                    IntercolonySettings.MaxRfqResponseSpeed,
                    0.1f,
                    RfqResponseSpeedLabel),
                ref rfqResponseSpeed,
                IntercolonySettings.MinRfqResponseSpeed,
                IntercolonySettings.MaxRfqResponseSpeed,
                0.1f,
                width,
                ref y,
                draw,
                RfqResponseSpeedTooltip);
            if (draw)
            {
                Settings.rfqResponseSpeed = rfqResponseSpeed;
            }

            SectionGap(ref y);
            SectionTitle("Find Buyer", width, ref y, draw);
            bool markReadyByDefault = Settings.markReadyNowByDefault;
            float markReadyHeight = Mathf.Max(
                24f, Text.CalcHeight(MarkReadyByDefaultLabel, Mathf.Max(1f, width - 28f)));
            Rect markReadyRect = new Rect(0f, y, width, markReadyHeight);
            if (draw)
            {
                Widgets.CheckboxLabeled(
                    markReadyRect, MarkReadyByDefaultLabel, ref markReadyByDefault);
                TooltipHandler.TipRegion(markReadyRect, MarkReadyByDefaultTooltip);
                Settings.markReadyNowByDefault = markReadyByDefault;
            }
            y += markReadyHeight + 2f;

            SectionGap(ref y);
            SectionTitle("Proposal previews", width, ref y, draw);
            bool showProposalAppealPercentage = Settings.showProposalAppealPercentage;
            float proposalAppealPercentageHeight = Mathf.Max(
                24f, Text.CalcHeight(
                    ShowProposalAppealPercentageLabel, Mathf.Max(1f, width - 28f)));
            Rect proposalAppealPercentageRect = new Rect(
                0f, y, width, proposalAppealPercentageHeight);
            if (draw)
            {
                Widgets.CheckboxLabeled(
                    proposalAppealPercentageRect,
                    ShowProposalAppealPercentageLabel,
                    ref showProposalAppealPercentage);
                TooltipHandler.TipRegion(
                    proposalAppealPercentageRect, ShowProposalAppealPercentageTooltip);
                Settings.showProposalAppealPercentage = showProposalAppealPercentage;
            }
            y += proposalAppealPercentageHeight + 2f;

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
            SectionTitle("Worker wages", width, ref y, draw);
            Paragraph(
                "What hired workers ask for. This applies to wages being quoted now: anyone " +
                "already employed keeps the wage they were hired at, and a renewal is quoted " +
                "fresh like any other offer.\n\n" +
                "How you pay changes the price on top of this. Paying the whole term up front " +
                "is cheapest, per quadrum sits in the middle, and paying by the day is dearest " +
                "— you are buying the freedom to stop at any point. Pay-as-you-go also takes a " +
                "fee at signing, covering the worker's journey however long they end up staying.",
                width, ref y, draw);

            float labor = Settings.laborCostMultiplier;
            Slider(
                LaborCostLabel(labor),
                TallestTextHeight(
                    width, IntercolonySettings.MinLaborCostMultiplier,
                    IntercolonySettings.MaxLaborCostMultiplier, 0.25f, LaborCostLabel),
                ref labor,
                IntercolonySettings.MinLaborCostMultiplier,
                IntercolonySettings.MaxLaborCostMultiplier, 0.25f, width, ref y, draw);
            if (draw)
            {
                Settings.laborCostMultiplier = labor;
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
        private const string MarkReadyByDefaultLabel =
            "Mark buyer-pickup sales ready immediately by default";
        private const string MarkReadyByDefaultTooltip =
            "Sets the initial state of Mark ready now in Find Buyer sale dialogs.";
        private const string ShowProposalAppealPercentageLabel =
            "Show proposal appeal percentage alongside the acceptance band";
        private const string ShowProposalAppealPercentageTooltip =
            "Shows the continuous proposal appeal percentage on both the selling and procurement " +
            "proposal screens.";
        private const string CommercialGoodwillIntervalTooltip =
            "Controls how often a qualifying commercial relationship can add goodwill. Shorter " +
            "intervals apply the change more often; changing it does not grant goodwill retroactively.";
        private const string CommercialGoodwillPerIntervalTooltip =
            "Sets the goodwill gained at each qualifying interval. Set it to 0 to turn off positive " +
            "commercial goodwill while leaving the rest of reputation working.";
        private const string CommercialGoodwillCeilingTooltip =
            "Caps goodwill gained through commercial relationships. It stops below vanilla's Ally " +
            "threshold, so commercial standing never creates an alliance on its own.";
        private const string CommercialReputationRequiredTooltip =
            "Sets the commercial reputation needed before a relationship can add goodwill. Higher " +
            "values require a stronger trading relationship.";
        private const string MinimumEmploymentDaysTooltip =
            "Sets the minimum number of observed days before a worker's experience can change goodwill. " +
            "A day counts only when the worker was actually present and working; days when they were " +
            "downed, absent, or refusing work do not count. This stops a very short employment from " +
            "moving diplomacy.";
        private const string PositiveExperienceThresholdTooltip =
            "An average mood at or above this level counts as a positive employment experience and " +
            "can improve goodwill.";
        private const string NegativeExperienceThresholdTooltip =
            "An average mood at or below this level counts as a negative employment experience and " +
            "can reduce goodwill. It must remain below the positive threshold.";
        private const string EmploymentGoodwillImpactTooltip =
            "Sets how much goodwill a qualifying employment experience gains or loses. Set it to 0 " +
            "to disable this goodwill change without stopping experience recording.";
        private const string RfqResponseSpeedTooltip =
            "Higher values make supplier replies arrive sooner, but distance still matters and " +
            "price or offer quality may still influence timing.";

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

        private static string CommercialGoodwillIntervalLabel(float days)
        {
            int roundedDays = Mathf.RoundToInt(days);
            return roundedDays == 1
                ? "Commercial goodwill interval: every 1 day"
                : $"Commercial goodwill interval: every {roundedDays} days";
        }

        private static string CommercialGoodwillPerIntervalLabel(float delta)
        {
            int roundedDelta = Mathf.RoundToInt(delta);
            return roundedDelta == 0
                ? "Commercial goodwill per interval: +0 (disabled)"
                : $"Commercial goodwill per interval: +{roundedDelta}";
        }

        private static string CommercialGoodwillCeilingLabel(float ceiling)
        {
            return $"Commercial goodwill ceiling: {Mathf.RoundToInt(ceiling)}";
        }

        private static string CommercialReputationRequiredLabel(float required)
        {
            return $"Commercial reputation required for goodwill: {Mathf.RoundToInt(required)}/100";
        }

        private static string MinimumEmploymentDaysLabel(float days)
        {
            return $"Observed days for goodwill: {Mathf.RoundToInt(days)}";
        }

        private static string PositiveExperienceThresholdLabel(float threshold)
        {
            return $"Positive experience threshold: {Mathf.RoundToInt(threshold * 100f)}% mood";
        }

        private static string NegativeExperienceThresholdLabel(float threshold)
        {
            return $"Negative experience threshold: {Mathf.RoundToInt(threshold * 100f)}% mood";
        }

        private static string EmploymentGoodwillImpactLabel(float impact)
        {
            int roundedImpact = Mathf.RoundToInt(impact);
            return roundedImpact == 0
                ? "Employment goodwill impact: ±0 (disabled)"
                : $"Employment goodwill impact: ±{roundedImpact}";
        }

        private static string RfqResponseSpeedLabel(float speed)
        {
            return $"RFQ response speed: {speed:0.0}x";
        }

        /// <summary>
        /// Names a concrete worker so the multiplier cannot be misread. A percentage alone
        /// leaves "200% of what?" unanswered, and the answer used to be wrong by half.
        /// </summary>
        private static string LaborCostLabel(float multiplier)
        {
            // Priced against the same reference worker the rest of the mod uses, so the number
            // means something concrete rather than a percentage of an unstated baseline.
            const int ReferenceWage = 10;
            int asks = Mathf.Max(1, Mathf.RoundToInt(
                ReferenceWage * LaborCandidateService.LaborBaselineMultiplier * multiplier));
            return $"Worker wages: {Mathf.RoundToInt(multiplier * 100f)}% — " +
                   $"a worker the mod originally priced at {ReferenceWage}/day asks {asks}/day";
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
            float width, ref float y, bool draw, string tooltip = null)
        {
            float rowY = y;
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
                if (!string.IsNullOrEmpty(tooltip))
                {
                    TooltipHandler.TipRegion(
                        new Rect(0f, rowY, width, labelHeight + sliderHeight), tooltip);
                }
            }

            y += sliderHeight + 8f;
        }

        private static void SectionGap(ref float y)
        {
            y += 16f;
        }
    }
}
