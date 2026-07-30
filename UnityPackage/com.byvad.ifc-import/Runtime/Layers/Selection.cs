// @author: Davy Bellens

namespace Conversion.Layers
{
    /// <summary>Products chosen for conversion, grouped by conceptual layer.</summary>
    public sealed class ProductSelection
    {
        public readonly List<(IfcEntity Product, Layer Layer)> Entries = new();

        /// <summary>Entity names with no layer mapping, in first-seen order.</summary>
        public readonly List<string> Unclassified = new();

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

        private const string UnclassifiedKey = "Unclassified";

        /// <summary>Counts by layer name, for reporting.</summary>
        public Dictionary<string, int> ByLayer() =>
            CountBy(layer => layer?.LayerName ?? UnclassifiedKey);

        public Dictionary<string, int> BySchema() =>
            CountBy(layer => layer == null ? UnclassifiedKey : $"{layer.LayerName}/{layer.LayerType}");

        private Dictionary<string, int> CountBy(Func<Layer, string> keySelector)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((IfcEntity _, Layer layer) in Entries)
            {
                string key = keySelector(layer);
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
        public static readonly HashSet<string> NonVisual = new(StringComparer.OrdinalIgnoreCase)
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
                if (Admits(product, layers, schemas, classes, out Layer layer))
                {
                    selection.Add(product, layer);
                }
            }

            return selection;
        }

        private bool Admits(IfcEntity product, ICollection<string> layers, ICollection<string> schemas,
            ICollection<string> classes, out Layer layer)
        {
            string name = product.IsA();
            layer = null;

            if (NonVisual.Contains(name) || ExcludedBy(classes, name))
            {
                return false;
            }

            layer = _classification.ClassifyInstance(product);
            return !ExcludedBy(layers, layer?.LayerName) && !ExcludedBy(schemas, layer?.LayerType);
        }

        /// <summary>An active, non-empty filter that either has no value to check against or doesn't contain it.</summary>
        private static bool ExcludedBy(ICollection<string> filter, string value) =>
            filter != null && filter.Count > 0 && (value == null || !filter.Contains(value));

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
            Select(model, layers: new[] { Taxonomy.DomainName });

        /// <summary>Only Interoperability-layer products: the shared building elements.</summary>
        public ProductSelection SharedProducts(IfcModel model) =>
            Select(model, layers: new[] { Taxonomy.InteroperabilityName });
    }
}
