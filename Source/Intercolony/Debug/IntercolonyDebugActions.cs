using LudeonTK;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Dev-mode actions for inspecting and poking the world state (DESIGN.md §67).
    /// Found under Debug actions → category "Intercolony".
    /// </summary>
    public static class IntercolonyDebugActions
    {
        private const string Category = "Intercolony";

        [DebugAction(Category, "Print state", allowedGameStates = AllowedGameStates.Playing)]
        private static void PrintState()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                IntercolonyLog.Warning("No world loaded; state owner unavailable.");
                return;
            }

            IntercolonyLog.Message(state.DebugStateSummary());
        }

        [DebugAction(Category, "Set test values (7 / \"Intercolony\")", allowedGameStates = AllowedGameStates.Playing)]
        private static void SetTestValues()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                IntercolonyLog.Warning("No world loaded; state owner unavailable.");
                return;
            }

            state.testCounter = 7;
            state.testString = "Intercolony";
            Report("Test values set. Save, quit to menu, reload, then Print state.");
        }

        [DebugAction(Category, "Test counter +1", allowedGameStates = AllowedGameStates.Playing)]
        private static void IncrementTestCounter()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                IntercolonyLog.Warning("No world loaded; state owner unavailable.");
                return;
            }

            state.testCounter++;
            Report($"testCounter = {state.testCounter}");
        }

        [DebugAction(Category, "Allocate ID", allowedGameStates = AllowedGameStates.Playing)]
        private static void AllocateId()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                IntercolonyLog.Warning("No world loaded; state owner unavailable.");
                return;
            }

            Report($"Allocated ID {state.NextId()}");
        }

        private static void Report(string text)
        {
            IntercolonyLog.Message(text);
            Messages.Message("[Intercolony] " + text, MessageTypeDefOf.SilentInput, historical: false);
        }
    }
}
