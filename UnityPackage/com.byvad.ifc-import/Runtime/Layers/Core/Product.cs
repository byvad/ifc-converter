// @author: Davy Bellens

using System;
using System.Collections.Generic;
using Conversion.Ifc;
using Conversion.Layers.Resource;

namespace Conversion.Layers.Core
{
    /// <summary>The result of descending through one product.</summary>
    public sealed class ProductGeometry
    {
        public IfcEntity Product { get; }
        public Mesh Mesh { get; internal set; }

        /// <summary>Item type names the Resource layer had no builder for.</summary>
        public readonly List<string> Unsupported = new List<string>();

        /// <summary>Representation identifiers passed over as not body geometry.</summary>
        public readonly List<string> SkippedRepresentations = new List<string>();

        public int ItemsBuilt { get; internal set; }
        public int OpeningsCut { get; internal set; }
        public int StyledBeforeMaterial { get; internal set; }
        public bool StyledByMaterial { get; internal set; }

        public ProductGeometry(IfcEntity product)
        {
            Product = product;
            Mesh = new Mesh();
        }

        public bool Styled => Mesh.Groups.Count > 0;
        public string Guid => Product?.String("GlobalId");
        public string Name => Product?.String("Name");
        public bool HasGeometry => Mesh.Triangles.Count > 0;
    }

    /// <summary>
    /// Product schema: resolving spatial placement and representation.
    /// <para>
    /// An instance rather than a free function, because the descent carries state
    /// worth keeping across products — the hole statistics, the appearance cache,
    /// and the material palette. One resolver per conversion; one per thread if
    /// you fan the work out.
    /// </para>
    /// </summary>
    public sealed class ProductResolver
    {
        /// <summary>Representations that carry the geometry we want. A null identifier
        /// is included: plenty of exporters leave it off entirely.</summary>
        public static readonly HashSet<string> MeshableIdentifiers =
            new HashSet<string> { "Body", "Facetation" };

        public static readonly HashSet<string> SkippedIdentifiers = new HashSet<string>
        {
            "Axis", "FootPrint", "Profile", "Box", "Annotation",
            "SurveyPoints", "Reference", "Clearance", "Lighting",
        };

        private readonly Builder _builder;
        private readonly Palette _palette;

        public ProductResolver(Builder builder = null, Palette palette = null)
        {
            _builder = builder ?? new Builder();
            _palette = palette;
        }

        public Builder Builder => _builder;
        public Palette Palette => _palette;
        public HoleStats Stats => _builder.Stats;

        /// <summary>Does this product carry a shape representation at all?</summary>
        public static bool HasShape(IfcEntity product)
        {
            IfcEntity representation = product?.Entity("Representation");
            return representation != null && representation.Entities("Representations").Count > 0;
        }

        /// <summary>Descend from a Core-layer product to a placed, cut, styled mesh.</summary>
        public ProductGeometry Resolve(IfcEntity product, bool includeOpenings = true)
        {
            var result = new ProductGeometry(product);

            if (!HasShape(product))
            {
                return result;
            }

            BuildRepresentations(product, result);
            ApplyPlacement(product, result);

            if (includeOpenings)
            {
                CutOpenings(product, result);
            }

            ApplyMaterial(product, result);

            return result;
        }

        private void BuildRepresentations(IfcEntity product, ProductGeometry result)
        {
            bool styles = _palette != null;

            foreach (IfcEntity shape in product.Entity("Representation").Entities("Representations"))
            {
                string identifier = shape.String("RepresentationIdentifier");

                if (IsSkippedRepresentation(identifier))
                {
                    result.SkippedRepresentations.Add(identifier);
                    continue;
                }

                BuildItems(shape, result, styles);
            }
        }

        private static bool IsSkippedRepresentation(string identifier) =>
            identifier != null &&
            (SkippedIdentifiers.Contains(identifier) || !MeshableIdentifiers.Contains(identifier));

        private void BuildItems(IfcEntity shape, ProductGeometry result, bool styles)
        {
            foreach (IfcEntity item in shape.Entities("Items"))
            {
                try
                {
                    result.Mesh.Extend(_builder.BuildItem(item, styles));
                    result.ItemsBuilt++;
                }
                catch (UnsupportedGeometryException exception)
                {
                    result.Unsupported.Add(exception.Message);
                }
                catch (Exception exception)
                {
                    // One malformed item should cost its own geometry, not the
                    // rest of the product's.
                    result.Unsupported.Add($"{item.IsA()}: {exception.Message}");
                }
            }
        }

        private static void ApplyPlacement(IfcEntity product, ProductGeometry result)
        {
            Matrix4 placement = Placement.LocalPlacementMatrix(product.Entity("ObjectPlacement"));
            result.Mesh = result.Mesh.Transformed(placement);
        }

        /// <summary>
        /// Openings are subtracted in world space: the void's own placement chain
        /// already runs through the host's, so both sides arrive in the same frame.
        /// This has to happen before the material fill, because the boolean rebuilds
        /// the triangle list and the fill claims whatever is left unstyled.
        /// </summary>
        private void CutOpenings(IfcEntity product, ProductGeometry result)
        {
            var cutters = new List<Mesh>();
            foreach (IfcEntity opening in Openings.Of(product))
            {
                ProductGeometry voidGeometry = Resolve(opening, includeOpenings: false);
                if (voidGeometry.HasGeometry)
                {
                    cutters.Add(voidGeometry.Mesh);
                }
            }
            if (cutters.Count == 0)
            {
                return;
            }
            result.Mesh = MeshBoolean.Subtract(result.Mesh, cutters);
            result.OpeningsCut = cutters.Count;
        }

        private void ApplyMaterial(IfcEntity product, ProductGeometry result)
        {
            if (_palette == null)
            {
                return;
            }
            result.StyledBeforeMaterial = result.Mesh.Groups.Count;
            result.Mesh.FillStyle(_palette.ProductRgba(product));
            result.StyledByMaterial = result.Mesh.Groups.Count > result.StyledBeforeMaterial;
        }
    }
}