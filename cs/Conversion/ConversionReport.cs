using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Conversion.Ifc;

namespace Conversion
{
    /// <summary>Everything the descent learned on the way through, in reportable form.</summary>
    public sealed class ConversionReport
    {
        public int Converted;
        public int Empty;
        public int Vertices;
        public int Triangles;
        public int ItemsBuilt;
        public int OpeningsCut;

        public readonly Dictionary<string, int> Unsupported = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> SkippedRepresentations = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> LayerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> SchemaCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public double UnitScale = 1.0;
        public string UpAxis = "z";

        public bool Colour;
        public int Materials;
        public int StyledDirect;
        public int StyledByMaterial;
        public int StyledNone;
        public string MtlPath;

        public int HolesBridged;
        public int HolesFilled;

        public void NoteUnsupported(IEnumerable<string> names)
        {
            foreach (string name in names)
            {
                string key = name.Split(':')[0];
                Unsupported.TryGetValue(key, out int count);
                Unsupported[key] = count + 1;
            }
        }

        public void NoteSkipped(IEnumerable<string> identifiers)
        {
            foreach (string identifier in identifiers)
            {
                string key = string.IsNullOrEmpty(identifier) ? "(unnamed)" : identifier;
                SkippedRepresentations.TryGetValue(key, out int count);
                SkippedRepresentations[key] = count + 1;
            }
        }

        private static string Number(double value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        public string Render()
        {
            var lines = new List<string>
            {
                $"Length unit scale : {Number(UnitScale)} m per model unit",
                $"Up axis           : {UpAxis.ToUpperInvariant()}",
                $"Products meshed   : {Converted}",
                $"Products empty    : {Empty}",
                $"Resource items    : {ItemsBuilt}",
                $"Vertices          : {Vertices}",
                $"Triangles         : {Triangles}",
            };

            if (Colour)
            {
                lines.Add("");
                lines.Add("Appearance:");
                lines.Add($"  Materials written  {Materials}");
                lines.Add($"  Styled per item    {StyledDirect}");
                lines.Add($"  Styled by material {StyledByMaterial}");
                lines.Add($"  No style found     {StyledNone}");
                if (MtlPath != null)
                {
                    lines.Add($"  Wrote              {Path.GetFileName(MtlPath)}");
                }
            }

            if (LayerCounts.Count > 0)
            {
                lines.Add("");
                lines.Add("Products by conceptual layer:");
                var order = new List<string>(Taxonomy.LayerOrder) { "Unclassified" };
                foreach (string name in order)
                {
                    if (LayerCounts.TryGetValue(name, out int count))
                    {
                        lines.Add($"  {name,-18} {count}");
                    }
                }
            }

            if (SchemaCounts.Count > 0)
            {
                lines.Add("");
                lines.Add("Products by schema:");
                var keys = new List<string>(SchemaCounts.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    lines.Add($"  {key,-40} {SchemaCounts[key]}");
                }
            }

            if (OpeningsCut > 0)
            {
                lines.Add("");
                lines.Add($"Openings subtracted : {OpeningsCut}");
            }

            if (HolesBridged > 0 || HolesFilled > 0)
            {
                lines.Add("");
                lines.Add("Openings in faces (inner bounds):");
                lines.Add($"  Cut open           {HolesBridged}");
                lines.Add($"  Filled in          {HolesFilled}");
            }

            if (SkippedRepresentations.Count > 0)
            {
                lines.Add("");
                lines.Add("Representations skipped (not body geometry):");
                var keys = new List<string>(SkippedRepresentations.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    lines.Add($"  {key,-28} {SkippedRepresentations[key]}");
                }
            }

            if (Unsupported.Count > 0)
            {
                lines.Add("");
                lines.Add("Unsupported geometry items:");
                var keys = new List<string>(Unsupported.Keys);
                keys.Sort((x, y) => Unsupported[y].CompareTo(Unsupported[x]));
                foreach (string key in keys)
                {
                    lines.Add($"  {key,-40} {Unsupported[key]}");
                }
            }

            return string.Join("\n", lines);
        }

        public override string ToString() => Render();
    }
}
