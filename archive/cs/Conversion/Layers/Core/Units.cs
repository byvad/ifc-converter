using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers.Core
{
    /// <summary>
    /// Measure schema logic: identifying scale factors from the IFC project.
    /// </summary>
    public static class Units
    {
        private static readonly Dictionary<string, double> SiPrefixes = new Dictionary<string, double>
        {
            { "EXA", 1e18 }, { "PETA", 1e15 }, { "TERA", 1e12 }, { "GIGA", 1e9 },
            { "MEGA", 1e6 }, { "KILO", 1e3 }, { "HECTO", 1e2 }, { "DECA", 1e1 },
            { "DECI", 1e-1 }, { "CENTI", 1e-2 }, { "MILLI", 1e-3 }, { "MICRO", 1e-6 },
            { "NANO", 1e-9 }, { "PICO", 1e-12 },
        };

        /// <summary>
        /// Metres per model length unit.
        /// <para>
        /// IFC files are commonly authored in millimetres. Nothing in the geometry
        /// entities records this: the scale lives in the project's IfcUnitAssignment,
        /// a Measure-schema structure sitting at the bottom of the descent.
        /// </para>
        /// </summary>
        public static double LengthScale(IfcModel model) => ScaleFor(model, "LENGTHUNIT", 1.0);

        /// <summary>
        /// Radians per model plane-angle unit. IFC2X3 exporters frequently write
        /// degrees here, which is what makes an arc trim parameter ambiguous
        /// without consulting the project.
        /// </summary>
        public static double PlaneAngleScale(IfcModel model) => ScaleFor(model, "PLANEANGLEUNIT", 1.0);

        private static double ScaleFor(IfcModel model, string unitType, double fallback)
        {
            foreach (IfcEntity project in model.ByType("IfcProject"))
            {
                IfcEntity assignment = project.Entity("UnitsInContext");
                if (assignment == null)
                {
                    continue;
                }
                foreach (IfcEntity unit in assignment.Entities("Units"))
                {
                    if (unit.String("UnitType") != unitType)
                    {
                        continue;
                    }
                    double? scale = NamedUnitScale(unit);
                    if (scale.HasValue)
                    {
                        return scale.Value;
                    }
                }
            }
            return fallback;
        }

        /// <summary>The size of one of this unit in the SI base unit, or null if it is not one.</summary>
        private static double? NamedUnitScale(IfcEntity unit, int depth = 0)
        {
            if (unit == null || depth > 8)
            {
                return null;
            }

            if (unit.IsA("IfcSIUnit"))
            {
                string prefix = unit.String("Prefix");
                if (string.IsNullOrEmpty(prefix))
                {
                    return 1.0;
                }
                return SiPrefixes.TryGetValue(prefix, out double factor) ? factor : 1.0;
            }

            if (unit.IsA("IfcConversionBasedUnit"))
            {
                IfcEntity measure = unit.Entity("ConversionFactor");
                if (measure == null)
                {
                    return null;
                }

                // ValueComponent is a select, usually wrapped: IFCRATIOMEASURE(0.3048).
                if (!measure["ValueComponent"].TryAsDouble(out double value))
                {
                    return null;
                }

                double? baseScale = NamedUnitScale(measure.Entity("UnitComponent"), depth + 1);
                return baseScale.HasValue ? value * baseScale.Value : (double?)null;
            }

            return null;
        }
    }
}
