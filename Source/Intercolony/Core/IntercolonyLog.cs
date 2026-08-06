using System.Text;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Consistently prefixed logging (DESIGN.md §68). Never call from a per-tick path.
    /// </summary>
    public static class IntercolonyLog
    {
        private const string Prefix = "[Intercolony] ";
        private static int verboseSuppressionDepth;

        /// <summary>
        /// Verbose logging is gated on dev mode until mod settings exist (DESIGN.md §66).
        /// </summary>
        public static bool VerboseEnabled => Prefs.DevMode && verboseSuppressionDepth == 0;

        /// <summary>
        /// Repeated benchmarks must not measure or flood the dev log. Suppression is scoped so an
        /// exception cannot leave ordinary diagnostics disabled for the rest of the session.
        /// </summary>
        internal static System.IDisposable SuppressVerbose()
        {
            verboseSuppressionDepth++;
            return new VerboseSuppression();
        }

        /// <summary>
        /// Prefixes every line, not just the first.
        ///
        /// RimWorld writes a multi-line entry as plain consecutive lines in Player.log with
        /// nothing marking the continuations. Any log filter that greps for the tag therefore
        /// keeps the first line of a state dump and silently drops the body — which is exactly
        /// what happened to the first settlement-profile dump. Tagging every line costs a few
        /// characters and makes multi-line dumps survive filtering.
        /// </summary>
        private static string WithPrefix(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Prefix;
            }

            string normalized = text.Replace("\r\n", "\n").TrimEnd('\n');
            if (normalized.IndexOf('\n') < 0)
            {
                return Prefix + normalized;
            }

            string[] lines = normalized.Split('\n');
            StringBuilder sb = new StringBuilder(normalized.Length + lines.Length * Prefix.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(Prefix).Append(lines[i]);
            }

            return sb.ToString();
        }

        public static void Message(string text)
        {
            Log.Message(WithPrefix(text));
        }

        public static void Warning(string text)
        {
            Log.Warning(WithPrefix(text));
        }

        public static void Error(string text)
        {
            Log.Error(WithPrefix(text));
        }

        public static void Verbose(string text)
        {
            if (VerboseEnabled)
            {
                Log.Message(WithPrefix(text));
            }
        }

        private sealed class VerboseSuppression : System.IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                verboseSuppressionDepth = System.Math.Max(0, verboseSuppressionDepth - 1);
            }
        }
    }
}
