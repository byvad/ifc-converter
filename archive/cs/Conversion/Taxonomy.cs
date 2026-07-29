using System;
using System.Collections.Generic;

namespace Conversion.Ifc
{
    /// <summary>
    /// A conceptual layer, holding the set of schemas legal within it.
    /// <para>
    /// The Python names these Domain / InterOperability / Core / Resource. Here they
    /// carry a Layer suffix: <c>Core</c> and <c>Resource</c> would otherwise collide
    /// with the Conversion.Layers.Core and Conversion.Layers.Resource namespaces, and
    /// C# resolves that collision in ways nobody enjoys debugging.
    /// </para>
    /// </summary>
    public abstract class Layer
    {
        public string LayerName { get; }
        public string LayerType { get; }

        protected Layer(string layerName, ICollection<string> validTypes, string layerType)
        {
            if (!validTypes.Contains(layerType))
            {
                throw new ArgumentException($"Invalid {layerName} Layer Schema: '{layerType}'");
            }
            LayerName = layerName;
            LayerType = layerType;
        }

        public override string ToString() => $"{GetType().Name}('{LayerType}')";
    }

    public static class Taxonomy
    {
        public static readonly HashSet<string> DomainSchemas = new HashSet<string>
        {
            "Building Controls", "Plumbing FireProtections", "Structural Elements",
            "Structural Analysis", "HVAC", "Electrical", "Architecture",
            "Construction Management",
        };

        public static readonly HashSet<string> InteroperabilitySchemas = new HashSet<string>
        {
            "Shared Bldg Services", "Shared Components", "Shared Building",
            "Shared Management", "Shared Facilities",
        };

        public static readonly HashSet<string> CoreSchemas = new HashSet<string>
        {
            "Control", "Product", "Process", "Kernel",
        };

        public static readonly HashSet<string> ResourceSchemas = new HashSet<string>
        {
            "DateTime", "Material", "External Reference", "Geometric Constraint",
            "Geometric Model", "Geometry", "Actor", "Profile", "Property", "Quantity",
            "Topology", "Utility", "Measure", "Presentation Appearance",
            "Presentation Definition", "Presentation Organization", "Representation",
            "Constraint", "Approval", "Structural Load", "Cost",
        };

        /// <summary>
        /// Descent order. References in IFC only ever point downward, so a product
        /// in a higher layer resolves through the ones beneath it and never the reverse.
        /// </summary>
        public static readonly string[] LayerOrder =
        {
            "Domain", "Interoperability", "Core", "Resource",
        };

        /// <summary>Position in the hierarchy. Lower index means higher layer.</summary>
        public static int LayerIndex(Layer layer)
        {
            if (layer == null)
            {
                return LayerOrder.Length;
            }
            return Array.IndexOf(LayerOrder, layer.LayerName);
        }
    }

    public sealed class DomainLayer : Layer
    {
        public DomainLayer(string schema) : base("Domain", Taxonomy.DomainSchemas, schema)
        {
        }
    }

    public sealed class InteroperabilityLayer : Layer
    {
        public InteroperabilityLayer(string schema)
            : base("Interoperability", Taxonomy.InteroperabilitySchemas, schema)
        {
        }
    }

    public sealed class CoreLayer : Layer
    {
        public CoreLayer(string schema) : base("Core", Taxonomy.CoreSchemas, schema)
        {
        }
    }

    public sealed class ResourceLayer : Layer
    {
        public ResourceLayer(string schema) : base("Resource", Taxonomy.ResourceSchemas, schema)
        {
        }
    }
}
