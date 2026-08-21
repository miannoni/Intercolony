using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Runs every self-test in one go and reports one verdict.
    ///
    /// **Why this exists.** Seventeen suites behind seventeen menu entries means the honest answer
    /// to "is the mod still sound?" costs seventeen clicks, so in practice it does not get asked —
    /// which is how a project ends up with six suites changed and none of them executed.
    ///
    /// It also checks something no individual suite can. Suites deliberately drive real
    /// transitions on synthetic orders, which since Stage 0.3b writes commercial events and will
    /// soon write market pressure. Each is wrapped in a guard that restores what it found, but a
    /// broken guard is invisible from inside the suite that tripped it. This records the world's
    /// counts before and after and says plainly whether anything leaked.
    /// </summary>
    public static class IntercolonyAllSelfTests
    {
        /// <summary>
        /// One suite's outcome. Public because the dev test bridge reports these counts as data
        /// rather than re-parsing the rendered table — a second parser over this class's own
        /// output would be exactly the drift the single-registry rule exists to prevent.
        /// </summary>
        public sealed class SuiteResult
        {
            /// <summary>Stable machine id, for callers that select a suite by name.</summary>
            public string id;

            /// <summary>Display name. This is what the table prints, so it must not change.</summary>
            public string name;

            public int passed;
            public int failed;
            public int skipped;
            public bool crashed;
            public string skipReason;
            public string output = "";
            public long durationMs;

            public bool Ran => !crashed && skipReason == null;
            public bool Clean => Ran && failed == 0;
        }

        /// <summary>
        /// One entry in the registry: what a suite is called by machine and by eye, whether it
        /// needs a map, and how to invoke it.
        ///
        /// The two names are deliberately separate. The table has printed "job posting" since this
        /// runner was written and changing it would change the output everyone reads, while a
        /// caller selecting a suite over a wire wants "job-posting" — stable, kebab-case, and
        /// independent of display text that may be reworded later.
        /// </summary>
        public sealed class SelfTestDefinition
        {
            public readonly string Id;
            public readonly string Label;
            public readonly bool RequiresMap;
            internal readonly Func<IntercolonyWorldComponent, Map, string> Invoke;

            internal SelfTestDefinition(
                string id, string label, bool requiresMap,
                Func<IntercolonyWorldComponent, Map, string> invoke)
            {
                Id = id;
                Label = label;
                RequiresMap = requiresMap;
                Invoke = invoke;
            }
        }

        /// <summary>
        /// The one list. Both the "Run ALL self-tests" debug action and the dev test bridge read
        /// it, so a suite added here appears in both without anyone remembering to update a second
        /// place. Order is the order the table prints: world-only suites first, then the ones that
        /// need a map.
        /// </summary>
        private static readonly SelfTestDefinition[] Definitions =
        {
            // World-only suites.
            new SelfTestDefinition("economy", "economy", false,
                (s, m) => IntercolonyEconomySelfTest.Run(s)),
            new SelfTestDefinition("timeline", "timeline", false,
                (s, m) => IntercolonyTimelineSelfTest.Run(s)),
            new SelfTestDefinition("profile", "profile", false,
                (s, m) => IntercolonyProfileSelfTest.Run()),
            new SelfTestDefinition("market", "market", false,
                (s, m) => IntercolonyMarketSelfTest.Run(s)),
            new SelfTestDefinition("reputation", "reputation", false,
                (s, m) => IntercolonyReputationSelfTest.Run(s)),
            new SelfTestDefinition("contract", "contract", false,
                (s, m) => IntercolonyContractSelfTest.Run(s)),
            new SelfTestDefinition("rfq", "rfq", false,
                (s, m) => IntercolonyRfqSelfTest.Run(s)),

            // Suites that need a map. Skipped loudly rather than quietly when there is none —
            // a suite that did not run is not a suite that passed.
            new SelfTestDefinition("order", "order", true,
                (s, m) => IntercolonyOrderSelfTest.Run(s, m)),
            new SelfTestDefinition("animal", "animal", true,
                (s, m) => IntercolonyAnimalSelfTest.Run(s, m)),
            new SelfTestDefinition("ledger", "ledger", true,
                (s, m) => IntercolonyLedgerSelfTest.Run(s, m)),
            new SelfTestDefinition("labor", "labor", true,
                (s, m) => IntercolonyLaborSelfTest.Run(s, m)),
            new SelfTestDefinition("payroll", "payroll", true,
                (s, m) => IntercolonyPayrollSelfTest.Run(s, m)),
            new SelfTestDefinition("transition", "transition", true,
                (s, m) => IntercolonyTransitionSelfTest.Run(s, m)),
            new SelfTestDefinition("job-posting", "job posting", true,
                (s, m) => IntercolonyJobPostingSelfTest.Run(s, m)),
            new SelfTestDefinition("combat-clause", "combat clause", true,
                (s, m) => IntercolonyCombatClauseSelfTest.Run(s, m)),
            new SelfTestDefinition("employer-reputation", "employer reputation", true,
                (s, m) => IntercolonyEmployerReputationSelfTest.Run(s, m)),
            new SelfTestDefinition("long-term", "long term", true,
                (s, m) => IntercolonyLongTermSelfTest.Run(s, m))
        };

        /// <summary>Every suite this runner knows about, in the order it runs them.</summary>
        public static IReadOnlyList<SelfTestDefinition> List()
        {
            return Definitions;
        }

        /// <summary>
        /// Runs exactly one suite, through the same guard the whole-suite run uses.
        ///
        /// The guard is not optional here. <see cref="IntercolonyDiagnosticGuard"/> is what stops a
        /// suite's synthetic orders and its deliberate missed payrolls from being written into the
        /// player's real history and employer standing. A caller that runs one suite repeatedly —
        /// which is the entire point of automating this — would otherwise do that damage on every
        /// invocation.
        ///
        /// Returns null for an unknown id so the caller can say so precisely rather than reporting
        /// a suite that failed.
        /// </summary>
        public static SuiteResult RunOne(string id, IntercolonyWorldComponent state, Map map)
        {
            SelfTestDefinition definition = null;
            foreach (SelfTestDefinition candidate in Definitions)
            {
                if (string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    break;
                }
            }

            if (definition == null)
            {
                return null;
            }

            return RunDefinition(state, definition, map);
        }

        /// <summary>
        /// Matches the summary every suite ends with. They agree on
        /// "N passed, M failed" and some add ", K skipped"; nothing else in the output looks
        /// like that, so one pattern reads all seventeen.
        ///
        /// **Case-insensitive, and that is not cosmetic.** The animal suite writes its skip count
        /// as "8 SKIPPED — not a clean run", so a case-sensitive pattern matched the first two
        /// groups, missed the third, and reported zero skips for a suite that was shouting about
        /// them. An aggregator that hides skips is worse than no aggregator: §20.1 exists because
        /// a skipped assertion is not proof, and this quietly turned eight of them into proof.
        /// </summary>
        private static readonly Regex SummaryPattern = new Regex(
            @"(\d+)\s+passed,\s*(\d+)\s+failed(?:,\s*(\d+)\s+skipped)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Intercolony: all self-tests ===");

            if (state == null)
            {
                sb.AppendLine("No world state. Load a game first.");
                return sb.ToString();
            }

            int timelineBefore = state.CommercialTimeline.Count;
            int pressureBefore = state.MarketStates.Count;
            int nextIdBefore = state.PeekNextId();

            List<SuiteResult> results = new List<SuiteResult>();
            foreach (SelfTestDefinition definition in Definitions)
            {
                results.Add(RunDefinition(state, definition, map));
            }

            AppendTable(sb, results);
            AppendLeakCheck(sb, state, timelineBefore, pressureBefore, nextIdBefore);
            AppendVerdict(sb, results);
            AppendFailureDetail(sb, results);

            return sb.ToString();
        }

        /// <summary>
        /// Each suite runs inside its own guard, matching what its individual debug action does.
        /// Calling the suites directly would otherwise bypass the guards entirely and this action
        /// would be the one thing in the mod that reliably corrupts the player's history.
        ///
        /// Per suite rather than once around the batch, so a suite that crashes mid-run still has
        /// its mess cleaned up before the next one starts from a known state.
        /// </summary>
        private static SuiteResult RunDefinition(
            IntercolonyWorldComponent state, SelfTestDefinition definition, Map map)
        {
            if (definition.RequiresMap && map == null)
            {
                return new SuiteResult
                {
                    id = definition.Id,
                    name = definition.Label,
                    skipReason = "no current map"
                };
            }

            SuiteResult result = new SuiteResult { id = definition.Id, name = definition.Label };
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                using (new IntercolonyDiagnosticGuard(state))
                {
                    result.output = definition.Invoke(state, map) ?? "";
                }

                ParseCounts(result);
            }
            catch (Exception ex)
            {
                result.crashed = true;
                result.output = ex.ToString();
            }
            finally
            {
                stopwatch.Stop();
                result.durationMs = stopwatch.ElapsedMilliseconds;
            }

            return result;
        }

        /// <summary>
        /// Reads the suite's own summary rather than counting PASS lines, so a suite that reports
        /// a failure without printing a marker is still counted as failing.
        /// </summary>
        private static void ParseCounts(SuiteResult result)
        {
            MatchCollection matches = SummaryPattern.Matches(result.output);
            if (matches.Count == 0)
            {
                // No summary at all is itself a problem: the suite returned something this runner
                // cannot verify, and treating that as success would be exactly the hidden pass
                // the testing rules forbid.
                result.crashed = true;
                return;
            }

            // Last match: a suite may mention the shape earlier in its own prose.
            Match summary = matches[matches.Count - 1];
            result.passed = int.Parse(summary.Groups[1].Value);
            result.failed = int.Parse(summary.Groups[2].Value);
            result.skipped = summary.Groups[3].Success
                ? int.Parse(summary.Groups[3].Value)
                : 0;
        }

        private static void AppendTable(StringBuilder sb, List<SuiteResult> results)
        {
            sb.AppendLine();
            sb.AppendLine("  suite                  passed  failed  skipped");
            foreach (SuiteResult result in results)
            {
                if (result.skipReason != null)
                {
                    sb.AppendLine($"  {result.name,-20}       -       -        -   SKIPPED ({result.skipReason})");
                }
                else if (result.crashed)
                {
                    sb.AppendLine($"  {result.name,-20}       -       -        -   DID NOT COMPLETE");
                }
                else
                {
                    sb.AppendLine(
                        $"  {result.name,-20} {result.passed,7} {result.failed,7} {result.skipped,8}" +
                        (result.failed > 0 ? "   <-- FAILURES" : ""));
                }
            }
        }

        /// <summary>
        /// The check no individual suite can make: did running them all leave anything behind in
        /// the player's world?
        /// </summary>
        private static void AppendLeakCheck(
            StringBuilder sb,
            IntercolonyWorldComponent state,
            int timelineBefore,
            int pressureBefore,
            int nextIdBefore)
        {
            int timelineAfter = state.CommercialTimeline.Count;
            int pressureAfter = state.MarketStates.Count;

            sb.AppendLine();
            sb.AppendLine("  -- did anything leak into the world? --");
            sb.AppendLine(timelineAfter == timelineBefore
                ? $"  OK    commercial timeline unchanged at {timelineAfter} record(s)"
                : $"  LEAK  commercial timeline went {timelineBefore} -> {timelineAfter}; " +
                  "a self-test guard is not restoring what it found");
            sb.AppendLine(pressureAfter == pressureBefore
                ? $"  OK    market pressure unchanged at {pressureAfter} settlement(s)"
                : $"  LEAK  market pressure went {pressureBefore} -> {pressureAfter}; " +
                  "a self-test guard is not restoring what it found");

            // Entity IDs are consumed by suites that build synthetic records and are never given
            // back. That is expected and harmless - IDs are opaque and monotonic - but it is worth
            // printing so nobody mistakes the movement for a leak.
            int consumed = state.PeekNextId() - nextIdBefore;
            sb.AppendLine($"  note  {consumed} entity id(s) consumed, which is expected and harmless");
        }

        private static void AppendVerdict(StringBuilder sb, List<SuiteResult> results)
        {
            int ran = 0;
            int totalPassed = 0;
            int totalFailed = 0;
            int totalSkippedAssertions = 0;
            List<string> notRun = new List<string>();

            foreach (SuiteResult result in results)
            {
                if (!result.Ran)
                {
                    notRun.Add(result.name + (result.crashed ? " (did not complete)" : " (skipped)"));
                    continue;
                }

                ran++;
                totalPassed += result.passed;
                totalFailed += result.failed;
                totalSkippedAssertions += result.skipped;
            }

            sb.AppendLine();
            sb.AppendLine($"  {ran}/{results.Count} suites ran   " +
                          $"{totalPassed} passed   {totalFailed} failed   " +
                          $"{totalSkippedAssertions} assertions skipped");

            if (notRun.Count > 0)
            {
                sb.AppendLine($"  did not run: {string.Join(", ", notRun.ToArray())}");
            }

            bool everythingRan = notRun.Count == 0;
            if (totalFailed == 0 && everythingRan && totalSkippedAssertions == 0)
            {
                sb.AppendLine("  VERDICT: all clean.");
            }
            else if (totalFailed == 0 && everythingRan)
            {
                sb.AppendLine("  VERDICT: no failures, but assertions were skipped — " +
                              "a skipped assertion is not proof.");
            }
            else if (totalFailed == 0)
            {
                sb.AppendLine("  VERDICT: no failures, but not everything ran. " +
                              "Load a colony with a map and run it again.");
            }
            else
            {
                sb.AppendLine("  VERDICT: FAILURES. Full output for the failing suites is below.");
            }
        }

        /// <summary>
        /// Only failing suites get printed in full. Seventeen complete outputs would bury the
        /// verdict, and the whole point of this action is that the verdict is readable.
        /// </summary>
        private static void AppendFailureDetail(StringBuilder sb, List<SuiteResult> results)
        {
            foreach (SuiteResult result in results)
            {
                if (result.Clean || result.skipReason != null)
                {
                    continue;
                }

                sb.AppendLine();
                sb.AppendLine($"  --- {result.name} ---");
                sb.AppendLine(result.output);
            }
        }
    }
}
