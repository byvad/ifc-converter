using System;
using System.Collections.Generic;

namespace Conversion.Ifc
{
    /// <summary>
    /// The IFC conceptual layer taxonomy.
    /// <para>
    /// The four layers form a dependency hierarchy, not a data flow. An entity in a
    /// given layer may reference entities in its own layer or any layer below it, but
    /// never above. Domain sits on top; Resource sits at the bottom.
    /// </para>
    /// <code>
    /// Domain            IfcAirTerminal, IfcLightFixture, IfcPile
    /// Interoperability  IfcWall, IfcSlab, IfcDoor, IfcFurniture
    /// Core              IfcProduct, IfcRelAggregates, IfcBuildingStorey
    /// Resource          IfcCartesianPoint, IfcExtrudedAreaSolid, IfcPolyline
    /// </code>
    /// <para>
    /// Because references only point downward, resolving a product's geometry is
    /// always a descent: start at a Domain or Interoperability product, pass through
    /// Core to reach its placement and representation, and bottom out in Resource
    /// where the actual coordinates live.
    /// </para>
    /// </summary>
    public sealed class Classification
    {
        private readonly Dictionary<string, (string Schema, string LayerName)> _entities =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Build from the taxonomy JSON documents — the same four files the Python
        /// package reads out of its schemas directory.
        /// </summary>
        public static Classification FromJson(IEnumerable<string> documents)
        {
            var classification = new Classification();

            foreach (string document in documents)
            {
                if (string.IsNullOrWhiteSpace(document))
                {
                    continue;
                }

                Dictionary<string, object> root = MiniJson.ParseObject(document);
                if (root == null || !root.TryGetValue("layer", out object layerValue))
                {
                    continue;
                }

                string layerName = layerValue as string;
                if (layerName == null || !IsKnownLayer(layerName))
                {
                    continue;
                }

                if (!root.TryGetValue("schemas", out object schemasValue) ||
                    !(schemasValue is Dictionary<string, object> schemas))
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> entry in schemas)
                {
                    if (!(entry.Value is List<object> names))
                    {
                        continue;
                    }
                    foreach (object name in names)
                    {
                        if (name is string entity)
                        {
                            classification._entities[entity] = (entry.Key, layerName);
                        }
                    }
                }
            }

            return classification;
        }

        public int Count => _entities.Count;

        private static bool IsKnownLayer(string layerName) =>
            layerName == "Domain" || layerName == "InterOperability"
            || layerName == "Core" || layerName == "Resource";

        private static Layer Build(string layerName, string schema)
        {
            switch (layerName)
            {
                case "Domain": return new DomainLayer(schema);
                case "InterOperability": return new InteroperabilityLayer(schema);
                case "Core": return new CoreLayer(schema);
                case "Resource": return new ResourceLayer(schema);
                default: return null;
            }
        }

        /// <summary>
        /// The layer for an IFC entity name, or null.
        /// <para>
        /// Falling back through the IFC inheritance chain is deliberately avoided
        /// here: an unknown entity should announce itself rather than be silently
        /// filed under its parent's layer.
        /// </para>
        /// </summary>
        public Layer Classify(string entityName)
        {
            if (entityName == null || !_entities.TryGetValue(entityName, out var hit))
            {
                return null;
            }
            return Build(hit.LayerName, hit.Schema);
        }

        /// <summary>
        /// Classify a concrete instance, walking up its supertypes.
        /// <para>
        /// Unlike <see cref="Classify"/>, this walks the inheritance chain, because
        /// an instance genuinely inherits its supertype's layer.
        /// </para>
        /// </summary>
        public Layer ClassifyInstance(IfcEntity instance)
        {
            if (instance == null)
            {
                return null;
            }

            Layer direct = Classify(instance.Type);
            if (direct != null)
            {
                return direct;
            }

            foreach (string parent in instance.Model.Schema.Supertypes(instance.Type))
            {
                Layer found = Classify(parent);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
