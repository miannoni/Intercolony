using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public enum IntercolonyLetterVolume
    {
        Everything,
        ImportantOnly,
        Minimal
    }

    public class IntercolonySettings : ModSettings
    {
        private const int CurrentSettingsVersion = 1;

        public const IntercolonyLetterVolume DefaultLetterVolume =
            IntercolonyLetterVolume.Minimal;
        public const float DefaultRefreshDays = 0.25f;
        public const int DefaultActiveOpportunities = 50;
        public const float DefaultEconomyDifficulty = 1f;

        public const IntercolonyLetterVolume LegacyLetterVolume =
            IntercolonyLetterVolume.ImportantOnly;
        public const float LegacyRefreshDays = 1f;
        public const int LegacyActiveOpportunities = 60;

        /// <summary>
        /// 100% now means three times the rate Intercolony shipped with. Doubling was not
        /// enough: hiring was still cheap enough that it was never really a decision.
        /// </summary>
        public const float DefaultLaborCostMultiplier = 1f;
        public const float MinLaborCostMultiplier = 0.5f;
        public const float MaxLaborCostMultiplier = 2f;

        public const int DefaultCommercialGoodwillIntervalDays = 7;
        public const int DefaultCommercialGoodwillPerInterval = 2;
        public const int DefaultCommercialGoodwillCeiling = 15;
        public const int DefaultCommercialReputationRequired = 85;
        public const int DefaultMinimumEmploymentDaysForGoodwill = 4;
        public const float DefaultPositiveExperienceThreshold = 0.75f;
        public const float DefaultNegativeExperienceThreshold = 0.35f;
        public const int DefaultEmploymentGoodwillImpact = 2;
        public const float DefaultRfqResponseSpeed = 1f;

        public const int LegacyCommercialGoodwillIntervalDays = 15;
        public const int LegacyCommercialGoodwillPerInterval = 1;
        public const int LegacyCommercialGoodwillCeiling = 60;
        public const int LegacyCommercialReputationRequired = 80;
        public const int LegacyMinimumEmploymentDaysForGoodwill = 10;
        public const int LegacyEmploymentGoodwillImpact = 3;

        private static readonly string[] DefaultEnabledBuyOnlyTradeCategoryKeys =
            { "FoodMeals", "StoneBlocks" };
        private static readonly string[] LegacyEnabledBuyOnlyTradeCategoryKeys =
            new string[0];

        public const float MinRefreshDays = 0.25f;
        public const float MaxRefreshDays = 7f;
        public const int MinActiveOpportunities = 10;
        public const int MaxActiveOpportunities = 200;
        public const float MinEconomyDifficulty = 0.5f;
        public const float MaxEconomyDifficulty = 1.5f;
        public const int MinCommercialGoodwillIntervalDays = 1;
        public const int MaxCommercialGoodwillIntervalDays = 60;
        public const int MinCommercialGoodwillPerInterval = 0;
        public const int MaxCommercialGoodwillPerInterval = 5;
        public const int MinCommercialGoodwillCeiling = 0;
        public const int MaxCommercialGoodwillCeiling = 74;
        public const int MinCommercialReputationRequired = 0;
        public const int MaxCommercialReputationRequired = 100;
        public const int MinMinimumEmploymentDaysForGoodwill = 1;
        public const int MaxMinimumEmploymentDaysForGoodwill = 60;
        public const float MinPositiveExperienceThreshold = 0.5f;
        public const float MaxPositiveExperienceThreshold = 0.95f;
        public const float MinNegativeExperienceThreshold = 0.05f;
        public const float MaxNegativeExperienceThreshold = 0.5f;
        public const int MinEmploymentGoodwillImpact = 0;
        public const int MaxEmploymentGoodwillImpact = 10;
        public const float MinRfqResponseSpeed = 0.5f;
        public const float MaxRfqResponseSpeed = 2f;

        // -1 is deliberately outside the settings-version domain, so this node is always saved.
        public int settingsVersion = CurrentSettingsVersion;
        public IntercolonyLetterVolume letterVolume = DefaultLetterVolume;
        public float refreshDays = DefaultRefreshDays;
        public int activeOpportunities = DefaultActiveOpportunities;
        public float economyDifficulty = DefaultEconomyDifficulty;
        public HashSet<string> enabledBuyOnlyTradeCategoryKeys =
            new HashSet<string>(DefaultEnabledBuyOnlyTradeCategoryKeys);

        /// <summary>
        /// Whether Intercolony may sell the player animals that no trader in the game sells.
        /// Off by default, because vanilla withholds them deliberately — the thrumbo is the
        /// example everyone knows.
        /// </summary>
        public bool allowBuyingUnsoldAnimals;
        /// <summary>
        /// Whether new Find Buyer pickup dialogs initially offer to mark the sale ready now.
        /// This is only a per-dialog starting value; the player can change it for each sale.
        /// </summary>
        public bool markReadyNowByDefault = true;
        /// <summary>Whether proposal screens show the continuous appeal percentage beside its band.</summary>
        public bool showProposalAppealPercentage = false;
        public float laborCostMultiplier = DefaultLaborCostMultiplier;
        public int commercialGoodwillIntervalDays = DefaultCommercialGoodwillIntervalDays;
        public int commercialGoodwillPerInterval = DefaultCommercialGoodwillPerInterval;
        public int commercialGoodwillCeiling = DefaultCommercialGoodwillCeiling;
        public int commercialReputationRequired = DefaultCommercialReputationRequired;
        public int minimumEmploymentDaysForGoodwill = DefaultMinimumEmploymentDaysForGoodwill;
        public float positiveExperienceThreshold = DefaultPositiveExperienceThreshold;
        public float negativeExperienceThreshold = DefaultNegativeExperienceThreshold;
        public int employmentGoodwillImpact = DefaultEmploymentGoodwillImpact;
        public float rfqResponseSpeed = DefaultRfqResponseSpeed;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref settingsVersion, "settingsVersion", -1);
            bool useLegacySettings = settingsVersion < CurrentSettingsVersion;

            Scribe_Values.Look(
                ref letterVolume,
                "letterVolume",
                useLegacySettings ? LegacyLetterVolume : DefaultLetterVolume);
            Scribe_Values.Look(
                ref refreshDays,
                "refreshDays",
                useLegacySettings ? LegacyRefreshDays : DefaultRefreshDays);
            Scribe_Values.Look(
                ref activeOpportunities,
                "activeOpportunities",
                useLegacySettings ? LegacyActiveOpportunities : DefaultActiveOpportunities);
            // Both keys are deliberately renamed. Their scales were recentred on 2026-08-10, so
            // an old saved number now means something different — reading it back would silently
            // compound the new baseline with a value chosen against the old one. A new key makes
            // the setting fall back to its default once, which is the intended reset.
            Scribe_Values.Look(
                ref economyDifficulty, "economyDifficultyV2", DefaultEconomyDifficulty);
            Scribe_Collections.Look(
                ref enabledBuyOnlyTradeCategoryKeys,
                "enabledBuyOnlyTradeCategoryKeys", LookMode.Value);
            Scribe_Values.Look(
                ref allowBuyingUnsoldAnimals, "allowBuyingUnsoldAnimals", false);
            Scribe_Values.Look(
                ref markReadyNowByDefault, "markReadyNowByDefault", true);
            Scribe_Values.Look(
                ref showProposalAppealPercentage, "showProposalAppealPercentage", false);
            Scribe_Values.Look(
                ref laborCostMultiplier, "laborCostMultiplierV2", DefaultLaborCostMultiplier);
            Scribe_Values.Look(
                ref commercialGoodwillIntervalDays,
                "commercialGoodwillIntervalDays",
                useLegacySettings
                    ? LegacyCommercialGoodwillIntervalDays
                    : DefaultCommercialGoodwillIntervalDays);
            Scribe_Values.Look(
                ref commercialGoodwillPerInterval,
                "commercialGoodwillPerInterval",
                useLegacySettings
                    ? LegacyCommercialGoodwillPerInterval
                    : DefaultCommercialGoodwillPerInterval);
            Scribe_Values.Look(
                ref commercialGoodwillCeiling,
                "commercialGoodwillCeiling",
                useLegacySettings
                    ? LegacyCommercialGoodwillCeiling
                    : DefaultCommercialGoodwillCeiling);
            Scribe_Values.Look(
                ref commercialReputationRequired,
                "commercialReputationRequired",
                useLegacySettings
                    ? LegacyCommercialReputationRequired
                    : DefaultCommercialReputationRequired);
            Scribe_Values.Look(
                ref minimumEmploymentDaysForGoodwill,
                "minimumEmploymentDaysForGoodwill",
                useLegacySettings
                    ? LegacyMinimumEmploymentDaysForGoodwill
                    : DefaultMinimumEmploymentDaysForGoodwill);
            Scribe_Values.Look(
                ref positiveExperienceThreshold,
                "positiveExperienceThreshold",
                DefaultPositiveExperienceThreshold);
            Scribe_Values.Look(
                ref negativeExperienceThreshold,
                "negativeExperienceThreshold",
                DefaultNegativeExperienceThreshold);
            Scribe_Values.Look(
                ref employmentGoodwillImpact,
                "employmentGoodwillImpact",
                useLegacySettings
                    ? LegacyEmploymentGoodwillImpact
                    : DefaultEmploymentGoodwillImpact);
            Scribe_Values.Look(
                ref rfqResponseSpeed,
                "rfqResponseSpeed",
                DefaultRfqResponseSpeed);

            if (enabledBuyOnlyTradeCategoryKeys == null)
            {
                enabledBuyOnlyTradeCategoryKeys = new HashSet<string>(
                    useLegacySettings
                        ? LegacyEnabledBuyOnlyTradeCategoryKeys
                        : DefaultEnabledBuyOnlyTradeCategoryKeys);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                settingsVersion = CurrentSettingsVersion;
            }

            if (letterVolume != IntercolonyLetterVolume.Everything &&
                letterVolume != IntercolonyLetterVolume.ImportantOnly &&
                letterVolume != IntercolonyLetterVolume.Minimal)
            {
                letterVolume = IntercolonyLetterVolume.ImportantOnly;
            }

            refreshDays = Mathf.Clamp(refreshDays, MinRefreshDays, MaxRefreshDays);
            activeOpportunities = Mathf.Clamp(
                activeOpportunities, MinActiveOpportunities, MaxActiveOpportunities);
            economyDifficulty = Mathf.Clamp(
                economyDifficulty, MinEconomyDifficulty, MaxEconomyDifficulty);
            laborCostMultiplier = Mathf.Clamp(
                laborCostMultiplier, MinLaborCostMultiplier, MaxLaborCostMultiplier);
            commercialGoodwillIntervalDays = Mathf.Clamp(
                commercialGoodwillIntervalDays,
                MinCommercialGoodwillIntervalDays,
                MaxCommercialGoodwillIntervalDays);
            commercialGoodwillPerInterval = Mathf.Clamp(
                commercialGoodwillPerInterval,
                MinCommercialGoodwillPerInterval,
                MaxCommercialGoodwillPerInterval);
            commercialGoodwillCeiling = Mathf.Clamp(
                commercialGoodwillCeiling,
                MinCommercialGoodwillCeiling,
                MaxCommercialGoodwillCeiling);
            commercialReputationRequired = Mathf.Clamp(
                commercialReputationRequired,
                MinCommercialReputationRequired,
                MaxCommercialReputationRequired);
            minimumEmploymentDaysForGoodwill = Mathf.Clamp(
                minimumEmploymentDaysForGoodwill,
                MinMinimumEmploymentDaysForGoodwill,
                MaxMinimumEmploymentDaysForGoodwill);
            positiveExperienceThreshold = Mathf.Clamp(
                positiveExperienceThreshold,
                MinPositiveExperienceThreshold,
                MaxPositiveExperienceThreshold);
            negativeExperienceThreshold = Mathf.Clamp(
                negativeExperienceThreshold,
                MinNegativeExperienceThreshold,
                MaxNegativeExperienceThreshold);
            if (negativeExperienceThreshold >= positiveExperienceThreshold)
            {
                negativeExperienceThreshold = positiveExperienceThreshold - 0.01f;
            }

            employmentGoodwillImpact = Mathf.Clamp(
                employmentGoodwillImpact,
                MinEmploymentGoodwillImpact,
                MaxEmploymentGoodwillImpact);
            rfqResponseSpeed = Mathf.Clamp(
                rfqResponseSpeed, MinRfqResponseSpeed, MaxRfqResponseSpeed);
        }
    }
}
