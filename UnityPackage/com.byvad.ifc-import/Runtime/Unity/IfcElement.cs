// @author: Davy Bellens

using UnityEngine;

namespace Conversion.Unity
{
    /// <summary>
    /// The IFC identity of a GameObject, kept alive after the geometry is baked.
    /// <para>
    /// This is the whole reason for loading IFC directly rather than going through
    /// OBJ. A mesh out of an OBJ is anonymous; here every object still knows its
    /// GlobalId, its IFC class, and which conceptual layer it came from, so a click
    /// in the scene can be traced back to a line in the file.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IfcElement : MonoBehaviour
    {
        [Tooltip("IfcRoot.GlobalId — the 22-character base64 GUID, stable across exports.")]
        public string GlobalId;

        [Tooltip("The declared IFC entity type, e.g. IfcWallStandardCase.")]
        public string IfcClass;

        [Tooltip("Conceptual layer: Domain, Interoperability, Core or Resource.")]
        public string ConceptualLayer;

        [Tooltip("Conceptual schema within that layer, e.g. Shared Building.")]
        public string ConceptualSchema;

        [Tooltip("IfcRoot.Name as authored.")]
        public string IfcName;

        [Tooltip("STEP line number in the source file. Useful when diffing exports.")]
        public int EntityId;

        [Tooltip("How this object hangs off its parent in the spatial tree.")]
        public string Relation;

        public override string ToString() =>
            string.IsNullOrEmpty(IfcName)
                ? $"{IfcClass} #{EntityId}"
                : $"{IfcClass} '{IfcName}' #{EntityId}";
    }
}
