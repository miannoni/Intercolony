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
        public const float DefaultRefreshDays = 1f;
        public const int DefaultActiveOpportunities = 60;
        public const float DefaultEconomyDifficulty = 1f;

        /// <summary>
        /// Workers were priced too cheaply to be a real decision, so the shipped default is
        /// double. 1.0 reproduces the rate Intercolony used before 2026-08-10.
        /// </summary>
        public const float DefaultLaborCostMultiplier = 2f;
        public const float MinLaborCostMultiplier = 0.5f;
        public const float MaxLaborCostMultiplier = 3f;

        public const float MinRefreshDays = 0.25f;
        public const float MaxRefreshDays = 7f;
        public const int MinActiveOpportunities = 10;
        public const int MaxActiveOpportunities = 200;
        public const float MinEconomyDifficulty = 0.5f;
        public const float MaxEconomyDifficulty = 1.5f;

        public IntercolonyLetterVolume letterVolume = IntercolonyLetterVolume.ImportantOnly;
        public float refreshDays = DefaultRefreshDays;
        public int activeOpportunities = DefaultActiveOpportunities;
        public float economyDifficulty = DefaultEconomyDifficulty;
        public HashSet<string> enabledBuyOnlyTradeCategoryKeys = new HashSet<string>();

        /// <summary>
        /// Whether Intercolony may sell the player animals that no trader in the game sells.
        /// Off by default, because vanilla withholds them deliberately — the thrumbo is the
        /// example everyone knows.
        /// </summary>
        public bool allowBuyingUnsoldAnimals;
        public float laborCostMultiplier = DefaultLaborCostMultiplier;

        public override void ExposeData()
        {
            Scribe_Values.Look(
                ref letterVolume, "letterVolume", IntercolonyLetterVolume.ImportantOnly);
            Scribe_Values.Look(ref refreshDays, "refreshDays", DefaultRefreshDays);
            Scribe_Values.Look(
                ref activeOpportunities, "activeOpportunities", DefaultActiveOpportunities);
            Scribe_Values.Look(
                ref economyDifficulty, "economyDifficulty", DefaultEconomyDifficulty);
            Scribe_Collections.Look(
                ref enabledBuyOnlyTradeCategoryKeys,
                "enabledBuyOnlyTradeCategoryKeys", LookMode.Value);
            Scribe_Values.Look(
                ref allowBuyingUnsoldAnimals, "allowBuyingUnsoldAnimals", false);
            Scribe_Values.Look(
                ref laborCostMultiplier, "laborCostMultiplier", DefaultLaborCostMultiplier);

            if (enabledBuyOnlyTradeCategoryKeys == null)
            {
                enabledBuyOnlyTradeCategoryKeys = new HashSet<string>();
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
        }
    }
}
