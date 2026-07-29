using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Conversion.Ifc
{
    /// <summary>Resolves a schema name from a file header to a loaded schema table.</summary>
    public sealed class IfcSchemaRegistry
    {
        private readonly Dictionary<string, IfcSchema> _loaded =
            new Dictionary<string, IfcSchema>(StringComparer.OrdinalIgnoreCase);

        private readonly Func<string, string> _textFor;

        /// <param name="textFor">Given a schema name such as IFC2X3, return the
        /// contents of the matching .schema table. In Unity this reads a TextAsset;
        /// on the desktop it reads a file.</param>
        public IfcSchemaRegistry(Func<string, string> textFor)
        {
            _textFor = textFor;
        }

        public static IfcSchemaRegistry FromDirectory(string directory) =>
            new IfcSchemaRegistry(name =>
            {
                string path = Path.Combine(directory, name.ToLowerInvariant() + ".schema");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });

        public IfcSchema Get(string schemaName)
        {
            if (_loaded.TryGetValue(schemaName, out IfcSchema schema))
            {
                return schema;
            }
            string text = _textFor(schemaName);
            if (text == null)
            {
                throw new NotSupportedException($"No schema table available for '{schemaName}'.");
            }
            schema = IfcSchema.Parse(text);
            _loaded[schemaName] = schema;
            return schema;
        }
    }

    /// <summary>
    /// A reader for ISO 10303-21 physical files, which is what an .ifc file is.
    /// <para>
    /// Works over raw bytes rather than a decoded string. A 50 MB model becomes a
    /// 100 MB UTF-16 string the moment you call ReadAllText, and every one of those
    /// bytes is ASCII apart from the occasional accented name. Scanning the bytes
    /// and decoding only inside quoted literals avoids the doubling.
    /// </para>
    /// </summary>
    public static class StepParser
    {
        public static IfcModel Load(string path, IfcSchemaRegistry registry) =>
            Parse(File.ReadAllBytes(path), registry);

        public static IfcModel Parse(byte[] data, IfcSchemaRegistry registry)
        {
            var reader = new Reader(data);
            string schemaName = reader.ReadSchemaName();
            IfcSchema schema = registry.Get(schemaName);
            return reader.ReadData(schema, schemaName);
        }

        private sealed class Reader
        {
            private readonly byte[] _data;
            private int _pos;

            /// <summary>Type names repeat millions of times in a large file; one instance each.</summary>
            private readonly Dictionary<string, string> _names =
                new Dictionary<string, string>(StringComparer.Ordinal);

            private char[] _scratch = new char[64];

            public Reader(byte[] data)
            {
                _data = data;
            }

            private bool AtEnd => _pos >= _data.Length;
            private byte Current => _data[_pos];

            private void SkipTrivia()
            {
                while (_pos < _data.Length)
                {
                    byte c = _data[_pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        _pos++;
                    }
                    else if (c == '/' && _pos + 1 < _data.Length && _data[_pos + 1] == '*')
                    {
                        _pos += 2;
                        while (_pos + 1 < _data.Length && !(_data[_pos] == '*' && _data[_pos + 1] == '/'))
                        {
                            _pos++;
                        }
                        _pos = Math.Min(_pos + 2, _data.Length);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            private static bool IsNameByte(byte c) =>
                (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') || c == '_' || c == '-';

            private string ReadName()
            {
                int start = _pos;
                while (_pos < _data.Length && IsNameByte(_data[_pos]))
                {
                    _pos++;
                }
                int length = _pos - start;
                if (length == 0)
                {
                    return string.Empty;
                }

                EnsureScratch(length);
                for (int i = 0; i < length; i++)
                {
                    _scratch[i] = (char)_data[start + i];
                }
                var candidate = new string(_scratch, 0, length);

                if (_names.TryGetValue(candidate, out string interned))
                {
                    return interned;
                }
                _names[candidate] = candidate;
                return candidate;
            }

            private void EnsureScratch(int length)
            {
                if (_scratch.Length < length)
                {
                    _scratch = new char[Math.Max(length, _scratch.Length * 2)];
                }
            }

            /// <summary>Pull the schema out of FILE_SCHEMA(('IFC2X3')); in the header.</summary>
            public string ReadSchemaName()
            {
                int marker = IndexOfAscii("FILE_SCHEMA", 0);
                if (marker < 0)
                {
                    throw new InvalidDataException("Not a STEP physical file: no FILE_SCHEMA in the header.");
                }
                _pos = marker;
                while (_pos < _data.Length && _data[_pos] != '\'')
                {
                    _pos++;
                }
                _pos++;

                var builder = new StringBuilder();
                while (_pos < _data.Length && _data[_pos] != '\'')
                {
                    builder.Append((char)_data[_pos]);
                    _pos++;
                }

                // IFC4X3_ADD2 and friends carry a suffix the table is not named for.
                string name = builder.ToString().Trim().ToUpperInvariant();
                return name;
            }

            private int IndexOfAscii(string needle, int from)
            {
                int limit = _data.Length - needle.Length;
                for (int i = from; i <= limit; i++)
                {
                    bool hit = true;
                    for (int j = 0; j < needle.Length; j++)
                    {
                        if (_data[i + j] != (byte)needle[j])
                        {
                            hit = false;
                            break;
                        }
                    }
                    if (hit)
                    {
                        return i;
                    }
                }
                return -1;
            }

            public IfcModel ReadData(IfcSchema schema, string schemaName)
            {
                int dataStart = IndexOfAscii("DATA;", 0);
                if (dataStart < 0)
                {
                    throw new InvalidDataException("No DATA section in the file.");
                }
                _pos = dataStart + "DATA;".Length;

                var byId = new Dictionary<int, IfcEntity>(1 << 16);
                var byExactType = new Dictionary<string, List<IfcEntity>>(StringComparer.OrdinalIgnoreCase);

                IfcModel model = null;
                var pendingIds = new List<int>();
                var pendingTypes = new List<string>();
                var pendingAttributes = new List<IfcValue[]>();

                while (true)
                {
                    SkipTrivia();
                    if (AtEnd)
                    {
                        break;
                    }

                    if (Current != '#')
                    {
                        // ENDSEC; or anything else that is not a record ends the section.
                        break;
                    }

                    _pos++;                       // '#'
                    int id = ReadInteger();
                    SkipTrivia();
                    if (AtEnd || Current != '=')
                    {
                        break;
                    }
                    _pos++;                       // '='
                    SkipTrivia();

                    string type;
                    IfcValue[] attributes;

                    if (!AtEnd && Current == '(')
                    {
                        (type, attributes) = ReadComplexInstance(schema);
                    }
                    else
                    {
                        type = schema.Canonical(ReadName());
                        SkipTrivia();
                        attributes = ReadArgumentList();
                    }

                    SkipTrivia();
                    if (!AtEnd && Current == ';')
                    {
                        _pos++;
                    }

                    pendingIds.Add(id);
                    pendingTypes.Add(type);
                    pendingAttributes.Add(attributes);
                }

                model = new IfcModel(schema, schemaName, byId, byExactType);

                for (int i = 0; i < pendingIds.Count; i++)
                {
                    var entity = new IfcEntity(model, pendingIds[i], pendingTypes[i], pendingAttributes[i]);
                    byId[pendingIds[i]] = entity;

                    if (!byExactType.TryGetValue(pendingTypes[i], out List<IfcEntity> bucket))
                    {
                        bucket = new List<IfcEntity>();
                        byExactType[pendingTypes[i]] = bucket;
                    }
                    bucket.Add(entity);
                }

                return model;
            }

            /// <summary>
            /// A complex instance, <c>#1=(IFCA(..)IFCB(..));</c>. The parts appear in
            /// inheritance order and their attribute lists concatenate, so the record
            /// is recorded under the most derived part with the parts joined end to end.
            /// </summary>
            private (string Type, IfcValue[] Attributes) ReadComplexInstance(IfcSchema schema)
            {
                _pos++;   // opening '('
                var parts = new List<(string Type, IfcValue[] Attributes)>();

                while (true)
                {
                    SkipTrivia();
                    if (AtEnd || Current == ')')
                    {
                        _pos = Math.Min(_pos + 1, _data.Length);
                        break;
                    }
                    string partType = schema.Canonical(ReadName());
                    SkipTrivia();
                    IfcValue[] partAttributes = ReadArgumentList();
                    parts.Add((partType, partAttributes));
                }

                string mostDerived = null;
                var merged = new List<IfcValue>();
                foreach ((string partType, IfcValue[] partAttributes) in parts)
                {
                    merged.AddRange(partAttributes);
                    if (mostDerived == null || schema.IsSubtypeOf(partType, mostDerived))
                    {
                        mostDerived = partType;
                    }
                }

                return (mostDerived ?? string.Empty, merged.ToArray());
            }

            private IfcValue[] ReadArgumentList()
            {
                if (AtEnd || Current != '(')
                {
                    return Array.Empty<IfcValue>();
                }
                _pos++;   // '('

                var values = new List<IfcValue>(8);
                while (true)
                {
                    SkipTrivia();
                    if (AtEnd)
                    {
                        break;
                    }
                    if (Current == ')')
                    {
                        _pos++;
                        break;
                    }
                    if (Current == ',')
                    {
                        _pos++;
                        continue;
                    }
                    values.Add(ReadValue());
                }
                return values.ToArray();
            }

            private IfcValue ReadValue()
            {
                SkipTrivia();
                if (AtEnd)
                {
                    return IfcValue.Null;
                }

                byte c = Current;

                if (c == '$')
                {
                    _pos++;
                    return IfcValue.Null;
                }
                if (c == '*')
                {
                    _pos++;
                    return IfcValue.Derived;
                }
                if (c == '#')
                {
                    _pos++;
                    return IfcValue.FromReference(ReadInteger());
                }
                if (c == '\'')
                {
                    return IfcValue.FromString(ReadQuotedString());
                }
                if (c == '(')
                {
                    return IfcValue.FromList(ReadArgumentList());
                }
                if (c == '.')
                {
                    return ReadEnumeration();
                }
                if (c == '-' || c == '+' || (c >= '0' && c <= '9'))
                {
                    return ReadNumber();
                }

                // A bare name here is a select wrapper: IFCPOSITIVELENGTHMEASURE(1760.)
                string typeName = ReadName();
                SkipTrivia();
                if (!AtEnd && Current == '(')
                {
                    IfcValue[] inner = ReadArgumentList();
                    IfcValue payload = inner.Length == 1 ? inner[0] : IfcValue.FromList(inner);
                    return IfcValue.FromTyped(typeName, payload);
                }
                return IfcValue.FromEnumeration(typeName);
            }

            private IfcValue ReadEnumeration()
            {
                _pos++;   // leading '.'
                int start = _pos;
                while (_pos < _data.Length && _data[_pos] != '.')
                {
                    _pos++;
                }
                int length = _pos - start;
                if (_pos < _data.Length)
                {
                    _pos++;   // trailing '.'
                }

                EnsureScratch(length);
                for (int i = 0; i < length; i++)
                {
                    _scratch[i] = (char)_data[start + i];
                }
                var literal = new string(_scratch, 0, length);

                if (literal == "T")
                {
                    return IfcValue.FromLogical(true);
                }
                if (literal == "F")
                {
                    return IfcValue.FromLogical(false);
                }
                if (literal == "U")
                {
                    return IfcValue.FromLogical(null);
                }

                if (_names.TryGetValue(literal, out string interned))
                {
                    return IfcValue.FromEnumeration(interned);
                }
                _names[literal] = literal;
                return IfcValue.FromEnumeration(literal);
            }

            private int ReadInteger()
            {
                int value = 0;
                bool negative = false;
                if (!AtEnd && (Current == '-' || Current == '+'))
                {
                    negative = Current == '-';
                    _pos++;
                }
                while (_pos < _data.Length && _data[_pos] >= '0' && _data[_pos] <= '9')
                {
                    value = value * 10 + (_data[_pos] - '0');
                    _pos++;
                }
                return negative ? -value : value;
            }

            private IfcValue ReadNumber()
            {
                int start = _pos;
                bool real = false;

                if (!AtEnd && (Current == '-' || Current == '+'))
                {
                    _pos++;
                }
                while (_pos < _data.Length)
                {
                    byte c = _data[_pos];
                    if (c >= '0' && c <= '9')
                    {
                        _pos++;
                    }
                    else if (c == '.')
                    {
                        real = true;
                        _pos++;
                    }
                    else if (c == 'E' || c == 'e')
                    {
                        real = true;
                        _pos++;
                        if (_pos < _data.Length && (_data[_pos] == '-' || _data[_pos] == '+'))
                        {
                            _pos++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                int length = _pos - start;
                EnsureScratch(length);
                for (int i = 0; i < length; i++)
                {
                    _scratch[i] = (char)_data[start + i];
                }

                if (!real)
                {
                    long integer = 0;
                    bool negative = _scratch[0] == '-';
                    for (int i = negative || _scratch[0] == '+' ? 1 : 0; i < length; i++)
                    {
                        integer = integer * 10 + (_scratch[i] - '0');
                    }
                    return IfcValue.FromInteger(negative ? -integer : integer);
                }

                double parsed = double.Parse(new string(_scratch, 0, length),
                    NumberStyles.Float, CultureInfo.InvariantCulture);
                return IfcValue.FromReal(parsed);
            }

            /// <summary>
            /// Decode a quoted literal, honouring the escapes Part 21 defines: a doubled
            /// quote, and the \S\ / \X\ / \X2\ sequences that carry anything outside
            /// plain ASCII. Dutch and German element names hit these constantly.
            /// </summary>
            private string ReadQuotedString()
            {
                _pos++;   // opening quote
                var builder = new StringBuilder();

                while (_pos < _data.Length)
                {
                    byte c = _data[_pos];

                    if (c == '\'')
                    {
                        if (_pos + 1 < _data.Length && _data[_pos + 1] == '\'')
                        {
                            builder.Append('\'');
                            _pos += 2;
                            continue;
                        }
                        _pos++;
                        break;
                    }

                    if (c == '\\' && _pos + 2 < _data.Length)
                    {
                        byte marker = _data[_pos + 1];

                        if (marker == 'S' && _data[_pos + 2] == '\\' && _pos + 3 < _data.Length)
                        {
                            builder.Append((char)(_data[_pos + 3] + 128));
                            _pos += 4;
                            continue;
                        }

                        if (marker == 'X' && _data[_pos + 2] == '\\')
                        {
                            int value = HexValue(_pos + 3, 2);
                            if (value >= 0)
                            {
                                builder.Append((char)value);
                                _pos += 5;
                                continue;
                            }
                        }

                        if ((marker == 'X' || marker == 'x') &&
                            (_data[_pos + 2] == '2' || _data[_pos + 2] == '4') &&
                            _pos + 3 < _data.Length && _data[_pos + 3] == '\\')
                        {
                            int width = _data[_pos + 2] == '2' ? 4 : 8;
                            int cursor = _pos + 4;
                            while (cursor + width <= _data.Length)
                            {
                                if (_data[cursor] == '\\')
                                {
                                    break;
                                }
                                int value = HexValue(cursor, width);
                                if (value < 0)
                                {
                                    break;
                                }
                                builder.Append(char.ConvertFromUtf32(value & 0x10FFFF));
                                cursor += width;
                            }
                            // Skip the \X0\ terminator when present.
                            if (cursor + 3 <= _data.Length && _data[cursor] == '\\')
                            {
                                cursor += 3;
                                if (cursor < _data.Length && _data[cursor] == '\\')
                                {
                                    cursor++;
                                }
                            }
                            _pos = cursor;
                            continue;
                        }
                    }

                    builder.Append((char)c);
                    _pos++;
                }

                return builder.ToString();
            }

            private int HexValue(int start, int count)
            {
                if (start + count > _data.Length)
                {
                    return -1;
                }
                int value = 0;
                for (int i = 0; i < count; i++)
                {
                    byte c = _data[start + i];
                    int digit;
                    if (c >= '0' && c <= '9') digit = c - '0';
                    else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                    else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                    else return -1;
                    value = value * 16 + digit;
                }
                return value;
            }
        }
    }
}
