// @author: Davy Bellens

using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>The model this entity belongs to, used to resolve references and consult the schema.</summary>
        public IfcModel Model { get; }

        private readonly IfcValue[] _attributes;

        internal IfcEntity(IfcModel model, int id, string type, IfcValue[] attributes)
        {
            Model = model;
            Id = id;
            Type = type;
            _attributes = attributes;
        }

        /// <summary>The number of positional attributes this entity was parsed with.</summary>
        public int AttributeCount => _attributes.Length;

        /// <summary>Positional attribute access. Out-of-range indices yield <see cref="IfcValue.Null"/> rather than throwing.</summary>
        public IfcValue this[int index] =>
            index >= 0 && index < _attributes.Length ? _attributes[index] : IfcValue.Null;

        /// <summary>Attribute access by schema name. An attribute the schema doesn't declare for this type yields <see cref="IfcValue.Null"/>.</summary>
        public IfcValue this[string attribute]
        {
            get
            {
                int index = Model.Schema.AttributeIndex(Type, attribute);
                return index < 0 ? IfcValue.Null : this[index];
            }
        }

        /// <summary>Whether the schema declares <paramref name="attribute"/> for this entity's type.</summary>
        public bool Has(string attribute) => Model.Schema.AttributeIndex(Type, attribute) >= 0;

        /// <summary>The declared type name, matching ifcopenshell's no-argument <c>is_a()</c>.</summary>
        public string IsA() => Type;

        /// <summary>Type test that walks the inheritance chain, like ifcopenshell's <c>is_a(name)</c>.</summary>
        public bool IsA(string type) => Model.Schema.IsSubtypeOf(Type, type);

        /// <summary>Resolve a single reference attribute to the entity it points at, or null if unresolvable.</summary>
        public IfcEntity Entity(string attribute) => Model.Resolve(this[attribute]);

        /// <summary>Resolve an aggregate of references, skipping anything unresolvable.</summary>
        public List<IfcEntity> Entities(string attribute) =>
            this[attribute].AsList()
                .Select(item => Model.Resolve(item))
                .Where(resolved => resolved != null)
                .ToList();

        /// <summary>Read a numeric attribute, or <paramref name="fallback"/> if it is missing or not a number.</summary>
        public double Double(string attribute, double fallback = 0.0) => this[attribute].AsDouble(fallback);

        /// <summary>Read a numeric attribute without a fallback, reporting success rather than substituting a default.</summary>
        public bool TryDouble(string attribute, out double value) => this[attribute].TryAsDouble(out value);

        /// <summary>Read an integer attribute, or <paramref name="fallback"/> if it is missing or not a number.</summary>
        public int Int(string attribute, int fallback = 0) => this[attribute].AsInt(fallback);

        /// <summary>Read a string attribute, or an empty-ish result if it is missing.</summary>
        public string String(string attribute) => this[attribute].AsString();

        /// <summary>Read an IFC logical (<c>TRUE</c>/<c>FALSE</c>/<c>UNKNOWN</c>) attribute as a nullable bool.</summary>
        public bool? Logical(string attribute) => this[attribute].AsLogical();

        /// <summary>Read an aggregate attribute as a list of raw values, without resolving references.</summary>
        public IReadOnlyList<IfcValue> List(string attribute) => this[attribute].AsList();

        /// <summary>Read an aggregate of numeric attributes, e.g. a direction or coordinate list.</summary>
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