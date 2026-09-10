using System;
using System.Collections.Generic;
using UnityEngine;

namespace Intercolony
{
    /// <summary>
    /// Captures one scoped set of self-test diagnostics without forwarding the expected entries
    /// to Player.log. All unexpected errors still reach the previous handler.
    /// </summary>
    internal sealed class ExpectedLogHandler : ILogHandler
    {
        private readonly ILogHandler previous;
        private readonly LogType expectedType;
        private readonly HashSet<string> expectedTexts;
        private readonly List<string> unexpectedErrors = new List<string>();

        public ExpectedLogHandler(
            LogType expectedType,
            ILogHandler previous,
            IEnumerable<string> expectedTexts)
        {
            if (previous == null)
            {
                throw new InvalidOperationException("Unity logger had no handler to wrap");
            }

            if (expectedTexts == null)
            {
                throw new ArgumentNullException(nameof(expectedTexts));
            }

            this.expectedType = expectedType;
            this.previous = previous;
            this.expectedTexts = new HashSet<string>(expectedTexts, StringComparer.Ordinal);
            if (this.expectedTexts.Count == 0)
            {
                throw new ArgumentException("At least one expected log entry is required.",
                    nameof(expectedTexts));
            }
        }

        public ILogHandler Previous => previous;

        public int ExpectedCount { get; private set; }

        public int UnexpectedErrorCount => unexpectedErrors.Count;

        public string FirstUnexpectedError =>
            unexpectedErrors.Count == 0 ? null : unexpectedErrors[0];

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            unexpectedErrors.Add(exception?.ToString() ?? "null exception");
            previous.LogException(exception, context);
        }

        public void LogFormat(
            LogType logType,
            UnityEngine.Object context,
            string format,
            params object[] args)
        {
            string text = Render(format, args);
            if (logType == expectedType && expectedTexts.Contains(text))
            {
                ExpectedCount++;
                return;
            }

            if (logType == LogType.Error ||
                logType == LogType.Exception ||
                logType == LogType.Assert)
            {
                unexpectedErrors.Add(text ?? "null log text");
            }

            previous.LogFormat(logType, context, format, args);
        }

        private static string Render(string format, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (Exception)
            {
                return format;
            }
        }
    }
}
