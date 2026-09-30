using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Removes every tree under the brush at once, of one type or of all types.
    /// </summary>
    public sealed class TreeEraseTool : ITerrainTool
    {
        /// <summary>Only remove this tree type, or every type when negative.</summary>
        public int TreeType { get; set; } = -1;

        /// <summary>Brush weight a tree has to be under to go. The default ignores the faint outer edge of soft brushes.</summary>
        public float MinWeight { get; set; } = 0.1f;

        /// <summary>Edits the trees.</summary>
        public TerrainChannel Channel => TerrainChannel.Trees;

        /// <summary>Removes the trees under the brush.</summary>
        public void Apply(in TerrainToolContext context)
        {
            BrushArea area = context.Area;
            int type = TreeType;
            float minWeight = MinWeight;
            context.Trees.RemoveAll(tree =>
            {
                if (type >= 0 && tree.prototypeIndex != type)
                    return false;
                float weight = area.WeightAt(new Vector2(tree.position.x, tree.position.z));
                return weight > 0f && weight >= minWeight;
            });
        }
    }
}
