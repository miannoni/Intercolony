using Verse;

namespace Intercolony
{
    /// <summary>
    /// Consistently prefixed logging (DESIGN.md §68). Never call from a per-tick path.
    /// </summary>
    public static class IntercolonyLog
    {
        private const string Prefix = "[Intercolony] ";

        /// <summary>
        /// Verbose logging is gated on dev mode until mod settings exist (DESIGN.md §66).
        /// </summary>
        public static bool VerboseEnabled => Prefs.DevMode;

        public static void Message(string text)
        {
            Log.Message(Prefix + text);
        }

        public static void Warning(string text)
        {
            Log.Warning(Prefix + text);
        }

        public static void Error(string text)
        {
            Log.Error(Prefix + text);
        }

        public static void Verbose(string text)
        {
            if (VerboseEnabled)
            {
                Log.Message(Prefix + text);
            }
        }
    }
}
