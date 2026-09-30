using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Saves and loads the editable data of one terrain or several terrain tiles (heights, painted layers, grass/details,
    /// trees and holes) as bytes, and where each tile was. Layers and vegetation types are stored by name and matched by
    /// name when loading, so a save keeps working when the palette order changes. Different resolutions or terrain heights
    /// are resampled to fit the terrain it loads into. <see cref="Load(byte[], IReadOnlyList{TerrainData})"/> matches
    /// tiles in order; <see cref="TerrainEditor"/> uses the saved tile positions to add and remove tiles to match first.
    /// </summary>
    public static class TerrainSaveFile
    {
        /// <summary>File extension for save files, with the dot.</summary>
        public const string FileExtension = ".rterrain";

        private const int Magic = 0x31455452; // "RTE1"
        // 1: one terrain without holes. 2: a list of tiles, each with holes. 3: tiles can have their world position.
        private const int Version = 3;

        // Larger than any terrain map Unity supports; bigger values in a file mean it is damaged
        private const int MaxResolution = 16385;
        // Saved bytes per tree: type, position, width, height, rotation, color, lightmap color
        private const int TreeRecordSize = 4 + 12 + 4 + 4 + 4 + 4 + 4;

        /// <summary>
        /// The terrain's heights (16 bit), painted layers and grass (8 bit), trees and holes, with the names of its layers,
        /// grass types and tree types.
        /// </summary>
        public static byte[] Save(TerrainData data)
        {
            return Save(new[] { data });
        }

        /// <summary>Like <see cref="Save(TerrainData)"/>, for every tile of a multi-tile terrain (without tile positions).</summary>
        public static byte[] Save(IReadOnlyList<TerrainData> tiles)
        {
            return Write(tiles, null);
        }

        /// <summary>
        /// Like <see cref="Save(TerrainData)"/>, for every tile of a multi-tile terrain, including where each tile is, so
        /// loading can bring back tiles that were added or removed.
        /// </summary>
        public static byte[] Save(IReadOnlyList<Terrain> tiles)
        {
            List<TerrainData> data = new List<TerrainData>(tiles.Count);
            List<Vector3> positions = new List<Vector3>(tiles.Count);
            foreach (Terrain tile in tiles)
            {
                data.Add(tile.terrainData);
                positions.Add(tile.GetPosition());
            }
            return Write(data, positions);
        }

        /// <summary>
        /// Writes saved data into a terrain. Returns warnings (unknown layers or vegetation types).
        /// The whole file is read first, so a file that isn't a terrain save, or is damaged, throws before the terrain changes.
        /// </summary>
        public static List<string> Load(byte[] bytes, TerrainData data)
        {
            return Load(bytes, new[] { data });
        }

        /// <summary>
        /// Like <see cref="Load(byte[], TerrainData)"/>, for every tile of a multi-tile terrain. Saved tiles are matched
        /// to the tiles in order; extra tiles on either side are left as they are.
        /// </summary>
        public static List<string> Load(byte[] bytes, IReadOnlyList<TerrainData> tiles)
        {
            return Apply(Read(bytes), tiles);
        }

        /// <summary>
        /// Reads and checks a whole save file without touching any terrain. Throws when the bytes aren't a terrain save or
        /// are damaged. Write it into the terrain with <see cref="Apply"/>.
        /// </summary>
        public static Contents Read(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Magic)
                    throw new InvalidDataException("Not a runtime terrain save file.");
                int version = reader.ReadInt32();
                if (version > Version)
                    throw new InvalidDataException("The save file is from a newer version (" + version + ").");

                // Version 1 files hold one terrain without the tile count
                int tileCount = version >= 2 ? ReadCount(reader, 1) : 1;
                SavedTerrain[] tiles = new SavedTerrain[tileCount];
                for (int i = 0; i < tileCount; i++)
                    tiles[i] = ReadTile(reader, version);
                return new Contents(tiles);
            }
        }

        /// <summary>
        /// Writes a save read with <see cref="Read"/> into the tiles, matched in order; extra tiles on either side are
        /// left as they are. Returns warnings (unknown layers or vegetation types, a different tile count).
        /// </summary>
        public static List<string> Apply(Contents contents, IReadOnlyList<TerrainData> tiles)
        {
            SavedTerrain[] saved = contents.Tiles;
            List<string> warnings = new List<string>();
            if (saved.Length != tiles.Count)
                warnings.Add("The save has " + saved.Length + " terrain tile(s) and the terrain has " + tiles.Count + "; tiles were matched in order.");
            for (int i = 0; i < Mathf.Min(saved.Length, tiles.Count); i++)
            {
                ApplyHeights(saved[i], tiles[i]);
                ApplyLayers(saved[i], tiles[i], warnings);
                ApplyDetails(saved[i], tiles[i], warnings);
                ApplyTrees(saved[i], tiles[i], warnings);
                ApplyHoles(saved[i], tiles[i]);
            }
            // Every tile reports the same missing layer or type
            return warnings.Distinct().ToList();
        }

        /// <summary>Resamples a square [z, x] grid to another resolution (bilinear when smooth, else nearest).</summary>
        public static float[,] Resample(float[,] source, int resolution, bool smooth)
        {
            int sourceResolution = source.GetLength(0);
            if (sourceResolution == resolution)
                return source;

            float[,] result = new float[resolution, resolution];
            float scale = (sourceResolution - 1f) / Mathf.Max(resolution - 1f, 1f);
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float sourceX = x * scale;
                    float sourceZ = z * scale;
                    if (!smooth)
                    {
                        result[z, x] = source[Mathf.RoundToInt(sourceZ), Mathf.RoundToInt(sourceX)];
                        continue;
                    }
                    int x0 = (int)sourceX;
                    int z0 = (int)sourceZ;
                    int x1 = Mathf.Min(x0 + 1, sourceResolution - 1);
                    int z1 = Mathf.Min(z0 + 1, sourceResolution - 1);
                    float fx = sourceX - x0;
                    float fz = sourceZ - z0;
                    result[z, x] = Mathf.Lerp(Mathf.Lerp(source[z0, x0], source[z0, x1], fx), Mathf.Lerp(source[z1, x0], source[z1, x1], fx), fz);
                }
            }
            return result;
        }

        /// <summary>Writes a whole save file: the tile count, then every tile with its position when there are positions.</summary>
        /// <param name="positions">World position of each tile, or null to save without positions.</param>
        private static byte[] Write(IReadOnlyList<TerrainData> tiles, IReadOnlyList<Vector3> positions)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(tiles.Count);
                for (int i = 0; i < tiles.Count; i++)
                {
                    writer.Write(positions != null);
                    if (positions != null)
                        WriteVector3(writer, positions[i]);
                    WriteTile(writer, tiles[i]);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Writes one terrain's data.</summary>
        private static void WriteTile(BinaryWriter writer, TerrainData data)
        {
            WriteVector3(writer, data.size);

            // Heights: 16 bit, the precision Unity stores them with
            int heightResolution = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, heightResolution, heightResolution);
            writer.Write(heightResolution);
            for (int z = 0; z < heightResolution; z++)
                for (int x = 0; x < heightResolution; x++)
                    writer.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01(heights[z, x]) * 65535f));

            // Painted layers: 8 bit weights
            TerrainLayer[] layers = data.terrainLayers;
            int alphaResolution = data.alphamapResolution;
            WriteNames(writer, Array.ConvertAll(layers, layer => layer != null ? layer.name : string.Empty));
            writer.Write(alphaResolution);
            if (layers.Length > 0)
            {
                float[,,] weights = data.GetAlphamaps(0, 0, alphaResolution, alphaResolution);
                for (int z = 0; z < alphaResolution; z++)
                    for (int x = 0; x < alphaResolution; x++)
                        for (int layer = 0; layer < layers.Length; layer++)
                            writer.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(weights[z, x, layer]) * 255f));
            }

            // Details (grass): one byte per cell and type, in the terrain's own units
            DetailPrototype[] detailTypes = data.detailPrototypes;
            int detailResolution = data.detailResolution;
            WriteNames(writer, Array.ConvertAll(detailTypes, GetDetailName));
            writer.Write(detailResolution);
            writer.Write(GetMaxDetailDensity(data));
            if (detailResolution > 0)
            {
                for (int type = 0; type < detailTypes.Length; type++)
                {
                    int[,] layer = data.GetDetailLayer(0, 0, detailResolution, detailResolution, type);
                    for (int z = 0; z < detailResolution; z++)
                        for (int x = 0; x < detailResolution; x++)
                            writer.Write((byte)Mathf.Clamp(layer[z, x], 0, 255));
                }
            }

            // Trees
            TreePrototype[] treeTypes = data.treePrototypes;
            WriteNames(writer, Array.ConvertAll(treeTypes, type => type.prefab != null ? type.prefab.name : string.Empty));
            TreeInstance[] trees = data.treeInstances;
            writer.Write(trees.Length);
            foreach (TreeInstance tree in trees)
            {
                writer.Write(tree.prototypeIndex);
                WriteVector3(writer, tree.position);
                writer.Write(tree.widthScale);
                writer.Write(tree.heightScale);
                writer.Write(tree.rotation);
                WriteColor(writer, tree.color);
                WriteColor(writer, tree.lightmapColor);
            }

            // Holes: one bit per heightmap quad, set where the surface is there
            int holesResolution = data.holesResolution;
            bool[,] surface = data.GetHoles(0, 0, holesResolution, holesResolution);
            byte[] bits = new byte[(holesResolution * holesResolution + 7) / 8];
            for (int z = 0; z < holesResolution; z++)
            {
                for (int x = 0; x < holesResolution; x++)
                {
                    int index = z * holesResolution + x;
                    if (surface[z, x])
                        bits[index >> 3] |= (byte)(1 << (index & 7));
                }
            }
            writer.Write(holesResolution);
            writer.Write(bits);
        }

        /// <summary>Reads one terrain's data (and, from version 3, its position when it was saved with one).</summary>
        private static SavedTerrain ReadTile(BinaryReader reader, int version)
        {
            SavedTerrain saved = new SavedTerrain();
            if (version >= 3)
            {
                saved.HasPosition = reader.ReadBoolean();
                if (saved.HasPosition)
                    saved.Position = ReadVector3(reader);
            }
            saved.Size = ReadVector3(reader);

            saved.HeightResolution = ReadResolution(reader, 2);
            byte[] heightBytes = ReadExactly(reader, (long)saved.HeightResolution * saved.HeightResolution * 2);
            saved.Heights = new ushort[heightBytes.Length / 2];
            for (int i = 0; i < saved.Heights.Length; i++)
                saved.Heights[i] = (ushort)(heightBytes[i * 2] | (heightBytes[i * 2 + 1] << 8)); // BinaryWriter is little-endian

            saved.LayerNames = ReadNames(reader);
            saved.AlphaResolution = ReadResolution(reader, 0);
            saved.Weights = saved.LayerNames.Length > 0
                ? ReadExactly(reader, (long)saved.AlphaResolution * saved.AlphaResolution * saved.LayerNames.Length)
                : Array.Empty<byte>();

            saved.DetailNames = ReadNames(reader);
            saved.DetailResolution = ReadResolution(reader, 0);
            saved.DetailMaxDensity = reader.ReadSingle();
            saved.DetailLayers = new byte[saved.DetailResolution > 0 ? saved.DetailNames.Length : 0][];
            for (int type = 0; type < saved.DetailLayers.Length; type++)
                saved.DetailLayers[type] = ReadExactly(reader, (long)saved.DetailResolution * saved.DetailResolution);

            saved.TreeNames = ReadNames(reader);
            saved.Trees = new TreeInstance[ReadCount(reader, TreeRecordSize)];
            for (int i = 0; i < saved.Trees.Length; i++)
            {
                saved.Trees[i] = new TreeInstance
                {
                    prototypeIndex = reader.ReadInt32(),
                    position = ReadVector3(reader),
                    widthScale = reader.ReadSingle(),
                    heightScale = reader.ReadSingle(),
                    rotation = reader.ReadSingle(),
                    color = ReadColor(reader),
                    lightmapColor = ReadColor(reader),
                };
            }

            if (version >= 2)
            {
                saved.HolesResolution = ReadResolution(reader, 0);
                saved.Holes = ReadExactly(reader, ((long)saved.HolesResolution * saved.HolesResolution + 7) / 8);
            }
            else
            {
                saved.Holes = Array.Empty<byte>();
            }
            return saved;
        }

        /// <summary>Writes the saved heights, converted to this terrain's height range and resolution.</summary>
        private static void ApplyHeights(SavedTerrain saved, TerrainData data)
        {
            int resolution = saved.HeightResolution;
            float heightScale = saved.Size.y / Mathf.Max(data.size.y, 0.001f);
            float[,] heights = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                    heights[z, x] = Mathf.Clamp01(saved.Heights[z * resolution + x] / 65535f * heightScale);
            data.SetHeights(0, 0, Resample(heights, data.heightmapResolution, true));
        }

        /// <summary>
        /// Writes the painted layers onto the layers with the same names, resampled to this terrain's alphamap resolution.
        /// </summary>
        private static void ApplyLayers(SavedTerrain saved, TerrainData data, List<string> warnings)
        {
            string[] savedNames = saved.LayerNames;
            int savedResolution = saved.AlphaResolution;
            int layerCount = data.alphamapLayers;
            if (savedNames.Length == 0)
            {
                // Nothing was painted: show the base layer everywhere
                if (layerCount > 0)
                {
                    float[,,] baseOnly = new float[data.alphamapResolution, data.alphamapResolution, layerCount];
                    for (int z = 0; z < data.alphamapResolution; z++)
                        for (int x = 0; x < data.alphamapResolution; x++)
                            baseOnly[z, x, 0] = 1f;
                    data.SetAlphamaps(0, 0, baseOnly);
                }
                return;
            }

            byte[] savedWeights = saved.Weights;
            if (layerCount == 0)
            {
                warnings.Add("The terrain has no layers, the painted textures were skipped.");
                return;
            }

            string[] currentNames = Array.ConvertAll(data.terrainLayers, layer => layer != null ? layer.name : string.Empty);
            int[] target = MatchByName(savedNames, currentNames, "Terrain layer", warnings);

            int resolution = data.alphamapResolution;
            float[,,] weights = new float[resolution, resolution, layerCount];
            for (int z = 0; z < resolution; z++)
            {
                int sourceZ = Mathf.Min(z * savedResolution / resolution, savedResolution - 1);
                for (int x = 0; x < resolution; x++)
                {
                    int sourceX = Mathf.Min(x * savedResolution / resolution, savedResolution - 1);
                    int source = (sourceZ * savedResolution + sourceX) * savedNames.Length;
                    float sum = 0f;
                    for (int layer = 0; layer < savedNames.Length; layer++)
                    {
                        if (target[layer] < 0)
                            continue;
                        float weight = savedWeights[source + layer] / 255f;
                        weights[z, x, target[layer]] += weight;
                        sum += weight;
                    }

                    // 8 bit weights don't add up to exactly 1, and unknown layers leave gaps: fill up the base layer
                    if (sum < 1f)
                    {
                        weights[z, x, 0] += 1f - sum;
                    }
                    else
                    {
                        for (int layer = 0; layer < layerCount; layer++)
                            weights[z, x, layer] /= sum;
                    }
                }
            }
            data.SetAlphamaps(0, 0, weights);
        }

        /// <summary>
        /// Clears all grass, then writes each saved grass type onto the type with the same name, resampled to this
        /// terrain's detail resolution and density range.
        /// </summary>
        private static void ApplyDetails(SavedTerrain saved, TerrainData data, List<string> warnings)
        {
            string[] savedNames = saved.DetailNames;
            int savedResolution = saved.DetailResolution;
            int currentResolution = data.detailResolution;
            DetailPrototype[] currentTypes = data.detailPrototypes;
            int[] target = MatchByName(savedNames, Array.ConvertAll(currentTypes, GetDetailName), "Detail type", warnings);

            // Start from no details, then write every saved type that exists here
            int[,] layer = currentResolution > 0 ? new int[currentResolution, currentResolution] : null;
            if (layer != null)
            {
                for (int type = 0; type < currentTypes.Length; type++)
                    data.SetDetailLayer(0, 0, type, layer);
            }

            if (savedResolution == 0 || savedNames.Length == 0)
                return;

            float scale = GetMaxDetailDensity(data) / Mathf.Max(saved.DetailMaxDensity, 1f);
            for (int type = 0; type < savedNames.Length; type++)
            {
                if (target[type] < 0 || layer == null)
                    continue;
                byte[] savedLayer = saved.DetailLayers[type];
                for (int z = 0; z < currentResolution; z++)
                {
                    int sourceZ = Mathf.Min(z * savedResolution / currentResolution, savedResolution - 1);
                    for (int x = 0; x < currentResolution; x++)
                    {
                        int sourceX = Mathf.Min(x * savedResolution / currentResolution, savedResolution - 1);
                        layer[z, x] = Mathf.RoundToInt(savedLayer[sourceZ * savedResolution + sourceX] * scale);
                    }
                }
                data.SetDetailLayer(0, 0, target[type], layer);
            }
            if (layer == null)
                warnings.Add("The terrain has no detail resolution, grass was skipped.");
        }

        /// <summary>Replaces the terrain's trees with the saved ones whose type exists here (matched by prefab name).</summary>
        private static void ApplyTrees(SavedTerrain saved, TerrainData data, List<string> warnings)
        {
            string[] currentNames = Array.ConvertAll(data.treePrototypes, type => type.prefab != null ? type.prefab.name : string.Empty);
            int[] target = MatchByName(saved.TreeNames, currentNames, "Tree type", warnings);

            List<TreeInstance> trees = new List<TreeInstance>(saved.Trees.Length);
            foreach (TreeInstance savedTree in saved.Trees)
            {
                int type = savedTree.prototypeIndex;
                if (type < 0 || type >= target.Length || target[type] < 0)
                    continue;
                TreeInstance tree = savedTree;
                tree.prototypeIndex = target[type];
                trees.Add(tree);
            }
            data.SetTreeInstances(trees.ToArray(), true);
        }

        /// <summary>
        /// Writes the saved holes, resampled to this terrain's hole resolution. Saves without holes (version 1) close all holes.
        /// </summary>
        private static void ApplyHoles(SavedTerrain saved, TerrainData data)
        {
            int resolution = data.holesResolution;
            int savedResolution = saved.HolesResolution;
            bool[,] surface = new bool[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                int sourceZ = savedResolution > 0 ? Mathf.Min(z * savedResolution / resolution, savedResolution - 1) : 0;
                for (int x = 0; x < resolution; x++)
                {
                    if (savedResolution == 0)
                    {
                        surface[z, x] = true;
                        continue;
                    }
                    int sourceX = Mathf.Min(x * savedResolution / resolution, savedResolution - 1);
                    int index = sourceZ * savedResolution + sourceX;
                    surface[z, x] = (saved.Holes[index >> 3] & (1 << (index & 7))) != 0;
                }
            }
            data.SetHoles(0, 0, surface);
        }

        /// <summary>
        /// For each saved name, the index of the same name on this terrain. Falls back to the same index, or -1 when
        /// there is none, and adds a warning for every name that wasn't found.
        /// </summary>
        private static int[] MatchByName(string[] saved, string[] current, string kind, List<string> warnings)
        {
            int[] target = new int[saved.Length];
            for (int i = 0; i < saved.Length; i++)
            {
                target[i] = Array.IndexOf(current, saved[i]);
                if (target[i] >= 0)
                    continue;
                target[i] = i < current.Length ? i : -1;
                warnings.Add(kind + " '" + saved[i] + "' isn't on this terrain" + (target[i] >= 0 ? ", using '" + current[i] + "' instead." : ", skipped."));
            }
            return target;
        }

        /// <summary>Highest value a detail cell can hold: 255 in coverage scatter mode, 16 in instance count mode.</summary>
        private static float GetMaxDetailDensity(TerrainData data)
        {
            return data.detailScatterMode == DetailScatterMode.CoverageMode ? 255f : 16f;
        }

        /// <summary>The name a grass type is saved under: its mesh prefab's name, or its texture's name.</summary>
        private static string GetDetailName(DetailPrototype type)
        {
            if (type.usePrototypeMesh)
                return type.prototype != null ? type.prototype.name : string.Empty;
            return type.prototypeTexture != null ? type.prototypeTexture.name : string.Empty;
        }

        /// <summary>Writes a count followed by the names.</summary>
        private static void WriteNames(BinaryWriter writer, string[] names)
        {
            writer.Write(names.Length);
            foreach (string name in names)
                writer.Write(name ?? string.Empty);
        }

        /// <summary>Reads names written by <see cref="WriteNames"/>.</summary>
        private static string[] ReadNames(BinaryReader reader)
        {
            string[] names = new string[ReadCount(reader, 1)]; // every name takes at least its length byte
            for (int i = 0; i < names.Length; i++)
                names[i] = reader.ReadString();
            return names;
        }

        // The sizes below come from the file, so they are checked against what is left in it before anything is allocated:
        // a damaged file then fails with an error instead of a huge allocation.

        /// <summary>Reads a map resolution and checks that it is between <paramref name="min"/> and the largest Unity supports.</summary>
        private static int ReadResolution(BinaryReader reader, int min)
        {
            int resolution = reader.ReadInt32();
            if (resolution < min || resolution > MaxResolution)
                throw new InvalidDataException("The save file is damaged (resolution " + resolution + ").");
            return resolution;
        }

        /// <summary>Reads an item count and checks that the file still has room for that many items.</summary>
        private static int ReadCount(BinaryReader reader, int bytesPerItem)
        {
            int count = reader.ReadInt32();
            if (count < 0 || (long)count * bytesPerItem > GetBytesLeft(reader))
                throw new InvalidDataException("The save file is damaged or cut short.");
            return count;
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes, or throws when the file is shorter.</summary>
        private static byte[] ReadExactly(BinaryReader reader, long count)
        {
            if (count > GetBytesLeft(reader))
                throw new InvalidDataException("The save file is cut short.");
            return reader.ReadBytes((int)count);
        }

        /// <summary>Bytes left to read in the file.</summary>
        private static long GetBytesLeft(BinaryReader reader)
        {
            return reader.BaseStream.Length - reader.BaseStream.Position;
        }

        /// <summary>Writes a vector as three floats.</summary>
        private static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        /// <summary>Reads a vector written by <see cref="WriteVector3"/>.</summary>
        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        /// <summary>Writes a color as four bytes.</summary>
        private static void WriteColor(BinaryWriter writer, Color32 color)
        {
            writer.Write(color.r);
            writer.Write(color.g);
            writer.Write(color.b);
            writer.Write(color.a);
        }

        /// <summary>Reads a color written by <see cref="WriteColor"/>.</summary>
        private static Color32 ReadColor(BinaryReader reader)
        {
            return new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        /// <summary>
        /// A save file read into memory and checked by <see cref="Read"/>, ready to be written into terrain tiles with
        /// <see cref="Apply"/>. Tells where the saved tiles were, so the tiles can be matched to them first.
        /// </summary>
        public sealed class Contents
        {
            /// <summary>Number of saved tiles.</summary>
            public int TileCount => Tiles.Length;

            /// <summary>True when every saved tile has its world position (saves made by TerrainEditor since version 3).</summary>
            public bool HasTilePositions => Tiles.All(tile => tile.HasPosition);

            /// <summary>The saved tiles, in file order.</summary>
            internal SavedTerrain[] Tiles { get; }

            /// <summary>Wraps the tiles read from a file.</summary>
            internal Contents(SavedTerrain[] tiles)
            {
                Tiles = tiles;
            }

            /// <summary>World position of a saved tile (only valid when <see cref="HasTilePositions"/>).</summary>
            public Vector3 GetTilePosition(int index)
            {
                return Tiles[index].Position;
            }

            /// <summary>Size of a saved tile in meters.</summary>
            public Vector3 GetTileSize(int index)
            {
                return Tiles[index].Size;
            }
        }

        /// <summary>Everything a save file holds for one terrain tile, as it was saved.</summary>
        internal sealed class SavedTerrain
        {
            /// <summary>True when the tile was saved with its world position.</summary>
            public bool HasPosition;
            /// <summary>World position of the terrain (only valid when <see cref="HasPosition"/>).</summary>
            public Vector3 Position;
            /// <summary>Terrain size in meters.</summary>
            public Vector3 Size;
            /// <summary>Heightmap samples per side.</summary>
            public int HeightResolution;
            /// <summary>16-bit heights, [z * resolution + x].</summary>
            public ushort[] Heights;
            /// <summary>Names of the painted terrain layers.</summary>
            public string[] LayerNames;
            /// <summary>Alphamap texels per side.</summary>
            public int AlphaResolution;
            /// <summary>8-bit layer weights, [(z * resolution + x) * layers + layer].</summary>
            public byte[] Weights;
            /// <summary>Names of the grass (detail) types.</summary>
            public string[] DetailNames;
            /// <summary>Detail map cells per side (0 when the terrain had no detail resolution).</summary>
            public int DetailResolution;
            /// <summary>Highest density the saving terrain allowed, to rescale to this terrain's scatter mode.</summary>
            public float DetailMaxDensity;
            /// <summary>Per grass type: densities, [z * resolution + x].</summary>
            public byte[][] DetailLayers;
            /// <summary>Names of the tree types.</summary>
            public string[] TreeNames;
            /// <summary>The trees; prototypeIndex is the saved type index.</summary>
            public TreeInstance[] Trees;
            /// <summary>Hole cells per side (0 for saves without holes).</summary>
            public int HolesResolution;
            /// <summary>One bit per cell, [z * resolution + x], set where the surface is there.</summary>
            public byte[] Holes;
        }
    }
}
