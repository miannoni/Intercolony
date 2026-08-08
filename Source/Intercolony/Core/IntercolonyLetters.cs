using RimWorld;
using Verse;

namespace Intercolony
{
    public enum IntercolonyLetterImportance
    {
        Always,
        Important,
        Chatty
    }

    /// <summary>Keeps letter-volume decisions explicit at the event that knows the consequence.</summary>
    public static class IntercolonyLetters
    {
        public static void Send(
            IntercolonyLetterImportance importance, string label, string text, LetterDef def)
        {
            if (ShouldShow(importance))
            {
                Find.LetterStack.ReceiveLetter(label, text, def);
                return;
            }

            LogSuppressed(importance, label, text);
        }

        public static void Send(
            IntercolonyLetterImportance importance, string label, string text, LetterDef def,
            LookTargets lookTargets)
        {
            if (ShouldShow(importance))
            {
                Find.LetterStack.ReceiveLetter(label, text, def, lookTargets);
                return;
            }

            LogSuppressed(importance, label, text);
        }

        private static bool ShouldShow(IntercolonyLetterImportance importance)
        {
            IntercolonyLetterVolume volume = IntercolonyMod.Settings.letterVolume;
            if (importance == IntercolonyLetterImportance.Always)
            {
                return true;
            }

            if (importance == IntercolonyLetterImportance.Important)
            {
                return volume == IntercolonyLetterVolume.Everything ||
                       volume == IntercolonyLetterVolume.ImportantOnly;
            }

            return volume == IntercolonyLetterVolume.Everything;
        }

        private static void LogSuppressed(
            IntercolonyLetterImportance importance, string label, string text)
        {
            IntercolonyLog.Message(
                $"Letter kept in log ({importance}, not shown): {label}\n{text}");
        }
    }
}
