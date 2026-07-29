using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Conversion.Ifc
{
    /// <summary>
    /// A small recursive-descent JSON reader, sufficient for the taxonomy files.
    /// <para>
    /// Unity's JsonUtility cannot express <c>{"schemas": {"HVAC": [...]}}</c> — it has
    /// no mapping for a dictionary with arbitrary keys — and pulling in Newtonsoft
    /// for four small files is out of proportion. Objects come back as
    /// Dictionary&lt;string, object&gt;, arrays as List&lt;object&gt;, and scalars as
    /// string, double, bool or null.
    /// </para>
    /// <para>
    /// Keeping the .json files as-is matters: they are the same four files the Python
    /// reads, so the taxonomy has one source of truth rather than two that drift.
    /// </para>
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int index = 0;
            object value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            return value;
        }

        /// <summary>Convenience: parse and cast to an object map.</summary>
        public static Dictionary<string, object> ParseObject(string text) =>
            Parse(text) as Dictionary<string, object>;

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new FormatException("Unexpected end of JSON.");
            }

            char c = text[index];
            switch (c)
            {
                case '{': return ParseMap(text, ref index);
                case '[': return ParseArray(text, ref index);
                case '"': return ParseString(text, ref index);
                case 't':
                    Expect(text, ref index, "true");
                    return true;
                case 'f':
                    Expect(text, ref index, "false");
                    return false;
                case 'n':
                    Expect(text, ref index, "null");
                    return null;
                default: return ParseNumber(text, ref index);
            }
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length ||
                string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException($"Expected '{literal}' at offset {index}.");
            }
            index += literal.Length;
        }

        private static Dictionary<string, object> ParseMap(string text, ref int index)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            index++;   // '{'

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new FormatException("Unterminated JSON object.");
                }
                if (text[index] == '}')
                {
                    index++;
                    return map;
                }
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw new FormatException($"Expected ':' after key '{key}'.");
                }
                index++;
                map[key] = ParseValue(text, ref index);
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            var items = new List<object>();
            index++;   // '['

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new FormatException("Unterminated JSON array.");
                }
                if (text[index] == ']')
                {
                    index++;
                    return items;
                }
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }
                items.Add(ParseValue(text, ref index));
            }
        }

        private static string ParseString(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length || text[index] != '"')
            {
                throw new FormatException($"Expected a string at offset {index}.");
            }
            index++;

            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (index >= text.Length)
                {
                    break;
                }

                char escape = text[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 <= text.Length &&
                            ushort.TryParse(text.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out ushort code))
                        {
                            builder.Append((char)code);
                            index += 4;
                        }
                        break;
                    default: builder.Append(escape); break;
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }

        private static double ParseNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length)
            {
                char c = text[index];
                if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    index++;
                }
                else
                {
                    break;
                }
            }
            string slice = text.Substring(start, index - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException($"Bad number '{slice}' at offset {start}.");
            }
            return value;
        }
    }
}
