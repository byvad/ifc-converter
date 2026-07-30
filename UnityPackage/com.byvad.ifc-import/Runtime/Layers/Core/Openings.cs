using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers.Core
{
    /// <summary>Product schema relationships: handling voiding elements.</summary>
    public static class Openings
    {
        /// <summary>
        /// The IfcOpeningElement instances voiding this product.
        /// <para>
        /// IfcRelVoidsElement is a Product-schema (Core) relationship. The opening's
        /// geometry is the <i>void volume</i>, not material, which is exactly why
        /// exporting openings as solids fills every doorway with a block — and why
        /// failing to subtract them leaves every window buried in its wall.
        /// </para>
        /// </summary>
        public static IEnumerable<IfcEntity> Of(IfcEntity product) =>
            RelatedThrough(product, "HasOpenings", "IfcRelVoidsElement", "RelatedOpeningElement");

        /// <summary>
        /// The elements filling an opening, e.g. the window seated in a wall void.
        /// Not part of the geometry descent, but the link a Unity scene needs to
        /// parent a window to the wall it sits in.
        /// </summary>
        public static IEnumerable<IfcEntity> FillingsOf(IfcEntity opening) =>
            RelatedThrough(opening, "HasFillings", "IfcRelFillsElement", "RelatedBuildingElement");

        /// <summary>
        /// IFC's general relationship idiom: walk an inverse attribute to the
        /// relationship instances pointing back at <paramref name="source"/>, keep
        /// only the relationship type that's actually relevant, and follow its
        /// forward attribute to the related entity.
        /// </summary>
        private static IEnumerable<IfcEntity> RelatedThrough(
            IfcEntity source, string inverseAttribute, string relationshipType, string targetAttribute)
        {
            if (source == null)
            {
                yield break;
            }
            foreach (IfcEntity relationship in source.Inverse(inverseAttribute))
            {
                if (!relationship.IsA(relationshipType))
                {
                    continue;
                }
                IfcEntity target = relationship.Entity(targetAttribute);
                if (target != null)
                {
                    yield return target;
                }
            }
        }
    }
}