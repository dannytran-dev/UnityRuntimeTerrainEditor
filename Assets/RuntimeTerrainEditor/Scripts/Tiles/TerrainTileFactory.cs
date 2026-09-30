using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Creates terrain tiles at runtime: new TerrainData with a tile's size and resolutions, and Terrain objects that look
    /// and behave like the tiles next to them. Used by <see cref="TerrainGridGenerator"/> and by
    /// <see cref="TerrainEditor.AddTile"/>. The caller owns the created TerrainData and destroys it when done.
    /// </summary>
    public static class TerrainTileFactory
    {
        /// <summary>
        /// A new TerrainData with the given size and resolutions. When a template is given, the layers, trees, grass and
        /// grass settings are copied from it.
        /// </summary>
        /// <param name="size">Width (X), highest height (Y) and length (Z) in meters.</param>
        /// <param name="heightmapResolution">Heightmap samples per side; Unity rounds it to 2^n + 1 (33 to 4097).</param>
        /// <param name="alphamapResolution">Paint texels per side.</param>
        /// <param name="detailResolution">Grass cells per side (0: no grass).</param>
        /// <param name="detailResolutionPerPatch">Grass patch size in cells.</param>
        /// <param name="template">Optional. TerrainData to copy the layers, trees and grass from.</param>
        public static TerrainData CreateData(Vector3 size, int heightmapResolution, int alphamapResolution, int detailResolution,
                                             int detailResolutionPerPatch, TerrainData template)
        {
            TerrainData data = new TerrainData { name = "Terrain Tile (Runtime)" };
            // Resolutions before size: changing the heightmap resolution rescales the size
            data.heightmapResolution = heightmapResolution;
            data.alphamapResolution = alphamapResolution;
            data.baseMapResolution = alphamapResolution;
            if (detailResolution > 0)
                data.SetDetailResolution(detailResolution, detailResolutionPerPatch);
            data.size = size;
            if (template != null)
                CopySettings(template, data);
            return data;
        }

        /// <summary>
        /// A new TerrainData shaped like another one: the same size, resolutions, layers, trees and grass, with flat
        /// ground, the base layer everywhere and no trees, grass or holes.
        /// </summary>
        public static TerrainData CreateDataLike(TerrainData source)
        {
            return CreateData(source.size, source.heightmapResolution, source.alphamapResolution, source.detailResolution,
                              source.detailResolutionPerPatch, source);
        }

        /// <summary>Copies the layers, tree and grass types and grass settings of one TerrainData to another.</summary>
        public static void CopySettings(TerrainData source, TerrainData target)
        {
            target.terrainLayers = source.terrainLayers;
            target.treePrototypes = source.treePrototypes;
            target.detailPrototypes = source.detailPrototypes;
            target.SetDetailScatterMode(source.detailScatterMode);
            target.wavingGrassAmount = source.wavingGrassAmount;
            target.wavingGrassSpeed = source.wavingGrassSpeed;
            target.wavingGrassStrength = source.wavingGrassStrength;
            target.wavingGrassTint = source.wavingGrassTint;
            target.RefreshPrototypes();
        }

        /// <summary>
        /// Creates a Terrain object (with a collider) for the data at a world position. When a look source is given, the
        /// new terrain copies its material, level of detail, tree and grass distances, shadows, grouping and layer, so it
        /// looks like the tile next to it.
        /// </summary>
        /// <param name="lookSource">Optional. Terrain to copy the settings from.</param>
        /// <param name="parent">Optional. Parent of the new object.</param>
        public static Terrain CreateTerrain(TerrainData data, Vector3 position, string name, Terrain lookSource, Transform parent)
        {
            GameObject tileObject = Terrain.CreateTerrainGameObject(data);
            tileObject.name = name;
            tileObject.transform.SetParent(parent, true);
            tileObject.transform.position = position;

            Terrain terrain = tileObject.GetComponent<Terrain>();
            if (lookSource != null)
                CopySettings(lookSource, terrain);
            return terrain;
        }

        /// <summary>
        /// Copies how a terrain draws and connects (material, level of detail, tree and grass distances, shadows,
        /// grouping and layer) to another terrain. The TerrainData isn't touched.
        /// </summary>
        public static void CopySettings(Terrain source, Terrain target)
        {
            target.gameObject.layer = source.gameObject.layer;
            target.materialTemplate = source.materialTemplate;
            target.drawInstanced = source.drawInstanced;
            target.groupingID = source.groupingID;
            target.allowAutoConnect = source.allowAutoConnect;
            target.heightmapPixelError = source.heightmapPixelError;
            target.basemapDistance = source.basemapDistance;
            target.shadowCastingMode = source.shadowCastingMode;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.renderingLayerMask = source.renderingLayerMask;
            target.drawHeightmap = source.drawHeightmap;
            target.drawTreesAndFoliage = source.drawTreesAndFoliage;
            target.detailObjectDistance = source.detailObjectDistance;
            target.detailObjectDensity = source.detailObjectDensity;
            target.treeDistance = source.treeDistance;
            target.treeBillboardDistance = source.treeBillboardDistance;
            target.treeCrossFadeLength = source.treeCrossFadeLength;
            target.treeMaximumFullLODCount = source.treeMaximumFullLODCount;

            TerrainCollider sourceCollider = source.GetComponent<TerrainCollider>();
            TerrainCollider targetCollider = target.GetComponent<TerrainCollider>();
            if (sourceCollider != null && targetCollider != null)
            {
                targetCollider.sharedMaterial = sourceCollider.sharedMaterial;
                targetCollider.enabled = sourceCollider.enabled;
            }
        }
    }
}
