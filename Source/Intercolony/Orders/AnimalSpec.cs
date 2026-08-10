using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// A fungible animal promise. Species remains the owning record's ThingDef; this stores
    /// only the independently selectable constraints and exact generation kind.
    /// </summary>
    public class AnimalSpec : IExposable
    {
        /// <summary>
        /// Exact generation variant. Required on promises that will generate a pawn, but a
        /// sell-side specification may leave it null because it matches an existing animal.
        /// </summary>
        public PawnKindDef kind;

        /// <summary>Required sex, or null when either is acceptable.</summary>
        public Gender? gender;

        /// <summary>Required race-specific life-stage definition, or null for any stage.</summary>
        public LifeStageDef lifeStage;

        /// <summary>Null means either; true and false are both real constraints.</summary>
        public bool? pregnant;

        /// <summary>Minimum summary health fraction, or null when health is unrestricted.</summary>
        public float? minHealthFraction;

        /// <summary>
        /// Minimum pregnancy progress, or null when any gestation progress is acceptable.
        /// Only coherent when pregnancy is explicitly required.
        /// </summary>
        public float? minGestationProgress;

        // A removed optional def resolves to null just like "don't care". Remember whether a
        // non-null XML node failed resolution so load validation cannot silently weaken a promise.
        private bool kindFailedToResolve;
        private bool lifeStageFailedToResolve;
        private bool minHealthFractionFailedToLoad;
        private bool minGestationProgressFailedToLoad;

        public AnimalSpec()
        {
        }

        public void ExposeData()
        {
            bool kindNodeWasPresent = Scribe.mode == LoadSaveMode.LoadingVars &&
                                      Scribe.loader.curXmlParent["kind"] != null;
            bool lifeStageNodeWasPresent = Scribe.mode == LoadSaveMode.LoadingVars &&
                                           Scribe.loader.curXmlParent["lifeStage"] != null;
            bool minHealthNodeWasPresent = Scribe.mode == LoadSaveMode.LoadingVars &&
                                           Scribe.loader.curXmlParent["minHealthFraction"] != null;
            bool minGestationNodeWasPresent = Scribe.mode == LoadSaveMode.LoadingVars &&
                                              Scribe.loader.curXmlParent["minGestationProgress"] != null;

            Scribe_Defs.Look(ref kind, "kind");
            Scribe_Values.Look(ref gender, "gender");
            Scribe_Defs.Look(ref lifeStage, "lifeStage");
            Scribe_Values.Look(ref pregnant, "pregnant");
            Scribe_Values.Look(ref minHealthFraction, "minHealthFraction");
            Scribe_Values.Look(ref minGestationProgress, "minGestationProgress");

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                kindFailedToResolve = kindNodeWasPresent && kind == null;
                lifeStageFailedToResolve = lifeStageNodeWasPresent && lifeStage == null;
                minHealthFractionFailedToLoad = minHealthNodeWasPresent && !minHealthFraction.HasValue;
                minGestationProgressFailedToLoad =
                    minGestationNodeWasPresent && !minGestationProgress.HasValue;
            }
        }

        /// <summary>A distinct copy for carrying an accepted promise into another saved owner.</summary>
        public AnimalSpec Copy()
        {
            return new AnimalSpec
            {
                kind = kind,
                gender = gender,
                lifeStage = lifeStage,
                pregnant = pregnant,
                minHealthFraction = minHealthFraction,
                minGestationProgress = minGestationProgress
            };
        }

        public bool IsValidFor(ThingDef race)
        {
            return TryValidateFor(race, requireKind: false, out _);
        }

        /// <summary>
        /// Validates the specification against its separately stored race and supplies a
        /// load-diagnostic reason. A generation owner can additionally require an exact kind.
        /// </summary>
        public bool TryValidateFor(ThingDef race, bool requireKind, out string reason)
        {
            if (race == null)
            {
                reason = "missing race definition";
                return false;
            }

            if (race.race == null)
            {
                reason = $"race {race.defName} has no race properties";
                return false;
            }

            if (race.race.Humanlike)
            {
                reason = $"race {race.defName} is humanlike";
                return false;
            }

            if (!race.race.Animal)
            {
                reason = $"race {race.defName} is not an animal";
                return false;
            }

            if (requireKind && kind == null)
            {
                reason = kindFailedToResolve
                    ? "saved pawn kind definition is missing"
                    : "missing pawn kind";
                return false;
            }

            if (kindFailedToResolve)
            {
                reason = "saved pawn kind definition is missing";
                return false;
            }

            if (kind != null && kind.race != race)
            {
                reason = $"pawn kind {kind.defName ?? "<unnamed>"} belongs to " +
                         $"{kind.race?.defName ?? "a missing race"}, not {race.defName}";
                return false;
            }

            if (lifeStageFailedToResolve)
            {
                reason = "saved life stage definition is missing";
                return false;
            }

            if (minHealthFractionFailedToLoad)
            {
                reason = "saved minimum health fraction could not be read";
                return false;
            }

            if (minGestationProgressFailedToLoad)
            {
                reason = "saved minimum gestation progress could not be read";
                return false;
            }

            if (minHealthFraction.HasValue && !IsFraction(minHealthFraction.Value))
            {
                reason = $"minimum health fraction {minHealthFraction.Value} is outside 0..1";
                return false;
            }

            if (minGestationProgress.HasValue && !IsFraction(minGestationProgress.Value))
            {
                reason = $"minimum gestation progress {minGestationProgress.Value} is outside 0..1";
                return false;
            }

            if (minGestationProgress.HasValue && pregnant != true)
            {
                reason = "minimum gestation progress requires pregnancy to be explicitly required";
                return false;
            }

            if (lifeStage != null)
            {
                int occurrences = 0;
                List<LifeStageAge> stages = race.race.lifeStageAges;
                if (stages != null)
                {
                    for (int i = 0; i < stages.Count; i++)
                    {
                        if (stages[i]?.def == lifeStage)
                        {
                            occurrences++;
                        }
                    }
                }

                if (occurrences == 0)
                {
                    reason = $"life stage {lifeStage.defName ?? "<unnamed>"} is missing from race {race.defName}";
                    return false;
                }

                if (occurrences > 1)
                {
                    reason = $"life stage {lifeStage.defName ?? "<unnamed>"} occurs {occurrences} times " +
                             $"in race {race.defName} and is ambiguous";
                    return false;
                }
            }

            if (pregnant == true)
            {
                if (race.race.gestationPeriodDays <= 0f)
                {
                    reason = $"pregnancy is required but race {race.defName} has no positive gestation period";
                    return false;
                }

                if (race.HasComp<CompEggLayer>())
                {
                    reason = $"pregnancy is required but race {race.defName} is an egg-layer";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>Compact constraint label for later order UI.</summary>
        public string ShortLabel(ThingDef race)
        {
            List<string> parts = ConstraintLabels();
            string raceLabel = race?.LabelCap.ToString() ?? "<missing animal race>";
            return parts.Count == 0
                ? raceLabel
                : $"{raceLabel} ({string.Join(", ", parts.ToArray())})";
        }

        /// <summary>Full constraint description; absent values are omitted, never formatted.</summary>
        public string Describe(ThingDef race)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(race?.LabelCap.ToString() ?? "<missing animal race>");
            if (kind != null) sb.AppendLine($"  Kind: {kind.LabelCap}");
            if (gender.HasValue) sb.AppendLine($"  Sex: {gender.Value.GetLabel(animal: true)}");
            if (lifeStage != null) sb.AppendLine($"  Life stage: {lifeStage.LabelCap}");
            if (pregnant.HasValue) sb.AppendLine($"  Pregnancy: {(pregnant.Value ? "required" : "not pregnant")}");
            if (minHealthFraction.HasValue)
            {
                sb.AppendLine($"  Minimum health: {Mathf.RoundToInt(minHealthFraction.Value * 100f)}%");
            }
            if (minGestationProgress.HasValue)
            {
                sb.AppendLine($"  Minimum gestation: {Mathf.RoundToInt(minGestationProgress.Value * 100f)}%");
            }
            return sb.ToString();
        }

        private List<string> ConstraintLabels()
        {
            List<string> parts = new List<string>();
            if (kind != null) parts.Add(kind.LabelCap.ToString());
            if (gender.HasValue) parts.Add(gender.Value.GetLabel(animal: true));
            if (lifeStage != null) parts.Add(lifeStage.LabelCap.ToString());
            if (pregnant.HasValue) parts.Add(pregnant.Value ? "pregnant" : "not pregnant");
            if (minHealthFraction.HasValue)
            {
                parts.Add($"health {Mathf.RoundToInt(minHealthFraction.Value * 100f)}%+");
            }
            if (minGestationProgress.HasValue)
            {
                parts.Add($"gestation {Mathf.RoundToInt(minGestationProgress.Value * 100f)}%+");
            }
            return parts;
        }

        private static bool IsFraction(float value)
        {
            return !float.IsNaN(value) && value >= 0f && value <= 1f;
        }
    }
}
