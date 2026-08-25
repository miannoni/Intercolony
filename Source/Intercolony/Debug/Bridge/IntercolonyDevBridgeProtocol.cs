#if INTERCOLONY_DEV_BRIDGE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Intercolony
{
    /// <summary>
    /// The wire format: newline-delimited UTF-8 JSON, one request and one response per
    /// connection.
    ///
    /// **Why this hand-rolls JSON.** Only Intercolony.dll ships, and the bridge is not allowed to
    /// put a second assembly in Assemblies\ (CLAUDE.md). DataContractJsonSerializer would avoid
    /// writing a parser, but every command returns a differently shaped result, so it would want a
    /// DTO and a serializer instance per command and would still need help with the loosely typed
    /// args. A small reader and writer used by nothing else is the smaller thing to own.
    ///
    /// The reader is deliberately strict and total: it either returns a value or throws
    /// <see cref="JsonException"/>. It never returns a partially parsed object, because a
    /// half-understood request is exactly the thing that should become a structured error rather
    /// than a command someone half executes.
    /// </summary>
    internal static class IntercolonyDevBridgeProtocol
    {
        /// <summary>
        /// Bumped when the shape of a request or response changes in a way a client could notice.
        /// Reported by `status` so a mismatched client can say so instead of guessing.
        /// </summary>
        public const int Version = 1;

        // ------------------------------------------------------------------ writing ----

        /// <summary>
        /// Appends a JSON string literal, escaped.
        ///
        /// Escaping is the one place a hand-rolled writer usually goes wrong, so it is explicit
        /// about all four cases the spec requires: the two mandatory escapes, the short forms for
        /// the common control characters, and \u for everything else below 0x20. Characters above
        /// ASCII are emitted as-is because the stream is UTF-8 and the reader on the other side is
        /// told so - escaping them would be legal but only makes the payload bigger.
        /// </summary>
        public static void WriteString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }

        /// <summary>
        /// Writes a value of one of the types the bridge actually produces. Anything else is a
        /// programming error here rather than a runtime condition, so it becomes its string form
        /// rather than silently vanishing.
        /// </summary>
        public static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                WriteString(sb, s);
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is float || value is double || value is decimal)
            {
                // "R" so a float survives the round trip, invariant so a comma never appears as a
                // decimal separator on a machine with a European locale - which would produce
                // JSON that parses as two values.
                sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary<string, object> map)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> entry in map)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    WriteString(sb, entry.Key);
                    sb.Append(':');
                    WriteValue(sb, entry.Value);
                }

                sb.Append('}');
                return;
            }

            if (value is System.Collections.IEnumerable list)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    WriteValue(sb, item);
                }

                sb.Append(']');
                return;
            }

            WriteString(sb, value.ToString());
        }

        /// <summary>
        /// The single response line. Always carries the request's id back, an explicit ok, an
        /// explicit error, and a result - so a client never has to infer failure from a missing
        /// field.
        /// </summary>
        public static string WriteResponse(string id, bool ok, string error, object result)
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"id\":");
            WriteString(sb, id);
            sb.Append(",\"ok\":").Append(ok ? "true" : "false");
            sb.Append(",\"error\":");
            WriteString(sb, error);
            sb.Append(",\"result\":");
            WriteValue(sb, result);
            sb.Append('}');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ reading ----

        public sealed class JsonException : Exception
        {
            public JsonException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// Parses one JSON value. Returns Dictionary&lt;string, object&gt;, List&lt;object&gt;,
        /// string, double, bool, or null.
        /// </summary>
        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new JsonException("empty request");
            }

            int index = 0;
            object value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index != text.Length)
            {
                throw new JsonException(
                    $"trailing content at position {index}");
            }

            return value;
        }

        private static object ParseValue(string text, ref int i)
        {
            SkipWhitespace(text, ref i);
            if (i >= text.Length)
            {
                throw new JsonException("unexpected end of input");
            }

            char c = text[i];
            switch (c)
            {
                case '{': return ParseObject(text, ref i);
                case '[': return ParseArray(text, ref i);
                case '"': return ParseString(text, ref i);
                case 't': Expect(text, ref i, "true"); return true;
                case 'f': Expect(text, ref i, "false"); return false;
                case 'n': Expect(text, ref i, "null"); return null;
                default: return ParseNumber(text, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string text, ref int i)
        {
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWhitespace(text, ref i);
            if (i < text.Length && text[i] == '}')
            {
                i++;
                return map;
            }

            while (true)
            {
                SkipWhitespace(text, ref i);
                if (i >= text.Length || text[i] != '"')
                {
                    throw new JsonException($"expected a key at position {i}");
                }

                string key = ParseString(text, ref i);
                SkipWhitespace(text, ref i);
                if (i >= text.Length || text[i] != ':')
                {
                    throw new JsonException($"expected ':' at position {i}");
                }

                i++;
                map[key] = ParseValue(text, ref i);
                SkipWhitespace(text, ref i);
                if (i >= text.Length)
                {
                    throw new JsonException("unterminated object");
                }

                if (text[i] == ',')
                {
                    i++;
                    continue;
                }

                if (text[i] == '}')
                {
                    i++;
                    return map;
                }

                throw new JsonException($"expected ',' or '}}' at position {i}");
            }
        }

        private static List<object> ParseArray(string text, ref int i)
        {
            List<object> list = new List<object>();
            i++; // '['
            SkipWhitespace(text, ref i);
            if (i < text.Length && text[i] == ']')
            {
                i++;
                return list;
            }

            while (true)
            {
                list.Add(ParseValue(text, ref i));
                SkipWhitespace(text, ref i);
                if (i >= text.Length)
                {
                    throw new JsonException("unterminated array");
                }

                if (text[i] == ',')
                {
                    i++;
                    continue;
                }

                if (text[i] == ']')
                {
                    i++;
                    return list;
                }

                throw new JsonException($"expected ',' or ']' at position {i}");
            }
        }

        private static string ParseString(string text, ref int i)
        {
            i++; // opening quote
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (i >= text.Length)
                {
                    throw new JsonException("unterminated string");
                }

                char c = text[i++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    if (c < ' ')
                    {
                        throw new JsonException(
                            $"unescaped control character at position {i - 1}");
                    }

                    sb.Append(c);
                    continue;
                }

                if (i >= text.Length)
                {
                    throw new JsonException("unterminated escape");
                }

                char escape = text[i++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > text.Length)
                        {
                            throw new JsonException("truncated \\u escape");
                        }

                        int codePoint;
                        if (!int.TryParse(
                                text.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out codePoint))
                        {
                            throw new JsonException($"invalid \\u escape at position {i - 2}");
                        }

                        sb.Append((char)codePoint);
                        i += 4;
                        break;
                    default:
                        throw new JsonException($"unknown escape '\\{escape}'");
                }
            }
        }

        private static double ParseNumber(string text, ref int i)
        {
            int start = i;
            if (i < text.Length && text[i] == '-')
            {
                i++;
            }

            if (i >= text.Length)
            {
                throw new JsonException($"expected a value at position {start}");
            }

            if (text[i] == '0')
            {
                i++;
                if (i < text.Length && IsDigit(text[i]))
                {
                    throw new JsonException($"leading zero in number at position {start}");
                }
            }
            else if (text[i] >= '1' && text[i] <= '9')
            {
                while (i < text.Length && IsDigit(text[i]))
                {
                    i++;
                }
            }
            else
            {
                throw new JsonException($"expected a value at position {start}");
            }

            if (i < text.Length && text[i] == '.')
            {
                i++;
                int fractionStart = i;
                while (i < text.Length && IsDigit(text[i]))
                {
                    i++;
                }

                if (i == fractionStart)
                {
                    throw new JsonException($"fraction has no digits at position {start}");
                }
            }

            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                if (i < text.Length && (text[i] == '-' || text[i] == '+'))
                {
                    i++;
                }

                int exponentStart = i;
                while (i < text.Length && IsDigit(text[i]))
                {
                    i++;
                }

                if (i == exponentStart)
                {
                    throw new JsonException($"exponent has no digits at position {start}");
                }
            }

            string slice = text.Substring(start, i - start);
            double parsed;
            if (!double.TryParse(
                    slice, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                double.IsInfinity(parsed) || double.IsNaN(parsed))
            {
                throw new JsonException($"'{slice}' is not a number");
            }

            return parsed;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static void Expect(string text, ref int i, string literal)
        {
            if (i + literal.Length > text.Length ||
                string.CompareOrdinal(text, i, literal, 0, literal.Length) != 0)
            {
                throw new JsonException($"expected '{literal}' at position {i}");
            }

            i += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int i)
        {
            while (i < text.Length &&
                   (text[i] == ' ' || text[i] == '\t' || text[i] == '\r' || text[i] == '\n'))
            {
                i++;
            }
        }

        // ------------------------------------------------------------- convenience ----

        public static string GetString(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value as string ?? value.ToString();
        }

        public static Dictionary<string, object> GetObject(
            Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value))
            {
                return null;
            }

            return value as Dictionary<string, object>;
        }
    }
}
#endif
