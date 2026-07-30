// @author: Davy Bellens

using System;
using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers.Core
{
    /// <summary>How a node came to hang off its parent.</summary>
    public enum SpatialRelation
    {
        /// <summary>The root of the tree, normally IfcProject.</summary>
        Root,

        /// <summary>IfcRelAggregates: a site within a project, a storey within a
        /// building, a stair flight within a stair.</summary>
        Aggregated,

        /// <summary>IfcRelContainedInSpatialStructure: a wall standing on a storey.</summary>
        Contained,

        /// <summary>IfcRelFillsElement: a window seated in a wall's void. Not part of
        /// the IFC spatial tree, but the parenting a scene graph actually wants.</summary>
        Filling,
    }

    /// <summary>One node of the spatial containment tree.</summary>
    public class SpatialNode
    {
        public IfcEntity Entity { get; }
        public SpatialRelation Relation { get; }
        public SpatialNode Parent { get; internal set; }
        public readonly List<SpatialNode> Children = new List<SpatialNode>();

        public SpatialNode(IfcEntity entity, SpatialRelation relation)
        {
            Entity = entity;
            Relation = relation;
        }

        public string Name => Entity?.String("Name");
        public string Guid => Entity?.String("GlobalId");
        public string Type => Entity?.IsA();

        public int Depth
        {
            get
            {
                int depth = 0;
                for (SpatialNode step = Parent; step != null; step = step.Parent)
                {
                    depth++;
                }
                return depth;
            }
        }

        /// <summary>This node and every descendant, depth first.</summary>
        public IEnumerable<SpatialNode> Descendants()
        {
            var pending = new Stack<SpatialNode>();
            pending.Push(this);
            while (pending.Count > 0)
            {
                SpatialNode node = pending.Pop();
                yield return node;
                for (int i = node.Children.Count - 1; i >= 0; i--)
                {
                    pending.Push(node.Children[i]);
                }
            }
        }

        public override string ToString() => $"{Type} '{Name}' ({Children.Count} children)";
    }

    /// <summary>
    /// Builds the spatial containment tree from the Core-layer relationships.
    /// <para>
    /// This has no counterpart in the OBJ pipeline, which is flat by necessity. A
    /// scene graph is not: an element wants to hang off the storey it stands on, so
    /// that hiding a floor hides its walls, and so that a storey can be moved as a
    /// unit. The information is all in the file, spread across three relationship
    /// types that have to be walked together.
    /// </para>
    /// </summary>
    public static class Spatial
    {
        /// <summary>
        /// Build the tree. Roots are normally a single IfcProject; a file with none
        /// falls back to whatever spatial elements have no parent, so a partial
        /// export still produces something usable.
        /// </summary>
        /// <param name="includeFillings">Re-parent windows and doors onto the element
        /// they fill, rather than leaving them on the storey. Truer to how a person
        /// thinks about a building, and it makes hiding a wall hide its windows.</param>
        public static List<SpatialNode> Build(IfcModel model, bool includeFillings = true)
        {
            var builder = new TreeBuilder(includeFillings);
            var roots = new List<SpatialNode>();

            BuildProjectRoots(model, roots, builder);
            BuildOrphanRoots(model, roots, builder);

            return roots;
        }

        private static void BuildProjectRoots(IfcModel model, List<SpatialNode> roots, TreeBuilder builder)
        {
            foreach (IfcEntity project in model.ByType("IfcProject"))
            {
                var root = new SpatialNode(project, SpatialRelation.Root);
                builder.Place(project.Id);
                builder.Expand(root, 0);
                roots.Add(root);
            }
        }

        private static void BuildOrphanRoots(IfcModel model, List<SpatialNode> roots, TreeBuilder builder)
        {
            // Anything the walk never reached: an orphaned storey, or a product
            // sitting outside the spatial structure entirely. Better surfaced at the
            // top level than silently dropped.
            foreach (IfcEntity product in model.ByType("IfcProduct"))
            {
                if (builder.IsPlaced(product.Id) || product.IsA("IfcOpeningElement"))
                {
                    continue;   // already placed, or a void — voids are not scene objects
                }

                var orphan = new SpatialNode(product, SpatialRelation.Root);
                builder.Place(product.Id);
                builder.Expand(orphan, 0);
                roots.Add(orphan);
            }
        }

        /// <summary>
        /// Walks the three relationship types into a tree, holding the
        /// already-placed set and the filling preference as state instead of
        /// threading them through every method — <see cref="Expand"/>'s call tree
        /// only ever needs to pass the two things that actually change per call:
        /// which node it's expanding, and how deep it is.
        /// </summary>
        private sealed class TreeBuilder
        {
            /// <summary>Guard against a malformed file whose decomposition cycles.</summary>
            private const int MaxDepth = 64;

            private readonly HashSet<int> _placed = new HashSet<int>();
            private readonly bool _includeFillings;

            public TreeBuilder(bool includeFillings)
            {
                _includeFillings = includeFillings;
            }

            public bool IsPlaced(int id) => _placed.Contains(id);

            public void Place(int id) => _placed.Add(id);

            public void Expand(SpatialNode node, int depth)
            {
                if (depth > MaxDepth)
                {
                    return;
                }

                ExpandAggregates(node, depth);
                ExpandContained(node, depth);

                if (_includeFillings)
                {
                    ExpandFillings(node, depth);
                }
            }

            private void ExpandAggregates(SpatialNode node, int depth)
            {
                // IfcRelAggregates: project -> site -> building -> storey, and also
                // assemblies such as a stair decomposing into its flights.
                foreach (IfcEntity relationship in node.Entity.Inverse("IsDecomposedBy"))
                {
                    if (!relationship.IsA("IfcRelAggregates"))
                    {
                        continue;
                    }
                    foreach (IfcEntity child in relationship.Entities("RelatedObjects"))
                    {
                        Attach(node, child, SpatialRelation.Aggregated, depth);
                    }
                }
            }

            private void ExpandContained(SpatialNode node, int depth)
            {
                // IfcRelContainedInSpatialStructure: the elements standing on a storey.
                foreach (IfcEntity relationship in node.Entity.Inverse("ContainsElements"))
                {
                    if (!relationship.IsA("IfcRelContainedInSpatialStructure"))
                    {
                        continue;
                    }
                    foreach (IfcEntity child in relationship.Entities("RelatedElements"))
                    {
                        Attach(node, child, SpatialRelation.Contained, depth);
                    }
                }
            }

            private void ExpandFillings(SpatialNode node, int depth)
            {
                // IfcRelVoidsElement then IfcRelFillsElement: the window in this wall.
                // The filling is normally also contained in the storey, so this only
                // fires when the containment pass has not already claimed it.
                foreach (IfcEntity opening in Openings.Of(node.Entity))
                {
                    foreach (IfcEntity filling in Openings.FillingsOf(opening))
                    {
                        Attach(node, filling, SpatialRelation.Filling, depth);
                    }
                }
            }

            private void Attach(SpatialNode parent, IfcEntity child, SpatialRelation relation, int depth)
            {
                if (child == null || IsPlaced(child.Id))
                {
                    return;   // already parented; an element belongs in exactly one place
                }
                Place(child.Id);

                var node = new SpatialNode(child, relation) { Parent = parent };
                parent.Children.Add(node);
                Expand(node, depth + 1);
            }
        }

        /// <summary>Every node across every root, depth first.</summary>
        public static IEnumerable<SpatialNode> Flatten(IEnumerable<SpatialNode> roots)
        {
            foreach (SpatialNode root in roots)
            {
                foreach (SpatialNode node in root.Descendants())
                {
                    yield return node;
                }
            }
        }

        /// <summary>An indented outline, for logging and for eyeballing a new file.</summary>
        public static string Describe(IEnumerable<SpatialNode> roots, int maxDepth = 3)
        {
            var text = new System.Text.StringBuilder();
            foreach (SpatialNode root in roots)
            {
                foreach (SpatialNode node in root.Descendants())
                {
                    int depth = node.Depth;
                    if (depth > maxDepth)
                    {
                        continue;
                    }
                    text.Append(new string(' ', depth * 2));
                    text.Append(node.Type);
                    string name = node.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        text.Append(" '").Append(name).Append('\'');
                    }
                    int children = node.Children.Count;
                    if (children > 0)
                    {
                        text.Append("  (").Append(children).Append(children == 1 ? " child)" : " children)");
                    }
                    text.Append('\n');
                }
            }
            return text.ToString();
        }
    }
}