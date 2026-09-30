using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// CPU copy of the terrain's tree instances. Positions are normalized terrain coordinates (x, z in [0,1],
    /// y the normalized height). Tools add and remove trees here; <see cref="TerrainTile"/> uploads the changes.
    /// </summary>
    public sealed class TreeField
    {
        // Smallest grid cell for spacing checks in meters, so tiny spacings don't make a huge grid
        private const float MinGridCell = 0.25f;

        /// <summary>The trees, in the order the terrain stores them.</summary>
        internal readonly List<TreeInstance> Instances = new List<TreeInstance>();

        // Tree positions in meters by grid cell, for spacing checks that don't look at every tree (see AnyWithin)
        private readonly Dictionary<Vector2Int, List<Vector2>> _grid = new Dictionary<Vector2Int, List<Vector2>>();
        private bool _gridValid;
        private float _gridCell;
        private Vector3 _gridTerrainSize;

        // Trees added and removed since BeginTracking, for undo without copying the whole tree list
        private bool _tracking;
        private readonly Dictionary<TreeKey, TreeInstance> _added = new Dictionary<TreeKey, TreeInstance>();
        private readonly List<TreeInstance> _removed = new List<TreeInstance>();
        private readonly HashSet<TreeKey> _matchKeys = new HashSet<TreeKey>();

        /// <summary>Number of tree types (prototypes) on the terrain.</summary>
        public int TypeCount { get; internal set; }

        /// <summary>Number of trees.</summary>
        public int Count => Instances.Count;

        /// <summary>The tree at an index.</summary>
        public TreeInstance this[int index] => Instances[index];

        /// <summary>True when trees were added, removed or moved since the last upload to the terrain.</summary>
        internal bool Changed { get; set; }

        /// <summary>Adds a tree.</summary>
        public void Add(TreeInstance tree)
        {
            Instances.Add(tree);
            Changed = true;
            if (_gridValid)
                AddToGrid(tree);
            if (_tracking)
                _added[new TreeKey(tree)] = tree;
        }

        /// <summary>Removes every tree that matches; returns how many were removed.</summary>
        public int RemoveAll(Predicate<TreeInstance> match)
        {
            int count = Instances.Count;
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                TreeInstance tree = Instances[i];
                if (match(tree))
                {
                    if (_tracking && !_added.Remove(new TreeKey(tree)))
                        _removed.Add(tree);
                    continue;
                }
                if (kept != i)
                    Instances[kept] = tree;
                kept++;
            }

            int removed = count - kept;
            if (removed > 0)
            {
                Instances.RemoveRange(kept, removed);
                Changed = true;
                _gridValid = false;
            }
            return removed;
        }

        /// <summary>True when a tree already stands closer than <paramref name="minDistance"/> meters (measured on the ground plane).</summary>
        public bool AnyWithin(Vector2 normalizedPosition, float minDistance, Vector3 terrainSize)
        {
            if (Instances.Count == 0)
                return false;
            EnsureGrid(minDistance, terrainSize);

            Vector2 position = new Vector2(normalizedPosition.x * terrainSize.x, normalizedPosition.y * terrainSize.z);
            Vector2Int center = GetCell(position);
            int reach = Mathf.CeilToInt(minDistance / _gridCell);
            float minSqr = minDistance * minDistance;
            for (int z = center.y - reach; z <= center.y + reach; z++)
            {
                for (int x = center.x - reach; x <= center.x + reach; x++)
                {
                    if (!_grid.TryGetValue(new Vector2Int(x, z), out List<Vector2> cell))
                        continue;
                    foreach (Vector2 other in cell)
                    {
                        if ((other - position).sqrMagnitude < minSqr)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Replaces all trees.</summary>
        internal void Set(TreeInstance[] trees)
        {
            Instances.Clear();
            Instances.AddRange(trees);
            Changed = true;
            _gridValid = false;
        }

        /// <summary>Starts recording the trees added and removed, for an undo step.</summary>
        internal void BeginTracking()
        {
            _added.Clear();
            _removed.Clear();
            _tracking = true;
        }

        /// <summary>Stops recording and returns what was added and removed since <see cref="BeginTracking"/>.</summary>
        /// <returns>False when nothing changed.</returns>
        internal bool EndTracking(out TreeInstance[] added, out TreeInstance[] removed)
        {
            _tracking = false;
            added = new TreeInstance[_added.Count];
            _added.Values.CopyTo(added, 0);
            removed = _removed.ToArray();
            _added.Clear();
            _removed.Clear();
            return added.Length > 0 || removed.Length > 0;
        }

        /// <summary>Stops recording and forgets what was recorded.</summary>
        internal void CancelTracking()
        {
            _tracking = false;
            _added.Clear();
            _removed.Clear();
        }

        /// <summary>Removes the trees standing where the given ones stand (same type and ground position, any height).</summary>
        internal void RemoveMatching(TreeInstance[] trees)
        {
            if (trees.Length == 0)
                return;
            _matchKeys.Clear();
            foreach (TreeInstance tree in trees)
                _matchKeys.Add(new TreeKey(tree));
            RemoveAll(tree => _matchKeys.Contains(new TreeKey(tree)));
            _matchKeys.Clear();
        }

        /// <summary>Builds the spacing grid when it is missing or its cells don't suit the distance being checked.</summary>
        private void EnsureGrid(float minDistance, Vector3 terrainSize)
        {
            float cell = Mathf.Max(minDistance, MinGridCell);
            if (_gridValid && terrainSize == _gridTerrainSize && cell >= _gridCell * 0.5f && cell <= _gridCell * 2f)
                return;

            _gridCell = cell;
            _gridTerrainSize = terrainSize;
            _grid.Clear();
            foreach (TreeInstance tree in Instances)
                AddToGrid(tree);
            _gridValid = true;
        }

        /// <summary>Puts a tree's position into the spacing grid.</summary>
        private void AddToGrid(TreeInstance tree)
        {
            Vector2 position = new Vector2(tree.position.x * _gridTerrainSize.x, tree.position.z * _gridTerrainSize.z);
            Vector2Int key = GetCell(position);
            if (!_grid.TryGetValue(key, out List<Vector2> cell))
            {
                cell = new List<Vector2>(4);
                _grid.Add(key, cell);
            }
            cell.Add(position);
        }

        /// <summary>The spacing grid cell of a position in meters.</summary>
        private Vector2Int GetCell(Vector2 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / _gridCell), Mathf.FloorToInt(position.y / _gridCell));
        }

        /// <summary>Identifies a tree by its type and ground position; the height changes when trees are put on the ground.</summary>
        private readonly struct TreeKey : IEquatable<TreeKey>
        {
            private readonly float _x;
            private readonly float _z;
            private readonly int _type;

            public TreeKey(TreeInstance tree)
            {
                _x = tree.position.x;
                _z = tree.position.z;
                _type = tree.prototypeIndex;
            }

            public bool Equals(TreeKey other)
            {
                return _x.Equals(other._x) && _z.Equals(other._z) && _type == other._type;
            }

            public override bool Equals(object obj)
            {
                return obj is TreeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_x, _z, _type);
            }
        }
    }
}
