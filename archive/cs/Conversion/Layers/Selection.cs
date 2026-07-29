using System;
using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers
{
    /// <summary>Products chosen for conversion, grouped by conceptual layer.</summary>
    public sealed class ProductSelection
    {
        public readonly List<(IfcEntity Product, Layer Layer)> Entries =
            new List<(IfcEntity, Layer)>();

        /// <summary>Entity names with no layer mapping, in first-seen order.</summary>
        public readonly List<string> Unclassified = new List<string>();

        public int Count => Entries.Count;

        public void Add(IfcEntity product, Layer layer)
        {
            Entries.Add((product, layer));
            if (layer == null)
            {
                string name = product.IsA();
                if (!Unclassified.Contains(name))
                {
                    Unclassified.Add(name);
                }
            }
        }

        /// <summary>Counts by layer name, for reporting.</summary>
        public Dictionary<string, int> ByLayer()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((IfcEntity _, Layer layer) in Entries)
            {
                string key = layer?.LayerName ?? "Unclassified";
                counts.TryGetValue(key, out int existing);
                counts[key] = existing + 1;
            }
            return counts;
        }

        public Dictionary<string, int> BySchema()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((IfcEntity _, Layer layer) in Entries)
            {
                string key = layer == null
                    ? "Unclassified"
                    : $"{layer.LayerName}/{layer.LayerType}";
                counts.TryGetValue(key, out int existing);
                counts[key] = existing + 1;
            }
            return counts;
        }
    }

    /// <summary>
    /// Domain and Interoperability layers: where the descent starts.
    /// <para>
    /// These are the top two layers, and between them they hold every entity a person
    /// would call "a thing in the building": Interoperability carries the elements
    /// shared across disciplines — walls, slabs, doors, windows, furniture — while
    /// Domain carries the discipline-specific equipment.
    /// </para>
    /// <para>
    /// Neither layer knows anything about geometry. Their job here is selection: work
    /// out which products to convert, and which conceptual schema each belongs to.
    /// The moment a product is chosen, the descent hands off to the Core layer.
    /// </para>
    /// </summary>
    public sealed class Selection
    {
        /// <summary>
        /// Openings carry real geometry — the void volume — but must not be drawn,
        /// and spaces are volumes of air. Both are Core/Product-schema entities
        /// rather than Domain or Interoperability products, but they turn up in
        /// by-type sweeps.
        /// </summary>
        public static readonly HashSet<string> NonVisual = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IfcOpeningElement", "IfcSpace", "IfcVoidingFeature",
            "IfcGrid", "IfcAnnotation",
        };

        private readonly Classification _classification;

        public Selection(Classification classification)
        {
            _classification = classification;
        }

        /// <summary>
        /// Choose which products to convert.
        /// <para>
        /// With no filters, every IfcProduct is selected. Filtering by layer is only
        /// meaningful because of the dependency rule: an entity's layer is fixed by
        /// the schema, not by the file.
        /// </para>
        /// </summary>
        /// <param name="layers">Restrict to conceptual layers, e.g. {"Domain"}.</param>
        /// <param name="schemas">Restrict to schemas, e.g. {"HVAC", "Shared Building"}.</param>
        /// <param name="classes">Restrict to entity names, e.g. {"IfcWall"}.</param>
        public ProductSelection Select(
            IfcModel model,
            ICollection<string> layers = null,
            ICollection<string> schemas = null,
            ICollection<string> classes = null)
        {
            var selection = new ProductSelection();

            foreach (IfcEntity product in model.ByType("IfcProduct"))
            {
                string name = product.IsA();

                if (NonVisual.Contains(name))
                {
                    continue;
                }
                if (classes != null && classes.Count > 0 && !classes.Contains(name))
                {
                    continue;
                }

                Layer layer = _classification.ClassifyInstance(product);

                if (layers != null && layers.Count > 0)
                {
                    if (layer == null || !layers.Contains(layer.LayerName))
                    {
                        continue;
                    }
                }
                if (schemas != null && schemas.Count > 0)
                {
                    if (layer == null || !schemas.Contains(layer.LayerType))
                    {
                        continue;
                    }
                }

                selection.Add(product, layer);
            }

            return selection;
        }

        /// <summary>Human-readable "IfcWall [Interoperability/Shared Building]".</summary>
        public string Describe(IfcEntity product)
        {
            Layer layer = _classification.ClassifyInstance(product);
            return layer == null
                ? $"{product.IsA()} [unclassified]"
                : $"{product.IsA()} [{layer.LayerName}/{layer.LayerType}]";
        }

        /// <summary>Only Domain-layer products: the discipline-specific equipment.</summary>
        public ProductSelection DisciplineProducts(IfcModel model) =>
            Select(model, layers: new[] { "Domain" });

        /// <summary>Only Interoperability-layer products: the shared building elements.</summary>
        public ProductSelection SharedProducts(IfcModel model) =>
            Select(model, layers: new[] { "Interoperability" });
    }
}
