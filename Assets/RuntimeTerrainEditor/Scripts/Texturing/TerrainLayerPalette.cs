using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// The terrain layers (textures) that can be painted, on every terrain tile. Set them in the inspector; layer 0 is the
    /// base layer the terrain starts filled with. Leave the list empty to keep the layers the (first) terrain already has.
    /// Layers can also be changed from code at runtime; painted weights are kept on the right layers.
    /// </summary>
    public class TerrainLayerPalette : MonoBehaviour
    {
        [Tooltip("Terrain layers to paint with. The first one is the base layer.")]
        [SerializeField] private List<TerrainLayer> layers = new List<TerrainLayer>();

        // The TerrainData of every tile; empty until Init
        private readonly List<TerrainData> _terrainData = new List<TerrainData>();
        // Layers made by CreateLayer, destroyed together with the palette
        private readonly List<TerrainLayer> _createdLayers = new List<TerrainLayer>();

        /// <summary>Raised after the terrain's layers changed.</summary>
        public event Action LayersChanged;

        /// <summary>The paintable layers; index 0 is the base layer.</summary>
        public IReadOnlyList<TerrainLayer> Layers => layers;

        /// <summary>Number of paintable layers.</summary>
        public int Count => layers.Count;

        /// <summary>Destroys the layers made by <see cref="CreateLayer"/>.</summary>
        private void OnDestroy()
        {
            foreach (TerrainLayer layer in _createdLayers)
            {
                if (layer != null)
                    Destroy(layer);
            }
        }

        /// <summary>
        /// Puts the inspector layers on every terrain tile, or adopts the first tile's layers when the list is empty.
        /// </summary>
        public void Init(IReadOnlyList<TerrainData> terrainData)
        {
            SetTiles(terrainData);
            layers.RemoveAll(layer => layer == null);
            if (layers.Count == 0)
                layers.AddRange(_terrainData[0].terrainLayers);
            else
                SetLayers(new List<TerrainLayer>(layers));
        }

        /// <summary>
        /// Connects the palette to the current terrain tiles, e.g. after tiles were added or removed. The tiles' layers
        /// are left as they are.
        /// </summary>
        public void SetTiles(IReadOnlyList<TerrainData> terrainData)
        {
            _terrainData.Clear();
            _terrainData.AddRange(terrainData);
        }

        /// <summary>
        /// Replaces all layers. Painted weights stay on the same index, so this also works for swapping themes;
        /// weight of layers beyond the new count moves to the base layer.
        /// </summary>
        public void SetLayers(IList<TerrainLayer> newLayers)
        {
            List<TerrainLayer> result = new List<TerrainLayer>();
            foreach (TerrainLayer layer in newLayers)
            {
                if (layer != null)
                    result.Add(layer);
            }
            if (result.Count == 0)
            {
                Debug.LogWarning("TerrainLayerPalette: a terrain needs at least one layer.", this);
                return;
            }

            int oldCount = _terrainData.Count > 0 ? _terrainData[0].alphamapLayers : layers.Count;
            int[] source = new int[result.Count];
            for (int i = 0; i < source.Length; i++)
                source[i] = i < oldCount ? i : -1;
            Apply(result, source);
        }

        /// <summary>Adds a layer at the end; it starts unpainted.</summary>
        public void AddLayer(TerrainLayer layer)
        {
            if (layer == null)
                return;

            List<TerrainLayer> result = new List<TerrainLayer>(layers) { layer };
            int[] source = new int[result.Count];
            for (int i = 0; i < layers.Count; i++)
                source[i] = i;
            source[layers.Count] = -1; // starts unpainted
            Apply(result, source);
        }

        /// <summary>Creates a layer from textures and adds it. The palette destroys it again when it is destroyed.</summary>
        public TerrainLayer CreateLayer(Texture2D diffuse, Texture2D normal = null, float tileSize = 8f)
        {
            TerrainLayer layer = new TerrainLayer
            {
                name = diffuse != null ? diffuse.name : "Runtime Terrain Layer",
                diffuseTexture = diffuse,
                normalMapTexture = normal,
                tileSize = new Vector2(tileSize, tileSize),
            };
            _createdLayers.Add(layer);
            AddLayer(layer);
            return layer;
        }

        /// <summary>Swaps the textures of one layer; what is painted with it stays.</summary>
        public void ReplaceLayer(int index, TerrainLayer layer)
        {
            if (layer == null || index < 0 || index >= layers.Count)
                return;

            List<TerrainLayer> result = new List<TerrainLayer>(layers);
            result[index] = layer;
            SetLayers(result);
        }

        /// <summary>Removes a layer; what was painted with it becomes the base layer. The last layer can't be removed.</summary>
        public bool RemoveLayer(int index)
        {
            if (index < 0 || index >= layers.Count || layers.Count == 1)
                return false;

            List<TerrainLayer> result = new List<TerrainLayer>(layers);
            result.RemoveAt(index);
            int[] source = new int[result.Count];
            for (int i = 0; i < source.Length; i++)
                source[i] = i < index ? i : i + 1;
            Apply(result, source);
            return true;
        }

        /// <summary>
        /// Puts a new layer list on every terrain tile, moving painted weights along with their layers. Before
        /// <see cref="Init"/> it only remembers the list.
        /// </summary>
        /// <param name="source">For each new layer, the old layer index whose weights it keeps, or -1 for none.</param>
        private void Apply(List<TerrainLayer> newLayers, int[] source)
        {
            foreach (TerrainData data in _terrainData)
                ApplyToTile(data, newLayers, source);

            layers.Clear();
            layers.AddRange(newLayers);
            if (_terrainData.Count > 0)
                LayersChanged?.Invoke();
        }

        /// <summary>Puts a new layer list on one terrain tile, moving painted weights along with their layers.</summary>
        private static void ApplyToTile(TerrainData data, List<TerrainLayer> newLayers, int[] source)
        {
            int oldCount = data.alphamapLayers;
            bool weightsMove = oldCount != newLayers.Count;
            for (int i = 0; i < source.Length && !weightsMove; i++)
                weightsMove = source[i] != i;

            if (!weightsMove)
            {
                data.terrainLayers = newLayers.ToArray();
                return;
            }

            // Unity keeps alphamap channels by position, so read the weights first and write them back remapped
            int resolution = data.alphamapResolution;
            float[,,] oldWeights = oldCount > 0 ? data.GetAlphamaps(0, 0, resolution, resolution) : null;
            data.terrainLayers = newLayers.ToArray();
            data.SetAlphamaps(0, 0, Remap(oldWeights, resolution, source));
        }

        /// <summary>
        /// Copies each kept layer's weights to its new channel; weight that no longer has a layer goes to the base layer.
        /// </summary>
        private static float[,,] Remap(float[,,] oldWeights, int resolution, int[] source)
        {
            int count = source.Length;
            float[,,] weights = new float[resolution, resolution, count];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float sum = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        float weight = source[i] >= 0 && oldWeights != null ? oldWeights[z, x, source[i]] : 0f;
                        weights[z, x, i] = weight;
                        sum += weight;
                    }
                    weights[z, x, 0] += Mathf.Max(0f, 1f - sum);
                }
            }
            return weights;
        }
    }
}
