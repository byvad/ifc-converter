using System;
using System.Collections.Generic;

namespace Conversion.Ifc
{
    /// <summary>
    /// One <c>#123= IFCWALL(...);</c> record, with its attributes addressable by name.
    /// <para>
    /// The accessors mirror the shape the Python resource layer already speaks:
    /// a missing attribute yields a null-ish result rather than throwing, because
    /// the descent is full of optional geometry and <c>getattr(x, "Position", None)</c>
    /// is the normal way to ask.
    /// </para>
    /// </summary>
    public sealed class IfcEntity
    {
        /// <summary>The <c>#123</c> line number. Zero for entities nested inline in an attribute.</summary>
        public int Id { get; }

        /// <summary>The declared type, e.g. <c>IfcExtrudedAreaSolid</c>.</summary>
        public string Type { get; }

        public IfcModel Model { get; }

        private readonly IfcValue[] _attributes;

        internal IfcEntity(IfcModel model, int id, string type, IfcValue[] attributes)
        {
            Model = model;
            Id = id;
            Type = type;
            _attributes = attributes;
        }

        public int AttributeCount => _attributes.Length;

        public IfcValue this[int index] =>
            index >= 0 && index < _attributes.Length ? _attributes[index] : IfcValue.Null;

        public IfcValue this[string attribute]
        {
            get
            {
                int index = Model.Schema.AttributeIndex(Type, attribute);
                return index < 0 ? IfcValue.Null : this[index];
            }
        }

        public bool Has(string attribute) => Model.Schema.AttributeIndex(Type, attribute) >= 0;

        /// <summary>The declared type name, matching ifcopenshell's no-argument <c>is_a()</c>.</summary>
        public string IsA() => Type;

        /// <summary>Type test that walks the inheritance chain, like ifcopenshell's <c>is_a(name)</c>.</summary>
        public bool IsA(string type) => Model.Schema.IsSubtypeOf(Type, type);

        public IfcEntity Entity(string attribute) => Model.Resolve(this[attribute]);

        /// <summary>Resolve an aggregate of references, skipping anything unresolvable.</summary>
        public List<IfcEntity> Entities(string attribute)
        {
            var result = new List<IfcEntity>();
            foreach (IfcValue item in this[attribute].AsList())
            {
                IfcEntity resolved = Model.Resolve(item);
                if (resolved != null)
                {
                    result.Add(resolved);
                }
            }
            return result;
        }

        public double Double(string attribute, double fallback = 0.0) => this[attribute].AsDouble(fallback);

        public bool TryDouble(string attribute, out double value) => this[attribute].TryAsDouble(out value);

        public int Int(string attribute, int fallback = 0) => this[attribute].AsInt(fallback);

        public string String(string attribute) => this[attribute].AsString();

        public bool? Logical(string attribute) => this[attribute].AsLogical();

        public IReadOnlyList<IfcValue> List(string attribute) => this[attribute].AsList();

        public double[] Doubles(string attribute) => this[attribute].AsDoubles();

        /// <summary>
        /// Follow an inverse attribute such as <c>HasOpenings</c> or <c>StyledByItem</c>.
        /// <para>
        /// Nothing is stored for these in the file; they are resolved by asking the
        /// model which entities point back at this one through the attribute the
        /// schema names.
        /// </para>
        /// </summary>
        public IReadOnlyList<IfcEntity> Inverse(string attribute) => Model.Inverse(this, attribute);

        public override string ToString() => Id > 0 ? $"#{Id}={Type}" : Type;
    }
}
