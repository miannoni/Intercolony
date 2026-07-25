using System.Collections.Generic;
using System.Text;
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

        /// <summary>Which pane the scroll view shows: the raw state dump, or settlement profiles.</summary>
        private bool showProfiles;

        private string cachedText;
        private bool cachedShowProfiles;
        private float cachedAtRealtime;

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
            y += ButtonHeight + Gap;

            // Row 3: settlement profiles (§96 debug inspector).
            x = 0f;
            DoRowButton(ref x, y, showProfiles ? "Show state" : "Show profiles",
                "Switch the pane below between raw state and settlement economic profiles.",
                () =>
                {
                    showProfiles = !showProfiles;
                    scrollPosition = Vector2.zero;
                });
            DoRowButton(ref x, y, "Clear profile cache",
                "Drop cached profiles. Regeneration is deterministic, so nothing should change.",
                state.ClearProfileCache);
            DoRowButton(ref x, y, "Reroll seed",
                "Assign a new economy seed. Every settlement's character changes.",
                state.RerollEconomySeed);
            y += ButtonHeight + Gap * 2f;

            // The chosen pane, scrolled so long content stays usable.
            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y);
            string summary = PaneText(state);
            float viewHeight = Mathf.Max(Text.CalcHeight(summary, inRect.width - 20f), outRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, viewHeight);

            DevGUI.BeginScrollView(outRect, ref scrollPosition, viewRect);
            DevGUI.Label(viewRect, summary);
            DevGUI.EndScrollView();
        }

        /// <summary>
        /// The pane text, rebuilt at most a few times a second.
        ///
        /// This matters: <see cref="SettlementEconomicProfile.DebugSummary"/> builds a
        /// StringBuilder per settlement, GUI code runs several times per frame (layout and
        /// repaint are separate passes), and a large world has dozens of settlements. Building
        /// it unconditionally made the window visibly janky.
        /// </summary>
        private string PaneText(IntercolonyWorldComponent state)
        {
            const float MaxAgeSeconds = 0.25f;

            if (cachedText == null ||
                cachedShowProfiles != showProfiles ||
                Time.realtimeSinceStartup - cachedAtRealtime > MaxAgeSeconds)
            {
                cachedText = showProfiles ? ProfilesText(state) : state.DebugStateSummary();
                cachedShowProfiles = showProfiles;
                cachedAtRealtime = Time.realtimeSinceStartup;
            }

            return cachedText;
        }

        private static string ProfilesText(IntercolonyWorldComponent state)
        {
            List<SettlementEconomicProfile> profiles = state.AllProfiles();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{profiles.Count} eligible settlement(s), economy seed {state.EconomySeed}");
            if (profiles.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("No eligible settlements. Every settlement is either the player's,");
                sb.AppendLine("hidden, temporary, or a permanent enemy.");
                return sb.ToString();
            }

            foreach (SettlementEconomicProfile profile in profiles)
            {
                sb.AppendLine();
                sb.Append(profile.DebugSummary());
            }

            return sb.ToString();
        }
    }
}
