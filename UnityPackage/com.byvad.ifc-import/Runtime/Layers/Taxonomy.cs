// @author: Davy Bellens

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
        public const string DomainName = "Domain";
        public const string InteroperabilityName = "Interoperability";
        public const string CoreName = "Core";
        public const string ResourceName = "Resource";

        public static readonly HashSet<string> DomainSchemas = new()
        {
            "Building Controls", "Plumbing FireProtections", "Structural Elements",
            "Structural Analysis", "HVAC", "Electrical", "Architecture",
            "Construction Management",
        };

        public static readonly HashSet<string> InteroperabilitySchemas = new()
        {
            "Shared Bldg Services", "Shared Components", "Shared Building",
            "Shared Management", "Shared Facilities",
        };

        public static readonly HashSet<string> CoreSchemas = new()
        {
            "Control", "Product", "Process", "Kernel",
        };

        public static readonly HashSet<string> ResourceSchemas = new()
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
            DomainName, InteroperabilityName, CoreName, ResourceName,
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
        public DomainLayer(string schema) : base(Taxonomy.DomainName, Taxonomy.DomainSchemas, schema)
        {
        }
    }

    public sealed class InteroperabilityLayer : Layer
    {
        public InteroperabilityLayer(string schema)
            : base(Taxonomy.InteroperabilityName, Taxonomy.InteroperabilitySchemas, schema)
        {
        }
    }

    public sealed class CoreLayer : Layer
    {
        public CoreLayer(string schema) : base(Taxonomy.CoreName, Taxonomy.CoreSchemas, schema)
        {
        }
    }

    public sealed class ResourceLayer : Layer
    {
        public ResourceLayer(string schema) : base(Taxonomy.ResourceName, Taxonomy.ResourceSchemas, schema)
        {
        }
    }
}