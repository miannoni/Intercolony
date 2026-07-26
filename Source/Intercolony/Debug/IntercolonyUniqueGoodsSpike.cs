using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Phase 7 technical spike (DESIGN.md §100): prove that individual objects — a masterwork
    /// chair, a sculpture with art metadata, a stove — can be represented, transferred and
    /// restored without losing what makes them individual.
    ///
    /// Findings are written up in <c>docs/unique-goods-spike.md</c>, which is the phase's
    /// actual deliverable. This class is the evidence behind it.
    ///
    /// Case 4 (save/load before completion) cannot run in one pass, so it is split:
    /// <see cref="PlantSaveLoadProbes"/> spawns tagged objects, and
    /// <see cref="VerifySaveLoadProbes"/> checks them after a reload.
    /// </summary>
    public static class IntercolonyUniqueGoodsSpike
    {
        /// <summary>Art title stamped on the probe sculpture so it can be recognised after a reload.</summary>
        private const string ProbeArtTitle = "Intercolony spike probe";

        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;

            void Check(string name, bool ok, string detail = null)
            {
                if (ok)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {name}{(detail == null ? "" : " — " + detail)}");
                }
            }

            sb.AppendLine("Unique goods spike (DESIGN.md §100)");

            // --- Case 1: sell one Masterwork chair ---
            sb.AppendLine("  [1] Masterwork chair");
            Thing chair = MakeQualityThing(ThingDefOf.DiningChair, ThingDefOf.WoodLog, QualityCategory.Masterwork);
            if (chair == null)
            {
                sb.AppendLine("      SKIPPED (DiningChair unavailable)");
            }
            else
            {
                Check("chair can be minified", chair.def.Minifiable, chair.def.defName);
                Thing minified = chair.TryMakeMinified();
                Check("minifying yields a MinifiedThing", minified is MinifiedThing,
                    minified?.GetType().Name);

                Thing inner = minified.GetInnerIfMinified();
                Check("inner thing is the original chair", inner == chair);
                Check("quality survives minification",
                    inner.TryGetQuality(out QualityCategory q) && q == QualityCategory.Masterwork,
                    inner.TryGetQuality(out QualityCategory q2) ? q2.ToString() : "none");
                Check("material survives minification", inner.Stuff == ThingDefOf.WoodLog,
                    inner.Stuff?.defName ?? "null");

                // The order matcher must see through the crate, or a chair order could never
                // be satisfied by a caravan that is physically carrying one.
                OrderLine line = new OrderLine(ThingDefOf.DiningChair, 1)
                {
                    minQuality = QualityCategory.Masterwork
                };
                Check("matcher accepts the minified chair",
                    OrderValidator.Matches(line, minified, out _));
                Check("minified chair counts as one unit",
                    OrderValidator.CountableUnits(minified) == 1,
                    OrderValidator.CountableUnits(minified).ToString());

                OrderLine tooPicky = new OrderLine(ThingDefOf.DiningChair, 1)
                {
                    minQuality = QualityCategory.Legendary
                };
                Check("matcher rejects below-threshold quality through the crate",
                    !OrderValidator.Matches(tooPicky, minified, out MatchFailure why) &&
                    why == MatchFailure.BelowMinimumQuality, why.ToString());

                minified.Destroy(DestroyMode.Vanish);
            }

            // --- Case 2: sculpture with art metadata ---
            sb.AppendLine("  [2] Sculpture with art metadata");
            ThingDef sculptureDef = DefDatabase<ThingDef>.GetNamedSilentFail("SculptureSmall");
            Thing sculpture = MakeQualityThing(sculptureDef, ThingDefOf.WoodLog, QualityCategory.Excellent);
            if (sculpture == null)
            {
                sb.AppendLine("      SKIPPED (SculptureSmall unavailable)");
            }
            else
            {
                CompArt art = sculpture.TryGetComp<CompArt>();
                Check("sculpture carries CompArt", art != null);
                if (art != null)
                {
                    art.InitializeArt(ArtGenerationContext.Outsider);
                    string titleBefore = art.Title;
                    string authorBefore = art.AuthorName;
                    Check("art has a title", !string.IsNullOrEmpty(titleBefore));

                    Thing minifiedArt = sculpture.TryMakeMinified();
                    CompArt afterArt = minifiedArt.GetInnerIfMinified()?.TryGetComp<CompArt>();
                    Check("art comp survives minification", afterArt != null);
                    Check("art title unchanged by minification",
                        afterArt != null && afterArt.Title == titleBefore,
                        $"{afterArt?.Title} vs {titleBefore}");
                    Check("art author unchanged by minification",
                        afterArt != null && afterArt.AuthorName == authorBefore);

                    minifiedArt.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    sculpture.Destroy(DestroyMode.Vanish);
                }
            }

            // --- Case 3 and 5: a stove can be crated and re-installed ---
            sb.AppendLine("  [3/5] Stove: crate and install path");
            ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("ElectricStove")
                                ?? DefDatabase<ThingDef>.GetNamedSilentFail("FueledStove");
            if (stoveDef == null)
            {
                sb.AppendLine("      SKIPPED (no stove def found)");
            }
            else
            {
                Check("stove is minifiable", stoveDef.Minifiable, stoveDef.defName);
                Check("stove has a minified def", stoveDef.minifiedDef != null);

                Thing stove = ThingMaker.MakeThing(stoveDef, stoveDef.MadeFromStuff ? ThingDefOf.Steel : null);
                Thing crated = stove.TryMakeMinified();
                Check("stove crates into a MinifiedThing", crated is MinifiedThing);

                // Installation needs no custom code: a MinifiedThing placed on the map is
                // installed through vanilla's own blueprint flow. That is the finding.
                Check("crated stove exposes its inner building",
                    crated.GetInnerIfMinified()?.def == stoveDef);
                crated.Destroy(DestroyMode.Vanish);
            }

            // --- Case 6: quality, material and hit points all survive a round trip ---
            sb.AppendLine("  [6] Quality / material / HP preservation");
            Thing hpProbe = MakeQualityThing(ThingDefOf.DiningChair, ThingDefOf.Steel, QualityCategory.Good);
            if (hpProbe == null)
            {
                sb.AppendLine("      SKIPPED");
            }
            else
            {
                hpProbe.HitPoints = Mathf.Max(1, hpProbe.MaxHitPoints / 2);
                int hpBefore = hpProbe.HitPoints;

                Thing crated = hpProbe.TryMakeMinified();
                Thing inner = crated.GetInnerIfMinified();

                Check("hit points survive minification", inner.HitPoints == hpBefore,
                    $"{inner.HitPoints} vs {hpBefore}");
                Check("damaged item fails a condition constraint",
                    !OrderValidator.Matches(
                        new OrderLine(ThingDefOf.DiningChair, 1) { minHitPointsPercent = 0.9f },
                        crated, out MatchFailure hpWhy) && hpWhy == MatchFailure.TooDamaged,
                    hpWhy.ToString());
                Check("damaged item passes a lenient condition constraint",
                    OrderValidator.Matches(
                        new OrderLine(ThingDefOf.DiningChair, 1) { minHitPointsPercent = 0.2f },
                        crated, out _));
                Check("material constraint is enforced through the crate",
                    !OrderValidator.Matches(
                        new OrderLine(ThingDefOf.DiningChair, 1) { allowedStuff = ThingDefOf.WoodLog },
                        crated, out MatchFailure stuffWhy) && stuffWhy == MatchFailure.WrongStuff,
                    stuffWhy.ToString());

                crated.Destroy(DestroyMode.Vanish);
            }

            // --- Case 7: a modded minifiable building ---
            sb.AppendLine("  [7] Modded minifiable building");
            ThingDef modded = FindModdedMinifiable();
            if (modded == null)
            {
                sb.AppendLine("      SKIPPED (no non-core minifiable building loaded — " +
                              "this case is UNPROVEN, see the spike note)");
            }
            else
            {
                Thing moddedThing = ThingMaker.MakeThing(modded, modded.MadeFromStuff ? ThingDefOf.Steel : null);
                Thing crated = moddedThing.TryMakeMinified();
                Check($"modded {modded.defName} crates cleanly", crated is MinifiedThing);
                Check($"modded {modded.defName} unwraps to itself",
                    crated.GetInnerIfMinified()?.def == modded);
                sb.AppendLine($"      tested against {modded.defName} " +
                              $"({modded.modContentPack?.Name ?? "unknown mod"})");
                crated.Destroy(DestroyMode.Vanish);
            }

            sb.AppendLine($"  {passed} passed, {failed} failed.");
            return sb.ToString();
        }

        /// <summary>Case 4, part one: leave objects in the world for a save/load round trip.</summary>
        public static string PlantSaveLoadProbes(Map map)
        {
            if (map == null)
            {
                return "No map.";
            }

            StringBuilder sb = new StringBuilder();
            IntVec3 cell = DropCellFinder.TradeDropSpot(map);

            Thing chair = MakeQualityThing(ThingDefOf.DiningChair, ThingDefOf.WoodLog, QualityCategory.Masterwork);
            if (chair != null)
            {
                chair.HitPoints = Mathf.Max(1, chair.MaxHitPoints / 2);
                GenPlace.TryPlaceThing(chair.TryMakeMinified(), cell, map, ThingPlaceMode.Near);
                sb.AppendLine("Planted: masterwork wooden chair, crated, at half HP.");
            }

            ThingDef sculptureDef = DefDatabase<ThingDef>.GetNamedSilentFail("SculptureSmall");
            Thing sculpture = MakeQualityThing(sculptureDef, ThingDefOf.WoodLog, QualityCategory.Excellent);
            if (sculpture != null)
            {
                CompArt art = sculpture.TryGetComp<CompArt>();
                if (art != null)
                {
                    art.InitializeArt(ArtGenerationContext.Outsider);
                    art.Title = ProbeArtTitle;
                }

                GenPlace.TryPlaceThing(sculpture.TryMakeMinified(), cell, map, ThingPlaceMode.Near);
                sb.AppendLine($"Planted: excellent sculpture titled \"{ProbeArtTitle}\", crated.");
            }

            sb.AppendLine("Now save, quit to menu, reload, and run \"Verify unique goods probes\".");
            return sb.ToString();
        }

        /// <summary>Case 4, part two: confirm the planted objects came back intact.</summary>
        public static string VerifySaveLoadProbes(Map map)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Unique goods save/load verification");

            if (map == null)
            {
                sb.AppendLine("  No map.");
                return sb.ToString();
            }

            int chairs = 0;
            int sculptures = 0;
            int intactArt = 0;
            int intactQuality = 0;
            int intactHp = 0;

            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (!(thing is MinifiedThing minified))
                {
                    continue;
                }

                Thing inner = minified.InnerThing;
                if (inner == null)
                {
                    sb.AppendLine("  FAIL: a crate came back empty.");
                    continue;
                }

                if (inner.def == ThingDefOf.DiningChair)
                {
                    chairs++;
                    if (inner.TryGetQuality(out QualityCategory q) && q == QualityCategory.Masterwork)
                    {
                        intactQuality++;
                    }

                    if (inner.HitPoints < inner.MaxHitPoints)
                    {
                        intactHp++;
                    }
                }

                CompArt art = inner.TryGetComp<CompArt>();
                if (art != null && art.Title == ProbeArtTitle)
                {
                    sculptures++;
                    intactArt++;
                }
            }

            sb.AppendLine($"  crated chairs found       : {chairs}");
            sb.AppendLine($"  masterwork quality intact : {intactQuality}");
            sb.AppendLine($"  reduced hit points intact : {intactHp}");
            sb.AppendLine($"  probe sculptures found    : {sculptures}");
            sb.AppendLine($"  art title intact          : {intactArt}");

            bool pass = chairs > 0 && intactQuality == chairs && intactHp == chairs &&
                        sculptures > 0 && intactArt == sculptures;
            sb.AppendLine(pass
                ? "  PASS: unique objects survived save/load with metadata intact."
                : "  FAIL: something was lost — see counts above.");
            return sb.ToString();
        }

        private static Thing MakeQualityThing(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            if (def == null)
            {
                return null;
            }

            Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
            thing.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
            return thing;
        }

        /// <summary>
        /// A minifiable building from something other than Core or an official DLC, for §100
        /// case 7. Returns null when the current mod list has none.
        /// </summary>
        private static ThingDef FindModdedMinifiable()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.Minifiable || def.category != ThingCategory.Building)
                {
                    continue;
                }

                ModContentPack pack = def.modContentPack;
                if (pack != null && !pack.IsCoreMod && !pack.IsOfficialMod)
                {
                    return def;
                }
            }

            return null;
        }
    }
}
