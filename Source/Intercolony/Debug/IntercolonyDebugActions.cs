using System;
using LudeonTK;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Dev-mode actions for inspecting and forcing world state (DESIGN.md §67, §95).
    /// Found under Debug actions → category "Intercolony" (default key `/`).
    /// </summary>
    public static class IntercolonyDebugActions
    {
        private const string Category = "Intercolony";

        [DebugAction(Category, "Open debug window", allowedGameStates = AllowedGameStates.Playing, displayPriority = 100)]
        private static void OpenDebugWindow()
        {
            IntercolonyDebugWindow.Toggle();
        }

        [DebugAction(Category, "Dump state", allowedGameStates = AllowedGameStates.Playing, displayPriority = 90)]
        private static void DumpState()
        {
            WithState(state => IntercolonyLog.Message(state.DebugStateSummary()));
        }

        [DebugAction(Category, "Create test record", allowedGameStates = AllowedGameStates.Playing)]
        private static void CreateTestRecord()
        {
            WithState(state => Report($"Created {state.CreateTestRecord()}"));
        }

        [DebugAction(Category, "Advance all test records", allowedGameStates = AllowedGameStates.Playing)]
        private static void AdvanceTestRecords()
        {
            WithState(state =>
            {
                int advanced = 0;
                foreach (IntercolonyTestRecord record in state.TestRecords)
                {
                    if (record.TryAdvance())
                    {
                        advanced++;
                    }
                }

                Report($"Advanced {advanced} of {state.TestRecords.Count} record(s).");
            });
        }

        [DebugAction(Category, "Advance refresh", allowedGameStates = AllowedGameStates.Playing)]
        private static void AdvanceRefresh()
        {
            WithState(state =>
            {
                state.ForceRefreshNow();
                Report($"Refresh #{state.RefreshCount} forced at tick {state.LastRefreshTick}.");
            });
        }

        [DebugAction(Category, "Clear test state", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearTestState()
        {
            WithState(state =>
            {
                state.ClearTestState();
                Report("Test state cleared.");
            });
        }

        [DebugAction(Category, "Set test values (7 / \"Intercolony\")", allowedGameStates = AllowedGameStates.Playing)]
        private static void SetTestValues()
        {
            WithState(state =>
            {
                state.testCounter = 7;
                state.testString = "Intercolony";
                Report("Test values set. Save, quit to menu, reload, then Dump state.");
            });
        }

        [DebugAction(Category, "Test counter +1", allowedGameStates = AllowedGameStates.Playing)]
        private static void IncrementTestCounter()
        {
            WithState(state => Report($"testCounter = {++state.testCounter}"));
        }

        [DebugAction(Category, "Allocate ID", allowedGameStates = AllowedGameStates.Playing)]
        private static void AllocateId()
        {
            WithState(state => Report($"Allocated ID {state.NextId()}"));
        }

        /// <summary>
        /// Runs <paramref name="action"/> against the live state owner, or warns if there
        /// isn't one. Debug actions are reachable in states where no world exists.
        /// </summary>
        private static void WithState(Action<IntercolonyWorldComponent> action)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                IntercolonyLog.Warning("No world loaded; state owner unavailable.");
                return;
            }

            action(state);
        }

        private static void Report(string text)
        {
            IntercolonyLog.Message(text);
            Messages.Message("[Intercolony] " + text, MessageTypeDefOf.SilentInput, historical: false);
        }
    }
}
