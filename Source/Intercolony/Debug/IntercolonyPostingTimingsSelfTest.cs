using System;
using System.Text;

namespace Intercolony
{
    /// <summary>
    /// Bridge-runnable P0 measurement for the Emergency + Elite posting path. This checks only
    /// that the fixture ran and that the timing session produced its block; applicant count is
    /// evidence about the world, not a pass/fail condition.
    /// </summary>
    public static class IntercolonyPostingTimingsSelfTest
    {
        private sealed class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;

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

            public void Info(string line)
            {
                sb.AppendLine($"        {line}");
            }
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine("Emergency + Elite posting timing self-test (P0)");

            PostingTimings.TimingFixtureResult fixture =
                PostingTimings.RunEmergencyEliteTimingFixture(state);

            if (!string.IsNullOrEmpty(fixture.TimingBlock))
            {
                r.sb.Append(fixture.TimingBlock);
            }
            else
            {
                r.Info("timing block was not produced.");
            }

            r.sb.AppendLine(fixture.ConfigurationLine);
            r.Info(
                $"applicants observed: {fixture.ApplicantCount}/" +
                $"{JobPostingService.MaxWaitingApplicants} (informational; not asserted)");

            r.Check(
                fixture.PostingException == null,
                "TryPost completed without throwing",
                fixture.PostingException == null
                    ? "threw=no"
                    : $"threw={fixture.PostingException.GetType().Name}: " +
                      fixture.PostingException.Message);
            r.Check(
                !string.IsNullOrEmpty(fixture.TimingBlock),
                "timing block was produced (session ran)",
                fixture.TimingBlock == null ? "missing" : "present");

            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
