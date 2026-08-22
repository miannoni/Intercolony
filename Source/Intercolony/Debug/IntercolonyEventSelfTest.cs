using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Self-test for the persisted economic-event model and its definition factory (Stages 3A/3C).
    ///
    /// Nothing generates or advances these records yet. The definition assertions therefore stop at
    /// construction: adding a synthetic event to world state would smuggle the next slice's lifecycle
    /// responsibility into a table/factory test and could leave player state changed after a failure.
    /// </summary>
    public static class IntercolonyEventSelfTest
    {
        private sealed class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;
            public int skipped = 0;

            public void Check(bool condition, string label, string detail = null)
            {
                if (condition)
                {
                    passed++;
                    sb.AppendLine($"  PASS  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
            }
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine("Economic event persistence and definition self-test (Stages 3A/3C)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            // Contents, not count. Load pruning can remove or replace arbitrary entries, so
            // restoring by length could leave synthetic events in place of the player's real ones —
            // the same Stage 0.3 defect that left synthetic timeline records behind.
            List<EconomicEvent> saved = new List<EconomicEvent>(state.EconomicEvents);

            try
            {
                List<EconomicEvent> roundTripped = RoundTrip(out string roundTripFailure);
                EconomicEvent sentinel = roundTripped != null && roundTripped.Count == 2
                    ? roundTripped[0]
                    : null;
                EconomicEvent realZero = roundTripped != null && roundTripped.Count == 2
                    ? roundTripped[1]
                    : null;

                r.Check(
                    roundTripFailure == null &&
                    sentinel != null &&
                    sentinel.id == 731 &&
                    sentinel.type == EconomicEventType.AnimalDisease &&
                    sentinel.startTick == 1200 &&
                    sentinel.endTick == 1800 &&
                    sentinel.anchorSettlementId == EconomicEvent.NoSettlement &&
                    sentinel.radiusTiles == EconomicEvent.NoRadius &&
                    sentinel.factionLoadId == EconomicEvent.NoFaction &&
                    ArraysEqual(sentinel.demandModifier, new[] { 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f }) &&
                    ArraysEqual(sentinel.supplyScarcityModifier, new[] { 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f }),
                    "a Scribe round trip preserves every economic-event field",
                    roundTripFailure);

                float[] shortSaved = EconomicEvent.FromSaved(new List<float> { 1.4f });
                float[] missingSaved = EconomicEvent.FromSaved(null);
                r.Check(
                    shortSaved.Length == IntercolonyProductCategoryUtility.Count &&
                    Mathf.Approximately(shortSaved[0], 1.4f) &&
                    AllNeutralFrom(shortSaved, 1) &&
                    AllNeutralFrom(missingSaved, 0),
                    "short and missing modifier lists load padded with neutral 1.0");

                EconomicEvent boundary = new EconomicEvent { startTick = 400, endTick = 500 };
                r.Check(
                    boundary.IsActiveAt(boundary.startTick) && !boundary.IsActiveAt(boundary.endTick),
                    "event activity is half-open at its end tick");

                r.Check(
                    sentinel != null && realZero != null &&
                    sentinel.anchorSettlementId == EconomicEvent.NoSettlement &&
                    sentinel.radiusTiles == EconomicEvent.NoRadius &&
                    sentinel.factionLoadId == EconomicEvent.NoFaction &&
                    realZero.anchorSettlementId == 0 &&
                    realZero.anchorSettlementId != EconomicEvent.NoSettlement,
                    "sentinels survive and settlement id zero remains real");

                int now = GenTicks.TicksGame;
                EconomicEvent ended = new EconomicEvent { id = 901, endTick = now };
                EconomicEvent live = new EconomicEvent
                {
                    id = 902,
                    startTick = now,
                    endTick = now + 1
                };
                state.EconomicEvents.Clear();
                state.EconomicEvents.Add(null);
                state.EconomicEvents.Add(ended);
                state.EconomicEvents.Add(live);
                IntercolonyWorldComponent.PruneLoadedEconomicEvents(state.EconomicEvents, now);
                r.Check(
                    state.EconomicEvents.Count == 1 && state.EconomicEvents[0] == live,
                    "load pruning drops null and ended events but keeps a live event");

                CheckDefinitions(r, state);
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(saved);
                r.sb.AppendLine($"        economic events restored to {state.EconomicEvents.Count}.");
            }

            return Summarize(r);
        }

        private static void CheckDefinitions(Results r, IntercolonyWorldComponent state)
        {
            EconomicEventType[] types = EconomicEventDefinitions.DefinedTypes;
            for (int i = 0; i < types.Length; i++)
            {
                EconomicEventType type = types[i];
                EconomicEventDefinitions.Definition definition = EconomicEventDefinitions.Get(type);
                r.Check(
                    AnyNonNeutral(definition.demandModifier) ||
                    AnyNonNeutral(definition.supplyScarcityModifier),
                    $"{type} definition has at least one non-neutral modifier");

                CheckNoChainPair(
                    r,
                    type,
                    "demand",
                    definition.demandModifier,
                    MarketPressureService.DemandLinks);
                CheckNoChainPair(
                    r,
                    type,
                    "supply",
                    definition.supplyScarcityModifier,
                    MarketPressureService.SupplyLinks);
            }

            EconomicEventDefinitions.Definition drought =
                EconomicEventDefinitions.Get(EconomicEventType.Drought);
            r.Check(
                drought.supplyScarcityModifier[(int)IntercolonyProductCategory.Commodities] >
                EconomicEvent.Neutral,
                "drought raises commodity scarcity rather than encoding supply down as a glut");

            // War scope needs a real faction load ID. A fabricated faction would make this assertion
            // prove the fixture rather than the shipped factory, the same self-test trap previously
            // seen when contract tests hand-built the object whose invariants they claimed to test.
            Settlement anchor = null;
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                anchor = settlements.Find(candidate => candidate?.Faction != null);
            }

            if (anchor == null)
            {
                r.skipped++;
                r.sb.AppendLine("  SKIP  factory assertions require a settlement with a faction");
                return;
            }

            const int startTick = 12_345_678;
            EconomicEvent droughtEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.Drought, anchor, startTick);
            EconomicEvent warEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.WarMobilization, anchor, startTick);
            EconomicEvent constructionEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.ConstructionBoom, anchor, startTick);
            EconomicEvent epidemicEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.Epidemic, anchor, startTick);

            r.Check(
                droughtEvent.anchorSettlementId == anchor.ID &&
                droughtEvent.radiusTiles > 0f &&
                droughtEvent.factionLoadId == EconomicEvent.NoFaction,
                "drought is radial and leaves faction scope at its sentinel");
            r.Check(
                warEvent.anchorSettlementId == EconomicEvent.NoSettlement &&
                warEvent.radiusTiles == EconomicEvent.NoRadius &&
                warEvent.factionLoadId == anchor.Faction.loadID,
                "war mobilization is faction-wide and leaves radial scope at its sentinels");
            r.Check(
                constructionEvent.anchorSettlementId == anchor.ID &&
                constructionEvent.radiusTiles == 0f &&
                constructionEvent.factionLoadId == EconomicEvent.NoFaction,
                "construction boom is single-settlement and leaves faction scope at its sentinel");
            r.Check(
                epidemicEvent.anchorSettlementId == anchor.ID &&
                epidemicEvent.radiusTiles == 0f &&
                epidemicEvent.factionLoadId == EconomicEvent.NoFaction,
                "epidemic is single-settlement and leaves faction scope at its sentinel");

            EconomicEvent[] first =
                { droughtEvent, warEvent, constructionEvent, epidemicEvent };
            for (int i = 0; i < types.Length; i++)
            {
                EconomicEvent again = EconomicEventDefinitions.Build(
                    state, types[i], anchor, startTick);
                r.Check(
                    first[i].endTick == again.endTick &&
                    ArraysEqual(first[i].demandModifier, again.demandModifier) &&
                    ArraysEqual(first[i].supplyScarcityModifier, again.supplyScarcityModifier),
                    $"{types[i]} factory duration and modifiers are deterministic");
                r.Check(
                    first[i].endTick - first[i].startTick > 0 &&
                    first[i].endTick > first[i].startTick,
                    $"{types[i]} duration is positive and ends after it starts");
            }
        }

        private static void CheckNoChainPair(
            Results r,
            EconomicEventType type,
            string tableLabel,
            float[] modifiers,
            MarketPressureService.EconomicChainLink[] links)
        {
            bool clean = true;
            string detail = null;
            for (int i = 0; i < links.Length; i++)
            {
                MarketPressureService.EconomicChainLink link = links[i];
                if (!Mathf.Approximately(modifiers[(int)link.source], EconomicEvent.Neutral) &&
                    !Mathf.Approximately(modifiers[(int)link.target], EconomicEvent.Neutral))
                {
                    clean = false;
                    detail = $"{link.source} -> {link.target}";
                    break;
                }
            }

            r.Check(
                clean,
                $"{type} does not double-count a {tableLabel}-chain link",
                detail);
        }

        private static bool AnyNonNeutral(float[] values)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!Mathf.Approximately(values[i], EconomicEvent.Neutral))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<EconomicEvent> RoundTrip(out string failure)
        {
            EconomicEvent sentinel = new EconomicEvent
            {
                id = 731,
                type = EconomicEventType.AnimalDisease,
                startTick = 1200,
                endTick = 1800,
                anchorSettlementId = EconomicEvent.NoSettlement,
                radiusTiles = EconomicEvent.NoRadius,
                factionLoadId = EconomicEvent.NoFaction,
                demandModifier = new[] { 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f },
                supplyScarcityModifier = new[] { 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f }
            };
            EconomicEvent realZero = new EconomicEvent
            {
                id = 732,
                anchorSettlementId = 0,
                radiusTiles = 0f,
                factionLoadId = 0,
                startTick = 1800,
                endTick = 2400
            };
            List<EconomicEvent> savedList = new List<EconomicEvent> { sentinel, realZero };
            List<EconomicEvent> loadedList = null;
            failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-EconomicEvent-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyEconomicEventTest");
                Scribe_Collections.Look(ref savedList, "economicEvents", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "economicEvents", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return loadedList;
        }

        private static bool ArraysEqual(float[] actual, float[] expected)
        {
            if (actual == null || actual.Length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (!Mathf.Approximately(actual[i], expected[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllNeutralFrom(float[] values, int startIndex)
        {
            if (values == null || values.Length != IntercolonyProductCategoryUtility.Count)
            {
                return false;
            }

            for (int i = startIndex; i < values.Length; i++)
            {
                if (!Mathf.Approximately(values[i], EconomicEvent.Neutral))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed, {r.skipped} skipped.");
            return r.sb.ToString();
        }
    }
}
