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
                if (line.Length == 0)
                {
                    continue;
                }

                string[] fields = line.Split('|');
                switch (fields[0])
                {
                    case "S":
                        schema.Name = fields[1];
                        break;

                    case "E":
                    {
                        var definition = new EntityDefinition
                        {
                            Name = fields[1],
                            SupertypeName = fields[2] == "-" ? null : fields[2],
                            OwnAttributes = fields[3].Length == 0
                                ? Array.Empty<string>()
                                : fields[3].Split(','),
                        };
                        schema._entities[definition.Name] = definition;

                        if (definition.SupertypeName != null)
                        {
                            if (!schema._children.TryGetValue(definition.SupertypeName, out List<string> siblings))
                            {
                                siblings = new List<string>();
                                schema._children[definition.SupertypeName] = siblings;
                            }
                            siblings.Add(definition.Name);
                        }
                        break;
                    }

                    case "V":
                        if (schema._entities.TryGetValue(fields[1], out EntityDefinition owner))
                        {
                            owner.OwnInverses.Add((fields[2], new InverseLink(fields[3], fields[4])));
                        }
                        break;
                }
            }

            return schema;
        }

        public static IfcSchema Load(string path) => Parse(File.ReadAllText(path));

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

        private static void Resolve(IfcSchema schema, EntityDefinition definition)
        {
            if (definition.AllAttributes != null)
            {
                return;
            }

            var chain = new List<EntityDefinition>();
            for (EntityDefinition step = definition; step != null;
                 step = step.SupertypeName == null ? null : schema.Definition(step.SupertypeName))
            {
                chain.Add(step);
            }
            chain.Reverse();   // root first: attribute order runs down the chain

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

            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < attributes.Count; i++)
            {
                index[attributes[i]] = i;
            }

            definition.AllAttributes = attributes.ToArray();
            definition.AttributeIndex = index;
            definition.AllInverses = inverses;
            definition.Ancestry = ancestry;
        }

        /// <summary>Attribute names in STEP order, inherited ones first.</summary>
        public IReadOnlyList<string> AttributeNames(string type)
        {
            EntityDefinition definition = Definition(type);
            if (definition == null)
            {
                return Array.Empty<string>();
            }
            Resolve(this, definition);
            return definition.AllAttributes;
        }

        /// <summary>Positional slot for a named attribute, or -1 if this type has no such attribute.</summary>
        public int AttributeIndex(string type, string attribute)
        {
            EntityDefinition definition = Definition(type);
            if (definition == null)
            {
                return -1;
            }
            Resolve(this, definition);
            return definition.AttributeIndex.TryGetValue(attribute, out int index) ? index : -1;
        }

        public bool TryGetInverse(string type, string attribute, out InverseLink link)
        {
            EntityDefinition definition = Definition(type);
            if (definition == null)
            {
                link = default;
                return false;
            }
            Resolve(this, definition);
            return definition.AllInverses.TryGetValue(attribute, out link);
        }

        /// <summary>
        /// Does <paramref name="type"/> derive from <paramref name="ancestor"/>, or equal it?
        /// This is what backs <c>IfcEntity.IsA("IfcProduct")</c>.
        /// </summary>
        public bool IsSubtypeOf(string type, string ancestor)
        {
            EntityDefinition definition = Definition(type);
            if (definition == null)
            {
                return string.Equals(type, ancestor, StringComparison.OrdinalIgnoreCase);
            }
            Resolve(this, definition);
            return definition.Ancestry.Contains(ancestor);
        }

        /// <summary>Names of the supertypes of <paramref name="type"/>, nearest first.</summary>
        public IEnumerable<string> Supertypes(string type)
        {
            EntityDefinition definition = Definition(type);
            if (definition == null)
            {
                yield break;
            }
            for (EntityDefinition step = definition.SupertypeName == null
                     ? null : Definition(definition.SupertypeName);
                 step != null;
                 step = step.SupertypeName == null ? null : Definition(step.SupertypeName))
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
