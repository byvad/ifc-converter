// @author: Davy Bellens

using System;
using System.Collections.Generic;
using System.IO;

namespace Conversion.Ifc
{
    /// <summary>Where an inverse attribute actually lives: some other entity's forward attribute.</summary>
    public readonly struct InverseLink
    {
        /// <summary>The entity type that points back at us, e.g. IfcRelVoidsElement.</summary>
        public readonly string ReferencingType;

        /// <summary>The attribute on that type holding the reference, e.g. RelatingBuildingElement.</summary>
        public readonly string ReferencingAttribute;

        public InverseLink(string referencingType, string referencingAttribute)
        {
            ReferencingType = referencingType;
            ReferencingAttribute = referencingAttribute;
        }

        public override string ToString() => $"{ReferencingType}.{ReferencingAttribute}";
    }

    /// <summary>
    /// The EXPRESS facts the parser cannot work out for itself: which entity derives
    /// from which, what order an entity's attributes appear in, and where its inverse
    /// attributes come from.
    /// <para>
    /// A STEP file records attributes positionally — <c>IFCEXTRUDEDAREASOLID(#12,#13,#14,1760.)</c>
    /// says nothing about which slot is <c>Depth</c>. Without this table you can read
    /// the file but not understand a word of it.
    /// </para>
    /// <para>
    /// The tables are generated from the official schemas rather than hand-written;
    /// see schemagen/generate.py. Treat the .schema files as build output.
    /// </para>
    /// </summary>
    public sealed class IfcSchema
    {
        private const string SchemaNameRecord = "S";
        private const string EntityRecord = "E";
        private const string InverseRecord = "V";
        private const string NoSupertype = "-";

        private sealed class EntityDefinition
        {
            public string Name;
            public string SupertypeName;
            public string[] OwnAttributes;
            public readonly List<(string Name, InverseLink Link)> OwnInverses
                = new List<(string, InverseLink)>();

            // Resolved lazily, once, on first use.
            public string[] AllAttributes;
            public Dictionary<string, int> AttributeIndex;
            public Dictionary<string, InverseLink> AllInverses;
            public HashSet<string> Ancestry;
        }

        private readonly Dictionary<string, EntityDefinition> _entities =
            new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<string>> _children =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string[]> _descendantCache =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public string Name { get; private set; }

        public static IfcSchema Parse(string text)
        {
            var schema = new IfcSchema();

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length > 0)
                {
                    schema.ParseLine(line);
                }
            }

            return schema;
        }

        public static IfcSchema Load(string path) => Parse(File.ReadAllText(path));

        private void ParseLine(string line)
        {
            string[] fields = line.Split('|');
            switch (fields[0])
            {
                case SchemaNameRecord:
                    Name = fields[1];
                    break;
                case EntityRecord:
                    AddEntity(fields);
                    break;
                case InverseRecord:
                    AddInverse(fields);
                    break;
            }
        }

        private void AddEntity(string[] fields)
        {
            var definition = new EntityDefinition
            {
                Name = fields[1],
                SupertypeName = fields[2] == NoSupertype ? null : fields[2],
                OwnAttributes = fields[3].Length == 0 ? Array.Empty<string>() : fields[3].Split(','),
            };
            _entities[definition.Name] = definition;

            if (definition.SupertypeName != null)
            {
                RegisterChild(definition.SupertypeName, definition.Name);
            }
        }

        private void RegisterChild(string supertypeName, string childName)
        {
            if (!_children.TryGetValue(supertypeName, out List<string> siblings))
            {
                siblings = new List<string>();
                _children[supertypeName] = siblings;
            }
            siblings.Add(childName);
        }

        private void AddInverse(string[] fields)
        {
            if (_entities.TryGetValue(fields[1], out EntityDefinition owner))
            {
                owner.OwnInverses.Add((fields[2], new InverseLink(fields[3], fields[4])));
            }
        }

        public bool IsKnown(string type) => _entities.ContainsKey(type);

        /// <summary>
        /// The schema's own spelling of a type name. STEP files shout their type
        /// names in upper case; the descent dispatches on canonical names like
        /// <c>IfcExtrudedAreaSolid</c>, and a case-sensitive dictionary lookup
        /// against IFCEXTRUDEDAREASOLID quietly finds nothing.
        /// </summary>
        public string Canonical(string type) =>
            _entities.TryGetValue(type, out EntityDefinition definition) ? definition.Name : type;

        private EntityDefinition Definition(string type)
        {
            _entities.TryGetValue(type, out EntityDefinition definition);
            return definition;
        }

        /// <summary>The definition's declared supertype, or null once the chain reaches the root.</summary>
        private EntityDefinition Supertype(EntityDefinition definition) =>
            definition.SupertypeName == null ? null : Definition(definition.SupertypeName);

        private void Resolve(EntityDefinition definition)
        {
            if (definition.AllAttributes != null)
            {
                return;
            }

            List<EntityDefinition> chain = InheritanceChain(definition);
            var attributes = new List<string>();
            var inverses = new Dictionary<string, InverseLink>(StringComparer.OrdinalIgnoreCase);
            var ancestry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (EntityDefinition step in chain)
            {
                attributes.AddRange(step.OwnAttributes);
                foreach ((string name, InverseLink link) in step.OwnInverses)
                {
                    inverses[name] = link;
                }
                ancestry.Add(step.Name);
            }

            definition.AllAttributes = attributes.ToArray();
            definition.AttributeIndex = BuildAttributeIndex(attributes);
            definition.AllInverses = inverses;
            definition.Ancestry = ancestry;
        }

        /// <summary>The definition and its ancestors, root first — attribute order runs down the chain.</summary>
        private List<EntityDefinition> InheritanceChain(EntityDefinition definition)
        {
            var chain = new List<EntityDefinition>();
            for (EntityDefinition step = definition; step != null; step = Supertype(step))
            {
                chain.Add(step);
            }
            chain.Reverse();
            return chain;
        }

        private static Dictionary<string, int> BuildAttributeIndex(List<string> attributes)
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < attributes.Count; i++)
            {
                index[attributes[i]] = i;
            }
            return index;
        }

        /// <summary>Look up a type's definition and lazily resolve its inherited attributes, if found.</summary>
        private EntityDefinition ResolvedDefinition(string type)
        {
            EntityDefinition definition = Definition(type);
            if (definition != null)
            {
                Resolve(definition);
            }
            return definition;
        }

        /// <summary>Attribute names in STEP order, inherited ones first.</summary>
        public IReadOnlyList<string> AttributeNames(string type) =>
            ResolvedDefinition(type)?.AllAttributes ?? Array.Empty<string>();

        /// <summary>Positional slot for a named attribute, or -1 if this type has no such attribute.</summary>
        public int AttributeIndex(string type, string attribute)
        {
            EntityDefinition definition = ResolvedDefinition(type);
            return definition != null && definition.AttributeIndex.TryGetValue(attribute, out int index)
                ? index
                : -1;
        }

        /// <summary>Look up the inverse-attribute link for <paramref name="attribute"/> on <paramref name="type"/>, if the schema declares one.</summary>
        public bool TryGetInverse(string type, string attribute, out InverseLink link)
        {
            EntityDefinition definition = ResolvedDefinition(type);
            if (definition == null)
            {
                link = default;
                return false;
            }
            return definition.AllInverses.TryGetValue(attribute, out link);
        }

        /// <summary>
        /// Does <paramref name="type"/> derive from <paramref name="ancestor"/>, or equal it?
        /// This is what backs <c>IfcEntity.IsA("IfcProduct")</c>.
        /// </summary>
        public bool IsSubtypeOf(string type, string ancestor)
        {
            EntityDefinition definition = ResolvedDefinition(type);
            return definition == null
                ? string.Equals(type, ancestor, StringComparison.OrdinalIgnoreCase)
                : definition.Ancestry.Contains(ancestor);
        }

        /// <summary>Names of the supertypes of <paramref name="type"/>, nearest first.</summary>
        public IEnumerable<string> Supertypes(string type)
        {
            EntityDefinition definition = Definition(type);
            if (definition == null)
            {
                yield break;
            }
            for (EntityDefinition step = Supertype(definition); step != null; step = Supertype(step))
            {
                yield return step.Name;
            }
        }

        /// <summary><paramref name="type"/> and everything deriving from it.</summary>
        public IReadOnlyList<string> TypeAndDescendants(string type)
        {
            if (_descendantCache.TryGetValue(type, out string[] cached))
            {
                return cached;
            }

            var collected = new List<string>();
            var pending = new Stack<string>();
            pending.Push(type);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                collected.Add(current);
                if (_children.TryGetValue(current, out List<string> kids))
                {
                    foreach (string kid in kids)
                    {
                        pending.Push(kid);
                    }
                }
            }

            string[] result = collected.ToArray();
            _descendantCache[type] = result;
            return result;
        }
    }
}