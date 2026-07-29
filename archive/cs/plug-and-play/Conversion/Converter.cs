using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Conversion.Ifc;
using Conversion.Layers;
using Conversion.Layers.Core;
using Conversion.Layers.Resource;

namespace Conversion
{
    public sealed class ConversionOptions
    {
        public string SourcePath;
        public string TargetPath;

        public ICollection<string> Layers;
        public ICollection<string> Schemas;
        public ICollection<string> Classes;

        public bool ToMetres = true;

        /// <summary>"z" keeps IFC's native up axis; "y" rotates for viewers that assume it.</summary>
        public string UpAxis = "z";

        public bool Colour = true;

        /// <summary>Floor on opacity. IFC glazing is often authored at Transparency 1.0.</summary>
        public double MinAlpha;

        public bool Linear;

        public Action<int, int, IfcEntity> Progress;
        public Func<bool> Cancelled;
    }

    /// <summary>
    /// IFC to OBJ, walking the conceptual layers by hand.
    /// <para>
    /// The descent, in full: selection picks the Domain / Interoperability products;
    /// Core resolves each one's placement, representation and voids; Resource turns
    /// the representation items into coordinates. Appearance rides along the same
    /// descent rather than as a second pass — Resource tags each item from its
    /// IfcStyledItem, Core fills the remainder from the product's material, and the
    /// writer turns those spans into usemtl runs.
    /// </para>
    /// <para>
    /// OBJ is not the destination any more, but keeping this path alive is what makes
    /// the C# port checkable: the output diffs directly against the Python's.
    /// </para>
    /// </summary>
    public static class Converter
    {
        private static readonly Regex Unsafe = new Regex(@"[^\w.-]+", RegexOptions.Compiled);

        public static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unnamed";
            }
            string cleaned = Unsafe.Replace(name.Trim(), "_");
            return cleaned.Length == 0 ? "unnamed" : cleaned;
        }

        /// <summary>output/&lt;stem&gt;/&lt;stem&gt;.obj — the .mtl lands beside it.</summary>
        public static string OutputObjPath(string ifcPath, string outputRoot)
        {
            string stem = Sanitise(Path.GetFileNameWithoutExtension(ifcPath));
            return Path.Combine(outputRoot, stem, stem + ".obj");
        }

        public static ConversionReport Convert(
            ConversionOptions options, IfcSchemaRegistry registry, Classification classification)
        {
            if (!File.Exists(options.SourcePath))
            {
                throw new FileNotFoundException(options.SourcePath);
            }

            string parent = Path.GetDirectoryName(Path.GetFullPath(options.TargetPath));
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            IfcModel model = StepParser.Load(options.SourcePath, registry);
            var report = new ConversionReport();

            // Resource layer, Measure schema: how big is one length unit?
            double scale = options.ToMetres ? Units.LengthScale(model) : 1.0;
            report.UnitScale = scale;
            report.UpAxis = options.UpAxis;
            report.Colour = options.Colour;

            // Resource layer, Presentation Appearance + Material: one index per model.
            Palette palette = options.Colour
                ? new Palette(model, options.MinAlpha, options.Linear)
                : null;
            string mtlPath = options.Colour
                ? Path.ChangeExtension(options.TargetPath, ".mtl")
                : null;

            var builder = new Builder { PlaneAngleScale = Units.PlaneAngleScale(model) };
            var resolver = new ProductResolver(builder, palette);

            // Top of the descent: Domain / Interoperability.
            ProductSelection selection = new Selection(classification)
                .Select(model, options.Layers, options.Schemas, options.Classes);
            report.LayerCounts = selection.ByLayer();
            report.SchemaCounts = selection.BySchema();

            int vertexOffset = 0;
            string currentMaterial = null;
            var text = new StringBuilder(1 << 22);

            text.Append($"# {Path.GetFileName(options.SourcePath)} -> OBJ\n");
            text.Append($"# schema {model.SchemaName}, unit scale " +
                        $"{scale.ToString("R", CultureInfo.InvariantCulture)} m\n");
            text.Append("# converted by layer descent: Domain -> Core -> Resource\n");
            text.Append($"# up axis {options.UpAxis.ToUpperInvariant()} " +
                        $"({(options.UpAxis == "z" ? "IFC native" : "rotated from IFC Z-up")})\n");
            if (mtlPath != null)
            {
                text.Append($"\nmtllib {Path.GetFileName(mtlPath)}\n");
            }
            text.Append("\n");

            int total = selection.Count;
            for (int index = 0; index < selection.Entries.Count; index++)
            {
                (IfcEntity product, Layer layer) = selection.Entries[index];

                if (options.Cancelled != null && options.Cancelled())
                {
                    break;
                }
                options.Progress?.Invoke(index, total, product);

                // Core layer: placement + representation + voids + material.
                ProductGeometry geometry = resolver.Resolve(product);

                report.ItemsBuilt += geometry.ItemsBuilt;
                report.OpeningsCut += geometry.OpeningsCut;
                report.NoteUnsupported(geometry.Unsupported);
                report.NoteSkipped(geometry.SkippedRepresentations);

                if (!geometry.HasGeometry)
                {
                    report.Empty++;
                    continue;
                }

                Mesh mesh = scale != 1.0 ? geometry.Mesh.Scaled(scale) : geometry.Mesh;
                if (options.UpAxis == "y")
                {
                    mesh = mesh.ToYUp();
                }

                string label = Sanitise(geometry.Name);
                string layerTag = layer != null ? layer.LayerType.Replace(" ", "_") : "Unclassified";

                text.Append($"o {label}_{geometry.Guid}\n");
                text.Append($"# {product.IsA()} | layer {layerTag}\n");

                foreach (Vec3 v in mesh.Vertices)
                {
                    text.Append("v ");
                    text.Append(v.X.ToString("F6", CultureInfo.InvariantCulture));
                    text.Append(' ');
                    text.Append(v.Y.ToString("F6", CultureInfo.InvariantCulture));
                    text.Append(' ');
                    text.Append(v.Z.ToString("F6", CultureInfo.InvariantCulture));
                    text.Append('\n');
                }

                // Faces are written in style spans.
                foreach (StyleSpan span in mesh.Spans())
                {
                    if (palette != null)
                    {
                        string name = palette.Register(span.Style) ?? palette.Unstyled();
                        if (name != currentMaterial)
                        {
                            text.Append($"usemtl {name}\n");
                            currentMaterial = name;
                        }
                    }
                    for (int t = span.Start; t < span.Stop; t++)
                    {
                        Tri tri = mesh.Triangles[t];
                        text.Append("f ");
                        text.Append(tri.A + 1 + vertexOffset);
                        text.Append(' ');
                        text.Append(tri.B + 1 + vertexOffset);
                        text.Append(' ');
                        text.Append(tri.C + 1 + vertexOffset);
                        text.Append('\n');
                    }
                }
                text.Append('\n');

                if (palette != null)
                {
                    if (!geometry.Styled)
                    {
                        report.StyledNone++;
                    }
                    else if (geometry.StyledByMaterial)
                    {
                        report.StyledByMaterial++;
                    }
                    else
                    {
                        report.StyledDirect++;
                    }
                }

                vertexOffset += mesh.Vertices.Count;
                report.Converted++;
                report.Triangles += mesh.Triangles.Count;
            }

            // Write with explicit LF and no BOM: the Python writes plain text, and a
            // byte-for-byte diff is the whole point of keeping this path.
            using (var stream = new FileStream(options.TargetPath, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.NewLine = "\n";
                writer.Write(text.ToString());
            }

            if (palette != null)
            {
                report.Materials = palette.WriteMtl(mtlPath, Path.GetFileName(options.SourcePath));
                report.MtlPath = mtlPath;
            }

            report.Vertices = vertexOffset;
            report.HolesBridged = builder.Stats.Bridged;
            report.HolesFilled = builder.Stats.Filled;
            return report;
        }
    }
}
