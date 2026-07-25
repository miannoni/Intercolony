using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
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

        [DebugAction(Category, "Dump settlement profiles", allowedGameStates = AllowedGameStates.Playing, displayPriority = 80)]
        private static void DumpSettlementProfiles()
        {
            WithState(state =>
            {
                List<SettlementEconomicProfile> profiles = state.AllProfiles();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Settlement economic profiles ({profiles.Count} eligible, economy seed {state.EconomySeed})");
                foreach (SettlementEconomicProfile profile in profiles)
                {
                    sb.AppendLine();
                    sb.Append(profile.DebugSummary());
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        /// <summary>
        /// Click a settlement on the world map to print its profile. A ToolWorld action is
        /// only offered while the world map is rendered, and must read the hovered tile itself
        /// (see <c>DebugActionNode.cs:286</c> — the tool is handed a plain Action).
        /// </summary>
        [DebugAction(Category, "Inspect settlement profile", actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld, displayPriority = 70)]
        private static void InspectSettlementProfile()
        {
            WithState(state =>
            {
                PlanetTile tile = GenWorld.MouseTile();
                Settlement settlement = Find.WorldObjects.SettlementAt(tile);
                if (settlement == null)
                {
                    IntercolonyLog.Message($"No settlement at tile {tile}.");
                    return;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    IntercolonyLog.Message(
                        $"{settlement.Label} is not an economic participant " +
                        $"(faction: {settlement.Faction?.Name ?? "none"}).");
                    return;
                }

                IntercolonyLog.Message(profile.DebugSummary());
            });
        }

        [DebugAction(Category, "Run profile self-test", allowedGameStates = AllowedGameStates.Playing, displayPriority = 60)]
        private static void RunProfileSelfTest()
        {
            IntercolonyLog.Message(IntercolonyProfileSelfTest.Run());
        }

        /// <summary>
        /// Destroys a settlement to prove §87 handling. Genuinely destructive — intended for a
        /// throwaway <c>-quicktest</c> world, not a save you care about.
        /// </summary>
        [DebugAction(Category, "Test settlement removal (DESTRUCTIVE)", allowedGameStates = AllowedGameStates.Playing, displayPriority = 50)]
        private static void TestSettlementRemoval()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Settlement removal test (DESIGN.md §87)");

                Settlement victim = null;
                foreach (Settlement settlement in Find.WorldObjects.Settlements)
                {
                    if (SettlementProfileGenerator.IsEligible(settlement))
                    {
                        victim = settlement;
                        break;
                    }
                }

                if (victim == null)
                {
                    IntercolonyLog.Warning("No eligible settlement to remove.");
                    return;
                }

                int id = victim.ID;
                int before = state.AllProfiles().Count;
                bool profileBefore = state.GetProfile(victim) != null;
                bool cachedBefore = state.HasCachedProfile(id);
                sb.AppendLine($"  victim: {victim.Label} (id {id})");
                sb.AppendLine($"  before: {before} eligible, profile={profileBefore}, cached={cachedBefore}");

                Find.WorldObjects.Remove(victim);

                bool eligibleAfter = SettlementProfileGenerator.IsEligible(victim);
                bool profileAfter = state.GetProfile(victim) != null;
                int after = state.AllProfiles().Count;
                sb.AppendLine($"  after removal: {after} eligible, IsEligible={eligibleAfter}, profile={profileAfter}");

                state.PruneProfileCacheNow();
                bool cachedAfterPrune = state.HasCachedProfile(id);
                sb.AppendLine($"  after prune: cached={cachedAfterPrune}");

                bool pass = profileBefore && !eligibleAfter && !profileAfter && after == before - 1 && !cachedAfterPrune;
                sb.AppendLine(pass
                    ? "  PASS: removal handled gracefully, no orphan profile left."
                    : "  FAIL: see values above.");

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Clear profile cache", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearProfileCache()
        {
            WithState(state => state.ClearProfileCache());
        }

        [DebugAction(Category, "Reroll economy seed", allowedGameStates = AllowedGameStates.Playing)]
        private static void RerollEconomySeed()
        {
            WithState(state =>
            {
                state.RerollEconomySeed();
                Report($"Economy seed is now {state.EconomySeed}.");
            });
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
