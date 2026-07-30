// @author: Davy Bellens

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Conversion.Ifc
{
    public enum IfcValueKind
    {
        /// <summary>A <c>$</c> in the file: the attribute is absent.</summary>
        Null,

        /// <summary>A <c>*</c> in the file: derived in a subtype, not stored here.</summary>
        Derived,

        Integer,
        Real,
        Logical,
        String,

        /// <summary>A <c>.SOMENAME.</c> enumeration literal.</summary>
        Enumeration,

        /// <summary>A <c>#123</c> reference. Resolved to an entity by the model.</summary>
        Reference,

        /// <summary>A parenthesised aggregate: list, set or array.</summary>
        List,

        /// <summary>A select wrapper such as <c>IFCPOSITIVELENGTHMEASURE(1760.)</c>.</summary>
        Typed,
    }

    /// <summary>
    /// One attribute slot from a STEP record.
    /// <para>
    /// Deliberately dynamic. The Python resource layer is duck-typed throughout —
    /// <c>getattr(profile, "Position", None)</c> works whether the file is IFC2X3
    /// or IFC4, and the same builder handles both. A statically typed entity model
    /// would force that single code path to split in two, which is a large price
    /// for type safety the geometry code was never going to use.
    /// </para>
    /// </summary>
    public readonly struct IfcValue
    {
        /// <summary>The variant this value holds — reference, number, string, and so on.</summary>
        public readonly IfcValueKind Kind;
        private readonly double _number;
        private readonly object _payload;

        private IfcValue(IfcValueKind kind, double number, object payload)
        {
            Kind = kind;
            _number = number;
            _payload = payload;
        }

        public static readonly IfcValue Null = new IfcValue(IfcValueKind.Null, 0.0, null);
        public static readonly IfcValue Derived = new IfcValue(IfcValueKind.Derived, 0.0, null);

        public static IfcValue FromInteger(long value) => new IfcValue(IfcValueKind.Integer, value, null);
        public static IfcValue FromReal(double value) => new IfcValue(IfcValueKind.Real, value, null);
        public static IfcValue FromLogical(bool? value) =>
            new IfcValue(IfcValueKind.Logical, 0.0, value.HasValue ? (object)value.Value : null);
        public static IfcValue FromString(string value) => new IfcValue(IfcValueKind.String, 0.0, value);
        public static IfcValue FromEnumeration(string value) => new IfcValue(IfcValueKind.Enumeration, 0.0, value);
        public static IfcValue FromReference(int id) => new IfcValue(IfcValueKind.Reference, id, null);
        public static IfcValue FromList(IfcValue[] items) => new IfcValue(IfcValueKind.List, 0.0, items);
        public static IfcValue FromTyped(string typeName, IfcValue inner) =>
            new IfcValue(IfcValueKind.Typed, 0.0, new TypedValue(typeName, inner));

        private sealed class TypedValue
        {
            public readonly string TypeName;
            public readonly IfcValue Inner;

            public TypedValue(string typeName, IfcValue inner)
            {
                TypeName = typeName;
                Inner = inner;
            }
        }

        /// <summary>Whether this attribute was absent (<c>$</c>) or derived (<c>*</c>) in the file.</summary>
        public bool IsNull => Kind == IfcValueKind.Null || Kind == IfcValueKind.Derived;

        /// <summary>Whether this is a parenthesised aggregate.</summary>
        public bool IsList => Kind == IfcValueKind.List;

        /// <summary>The name of a select wrapper, or null when this is not one.</summary>
        public string TypeName => (_payload as TypedValue)?.TypeName;

        /// <summary>
        /// Unwrap a select. <c>IFCPOSITIVELENGTHMEASURE(1760.)</c> reads as 1760, which
        /// is the Python's <c>getattr(value, "wrappedValue", value)</c> made explicit.
        /// </summary>
        public IfcValue Unwrapped => _payload is TypedValue typed ? typed.Inner.Unwrapped : this;

        /// <summary>The raw <c>#123</c> line number this reference points at, or 0 when this isn't a reference.</summary>
        internal int ReferenceId => Kind == IfcValueKind.Reference ? (int)_number : 0;

        /// <summary>Read a numeric value, reporting success rather than substituting a default.</summary>
        public bool TryAsDouble(out double result)
        {
            IfcValue value = Unwrapped;
            switch (value.Kind)
            {
                case IfcValueKind.Real:
                case IfcValueKind.Integer:
                    result = value._number;
                    return true;
                default:
                    result = 0.0;
                    return false;
            }
        }

        /// <summary>Read a numeric value, or <paramref name="fallback"/> if this isn't one.</summary>
        public double AsDouble(double fallback = 0.0) => TryAsDouble(out double value) ? value : fallback;

        /// <summary>Read a numeric value rounded to the nearest integer, reporting success rather than substituting a default.</summary>
        public bool TryAsInt(out int result)
        {
            if (TryAsDouble(out double value))
            {
                result = (int)Math.Round(value);
                return true;
            }
            result = 0;
            return false;
        }

        /// <summary>Read a numeric value rounded to the nearest integer, or <paramref name="fallback"/> if this isn't one.</summary>
        public int AsInt(int fallback = 0) => TryAsInt(out int value) ? value : fallback;

        /// <summary>String and enumeration literals both read as text; everything else is null.</summary>
        public string AsString()
        {
            IfcValue value = Unwrapped;
            return value.Kind == IfcValueKind.String || value.Kind == IfcValueKind.Enumeration
                ? (string)value._payload
                : null;
        }

        /// <summary>
        /// Three-valued, because EXPRESS LOGICAL is. Absent and UNKNOWN both read as
        /// null, which matters for things like IfcFaceBound.Orientation where the
        /// default is TRUE and only an explicit FALSE reverses the loop.
        /// </summary>
        public bool? AsLogical()
        {
            IfcValue value = Unwrapped;
            if (value.Kind != IfcValueKind.Logical)
            {
                return null;
            }
            return value._payload as bool?;
        }

        /// <summary>The items of a list-kind value, or empty if this isn't one.</summary>
        public IReadOnlyList<IfcValue> AsList()
        {
            IfcValue value = Unwrapped;
            return value._payload as IfcValue[] ?? Array.Empty<IfcValue>();
        }

        /// <summary>Flatten a list of numbers, e.g. IfcCartesianPoint.Coordinates.</summary>
        public double[] AsDoubles()
        {
            IReadOnlyList<IfcValue> items = AsList();
            var result = new double[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                result[i] = items[i].AsDouble();
            }
            return result;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case IfcValueKind.Null: return "$";
                case IfcValueKind.Derived: return "*";
                case IfcValueKind.Integer: return ((long)_number).ToString(CultureInfo.InvariantCulture);
                case IfcValueKind.Real: return _number.ToString("R", CultureInfo.InvariantCulture);
                case IfcValueKind.Logical: return (_payload as bool?)?.ToString() ?? "UNKNOWN";
                case IfcValueKind.String: return $"'{_payload}'";
                case IfcValueKind.Enumeration: return $".{_payload}.";
                case IfcValueKind.Reference: return $"#{(int)_number}";
                case IfcValueKind.List: return $"({((IfcValue[])_payload).Length} items)";
                case IfcValueKind.Typed:
                    var typed = (TypedValue)_payload;
                    return $"{typed.TypeName}({typed.Inner})";
                default: return "?";
            }
        }
    }
}