using System.Collections.Generic;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Makes a grid of terrain tiles for scenes without a terrain. <see cref="TerrainEditor"/> calls <see cref="Generate"/>
    /// when it finds no Terrain, so the editor always has ground to edit. Set up the tiles' layers, trees and grass on a
    /// Template TerrainData, the same way as on a terrain placed in the scene.
    /// </summary>
    public class TerrainGridGenerator : MonoBehaviour
    {
        [Tooltip("Number of tiles along X and Z")]
        [SerializeField] private Vector2Int tileCount = new Vector2Int(2, 2);
        [Tooltip("Size of one tile in meters: width (X), highest height (Y) and length (Z)")]
        [SerializeField] private Vector3 tileSize = new Vector3(100f, 60f, 100f);
        [Tooltip("World position of the grid's corner with the smallest X and Z")]
        [SerializeField] private Vector3 origin = Vector3.zero;
        [Tooltip("Heightmap samples per tile side; Unity rounds it to 2^n + 1 (33 to 4097)")]
        [SerializeField] private int heightmapResolution = 257;
        [Tooltip("Paint texels per tile side")]
        [SerializeField] private int alphamapResolution = 256;
        [Tooltip("Grass cells per tile side (0: no grass)")]
        [SerializeField, Min(0)] private int detailResolution = 256;
        [Tooltip("Optional. The tiles copy their layers, trees, grass and grass settings from this TerrainData")]
        [SerializeField] private TerrainData template;
        [Tooltip("Optional. Material of the tiles; the render pipeline's default terrain material when empty")]
        [SerializeField] private Material terrainMaterial;

        private readonly List<TerrainData> _createdData = new List<TerrainData>();

        /// <summary>Destroys the TerrainData made for the tiles.</summary>
        private void OnDestroy()
        {
            foreach (TerrainData data in _createdData)
            {
                if (data != null)
                    Destroy(data);
            }
            _createdData.Clear();
        }

        /// <summary>
        /// Creates the tiles as children of this object, connected to each other so their levels of detail match at the
        /// seams, and returns them.
        /// </summary>
        public IReadOnlyList<Terrain> Generate()
        {
            List<Terrain> tiles = new List<Terrain>();
            int groupingId = GetInstanceID(); // tiles with the same grouping ID connect to their neighbours
            for (int z = 0; z < tileCount.y; z++)
            {
                for (int x = 0; x < tileCount.x; x++)
                {
                    Vector3 position = origin + new Vector3(x * tileSize.x, 0f, z * tileSize.z);
                    Terrain terrain = TerrainTileFactory.CreateTerrain(CreateTileData(), position, "Terrain Tile " + x + "_" + z, null, transform);
                    if (terrainMaterial != null)
                        terrain.materialTemplate = terrainMaterial;
                    // GPU instanced terrain redraws from the heightmap texture, so runtime height edits don't rebuild meshes
                    terrain.drawInstanced = true;
                    terrain.groupingID = groupingId;
                    terrain.allowAutoConnect = true;
                    tiles.Add(terrain);
                }
            }
            Terrain.SetConnectivityDirty();
            return tiles;
        }

        /// <summary>A new TerrainData with this grid's tile size and resolutions and the template's settings.</summary>
        private TerrainData CreateTileData()
        {
            TerrainData data = TerrainTileFactory.CreateData(tileSize, heightmapResolution, alphamapResolution, detailResolution, 32, template);
            data.name = "Generated Terrain Tile";
            _createdData.Add(data);
            return data;
        }
    }
}
