using System;
using System.Collections.Generic;

namespace Conversion.Ifc
{
    /// <summary>
    /// A parsed IFC file: the entity table, plus the two indices the descent needs.
    /// </summary>
    public sealed class IfcModel
    {
        public IfcSchema Schema { get; }

        /// <summary>The schema named in the file header, e.g. <c>IFC2X3</c>.</summary>
        public string SchemaName { get; }

        private readonly Dictionary<int, IfcEntity> _byId;
        private readonly Dictionary<string, List<IfcEntity>> _byExactType;

        /// <summary>
        /// Inverse indices, built on demand and cached.
        /// <para>
        /// Indexing every reference in the file up front would cost hundreds of
        /// megabytes on a model this size, and almost all of it would go untouched.
        /// Asking for <c>HasOpenings</c> instead builds exactly one index — every
        /// IfcRelVoidsElement keyed by its RelatingBuildingElement — which on the
        /// castle is 79 entries.
        /// </para>
        /// </summary>
        private readonly Dictionary<(string Type, string Attribute), Dictionary<int, List<IfcEntity>>>
            _inverseIndices = new Dictionary<(string, string), Dictionary<int, List<IfcEntity>>>();

        private static readonly IReadOnlyList<IfcEntity> NoEntities = Array.Empty<IfcEntity>();

        internal IfcModel(IfcSchema schema, string schemaName,
            Dictionary<int, IfcEntity> byId, Dictionary<string, List<IfcEntity>> byExactType)
        {
            Schema = schema;
            SchemaName = schemaName;
            _byId = byId;
            _byExactType = byExactType;
        }

        public int EntityCount => _byId.Count;

        public IfcEntity ById(int id) => _byId.TryGetValue(id, out IfcEntity entity) ? entity : null;

        public IfcEntity Resolve(IfcValue value)
        {
            IfcValue unwrapped = value.Unwrapped;
            return unwrapped.Kind == IfcValueKind.Reference ? ById(unwrapped.ReferenceId) : null;
        }

        /// <summary>
        /// Every instance of <paramref name="type"/>, including subtypes — which is
        /// what makes <c>ByType("IfcProduct")</c> return walls and windows rather
        /// than the empty set.
        /// </summary>
        public List<IfcEntity> ByType(string type, bool includeSubtypes = true)
        {
            var result = new List<IfcEntity>();

            if (!includeSubtypes)
            {
                if (_byExactType.TryGetValue(type, out List<IfcEntity> exact))
                {
                    result.AddRange(exact);
                }
                return result;
            }

            foreach (string name in Schema.TypeAndDescendants(type))
            {
                if (_byExactType.TryGetValue(name, out List<IfcEntity> bucket))
                {
                    result.AddRange(bucket);
                }
            }
            return result;
        }

        /// <summary>The distinct entity type names actually present in the file.</summary>
        public IEnumerable<string> PresentTypes => _byExactType.Keys;

        internal IReadOnlyList<IfcEntity> Inverse(IfcEntity target, string attribute)
        {
            if (!Schema.TryGetInverse(target.Type, attribute, out InverseLink link))
            {
                return NoEntities;
            }

            var key = (link.ReferencingType, link.ReferencingAttribute);
            if (!_inverseIndices.TryGetValue(key, out Dictionary<int, List<IfcEntity>> index))
            {
                index = BuildInverseIndex(link);
                _inverseIndices[key] = index;
            }

            return index.TryGetValue(target.Id, out List<IfcEntity> hits) ? hits : NoEntities;
        }

        private Dictionary<int, List<IfcEntity>> BuildInverseIndex(InverseLink link)
        {
            var index = new Dictionary<int, List<IfcEntity>>();

            void Record(int id, IfcEntity source)
            {
                if (id == 0)
                {
                    return;
                }
                if (!index.TryGetValue(id, out List<IfcEntity> bucket))
                {
                    bucket = new List<IfcEntity>();
                    index[id] = bucket;
                }
                bucket.Add(source);
            }

            foreach (IfcEntity source in ByType(link.ReferencingType))
            {
                IfcValue value = source[link.ReferencingAttribute];

                // The far side may be a single reference (IfcRelVoidsElement.RelatingBuildingElement)
                // or a set of them (IfcRelAssociates.RelatedObjects). Both are normal.
                if (value.IsList)
                {
                    foreach (IfcValue item in value.AsList())
                    {
                        IfcValue unwrapped = item.Unwrapped;
                        if (unwrapped.Kind == IfcValueKind.Reference)
                        {
                            Record(unwrapped.ReferenceId, source);
                        }
                    }
                }
                else
                {
                    IfcValue unwrapped = value.Unwrapped;
                    if (unwrapped.Kind == IfcValueKind.Reference)
                    {
                        Record(unwrapped.ReferenceId, source);
                    }
                }
            }

            return index;
        }
    }
}
