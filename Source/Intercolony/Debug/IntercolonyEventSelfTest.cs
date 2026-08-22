using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Self-test for the persisted economic-event model (the 1.0 program Stage 3A).
    ///
    /// Nothing consumes these records yet. This suite therefore tests only their save contract,
    /// sentinel boundaries, half-open lifetime and load pruning; asserting economic effects here
    /// would quietly wire policy into a slice whose job is persistence alone.
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
            r.sb.AppendLine("Economic event persistence self-test (the 1.0 program Stage 3A)");

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
