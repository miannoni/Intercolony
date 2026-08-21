using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
    /// <summary>
    /// Goods and animal selection for a purchase request (DESIGN.md §19, §103).
    /// Animals use their own cached discovery path; they never enter the goods classifier.
    /// </summary>
    public class Dialog_CreateRequest : Window
    {
        private const float ChoiceGap = 8f;
        private const float RowHeight = 26f;
        private const float BottomControlsHeight = 184f;

        private static List<ThingDef> offerableAnimalRaces;
        private static Dictionary<ThingDef, List<PawnKindDef>> offerableKindsByRace;
        private static readonly SettlementEconomicProfile AnimalPreviewProfile = CreatePreviewProfile();

        private readonly IntercolonyWorldComponent state;

        private bool animalMode;
        private string searchText = "";
        private Vector2 listScroll;
        private Vector2 animalControlsScroll;
        private ThingDef selected;
        private AnimalSpec animalSpec = new AnimalSpec();
        private List<PawnKindDef> selectedKinds = new List<PawnKindDef>();
        private List<LifeStageDef> selectedLifeStages = new List<LifeStageDef>();
        private bool selectedPregnancyCapable;

        private string quantityBuffer = "40";
        private int quantity = 40;
        private string deadlineBuffer = "15";
        private int deadlineDays = 15;
        // Having it brought to you is the ordinary case; sending a caravan to fetch it is the
        // exception you opt into. This is the dialog's starting selection only — the persisted
        // field and its scribe default stay as they were, because changing a scribe default
        // silently reinterprets every old save that omitted the value.
        private ProcurementFulfillmentPreference fulfillmentPreference =
            ProcurementFulfillmentPreference.SupplierDelivers;

        /// <summary>Material asked for, or null to let each supplier work in what it has.</summary>
        private ThingDef requestedStuff;

        /// <summary>Minimum workmanship asked for, or null to accept whatever is offered.</summary>
        private QualityCategory? requestedQuality;

        private List<ThingDef> cachedMatches;
        private string cachedSearch;

        public Dialog_CreateRequest(IntercolonyWorldComponent state)
        {
            this.state = state;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        // The height stays at the existing scale-tested value. The extra width makes room for
        // a separately scrolling animal specification pane without pushing the fixed actions down.
        public override Vector2 InitialSize => new Vector2(820f, 620f);

        /// <summary>
        /// Animal races the player may ask a supplier to sell. Built once because this dialog
        /// draws on every GUI event; the companion kind index is built in the same pass.
        /// </summary>
        internal static List<ThingDef> OfferableAnimalRaces()
        {
            if (offerableAnimalRaces != null)
            {
                return offerableAnimalRaces;
            }

            offerableAnimalRaces = new List<ThingDef>();
            offerableKindsByRace = new Dictionary<ThingDef, List<PawnKindDef>>();

            foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (kind?.race == null)
                {
                    continue;
                }

                if (!offerableKindsByRace.TryGetValue(kind.race, out List<PawnKindDef> kinds))
                {
                    kinds = new List<PawnKindDef>();
                    offerableKindsByRace.Add(kind.race, kinds);
                }

                kinds.Add(kind);
            }

            foreach (ThingDef race in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (race?.category != ThingCategory.Pawn || race.race == null ||
                    !race.race.Animal || race.race.Humanlike || race.BaseMarketValue <= 0f ||
                    IntercolonyTradeBlacklist.IsBlacklisted(race) ||
                    !race.tradeability.TraderCanSell() ||
                    !offerableKindsByRace.TryGetValue(race, out List<PawnKindDef> kinds) ||
                    kinds.Count == 0)
                {
                    continue;
                }

                if (!IntercolonyMod.Settings.allowBuyingUnsoldAnimals &&
                    !AnyTraderSells(race))
                {
                    continue;
                }

                kinds.Sort(CompareDefLabels);
                offerableAnimalRaces.Add(race);
            }

            offerableAnimalRaces.Sort(CompareDefLabels);
            return offerableAnimalRaces;
        }

        /// <summary>
        /// Whether any trader in the game would sell this animal to the player.
        ///
        /// Vanilla expresses "you may not buy this" through trade tags rather than through
        /// tradeability: a thrumbo is tagged <c>AnimalExotic</c>, which appears only in
        /// traders' buy lists, so every trader will take one off your hands and none will
        /// hand you one. Asking the stock generators directly means no def name is named
        /// here and modded animals are judged by their own definitions.
        /// </summary>
        internal static bool AnyTraderSells(ThingDef race)
        {
            foreach (TraderKindDef trader in DefDatabase<TraderKindDef>.AllDefsListForReading)
            {
                List<StockGenerator> generators = trader?.stockGenerators;
                for (int i = 0; generators != null && i < generators.Count; i++)
                {
                    if (generators[i]?.TradeabilityFor(race).TraderCanSell() == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Drops the cached race list so a settings change takes effect at once.</summary>
        internal static void InvalidateAnimalDiscovery()
        {
            offerableAnimalRaces = null;
            offerableKindsByRace = null;
        }

        internal static List<PawnKindDef> OfferableKinds(ThingDef race)
        {
            OfferableAnimalRaces();
            return race != null &&
                   offerableKindsByRace.TryGetValue(race, out List<PawnKindDef> kinds)
                ? kinds
                : new List<PawnKindDef>();
        }

        /// <summary>
        /// Life stages are race-local. Repeated references to the same def in one race are
        /// omitted because AnimalSpec cannot unambiguously promise one of them.
        /// </summary>
        internal static List<LifeStageDef> UnambiguousLifeStages(ThingDef race)
        {
            List<LifeStageDef> result = new List<LifeStageDef>();
            List<LifeStageAge> ages = race?.race?.lifeStageAges;
            if (ages == null)
            {
                return result;
            }

            foreach (LifeStageAge age in ages)
            {
                LifeStageDef stage = age?.def;
                if (stage == null || result.Contains(stage))
                {
                    continue;
                }

                int occurrences = 0;
                foreach (LifeStageAge candidate in ages)
                {
                    if (candidate?.def == stage)
                    {
                        occurrences++;
                    }
                }

                if (occurrences == 1)
                {
                    result.Add(stage);
                }
            }

            return result;
        }

        /// <summary>
        /// Uses AnimalSpec's capability validation and adds the UI dependency that pregnancy
        /// can only be requested after the player has explicitly chosen female.
        /// </summary>
        internal static bool PregnancyOfferable(ThingDef race, Gender? gender)
        {
            return gender == Gender.Female &&
                   new AnimalSpec { pregnant = true }.TryValidateFor(
                       race, requireKind: false, out _);
        }

        /// <summary>Clears terms made incoherent by a race, sex, or pregnancy change.</summary>
        internal static void NormalizeAnimalSpec(ThingDef race, AnimalSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            List<PawnKindDef> kinds = OfferableKinds(race);
            if (kinds.Count > 0 && !kinds.Contains(spec.kind))
            {
                // Generation promises always need an exact kind. Multi-kind races still expose
                // the chooser, but start from a valid deterministic default.
                spec.kind = kinds[0];
            }
            else if (kinds.Count == 0)
            {
                spec.kind = null;
            }

            if (!UnambiguousLifeStages(race).Contains(spec.lifeStage))
            {
                spec.lifeStage = null;
            }

            if (!PregnancyOfferable(race, spec.gender))
            {
                spec.pregnant = null;
                spec.minGestationProgress = null;
            }
            else if (spec.pregnant != true)
            {
                spec.minGestationProgress = null;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 30f),
                animalMode ? "Request animals" : "Request goods");
            y += 34f;
            Text.Font = GameFont.Small;

            string introduction = animalMode
                ? "Choose a species and state only the traits a supplier must guarantee."
                : "State what you need. Suppliers may answer with full or partial quotes — or not at all.";
            float introductionHeight = Text.CalcHeight(introduction, inRect.width);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(0f, y, inRect.width, introductionHeight), introduction);
            GUI.color = Color.white;
            y += introductionHeight + 6f;

            const float modeWidth = 128f;
            DrawModeChoice(new Rect(0f, y, modeWidth, 28f), "Goods", animal: false);
            DrawModeChoice(new Rect(modeWidth + ChoiceGap, y, modeWidth, 28f),
                "Animals", animal: true);
            y += 34f;

            string newSearch = Widgets.TextField(new Rect(0f, y, inRect.width, 28f), searchText);
            if (newSearch != searchText)
            {
                searchText = newSearch;
                cachedMatches = null;
            }
            y += 34f;

            float bottomHeight = CurrentBottomControlsHeight(inRect.width);
            float controlsTop = inRect.height - bottomHeight;
            Rect candidatesRect;
            if (animalMode)
            {
                float candidateWidth = Mathf.Floor(inRect.width * 0.42f);
                candidatesRect = new Rect(0f, y, candidateWidth, controlsTop - y - 8f);
                Rect specificationRect = new Rect(
                    candidateWidth + 10f, y, inRect.width - candidateWidth - 10f,
                    controlsTop - y - 8f);
                DrawCandidates(candidatesRect);
                DrawAnimalSpecification(specificationRect);
            }
            else
            {
                candidatesRect = new Rect(0f, y, inRect.width, controlsTop - y - 8f);
                DrawCandidates(candidatesRect);
            }

            DrawBottomControls(new Rect(
                0f, controlsTop, inRect.width, bottomHeight));
        }

        /// <summary>
        /// Measures the bottom block instead of assuming it. It was a fixed reserve, so adding
        /// the material and quality row pushed Send and Cancel off the bottom of a fixed-size
        /// window — and only for items that carry those properties, so it looked fine until a
        /// parka was picked. The summary line wraps too, which was a second cliff waiting at a
        /// larger UI scale. The candidate list absorbs the difference; it has room to spare.
        /// </summary>
        private float CurrentBottomControlsHeight(float width)
        {
            float height = ShowsItemConstraintRow() ? ItemConstraintRowHeight : 0f;
            height += 24f;  // "Fulfillment:" label
            height += 36f;  // fulfilment choices
            height += 34f;  // quantity and deadline
            height += PricePreviewHeight(width);
            height += SendRowHeight;
            return Mathf.Max(BottomControlsHeight, height);
        }

        private const float SendRowHeight = 38f;

        private float PricePreviewHeight(float width)
        {
            if (selected == null || animalMode)
            {
                // The animal branch has its own multi-line layout and was already sized by the
                // original reserve, which the Max below preserves.
                return 48f;
            }

            return Mathf.Max(48f, Text.CalcHeight(GoodsPreviewSummary(), width) + 4f);
        }

        private string GoodsPreviewSummary()
        {
            return $"Requesting {quantity}x {selected.LabelCap} — roughly " +
                   $"{Mathf.RoundToInt(selected.BaseMarketValue * quantity * 1.3f)} silver if anyone answers.";
        }

        private bool ShowsItemConstraintRow()
        {
            return !animalMode && selected != null &&
                   (selected.MadeFromStuff || IntercolonyPricing.CanHaveQuality(selected));
        }

        private const float ItemConstraintRowHeight = 34f;

        private void DrawCandidates(Rect rect)
        {
            List<ThingDef> matches = Matches();
            Rect viewRect = new Rect(
                0f, 0f, rect.width - 16f,
                Mathf.Max(rect.height, matches.Count * RowHeight));

            Widgets.BeginScrollView(rect, ref listScroll, viewRect);
            float rowY = 0f;
            foreach (ThingDef def in matches)
            {
                Rect row = new Rect(0f, rowY, viewRect.width, RowHeight);
                if (selected == def)
                {
                    Widgets.DrawHighlightSelected(row);
                }

                Widgets.DrawHighlightIfMouseover(row);
                Rect labelRect = new Rect(row.x + 4f, row.y + 2f, row.width - 90f, 24f);
                string label = def.LabelCap.ToString();
                Widgets.LabelEllipses(labelRect, label);
                if (Text.CalcSize(label).x > labelRect.width)
                {
                    TooltipHandler.TipRegion(labelRect, label);
                }

                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(new Rect(row.xMax - 84f, row.y + 2f, 80f, 24f),
                    $"~{def.BaseMarketValue:F0}");
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row))
                {
                    if (animalMode)
                    {
                        SelectAnimal(def);
                    }
                    else
                    {
                        selected = def;

                        // A material or workmanship floor belongs to the item it was chosen
                        // for. Carrying one across to a different item would quietly attach a
                        // constraint the player never asked for on this thing.
                        requestedStuff = null;
                        requestedQuality = null;
                    }
                }

                rowY += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawAnimalSpecification(Rect rect)
        {
            if (selected == null)
            {
                string prompt = "Select a species to set its animal specification.";
                float promptHeight = Text.CalcHeight(prompt, rect.width);
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, promptHeight), prompt);
                GUI.color = Color.white;
                return;
            }

            float contentWidth = rect.width - 16f;
            float contentHeight = AnimalSpecificationHeight(contentWidth);
            Rect viewRect = new Rect(0f, 0f, contentWidth, Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(rect, ref animalControlsScroll, viewRect);

            float y = 0f;
            string speciesLabel = $"Specification for {selected.LabelCap}";
            Text.Font = GameFont.Medium;
            float speciesHeight = Text.CalcHeight(speciesLabel, contentWidth);
            Widgets.Label(new Rect(0f, y, contentWidth, speciesHeight), speciesLabel);
            y += speciesHeight + 8f;
            Text.Font = GameFont.Small;

            if (selectedKinds.Count > 1)
            {
                y = DrawSelector(
                    y, contentWidth, "Kind", animalSpec.kind?.LabelCap.ToString() ?? "Choose a kind",
                    () => ChooseKind(selectedKinds));
            }

            Widgets.Label(new Rect(0f, y, contentWidth, 22f), "Sex:");
            y += 22f;
            DrawGenderChoices(new Rect(0f, y, contentWidth, 28f));
            y += 36f;

            if (selectedLifeStages.Count > 0)
            {
                y = DrawSelector(
                    y, contentWidth, "Life stage",
                    animalSpec.lifeStage?.LabelCap.ToString() ?? "Any life stage",
                    () => ChooseLifeStage(selectedLifeStages));
            }
            else
            {
                string noStages = "Life stage: Any (this race has no unambiguous stage choice)";
                float noStagesHeight = Text.CalcHeight(noStages, contentWidth);
                Widgets.Label(new Rect(0f, y, contentWidth, noStagesHeight), noStages);
                y += noStagesHeight + 8f;
            }

            if (selectedPregnancyCapable && animalSpec.gender == Gender.Female)
            {
                Widgets.Label(new Rect(0f, y, contentWidth, 22f), "Pregnancy:");
                y += 22f;
                DrawPregnancyChoices(new Rect(0f, y, contentWidth, 28f));
                y += 36f;
            }

            bool healthEnabled = animalSpec.minHealthFraction.HasValue;
            bool previousHealthEnabled = healthEnabled;
            string healthLabel = healthEnabled
                ? $"Minimum health: {Mathf.RoundToInt(animalSpec.minHealthFraction.Value * 100f)}%"
                : "Minimum health: Any";
            Widgets.CheckboxLabeled(
                new Rect(0f, y, contentWidth, 28f), healthLabel, ref healthEnabled);
            if (healthEnabled != previousHealthEnabled)
            {
                animalSpec.minHealthFraction = healthEnabled ? 0.75f : (float?)null;
            }
            y += 28f;
            if (animalSpec.minHealthFraction.HasValue)
            {
                animalSpec.minHealthFraction = Widgets.HorizontalSlider(
                    new Rect(0f, y, contentWidth, 20f),
                    animalSpec.minHealthFraction.Value, 0f, 1f, roundTo: 0.01f);
                y += 28f;
            }

            if (animalSpec.pregnant == true)
            {
                bool gestationEnabled = animalSpec.minGestationProgress.HasValue;
                bool previousGestationEnabled = gestationEnabled;
                string gestationLabel = gestationEnabled
                    ? $"Minimum gestation: {Mathf.RoundToInt(animalSpec.minGestationProgress.Value * 100f)}%"
                    : "Minimum gestation: Any";
                Widgets.CheckboxLabeled(
                    new Rect(0f, y, contentWidth, 28f), gestationLabel, ref gestationEnabled);
                if (gestationEnabled != previousGestationEnabled)
                {
                    animalSpec.minGestationProgress = gestationEnabled ? 0.25f : (float?)null;
                }
                y += 28f;
                if (animalSpec.minGestationProgress.HasValue)
                {
                    animalSpec.minGestationProgress = Widgets.HorizontalSlider(
                        new Rect(0f, y, contentWidth, 20f),
                        animalSpec.minGestationProgress.Value, 0f, 1f, roundTo: 0.01f);
                }
            }

            Widgets.EndScrollView();
        }

        private float AnimalSpecificationHeight(float width)
        {
            Text.Font = GameFont.Medium;
            float height = Text.CalcHeight($"Specification for {selected.LabelCap}", width) + 8f;
            Text.Font = GameFont.Small;
            if (selectedKinds.Count > 1) height += 58f;
            height += 58f; // Sex label, choices, and gap.
            height += selectedLifeStages.Count > 0
                ? 58f
                : Text.CalcHeight(
                    "Life stage: Any (this race has no unambiguous stage choice)", width) + 8f;
            if (selectedPregnancyCapable && animalSpec.gender == Gender.Female) height += 58f;
            height += animalSpec.minHealthFraction.HasValue ? 56f : 28f;
            if (animalSpec.pregnant == true)
            {
                height += animalSpec.minGestationProgress.HasValue ? 56f : 28f;
            }
            return height;
        }

        private float DrawSelector(float y, float width, string heading, string selection, Action open)
        {
            Widgets.Label(new Rect(0f, y, width, 22f), heading + ":");
            y += 22f;
            Rect buttonRect = new Rect(0f, y, width, 28f);
            if (Widgets.ButtonText(buttonRect, selection))
            {
                open();
            }
            if (Text.CalcSize(selection).x > buttonRect.width - 12f)
            {
                TooltipHandler.TipRegion(buttonRect, selection);
            }
            return y + 36f;
        }

        private void DrawGenderChoices(Rect rect)
        {
            float width = (rect.width - ChoiceGap * 2f) / 3f;
            DrawGenderChoice(new Rect(rect.x, rect.y, width, rect.height), "Either", null);
            DrawGenderChoice(new Rect(rect.x + width + ChoiceGap, rect.y, width, rect.height),
                "Male", Gender.Male);
            DrawGenderChoice(new Rect(rect.x + (width + ChoiceGap) * 2f, rect.y, width, rect.height),
                "Female", Gender.Female);
        }

        private void DrawGenderChoice(Rect rect, string label, Gender? gender)
        {
            bool selectedChoice = animalSpec.gender == gender;
            if (Widgets.ButtonText(rect, label) && !selectedChoice)
            {
                animalSpec.gender = gender;
                NormalizeAnimalSpec(selected, animalSpec);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            if (selectedChoice)
            {
                Widgets.DrawHighlightSelected(rect);
            }
        }

        private void DrawPregnancyChoices(Rect rect)
        {
            float width = (rect.width - ChoiceGap * 2f) / 3f;
            DrawPregnancyChoice(new Rect(rect.x, rect.y, width, rect.height), "Either", null);
            DrawPregnancyChoice(new Rect(rect.x + width + ChoiceGap, rect.y, width, rect.height),
                "Required", true);
            DrawPregnancyChoice(
                new Rect(rect.x + (width + ChoiceGap) * 2f, rect.y, width, rect.height),
                "Not pregnant", false);
        }

        private void DrawPregnancyChoice(Rect rect, string label, bool? pregnant)
        {
            bool selectedChoice = animalSpec.pregnant == pregnant;
            if (Widgets.ButtonText(rect, label) && !selectedChoice)
            {
                animalSpec.pregnant = pregnant;
                NormalizeAnimalSpec(selected, animalSpec);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            if (selectedChoice)
            {
                Widgets.DrawHighlightSelected(rect);
            }
        }

        private void DrawBottomControls(Rect rect)
        {
            float y = rect.y;

            // Only for things that actually carry these properties. Offering a material choice
            // on steel, or a workmanship floor on rice, would invite the player to specify
            // something the item cannot have. Shares its predicate with the height reserve, so
            // the space set aside and the space used cannot drift apart.
            if (ShowsItemConstraintRow())
            {
                bool stuffable = selected.MadeFromStuff;
                bool qualityable = IntercolonyPricing.CanHaveQuality(selected);
                float half = (rect.width - ChoiceGap) / 2f;
                if (stuffable)
                {
                    DrawStuffChoice(new Rect(0f, y, qualityable ? half : rect.width, 28f));
                }

                if (qualityable)
                {
                    DrawQualityChoice(
                        new Rect(stuffable ? half + ChoiceGap : 0f, y,
                            stuffable ? half : rect.width, 28f));
                }

                y += ItemConstraintRowHeight;
            }

            Widgets.Label(new Rect(0f, y, rect.width, 22f), "Fulfillment:");
            y += 24f;

            float choiceWidth = (rect.width - ChoiceGap * 2f) / 3f;
            DrawFulfillmentChoice(new Rect(0f, y, choiceWidth, 28f),
                "Supplier delivers", ProcurementFulfillmentPreference.SupplierDelivers);
            DrawFulfillmentChoice(new Rect(choiceWidth + ChoiceGap, y, choiceWidth, 28f),
                "We collect", ProcurementFulfillmentPreference.PlayerPickup);
            DrawFulfillmentChoice(new Rect((choiceWidth + ChoiceGap) * 2f, y, choiceWidth, 28f),
                "Either", ProcurementFulfillmentPreference.Either);
            y += 36f;

            Widgets.Label(new Rect(0f, y, 90f, 28f), "Quantity:");
            Widgets.TextFieldNumeric(
                new Rect(94f, y, 90f, 28f), ref quantity, ref quantityBuffer, 1, 5000);
            Widgets.Label(new Rect(220f, y, 140f, 28f), "Wanted within:");
            Widgets.TextFieldNumeric(
                new Rect(364f, y, 70f, 28f), ref deadlineDays, ref deadlineBuffer, 1, 60);
            Widgets.Label(new Rect(438f, y, 60f, 28f), "days");
            y += 34f;

            y = DrawPricePreview(y, rect.width);

            Rect sendRect = new Rect(0f, y, 180f, 34f);
            if (selected == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.Label(sendRect, animalMode ? "Select a species first." : "Select an item first.");
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(sendRect, "Send request"))
            {
                Send();
            }

            Rect cancelRect = new Rect(rect.width - 120f, y, 110f, 34f);
            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }
        }

        private float DrawPricePreview(float y, float width)
        {
            if (selected == null)
            {
                return y + 48f;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            if (!animalMode)
            {
                string goodsSummary = GoodsPreviewSummary();
                float goodsHeight = Text.CalcHeight(goodsSummary, width);
                Widgets.Label(new Rect(0f, y, width, goodsHeight), goodsSummary);
                GUI.color = Color.white;
                return y + Mathf.Max(48f, goodsHeight + 4f);
            }

            float unitPrice = IntercolonyPricing.UnitPrice(
                state, selected, null, animalSpec, Mathf.Max(1, quantity), AnimalPreviewProfile,
                IntercolonyProductCategory.Commodities, -1f, null,
                out List<PriceFactor> factors);
            string priceSummary =
                $"Market estimate: {unitPrice:F2} each, " +
                $"{Mathf.RoundToInt(unitPrice * quantity)} silver total.";
            float priceHeight = Text.CalcHeight(priceSummary, width);
            Rect priceRect = new Rect(0f, y, width, priceHeight);
            Widgets.Label(priceRect, priceSummary);
            TooltipHandler.TipRegion(priceRect, IntercolonyPricing.Explain(
                selected, null, animalSpec, quantity, unitPrice, factors));
            y += priceHeight + 1f;

            string cheapestHint =
                "Unspecified traits are priced as the cheapest animal that could satisfy the request.";
            float hintHeight = Text.CalcHeight(cheapestHint, width);
            Widgets.Label(new Rect(0f, y, width, hintHeight), cheapestHint);
            TooltipHandler.TipRegion(
                new Rect(0f, y, width, hintHeight),
                "The supplier chooses any animal matching the terms you state. " +
                "Traits left as Either or Any are therefore not guaranteed and do not add their higher cost.");
            GUI.color = Color.white;
            return y + Mathf.Max(48f - priceHeight - 1f, hintHeight + 4f);
        }

        private void Send()
        {
            if (animalMode &&
                !animalSpec.TryValidateFor(selected, requireKind: true, out string reason))
            {
                Messages.Message(
                    $"Cannot request this animal: {reason}.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            RfqService.CreateRequest(
                state, selected, animalMode ? null : requestedStuff, quantity, deadlineDays,
                fulfillmentPreference, animalMode ? animalSpec : null,
                animalMode ? null : requestedQuality);
            Close();
        }

        private void SelectAnimal(ThingDef race)
        {
            selected = race;
            animalSpec = new AnimalSpec();
            selectedKinds = new List<PawnKindDef>(OfferableKinds(race));
            selectedLifeStages = UnambiguousLifeStages(race);
            selectedPregnancyCapable = PregnancyOfferable(race, Gender.Female);
            NormalizeAnimalSpec(race, animalSpec);
            animalControlsScroll = Vector2.zero;
        }

        private void DrawModeChoice(Rect rect, string label, bool animal)
        {
            bool selectedChoice = animalMode == animal;
            if (Widgets.ButtonText(rect, label) && !selectedChoice)
            {
                animalMode = animal;
                selected = null;
                requestedStuff = null;
                requestedQuality = null;
                animalSpec = new AnimalSpec();
                selectedKinds.Clear();
                selectedLifeStages.Clear();
                selectedPregnancyCapable = false;
                cachedMatches = null;
                listScroll = Vector2.zero;
                animalControlsScroll = Vector2.zero;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            if (selectedChoice)
            {
                Widgets.DrawHighlightSelected(rect);
            }
        }

        private void DrawStuffChoice(Rect rect)
        {
            string label = requestedStuff != null
                ? $"Material: {requestedStuff.LabelCap}"
                : "Material: any";
            if (!Widgets.ButtonText(rect, label))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Any material", () => requestedStuff = null)
            };

            List<ThingDef> stuffs = new List<ThingDef>(GenStuff.AllowedStuffsFor(selected));
            stuffs.Sort(CompareDefLabels);
            foreach (ThingDef stuff in stuffs)
            {
                if (stuff.BaseMarketValue <= 0f)
                {
                    continue;
                }

                ThingDef chosen = stuff;
                options.Add(new FloatMenuOption(
                    chosen.LabelCap, () => requestedStuff = chosen));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawQualityChoice(Rect rect)
        {
            string label = requestedQuality.HasValue
                ? $"Quality: {requestedQuality.Value.GetLabel()}+"
                : "Quality: any";
            if (!Widgets.ButtonText(rect, label))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Any quality", () => requestedQuality = null)
            };

            // Awful is not offered: it is a floor nobody would ask for, and Legendary is left
            // out because no settlement will promise it.
            foreach (QualityCategory quality in new[]
            {
                QualityCategory.Poor, QualityCategory.Normal, QualityCategory.Good,
                QualityCategory.Excellent, QualityCategory.Masterwork
            })
            {
                QualityCategory chosen = quality;
                options.Add(new FloatMenuOption(
                    $"{chosen.GetLabel()} or better", () => requestedQuality = chosen));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawFulfillmentChoice(
            Rect rect, string label, ProcurementFulfillmentPreference preference)
        {
            bool selectedChoice = fulfillmentPreference == preference;
            if (Widgets.ButtonText(rect, label) && !selectedChoice)
            {
                fulfillmentPreference = preference;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            if (selectedChoice)
            {
                Widgets.DrawHighlightSelected(rect);
            }
        }

        private void ChooseKind(List<PawnKindDef> kinds)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (PawnKindDef kind in kinds)
            {
                PawnKindDef chosen = kind;
                options.Add(new FloatMenuOption(chosen.LabelCap, () => animalSpec.kind = chosen));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ChooseLifeStage(List<LifeStageDef> stages)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Any life stage", () => animalSpec.lifeStage = null)
            };
            foreach (LifeStageDef stage in stages)
            {
                LifeStageDef chosen = stage;
                options.Add(new FloatMenuOption(chosen.LabelCap, () => animalSpec.lifeStage = chosen));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Search results are cached per search term. Animal discovery itself is also cached and
        /// never re-scans either def database during drawing.
        /// </summary>
        private List<ThingDef> Matches()
        {
            if (cachedMatches != null && cachedSearch == searchText)
            {
                return cachedMatches;
            }

            cachedMatches = new List<ThingDef>();
            cachedSearch = searchText;
            string needle = searchText?.Trim().ToLowerInvariant() ?? "";
            List<ThingDef> source = animalMode
                ? OfferableAnimalRaces()
                : IntercolonyProductClassifier.TradableDefs;
            foreach (ThingDef def in source)
            {
                if (needle.Length > 0 &&
                    !(def.label ?? "").ToLowerInvariant().Contains(needle))
                {
                    continue;
                }
                cachedMatches.Add(def);
            }

            // The source lists are already sorted. Keep this local sort so a future invalidation
            // cannot make the search result jump into definition order.
            cachedMatches.Sort(CompareDefLabels);
            return cachedMatches;
        }

        private static int CompareDefLabels(Def a, Def b)
        {
            return string.Compare(
                a?.label ?? "", b?.label ?? "", StringComparison.CurrentCultureIgnoreCase);
        }

        private static SettlementEconomicProfile CreatePreviewProfile()
        {
            SettlementEconomicProfile profile = new SettlementEconomicProfile
            {
                seed = 0,
                wealthTier = IntercolonyWealthTier.Modest,
                qualityPreference = 0.5f
            };
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                profile.demandWeights[(int)category] = 1f;
            }
            return profile;
        }
    }
}
