using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// One application of a brush, in world space.
    /// </summary>
    public readonly struct BrushDab
    {
        /// <summary>World position of the brush center. Only X and Z are used.</summary>
        public readonly Vector3 Center;
        /// <summary>Brush diameter in meters.</summary>
        public readonly float Diameter;
        /// <summary>The brush shape (weights).</summary>
        public readonly BrushShape Shape;
        /// <summary>Tool specific amount for this dab, usually already scaled by the frame time.</summary>
        public readonly float Strength;
        /// <summary>Brush rotation around the up axis in degrees.</summary>
        public readonly float Rotation;

        /// <summary>Creates a dab at a world position.</summary>
        public BrushDab(Vector3 center, float diameter, BrushShape shape, float strength, float rotation = 0f)
        {
            Center = center;
            Diameter = diameter;
            Shape = shape;
            Strength = strength;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Where a dab lands on one terrain: its center in normalized terrain coordinates, size and rotation.
    /// Answers "how strong is the brush here" for any point, which tools that don't work on a grid (like trees) use.
    /// </summary>
    public readonly struct BrushArea
    {
        /// <summary>Brush center in normalized terrain coordinates (x along X, y along Z).</summary>
        public readonly Vector2 Center;
        /// <summary>Brush radius in meters.</summary>
        public readonly float Radius;
        /// <summary>Rotation around the up axis in degrees.</summary>
        public readonly float Rotation;
        /// <summary>The brush shape (weights).</summary>
        public readonly BrushShape Shape;
        /// <summary>Size of the terrain in meters, to convert between meters and normalized coordinates.</summary>
        public readonly Vector3 TerrainSize;

        // Cosine and sine of the rotation, worked out once per dab
        private readonly float _cos;
        private readonly float _sin;

        /// <summary>Half the width of the (rotated) brush square's bounding box, in meters.</summary>
        public float Extent => Radius * (Mathf.Abs(_cos) + Mathf.Abs(_sin));

        /// <summary>Area covered by the brush square in square meters.</summary>
        public float AreaSquareMeters => 4f * Radius * Radius;

        /// <summary>Places a brush on a terrain; the center is in normalized terrain coordinates, the radius in meters.</summary>
        public BrushArea(Vector2 center, float radius, float rotation, BrushShape shape, Vector3 terrainSize)
        {
            Center = center;
            Radius = radius;
            Rotation = rotation;
            Shape = shape;
            TerrainSize = terrainSize;
            float radians = rotation * Mathf.Deg2Rad;
            _cos = Mathf.Cos(radians);
            _sin = Mathf.Sin(radians);
        }

        /// <summary>Brush weight [0,1] at a normalized terrain position; 0 outside the brush.</summary>
        public float WeightAt(Vector2 normalizedPosition)
        {
            return WeightAtOffset((normalizedPosition.x - Center.x) * TerrainSize.x, (normalizedPosition.y - Center.y) * TerrainSize.z);
        }

        /// <summary>Brush weight [0,1] at an offset in meters from the brush center; 0 outside the brush.</summary>
        public float WeightAtOffset(float offsetX, float offsetZ)
        {
            Vector2 uv = ToBrushUV(offsetX, offsetZ);
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                return 0f;
            return Shape.Sample(uv.x, uv.y);
        }

        /// <summary>Brush texture coordinates for an offset in meters from the brush center (outside [0,1] means off the brush).</summary>
        public Vector2 ToBrushUV(float offsetX, float offsetZ)
        {
            return ToBrushUV(offsetX, offsetZ, Radius, _cos, _sin);
        }

        /// <summary>
        /// The one mapping from world offsets to brush texture coordinates, shared by the tools and the brush preview.
        /// </summary>
        public static Vector2 ToBrushUV(float offsetX, float offsetZ, float radius, float cos, float sin)
        {
            // Rotate the offset into the brush's own axes
            float brushX = offsetX * cos + offsetZ * sin;
            float brushZ = -offsetX * sin + offsetZ * cos;
            float scale = 0.5f / Mathf.Max(radius, 1e-5f);
            return new Vector2(0.5f + brushX * scale, 0.5f + brushZ * scale);
        }
    }

    /// <summary>
    /// The grid cells one dab covers and the brush weight [0,1] of each of them.
    /// </summary>
    public readonly struct BrushFootprint
    {
        private readonly float[] _weights;

        /// <summary>Covered cells: x/xMax are columns along terrain X, y/yMax are rows along terrain Z.</summary>
        public RectInt Cells { get; }

        /// <summary>True when the brush covers no cells (e.g. it is off the terrain).</summary>
        public bool IsEmpty => Cells.width <= 0 || Cells.height <= 0;

        /// <summary>Brush weight of one cell (absolute column x and row z, inside <see cref="Cells"/>).</summary>
        public float this[int x, int z] => _weights[(z - Cells.yMin) * Cells.width + x - Cells.xMin];

        /// <summary>Wraps rasterized weights; use <see cref="Rasterize"/> to make one.</summary>
        private BrushFootprint(RectInt cells, float[] weights)
        {
            Cells = cells;
            _weights = weights;
        }

        /// <summary>Weights of row z (absolute row index), one per column from Cells.xMin.</summary>
        public ReadOnlySpan<float> Row(int z)
        {
            return new ReadOnlySpan<float>(_weights, (z - Cells.yMin) * Cells.width, Cells.width);
        }

        /// <summary>
        /// Rasterizes a brush onto a field. The weight buffer is reused between calls and grown when needed.
        /// </summary>
        internal static BrushFootprint Rasterize(TerrainGridField field, in BrushArea area, ref float[] buffer)
        {
            if (area.Radius <= 0f)
                return default;

            float cellX = area.TerrainSize.x / field.CellsPerUnit;
            float cellZ = area.TerrainSize.z / field.CellsPerUnit;
            float centerX = field.ToCell(area.Center.x);
            float centerZ = field.ToCell(area.Center.y);
            float extent = area.Extent;

            int last = field.Resolution - 1;
            ClampSpan(centerX, extent / cellX, last, out int xMin, out int xMax);
            ClampSpan(centerZ, extent / cellZ, last, out int zMin, out int zMax);
            if (xMin > xMax || zMin > zMax)
                return default; // entirely off the terrain

            int width = xMax - xMin + 1;
            int height = zMax - zMin + 1;
            if (buffer == null || buffer.Length < width * height)
                buffer = new float[Mathf.NextPowerOfTwo(width * height)];

            if (width == 1 && height == 1)
            {
                // A brush smaller than a cell still affects the cell under its center
                buffer[0] = area.Shape.Sample(0.5f, 0.5f);
                return new BrushFootprint(new RectInt(xMin, zMin, 1, 1), buffer);
            }

            int i = 0;
            for (int z = zMin; z <= zMax; z++)
            {
                float offsetZ = (z - centerZ) * cellZ;
                for (int x = xMin; x <= xMax; x++)
                {
                    buffer[i++] = area.WeightAtOffset((x - centerX) * cellX, offsetZ);
                }
            }
            return new BrushFootprint(new RectInt(xMin, zMin, width, height), buffer);
        }

        /// <summary>
        /// Cells whose centers fall inside [center - radius, center + radius], clamped to the grid; a brush thinner than a
        /// cell still gets the nearest one.
        /// </summary>
        private static void ClampSpan(float center, float radius, int last, out int min, out int max)
        {
            min = Mathf.CeilToInt(center - radius);
            max = Mathf.FloorToInt(center + radius);
            if (min > max)
                min = max = Mathf.RoundToInt(center);
            min = Mathf.Max(min, 0);
            max = Mathf.Min(max, last);
        }
    }
}
