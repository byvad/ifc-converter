// @author: Davy Bellens

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
        /// <summary>Parse a single JSON value — an object, array, string, number, bool or null.</summary>
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

        /// <summary>What comes next inside a <c>{...}</c> or <c>[...]</c>, once whitespace is out of the way.</summary>
        private enum NextToken { Closed, Comma, Element }

        /// <summary>
        /// Skip whitespace and classify what follows: the container's closing bracket
        /// (consumed), a comma separating elements (consumed), or the start of an
        /// element (left in place for the caller to parse).
        /// </summary>
        private static NextToken SkipToNextElement(string text, ref int index, char closing, string unterminatedMessage)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new FormatException(unterminatedMessage);
            }
            if (text[index] == closing)
            {
                index++;
                return NextToken.Closed;
            }
            if (text[index] == ',')
            {
                index++;
                return NextToken.Comma;
            }
            return NextToken.Element;
        }

        private static Dictionary<string, object> ParseMap(string text, ref int index)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            index++;   // '{'

            while (true)
            {
                NextToken next = SkipToNextElement(text, ref index, '}', "Unterminated JSON object.");
                if (next == NextToken.Closed)
                {
                    return map;
                }
                if (next == NextToken.Comma)
                {
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
                NextToken next = SkipToNextElement(text, ref index, ']', "Unterminated JSON array.");
                if (next == NextToken.Closed)
                {
                    return items;
                }
                if (next == NextToken.Comma)
                {
                    continue;
                }

                items.Add(ParseValue(text, ref index));
            }
        }

        /// <summary>A <c>\uXXXX</c> escape always spends exactly four hex digits.</summary>
        private const int UnicodeEscapeDigits = 4;

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
                AppendEscape(builder, text, ref index);
            }
            throw new FormatException("Unterminated JSON string.");
        }

        /// <summary>Decode one escape sequence following a <c>\</c> and append it to <paramref name="builder"/>.</summary>
        private static void AppendEscape(StringBuilder builder, string text, ref int index)
        {
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
                case 'u': AppendUnicodeEscape(builder, text, ref index); break;
                default: builder.Append(escape); break;
            }
        }

        /// <summary>Decode a <c>\uXXXX</c> escape. A malformed one is silently dropped rather than thrown on.</summary>
        private static void AppendUnicodeEscape(StringBuilder builder, string text, ref int index)
        {
            if (index + UnicodeEscapeDigits <= text.Length &&
                ushort.TryParse(text.Substring(index, UnicodeEscapeDigits), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ushort code))
            {
                builder.Append((char)code);
                index += UnicodeEscapeDigits;
            }
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
