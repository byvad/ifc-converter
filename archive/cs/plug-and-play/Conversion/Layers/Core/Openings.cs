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
        public static IEnumerable<IfcEntity> Of(IfcEntity product)
        {
            if (product == null)
            {
                yield break;
            }
            foreach (IfcEntity relationship in product.Inverse("HasOpenings"))
            {
                if (!relationship.IsA("IfcRelVoidsElement"))
                {
                    continue;
                }
                IfcEntity opening = relationship.Entity("RelatedOpeningElement");
                if (opening != null)
                {
                    yield return opening;
                }
            }
        }

        /// <summary>
        /// The elements filling an opening, e.g. the window seated in a wall void.
        /// Not part of the geometry descent, but the link a Unity scene needs to
        /// parent a window to the wall it sits in.
        /// </summary>
        public static IEnumerable<IfcEntity> FillingsOf(IfcEntity opening)
        {
            if (opening == null)
            {
                yield break;
            }
            foreach (IfcEntity relationship in opening.Inverse("HasFillings"))
            {
                if (!relationship.IsA("IfcRelFillsElement"))
                {
                    continue;
                }
                IfcEntity element = relationship.Entity("RelatedBuildingElement");
                if (element != null)
                {
                    yield return element;
                }
            }
        }
    }
}
