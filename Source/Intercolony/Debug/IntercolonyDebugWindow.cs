using LudeonTK;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Live view of the world state with the actions needed to force a known test state
    /// in seconds (DESIGN.md §95). Derives from <see cref="EditWindow"/> so it is draggable,
    /// resizeable, and does not block the camera — it can stay open while you play.
    /// </summary>
    public class IntercolonyDebugWindow : EditWindow
    {
        private const float RowHeight = 24f;
        private const float ButtonHeight = 28f;
        private const float Gap = 4f;

        private Vector2 scrollPosition;

        public IntercolonyDebugWindow()
        {
            optionalTitle = "Intercolony";
        }

        public override Vector2 InitialSize => new Vector2(520f, 460f);

        /// <summary>Toggles the window, matching how vanilla dev windows behave.</summary>
        public static void Toggle()
        {
            if (!Find.WindowStack.TryRemove(typeof(IntercolonyDebugWindow)))
            {
                Find.WindowStack.Add(new IntercolonyDebugWindow());
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                DevGUI.Label(new Rect(0f, 0f, inRect.width, RowHeight), "No world loaded.");
                return;
            }

            Text.Font = GameFont.Small;
            float y = 0f;

            // Row 1: state-shaping actions.
            float x = 0f;
            DoRowButton(ref x, y, "Dump state", "Write the full state to the debug log.",
                () => IntercolonyLog.Message(state.DebugStateSummary()));
            DoRowButton(ref x, y, "New record", "Create a persisted test record with a fresh ID.",
                () => IntercolonyLog.Message($"Created {state.CreateTestRecord()}"));
            DoRowButton(ref x, y, "Advance refresh", "Run the refresh now instead of waiting for the schedule.",
                state.ForceRefreshNow);
            DoRowButton(ref x, y, "Clear test state", "Reset every test field. Does not rewind the ID counter.",
                state.ClearTestState);
            y += ButtonHeight + Gap;

            // Row 2: the Phase 1 probe values, kept reachable from here too.
            x = 0f;
            DoRowButton(ref x, y, "Set 7 / \"Intercolony\"", "Set the Phase 1 probe values (DESIGN.md §94).",
                () =>
                {
                    state.testCounter = 7;
                    state.testString = "Intercolony";
                });
            DoRowButton(ref x, y, "Counter +1", "Increment the test counter.", () => state.testCounter++);
            DoRowButton(ref x, y, "Advance all records", "Step every record through its state machine.",
                () =>
                {
                    foreach (IntercolonyTestRecord record in state.TestRecords)
                    {
                        record.TryAdvance();
                    }
                });
            y += ButtonHeight + Gap * 2f;

            // The state dump itself, scrolled so a long record list stays usable.
            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y);
            string summary = state.DebugStateSummary();
            float viewHeight = Mathf.Max(Text.CalcHeight(summary, inRect.width - 20f), outRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, viewHeight);

            DevGUI.BeginScrollView(outRect, ref scrollPosition, viewRect);
            DevGUI.Label(viewRect, summary);
            DevGUI.EndScrollView();
        }
    }
}
