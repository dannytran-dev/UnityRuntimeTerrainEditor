using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>Settings for one of the terrain's tree types, or a tree type to add at runtime.</summary>
    [Serializable]
    public class TerrainTreeType
    {
        [Tooltip("The tree, matched against the terrain's trees. Give it an LODGroup so the terrain draws it with its own materials " +
                 "(without one, it needs the legacy Nature/Soft Occlusion shaders and looks unlit in URP). Leave colliders off so the brush isn't blocked.")]
        public GameObject prefab;
        [Tooltip("Random height scale range for new trees")]
        public Vector2 heightScale = new Vector2(0.8f, 1.2f);
        [Tooltip("Minimum distance between trees in meters at the lowest density; the density slider packs them up to 4x closer")]
        [Min(0.1f)] public float spacing = 6f;
        [Tooltip("How much wind bends the tree (only used when the tree is added at runtime)")]
        public float bendFactor;
        [Tooltip("Optional. Picture for UI buttons")]
        public Texture2D icon;
    }

    /// <summary>
    /// Icon for one of the terrain's grass (detail) types, or a grass type to add at runtime. Everything except the
    /// texture, prefab and icon is only used when the grass is added at runtime; the terrain's own grass keeps its settings.
    /// </summary>
    [Serializable]
    public class TerrainDetailType
    {
        [Tooltip("Grass texture, matched against the terrain's grass. Leave empty and set a prefab for mesh details like small rocks.")]
        public Texture2D texture;
        [Tooltip("Mesh detail, used instead of the texture when set")]
        public GameObject prefab;
        [Tooltip("Grass textures always face the camera when on")]
        public bool billboard = true;
        [Tooltip("Random width range in meters (min, max)")]
        public Vector2 width = new Vector2(1f, 2f);
        [Tooltip("Random height range in meters (min, max)")]
        public Vector2 height = new Vector2(1f, 2f);
        [Tooltip("Tint of lush patches (blended with Dry Color by a noise pattern)")]
        public Color healthyColor = new Color(0.26f, 0.98f, 0.16f);
        [Tooltip("Tint of dry patches")]
        public Color dryColor = new Color(0.8f, 0.74f, 0.1f);
        [Tooltip("How many instances a fully painted area gets (coverage scatter mode)")]
        [Range(0f, 3f)] public float density = 1f;
        [Tooltip("Optional. Picture for UI buttons (the grass texture is used when empty)")]
        public Texture2D icon;
    }

    /// <summary>
    /// Extra settings for the terrain's trees and grass, and functions to add tree and grass types at runtime.
    /// The trees and grass themselves are set up on the Terrain (Paint Trees and Paint Details in the Terrain inspector).
    /// Entries here are matched to them by prefab or texture and give UI icons, tree spacing and tree height ranges.
    /// Nothing is put on the terrain unless you call <see cref="AddTreeType"/>, <see cref="AddDetailType"/> or
    /// <see cref="ApplyToTerrain"/>.
    /// </summary>
    public class TerrainVegetationPalette : MonoBehaviour
    {
        [Tooltip("Icons, spacing and height ranges for the terrain's trees (matched by prefab)")]
        [SerializeField] private List<TerrainTreeType> trees = new List<TerrainTreeType>();
        [Tooltip("Optional icons for the terrain's grass (matched by texture or prefab). Without one the grass texture is the icon.")]
        [SerializeField] private List<TerrainDetailType> details = new List<TerrainDetailType>();
        [Tooltip("Detail map resolution to use when grass is added at runtime to a terrain that has none (details can't be painted at 0)")]
        [SerializeField] private int detailResolution = 512;
        [Tooltip("Detail patch size in cells; smaller patches cull better, bigger ones draw with fewer calls")]
        [SerializeField] private int detailResolutionPerPatch = 32;

        // The TerrainData of every tile; empty until Init. Tiles share their tree and grass types.
        private readonly List<TerrainData> _terrainData = new List<TerrainData>();

        /// <summary>Raised after tree or grass types were added to or replaced on the terrain.</summary>
        public event Action TypesChanged;

        /// <summary>Settings for the terrain's tree types (matched by prefab).</summary>
        public IReadOnlyList<TerrainTreeType> Trees => trees;

        /// <summary>Icons for the terrain's grass types (matched by texture or prefab).</summary>
        public IReadOnlyList<TerrainDetailType> Details => details;

        /// <summary>Number of tree types on the terrain.</summary>
        public int TreeTypeCount => FirstData != null ? FirstData.treePrototypes.Length : 0;

        /// <summary>Number of grass/detail types on the terrain.</summary>
        public int DetailTypeCount => FirstData != null ? FirstData.detailPrototypes.Length : 0;

        /// <summary>The first tile's TerrainData, whose tree and grass types all tiles share; null before Init.</summary>
        private TerrainData FirstData => _terrainData.Count > 0 ? _terrainData[0] : null;

        /// <summary>Connects the palette to every terrain tile. The terrain's trees and grass are left as they are.</summary>
        public void Init(IReadOnlyList<TerrainData> terrainData)
        {
            SetTiles(terrainData);
        }

        /// <summary>
        /// Connects the palette to the current terrain tiles, e.g. after tiles were added or removed. The tiles' trees and
        /// grass are left as they are.
        /// </summary>
        public void SetTiles(IReadOnlyList<TerrainData> terrainData)
        {
            _terrainData.Clear();
            _terrainData.AddRange(terrainData);
        }

        /// <summary>The settings for the terrain's tree type at index (same prefab), or null.</summary>
        public TerrainTreeType FindTreeType(int index)
        {
            if (FirstData == null)
                return null;
            TreePrototype[] prototypes = FirstData.treePrototypes;
            if (index < 0 || index >= prototypes.Length || prototypes[index].prefab == null)
                return null;

            GameObject prefab = prototypes[index].prefab;
            return trees.Find(type => type != null && type.prefab == prefab);
        }

        /// <summary>The settings for the terrain's grass type at index (same texture or prefab), or null.</summary>
        public TerrainDetailType FindDetailType(int index)
        {
            if (FirstData == null)
                return null;
            DetailPrototype[] prototypes = FirstData.detailPrototypes;
            if (index < 0 || index >= prototypes.Length)
                return null;

            DetailPrototype prototype = prototypes[index];
            return details.Find(type => type != null && Matches(type, prototype));
        }

        /// <summary>Picture for a tree type's UI button, or null.</summary>
        public Texture2D GetTreeIcon(int index)
        {
            TerrainTreeType type = FindTreeType(index);
            return type != null ? type.icon : null;
        }

        /// <summary>Picture for a grass type's UI button: its icon, else its grass texture, else null.</summary>
        public Texture2D GetDetailIcon(int index)
        {
            TerrainDetailType type = FindDetailType(index);
            if (type != null && type.icon != null)
                return type.icon;
            if (FirstData != null && index >= 0 && index < FirstData.detailPrototypes.Length)
                return FirstData.detailPrototypes[index].prototypeTexture;
            return null;
        }

        /// <summary>Minimum tree spacing in meters for a tree type at a density [0,1] (1 packs trees 4x closer).</summary>
        public float GetTreeSpacing(int index, float density)
        {
            TerrainTreeType type = FindTreeType(index);
            float spacing = type != null ? type.spacing : 6f;
            return spacing / Mathf.Lerp(1f, 4f, Mathf.Clamp01(density));
        }

        /// <summary>Height scale range for new trees of a type.</summary>
        public Vector2 GetTreeHeightScale(int index)
        {
            TerrainTreeType type = FindTreeType(index);
            return type != null ? type.heightScale : new Vector2(0.8f, 1.2f);
        }

        #region Adding trees and grass at runtime
        /// <summary>
        /// Adds a tree type to every terrain tile and returns its index (the existing index when the terrain already has that
        /// prefab), or -1 when it can't be added. Its icon, spacing and height range are used for that prefab from now on.
        /// </summary>
        public int AddTreeType(TerrainTreeType type)
        {
            if (type == null || type.prefab == null || FirstData == null)
                return -1;
            int entry = trees.FindIndex(other => other != null && other.prefab == type.prefab);
            if (entry >= 0)
                trees[entry] = type;
            else
                trees.Add(type);

            List<TreePrototype> prototypes = new List<TreePrototype>(FirstData.treePrototypes);
            int existing = prototypes.FindIndex(prototype => prototype.prefab == type.prefab);
            if (existing >= 0)
                return existing;

            prototypes.Add(new TreePrototype { prefab = type.prefab, bendFactor = type.bendFactor });
            foreach (TerrainData data in _terrainData)
            {
                data.treePrototypes = prototypes.ToArray();
                data.RefreshPrototypes();
            }
            TypesChanged?.Invoke();
            return prototypes.Count - 1;
        }

        /// <summary>
        /// Adds a grass/detail type to every terrain tile and returns its index (the existing index when the terrain already
        /// has that texture or prefab), or -1 when it can't be added. Painted grass stays. Its icon is used from now on.
        /// </summary>
        public int AddDetailType(TerrainDetailType type)
        {
            if (type == null || (type.texture == null && type.prefab == null) || FirstData == null)
                return -1;

            int entry = details.FindIndex(other => other != null && other.prefab == type.prefab && (type.prefab != null || other.texture == type.texture));
            if (entry >= 0)
                details[entry] = type;
            else
                details.Add(type);

            List<DetailPrototype> prototypes = new List<DetailPrototype>(FirstData.detailPrototypes);
            int existing = prototypes.FindIndex(prototype => Matches(type, prototype));
            if (existing >= 0)
                return existing;

            DetailPrototype newPrototype = CreateDetailPrototype(type);
            if (!newPrototype.Validate(out string error))
            {
                Debug.LogWarning("TerrainVegetationPalette: can't add detail type: " + error, this);
                return -1;
            }

            prototypes.Add(newPrototype);
            foreach (TerrainData data in _terrainData)
            {
                EnsureDetailResolution(data);
                data.detailPrototypes = prototypes.ToArray();
                data.RefreshPrototypes();
            }
            TypesChanged?.Invoke();
            return prototypes.Count - 1;
        }

        /// <summary>
        /// Replaces the tree and grass types of every terrain tile with the ones in this palette. An empty list leaves that
        /// kind as it is. Trees whose type no longer exists are removed.
        /// </summary>
        public void ApplyToTerrain()
        {
            if (FirstData == null)
                return;

            List<TreePrototype> treePrototypes = new List<TreePrototype>();
            foreach (TerrainTreeType type in trees)
            {
                if (type != null && type.prefab != null)
                    treePrototypes.Add(new TreePrototype { prefab = type.prefab, bendFactor = type.bendFactor });
            }
            if (treePrototypes.Count > 0)
            {
                foreach (TerrainData data in _terrainData)
                {
                    // Trees that point at a type that no longer exists would break the terrain
                    TreeInstance[] instances = Array.FindAll(data.treeInstances, tree => tree.prototypeIndex < treePrototypes.Count);
                    data.treePrototypes = treePrototypes.ToArray();
                    data.SetTreeInstances(instances, true);
                }
            }

            List<DetailPrototype> detailPrototypes = new List<DetailPrototype>();
            foreach (TerrainDetailType type in details)
            {
                if (type == null || (type.texture == null && type.prefab == null))
                    continue;
                DetailPrototype prototype = CreateDetailPrototype(type);
                if (prototype.Validate(out string error))
                    detailPrototypes.Add(prototype);
                else
                    Debug.LogWarning("TerrainVegetationPalette: skipping detail type: " + error, this);
            }
            foreach (TerrainData data in _terrainData)
            {
                if (detailPrototypes.Count > 0)
                {
                    EnsureDetailResolution(data);
                    data.detailPrototypes = detailPrototypes.ToArray();
                }
                data.RefreshPrototypes();
            }
            TypesChanged?.Invoke();
        }

        /// <summary>Gives a terrain tile a detail resolution when it has none, so grass can be painted.</summary>
        private void EnsureDetailResolution(TerrainData data)
        {
            if (data.detailResolution == 0)
                data.SetDetailResolution(detailResolution, detailResolutionPerPatch);
        }
        #endregion

        /// <summary>The terrain grass setting for a detail type (also handy for setting up terrains in editor scripts).</summary>
        public static DetailPrototype CreateDetailPrototype(TerrainDetailType type)
        {
            DetailPrototype prototype = new DetailPrototype
            {
                minWidth = type.width.x,
                maxWidth = type.width.y,
                minHeight = type.height.x,
                maxHeight = type.height.y,
                healthyColor = type.healthyColor,
                dryColor = type.dryColor,
                density = type.density,
            };

            if (type.prefab != null)
            {
                prototype.usePrototypeMesh = true;
                prototype.prototype = type.prefab;
                prototype.renderMode = DetailRenderMode.VertexLit;
                prototype.useInstancing = true;
            }
            else
            {
                prototype.usePrototypeMesh = false;
                prototype.prototypeTexture = type.texture;
                prototype.renderMode = type.billboard ? DetailRenderMode.GrassBillboard : DetailRenderMode.Grass;
                prototype.useInstancing = false;
            }
            return prototype;
        }

        /// <summary>True when a grass setting is for a terrain grass type: the same mesh prefab, or the same texture.</summary>
        private static bool Matches(TerrainDetailType type, DetailPrototype prototype)
        {
            return type.prefab != null
                ? prototype.usePrototypeMesh && prototype.prototype == type.prefab
                : !prototype.usePrototypeMesh && prototype.prototypeTexture == type.texture;
        }
    }
}
