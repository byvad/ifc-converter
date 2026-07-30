using System;
using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers.Resource
{
    /// <summary>Resource layer: Presentation Appearance and Material schemas.</summary>
    public sealed class Appearance
    {
        public static readonly Rgba UnstyledColour = new Rgba(0.78, 0.78, 0.78, 1.0);
        public const string UnstyledName = "ifc_unstyled";

        private const int MaxMaterialDepth = 6;

        private static Rgba? RgbOf(IfcEntity colour)
        {
            if (colour == null || !colour.IsA("IfcColourRgb"))
            {
                return null;
            }
            if (!colour.TryDouble("Red", out double r) ||
                !colour.TryDouble("Green", out double g) ||
                !colour.TryDouble("Blue", out double b))
            {
                return null;
            }
            return new Rgba(r, g, b, 1.0);
        }

        public static Rgba? SurfaceStyleRgba(IfcEntity style)
        {
            if (style == null || !style.IsA("IfcSurfaceStyle"))
            {
                return null;
            }

            foreach (IfcEntity shading in style.Entities("Styles"))
            {
                // IfcSurfaceStyleRendering derives from IfcSurfaceStyleShading, so
                // the supertype test catches both.
                if (!shading.IsA("IfcSurfaceStyleShading"))
                {
                    continue;
                }

                Rgba? rgb = RgbOf(shading.Entity("SurfaceColour"));
                if (!rgb.HasValue)
                {
                    continue;
                }

                double alpha = 1.0;
                if (shading.TryDouble("Transparency", out double transparency))
                {
                    alpha = 1.0 - transparency;
                }
                alpha = Math.Max(0.0, Math.Min(1.0, alpha));

                return new Rgba(rgb.Value.R, rgb.Value.G, rgb.Value.B, alpha);
            }
            return null;
        }

        public static Rgba? StyledItemRgba(IfcEntity styledItem)
        {
            if (styledItem == null)
            {
                return null;
            }

            foreach (IfcEntity entry in styledItem.Entities("Styles"))
            {
                if (entry.IsA("IfcPresentationStyleAssignment"))
                {
                    foreach (IfcEntity inner in entry.Entities("Styles"))
                    {
                        Rgba? nested = SurfaceStyleRgba(inner);
                        if (nested.HasValue)
                        {
                            return nested;
                        }
                    }
                }
                else
                {
                    Rgba? direct = SurfaceStyleRgba(entry);
                    if (direct.HasValue)
                    {
                        return direct;
                    }
                }
            }
            return null;
        }

        /// <summary>The style attached directly to a representation item, if any.</summary>
        public Rgba? ItemRgba(IfcEntity item)
        {
            if (item == null)
            {
                return null;
            }
            foreach (IfcEntity styledItem in item.Inverse("StyledByItem"))
            {
                Rgba? rgba = StyledItemRgba(styledItem);
                if (rgba.HasValue)
                {
                    return rgba;
                }
            }
            return null;
        }

        private static readonly Dictionary<string, string> SetLists = new Dictionary<string, string>
        {
            { "IfcMaterialLayerSet", "MaterialLayers" },
            { "IfcMaterialList", "Materials" },
            { "IfcMaterialProfileSet", "MaterialProfiles" },
            { "IfcMaterialConstituentSet", "MaterialConstituents" },
        };

        private static readonly Dictionary<string, string> Usages = new Dictionary<string, string>
        {
            { "IfcMaterialLayerSetUsage", "ForLayerSet" },
            { "IfcMaterialProfileSetUsage", "ForProfileSet" },
        };

        private static readonly HashSet<string> Holders = new HashSet<string>
        {
            "IfcMaterialLayer", "IfcMaterialProfile", "IfcMaterialConstituent",
        };

        /// <summary>Flatten whatever material structure a product points at into plain materials.</summary>
        public static List<IfcEntity> MaterialsOf(IfcEntity definition, int depth = 0)
        {
            var result = new List<IfcEntity>();
            if (definition == null || depth > MaxMaterialDepth)
            {
                return result;
            }

            string name = definition.IsA();

            if (name == "IfcMaterial")
            {
                result.Add(definition);
                return result;
            }

            if (Usages.TryGetValue(name, out string usageAttribute))
            {
                result.AddRange(MaterialsOf(definition.Entity(usageAttribute), depth + 1));
                return result;
            }

            if (SetLists.TryGetValue(name, out string listAttribute))
            {
                foreach (IfcEntity child in definition.Entities(listAttribute))
                {
                    result.AddRange(MaterialsOf(child, depth + 1));
                }
                return result;
            }

            if (Holders.Contains(name))
            {
                result.AddRange(MaterialsOf(definition.Entity("Material"), depth + 1));
            }
            return result;
        }
    }

    /// <summary>
    /// One colour index per model: materials resolved once, products cached on demand.
    /// </summary>
    public sealed class Palette
    {
        private readonly double _minAlpha;
        private readonly bool _linear;
        private readonly Dictionary<int, Rgba> _materialRgba = new Dictionary<int, Rgba>();
        private readonly Dictionary<int, Rgba?> _productRgba = new Dictionary<int, Rgba?>();
        private readonly Dictionary<string, Rgba> _registered =
            new Dictionary<string, Rgba>(StringComparer.Ordinal);

        /// <param name="minAlpha">Floor on opacity. IFC glazing is routinely authored at
        /// Transparency 1.0, which resolves to an invisible surface.</param>
        /// <param name="linear">Convert from sRGB to linear when writing out.</param>
        public Palette(IfcModel model, double minAlpha = 0.0, bool linear = false)
        {
            _minAlpha = minAlpha;
            _linear = linear;
            Index(model);
        }

        public IReadOnlyDictionary<string, Rgba> Materials => _registered;

        private void Index(IfcModel model)
        {
            foreach (IfcEntity definition in model.ByType("IfcMaterialDefinitionRepresentation"))
            {
                IfcEntity material = definition.Entity("RepresentedMaterial");
                if (material == null)
                {
                    continue;
                }
                Rgba? rgba = FirstStyledRgba(definition);
                if (rgba.HasValue && !_materialRgba.ContainsKey(material.Id))
                {
                    _materialRgba[material.Id] = rgba.Value;
                }
            }
        }

        /// <summary>The first styled item's colour found across this definition's representations, in document order.</summary>
        private static Rgba? FirstStyledRgba(IfcEntity definition)
        {
            foreach (IfcEntity representation in definition.Entities("Representations"))
            {
                foreach (IfcEntity item in representation.Entities("Items"))
                {
                    if (!item.IsA("IfcStyledItem"))
                    {
                        continue;
                    }
                    Rgba? rgba = Appearance.StyledItemRgba(item);
                    if (rgba.HasValue)
                    {
                        return rgba;
                    }
                }
            }
            return null;
        }

        /// <summary>The colour a product inherits from its associated material.</summary>
        public Rgba? ProductRgba(IfcEntity product)
        {
            if (_productRgba.TryGetValue(product.Id, out Rgba? cached))
            {
                return cached;
            }

            Rgba? resolved = FindMaterialRgba(product);
            _productRgba[product.Id] = resolved;
            return resolved;
        }

        private Rgba? FindMaterialRgba(IfcEntity product)
        {
            foreach (IfcEntity association in product.Inverse("HasAssociations"))
            {
                if (!association.IsA("IfcRelAssociatesMaterial"))
                {
                    continue;
                }
                foreach (IfcEntity material in Appearance.MaterialsOf(association.Entity("RelatingMaterial")))
                {
                    if (_materialRgba.TryGetValue(material.Id, out Rgba hit))
                    {
                        return hit;
                    }
                }
            }
            return null;
        }

        public string Unstyled()
        {
            if (!_registered.ContainsKey(Appearance.UnstyledName))
            {
                _registered[Appearance.UnstyledName] = Appearance.UnstyledColour;
            }
            return Appearance.UnstyledName;
        }

        /// <summary>Intern a colour and return its stable name, matching the Python's ifc_RRGGBBAA.</summary>
        public string Register(Rgba? rgba)
        {
            if (!rgba.HasValue)
            {
                return null;
            }

            Rgba value = rgba.Value;
            double a = _minAlpha > 0.0 ? Math.Max(value.A, _minAlpha) : value.A;

            double Clamp(double c) => Math.Max(0.0, Math.Min(1.0, c));
            var clamped = new Rgba(Clamp(value.R), Clamp(value.G), Clamp(value.B), Clamp(a));

            string name = clamped.HexName();

            if (!_registered.ContainsKey(name))
            {
                _registered[name] = clamped;
            }
            return name;
        }

        public static double SrgbToLinear(double c) =>
            c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        /// <summary>
        /// Write a Wavefront .mtl. Not needed for Unity, but it keeps the OBJ path
        /// alive and makes the C# output diffable against the Python's.
        /// </summary>
        public int WriteMtl(string path, string source = "")
        {
            var names = new List<string>(_registered.Keys);
            names.Sort(StringComparer.Ordinal);

            using (var writer = new System.IO.StreamWriter(path))
            {
                writer.Write("# materials resolved from " + source + "\n");
                writer.Write("# IfcSurfaceStyleRendering -> Kd, Transparency -> d\n");
                foreach (string name in names)
                {
                    Rgba c = _registered[name];
                    double r = c.R, g = c.G, b = c.B;
                    if (_linear)
                    {
                        r = SrgbToLinear(r);
                        g = SrgbToLinear(g);
                        b = SrgbToLinear(b);
                    }
                    writer.Write("\nnewmtl " + name + "\n");
                    writer.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Kd {0:F6} {1:F6} {2:F6}\n", r, g, b));
                    writer.Write("Ka 0.000000 0.000000 0.000000\n");
                    writer.Write("Ks 0.000000 0.000000 0.000000\n");
                    writer.Write("Ns 10.0\n");
                    writer.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "d {0:F4}\n", c.A));
                    writer.Write("illum " + (c.A < 0.999 ? 4 : 2) + "\n");
                }
            }
            return _registered.Count;
        }
    }
}
