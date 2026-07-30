// @author: Davy Bellens

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
            new(StringComparer.OrdinalIgnoreCase);

        public int Count => _entities.Count;

        /// <summary>
        /// Build from the taxonomy JSON documents — the same four files the Python
        /// package reads out of its schemas directory.
        /// </summary>
        public static Classification FromJson(IEnumerable<string> documents)
        {
            var classification = new Classification();

            foreach (string document in documents)
            {
                if (!string.IsNullOrWhiteSpace(document))
                {
                    classification.AddDocument(document);
                }
            }

            return classification;
        }

        private void AddDocument(string document)
        {
            Dictionary<string, object> root = MiniJson.ParseObject(document);
            if (!TryGetLayerName(root, out string layerName) || !TryGetSchemas(root, out var schemas))
            {
                return;
            }

            foreach (KeyValuePair<string, object> entry in schemas)
            {
                RegisterEntities(entry.Key, layerName, entry.Value);
            }
        }

        private static bool TryGetLayerName(Dictionary<string, object> root, out string layerName)
        {
            layerName = null;
            if (root == null || !root.TryGetValue("layer", out object layerValue))
            {
                return false;
            }
            layerName = layerValue as string;
            return layerName != null && IsKnownLayer(layerName);
        }

        private static bool TryGetSchemas(Dictionary<string, object> root, out Dictionary<string, object> schemas)
        {
            if (root.TryGetValue("schemas", out object schemasValue) &&
                schemasValue is Dictionary<string, object> found)
            {
                schemas = found;
                return true;
            }
            schemas = null;
            return false;
        }

        private void RegisterEntities(string schema, string layerName, object namesValue)
        {
            if (namesValue is not List<object> names)
            {
                return;
            }
            foreach (object name in names)
            {
                if (name is string entity)
                {
                    _entities[entity] = (schema, layerName);
                }
            }
        }

        /// <summary>
        /// The complete set of valid layer names and how to build each one — the single
        /// place that lists them, so <see cref="IsKnownLayer"/> and <see cref="Build"/>
        /// can never disagree about which layers exist.
        /// </summary>
        private static readonly Dictionary<string, Func<string, Layer>> LayerFactories =
            new(StringComparer.Ordinal)
            {
                [Taxonomy.DomainName] = schema => new DomainLayer(schema),
                [Taxonomy.InteroperabilityName] = schema => new InteroperabilityLayer(schema),
                [Taxonomy.CoreName] = schema => new CoreLayer(schema),
                [Taxonomy.ResourceName] = schema => new ResourceLayer(schema),
            };

        private static bool IsKnownLayer(string layerName) => LayerFactories.ContainsKey(layerName);

        private static Layer Build(string layerName, string schema) =>
            LayerFactories.TryGetValue(layerName, out Func<string, Layer> factory) ? factory(schema) : null;

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