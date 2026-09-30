using System;
using System.Collections.Generic;
using RuntimeTerrainEditor.Sculpting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Runtime terrain editor: raise/lower, flatten, smooth, texture painting, trees, grass and holes, with undo/redo and
    /// save/load (in TerrainEditor.SaveLoad.cs). Edits one terrain or a grid of terrain tiles as one surface, and adds and
    /// removes tiles while the game runs (in TerrainEditor.Tiles.cs). Controls come from <see cref="TerrainEditorInput"/>
    /// (by default: left mouse edits, Shift inverts, Ctrl+Z / Ctrl+Y undo and redo, [ ] or Ctrl+scroll resize the brush,
    /// , . or Alt+scroll rotate it, H hides it). UI and gameplay code talk to it through <see cref="Instance"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(TerrainMouseSelection))]
    [RequireComponent(typeof(TerrainBrush))]
    [RequireComponent(typeof(TerrainEditorInput))]
    public partial class TerrainEditor : MonoBehaviour
    {
        // Longest frame time a stroke uses, so a hitch doesn't turn into one huge dab
        private const float MaxStrokeDeltaTime = 0.1f;

        private static TerrainEditor _instance;

        [Header("References")]
        [Tooltip("Terrains to edit, as tiles of one surface. When empty every active Terrain in the scene is used, or, when " +
                 "there is none, a TerrainGridGenerator on this object makes a grid.")]
        [SerializeField] private List<Terrain> terrains = new List<Terrain>();
        [Tooltip("Optional. Draws the brush on the terrain under the mouse.")]
        [SerializeField] private BrushProjector brushProjector;
        [Tooltip("Optional. Shows where tiles can be added and which tile would be removed.")]
        [SerializeField] private TerrainTilePreview tilePreview;
        [Tooltip("Optional. The terrain layers to paint with; without it the terrain keeps its own layers.")]
        [SerializeField] private TerrainLayerPalette layerPalette;
        [Tooltip("Optional. Icons, spacing and height ranges for the terrain's trees and grass, and functions to add more at runtime.")]
        [SerializeField] private TerrainVegetationPalette vegetationPalette;

        [Header("Settings")]
        [Tooltip("Editing is on when play starts (the demo UI switches it on and off with its panels)")]
        [SerializeField] private bool editorEnabled = true;
        [Tooltip("Editing mode when play starts")]
        [SerializeField] private DeformMode currentMode;
        [Tooltip("Trees and Details modes erase everything under the brush instead of painting (Invert flips this while held)")]
        [SerializeField] private bool eraseVegetation;
        [Tooltip("Erasing removes only the selected tree or grass type instead of every type")]
        [SerializeField] private bool eraseSelectedTypeOnly;
        [Tooltip("Height (0-1 of the terrain height) the terrain is reset to, so it can be lowered as well as raised")]
        [SerializeField, Range(0f, 1f)] private float startingHeightPercent = 0.3f;
        [Tooltip("Flatten the terrain, fill the base texture and remove trees and grass when play starts")]
        [SerializeField] private bool resetTerrainOnStart = true;
        [Tooltip("Edit a runtime copy of the TerrainData so play mode never changes the terrain asset")]
        [SerializeField] private bool editRuntimeCopy = true;
        [Tooltip("How many edits can be undone")]
        [SerializeField] private int maxUndoSteps = 30;
        [Tooltip("Memory the undo history may use, in megabytes. The oldest edits are dropped beyond it (the newest is always kept).")]
        [SerializeField, Min(1)] private int maxUndoMemoryMB = 256;
        [Tooltip("Refresh terrain colliders while sculpting. Off refreshes them only when a stroke ends, which is much cheaper " +
                 "on big terrains; the editor's own picking doesn't need the collider.")]
        [SerializeField] private bool syncCollidersDuringStrokes = true;
        [Tooltip("Seconds between terrain collider refreshes while sculpting. Lower keeps physics up to date, higher is cheaper.")]
        [SerializeField, Min(0f)] private float colliderSyncInterval = 0.1f;
        [Tooltip("How far into a new tile the ground blends from its neighbours' edge heights to the starting height, as a fraction of the tile")]
        [SerializeField, Range(0.05f, 1f)] private float newTileBlend = 0.4f;

        [Header("Strokes")]
        [Tooltip("Distance between brush dabs along a stroke, as a fraction of the brush diameter. Fills the gaps when the mouse moves fast.")]
        [SerializeField, Range(0.05f, 1f)] private float brushSpacing = 0.25f;
        [Tooltip("Upper limit of dabs per frame, for very fast strokes with small brushes")]
        [SerializeField, Min(1)] private int maxDabsPerFrame = 32;

        [Header("Brush Response (lowest / highest strength)")]
        [Tooltip("Raise, lower and flatten speed in meters per second")]
        [SerializeField] private Vector2 heightSpeed = new Vector2(0.5f, 60f);
        [Tooltip("Smoothing radius in meters")]
        [SerializeField] private Vector2 smoothRadius = new Vector2(0.5f, 6f);
        [Tooltip("How quickly smoothing converges, per second")]
        [SerializeField, Min(0f)] private float smoothRate = 4f;
        [Tooltip("Paint opacity change per second")]
        [SerializeField] private Vector2 paintRate = new Vector2(0.5f, 8f);
        [Tooltip("How much of the brush area fills with trees per second")]
        [SerializeField] private Vector2 treeRate = new Vector2(0.3f, 4f);
        [Tooltip("Grass density change per second")]
        [SerializeField] private Vector2 detailRate = new Vector2(0.5f, 8f);

        [Header("Events")]
        [Tooltip("Called after every finished edit (stroke, undo, redo, reset, load, import)")]
        [SerializeField] private UnityEvent onTerrainChanged = new UnityEvent();

        private TerrainEditorInput _input;
        private TerrainMouseSelection _mouseSelection;
        private TerrainBrush _terrainBrush;
        private TerrainBaseBoard _terrainBaseBoard;
        private TerrainUndoRedo _undoRedo;
        // The copies edited when Edit Runtime Copy is on; destroyed with the editor
        private readonly List<TerrainData> _runtimeTerrainData = new List<TerrainData>();
        private bool _strokeActive;
        // Where the previous dab of the running stroke landed, so the next frame can fill the gap
        private bool _hasLastDab;
        private Vector3 _lastDab;
        // Layer count when the undo history was last checked, see HandleLayersChanged
        private int _layerCount;
        // Removed tiles, switched off and kept while an undo or redo step can bring them back
        private readonly List<TerrainTile> _detachedTiles = new List<TerrainTile>();
        // Reused list of the slots tiles can be added to, and the tile layout it was made for
        private readonly List<Vector3> _freeSlots = new List<Vector3>();
        private int _freeSlotsVersion = -1;

        private readonly RaiseLowerTool _raiseLowerTool = new RaiseLowerTool();
        private readonly FlattenTool _flattenTool = new FlattenTool();
        private readonly SmoothTool _smoothTool = new SmoothTool();
        private readonly PaintLayerTool _paintTool = new PaintLayerTool();
        private readonly TreePaintTool _treePaintTool = new TreePaintTool();
        private readonly TreeEraseTool _treeEraseTool = new TreeEraseTool();
        private readonly DetailPaintTool _detailTool = new DetailPaintTool();
        private readonly DetailEraseTool _detailEraseTool = new DetailEraseTool();
        private readonly HoleTool _holeTool = new HoleTool();
        private ITerrainTool _customTool;

        /// <summary>Raised when a stroke starts, with the mode it uses.</summary>
        public event Action<DeformMode> StrokeStarted;

        /// <summary>Raised after every finished edit: what changed and where.</summary>
        public event Action<TerrainChange> TerrainChanged;

        /// <summary>The terrain editor in the scene, or null when there is none.</summary>
        public static TerrainEditor Instance => _instance;

        /// <summary>The terrain being edited. Use it to run your own <see cref="ITerrainTool"/>s.</summary>
        public TerrainSurface Surface { get; private set; }

        /// <summary>The paintable terrain layers, or null when the terrain keeps its own layers.</summary>
        public TerrainLayerPalette LayerPalette => layerPalette;

        /// <summary>Extra settings for the terrain's trees and grass (and runtime adding), or null.</summary>
        public TerrainVegetationPalette VegetationPalette => vegetationPalette;

        /// <summary>The brush in use: its shape, size, strength and the other current values.</summary>
        public TerrainBrush Brush => _terrainBrush;

        /// <summary>The controls (Input System actions).</summary>
        public TerrainEditorInput Controls => _input;

        /// <summary>True while a stroke is being painted.</summary>
        public bool IsStrokeActive => _strokeActive;

        /// <summary>True when there is an edit to undo.</summary>
        public bool CanUndo => _undoRedo != null && _undoRedo.UndoCount > 0;

        /// <summary>True when there is an undone edit to redo.</summary>
        public bool CanRedo => _undoRedo != null && _undoRedo.RedoCount > 0;

        /// <summary>True while editing is on (see <see cref="SetEditorEnabled"/>).</summary>
        public bool EditorEnabled => editorEnabled;

        /// <summary>The selected editing mode (see <see cref="SetDeformMode"/>).</summary>
        public DeformMode CurrentMode => currentMode;

        /// <summary>
        /// The mode edits use right now: holding Invert (Shift) swaps Raise and Lower, digging and filling holes, and
        /// adding and removing tiles (see <see cref="DeformModeExtensions.GetOpposite"/>).
        /// </summary>
        public DeformMode ActiveMode => _input != null && _input.Invert.IsPressed() ? currentMode.GetOpposite() : currentMode;

        /// <summary>True when the Trees and Details modes erase instead of paint (see <see cref="SetVegetationErase"/>).</summary>
        public bool EraseVegetation => eraseVegetation;

        /// <summary>True when erasing only removes the selected tree or grass type (see <see cref="SetEraseSelectedTypeOnly"/>).</summary>
        public bool EraseSelectedTypeOnly => eraseSelectedTypeOnly;

        /// <summary>
        /// Registers the editor, finds (or generates) the terrain tiles, makes the runtime terrain copies and sets up the
        /// palettes, brush, raycasting and base board. Disables itself when brush settings or a terrain are missing.
        /// </summary>
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _input = GetComponent<TerrainEditorInput>();
            if (!_input.HasAllActions)
            {
                Debug.LogError("TerrainEditor: assign every action on the TerrainEditorInput component (Input/TerrainEditorControls.inputactions has them all).", this);
                enabled = false;
                return;
            }
            _mouseSelection = GetComponent<TerrainMouseSelection>();
            _terrainBrush = GetComponent<TerrainBrush>();
            _terrainBaseBoard = GetComponent<TerrainBaseBoard>();
            if (layerPalette == null)
                layerPalette = GetComponentInChildren<TerrainLayerPalette>();
            if (vegetationPalette == null)
                vegetationPalette = GetComponentInChildren<TerrainVegetationPalette>();
            if (tilePreview == null)
                tilePreview = GetComponentInChildren<TerrainTilePreview>();
            if (_terrainBrush.Settings == null)
            {
                Debug.LogError("TerrainEditor: assign brush settings on the TerrainBrush component (Create > Runtime Terrain Editor > Brush Settings).", this);
                enabled = false;
                return;
            }

            List<Terrain> tiles = FindTerrains(out bool generated);
            if (tiles.Count == 0)
            {
                Debug.LogError("TerrainEditor: no Terrain found in the scene. Add one, or a TerrainGridGenerator on this object.", this);
                enabled = false;
                return;
            }

            // Generated tiles are runtime objects already, so they need no copy
            List<TerrainData> tileData = new List<TerrainData>(tiles.Count);
            foreach (Terrain tile in tiles)
            {
                if (editRuntimeCopy && !generated)
                    UseRuntimeCopy(tile);
                tileData.Add(tile.terrainData);
                if (tile.terrainData.detailPrototypes.Length > 0 && tile.terrainData.detailResolution == 0)
                    Debug.LogWarning("TerrainEditor: the terrain has grass but its Detail Resolution is 0, so grass can't be painted. Set it in the Terrain settings.", tile);
            }

            // Layer palette first, so the surface picks up the final layers
            if (layerPalette != null)
            {
                layerPalette.Init(tileData);
                layerPalette.LayersChanged += HandleLayersChanged;
            }
            if (vegetationPalette != null)
            {
                vegetationPalette.Init(tileData);
                vegetationPalette.TypesChanged += HandleVegetationTypesChanged;
            }

            Surface = new TerrainSurface(tiles) { HeightSyncInterval = syncCollidersDuringStrokes ? colliderSyncInterval : float.PositiveInfinity };
            _layerCount = Surface.LayerCount;
            _undoRedo = new TerrainUndoRedo(maxUndoSteps, maxUndoMemoryMB * 1024L * 1024L);
            if (resetTerrainOnStart)
            {
                Surface.Fill(startingHeightPercent);
                Surface.ClearVegetation();
            }

            _mouseSelection.Init(Surface);
            _terrainBrush.Init(Surface, brushProjector);
            if (_terrainBaseBoard != null)
                _terrainBaseBoard.Init(Surface);
            SetEditorEnabled(editorEnabled);
        }

        /// <summary>Unregisters the editor and destroys the runtime terrain copies and the removed tiles it kept for undo.</summary>
        private void OnDestroy()
        {
            if (_instance != this)
                return;
            _instance = null;
            if (layerPalette != null)
                layerPalette.LayersChanged -= HandleLayersChanged;
            if (vegetationPalette != null)
                vegetationPalette.TypesChanged -= HandleVegetationTypesChanged;
            foreach (TerrainTile tile in _detachedTiles)
            {
                if (tile.Terrain != null)
                    Destroy(tile.Terrain.gameObject);
            }
            _detachedTiles.Clear();
            foreach (TerrainData data in _runtimeTerrainData)
            {
                if (data != null)
                    Destroy(data);
            }
            _runtimeTerrainData.Clear();
        }

        /// <summary>
        /// Handles shortcuts, finds the terrain under the pointer, moves the brush preview and paints strokes, or in the
        /// tile modes shows and handles adding and removing tiles.
        /// </summary>
        private void Update()
        {
            // Consumed even while editing is off, so steps don't pile up and apply all at once later
            int sizeSteps = _input.ConsumeSizeSteps();
            int rotationSteps = _input.ConsumeRotationSteps();
            if (!editorEnabled)
                return;

            HandleShortcuts(sizeSteps, rotationSteps);

            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (pointerOverUI)
                _mouseSelection.ClearHit();
            else
                _mouseSelection.Raycast(_input.PointerPosition);

            if (ActiveMode.IsTileMode())
            {
                if (_strokeActive)
                    EndStroke();
                HandleTileEditing(pointerOverUI);
                return;
            }

            if (tilePreview != null)
                tilePreview.Hide();
            UpdateBrushPreview();
            HandleStroke();
        }

        /// <summary>
        /// The terrains to edit: the assigned ones, else every active Terrain in the scene, else a grid made by a
        /// <see cref="TerrainGridGenerator"/> on this object.
        /// </summary>
        /// <param name="generated">True when the grid generator made them.</param>
        private List<Terrain> FindTerrains(out bool generated)
        {
            generated = false;
            List<Terrain> tiles = new List<Terrain>(terrains);
            tiles.RemoveAll(tile => tile == null);
            if (tiles.Count == 0)
                tiles.AddRange(FindObjectsByType<Terrain>(FindObjectsSortMode.None));

            TerrainGridGenerator generator = GetComponent<TerrainGridGenerator>();
            if (tiles.Count == 0 && generator != null && generator.enabled)
            {
                tiles.AddRange(generator.Generate());
                generated = true;
            }
            return tiles;
        }

        /// <summary>Makes a terrain (and its collider) use a copy of its TerrainData, so play mode never changes the asset.</summary>
        private void UseRuntimeCopy(Terrain tile)
        {
            TerrainData copy = Instantiate(tile.terrainData);
            copy.name = tile.terrainData.name + " (Runtime)";
            tile.terrainData = copy;
            TerrainCollider terrainCollider = tile.GetComponent<TerrainCollider>();
            if (terrainCollider != null)
                terrainCollider.terrainData = copy;
            _runtimeTerrainData.Add(copy);
        }

        /// <summary>
        /// Picks up new terrain layers. Paint steps are stored per layer index, so they are dropped from the undo history
        /// once layers were added or removed.
        /// </summary>
        private void HandleLayersChanged()
        {
            if (_strokeActive)
                EndStroke();
            Surface.Reload();

            int layerCount = Surface.LayerCount;
            if (layerCount != _layerCount)
            {
                _undoRedo.RemoveAll(action => action is TerrainStrokeRecord record && record.ChangesSplat);
                ReleaseUnusedTiles();
            }
            _layerCount = layerCount;

            if (_terrainBrush.PaintLayer >= layerCount)
                _terrainBrush.SetPaintLayer(0);
        }

        /// <summary>
        /// Picks up new tree and grass types. Grass steps hold every grass type per cell and tree steps hold type
        /// indices, so both are dropped from the undo history.
        /// </summary>
        private void HandleVegetationTypesChanged()
        {
            if (_strokeActive)
                EndStroke();
            Surface.Reload();

            _undoRedo.RemoveAll(action => action is TerrainStrokeRecord record && (record.Channels & (TerrainChannels.Details | TerrainChannels.Trees)) != 0);
            ReleaseUnusedTiles();
        }

        /// <summary>Brush preview toggle, undo/redo, and brush size and rotation steps.</summary>
        private void HandleShortcuts(int sizeSteps, int rotationSteps)
        {
            if (_input.TogglePreview.WasPerformedThisFrame() && brushProjector != null)
                brushProjector.ToggleVisible();

            if (!_strokeActive)
            {
                // Ctrl+Shift+Z also matches Ctrl+Z, so redo wins
                if (_input.Redo.WasPerformedThisFrame())
                    Redo();
                else if (_input.Undo.WasPerformedThisFrame())
                    Undo();
            }

            if (sizeSteps != 0)
                _terrainBrush.NudgeSize(sizeSteps * _input.SizeStep);
            if (rotationSteps != 0)
                _terrainBrush.NudgeRotation(rotationSteps * _input.RotationStep);
        }

        /// <summary>Draws the brush under the pointer, and the flatten height as a disc in Flatten mode.</summary>
        private void UpdateBrushPreview()
        {
            if (brushProjector == null)
                return;

            if (!_mouseSelection.IsHittingTerrain)
            {
                brushProjector.Hide();
                return;
            }

            brushProjector.UpdateBrush(Surface, _mouseSelection.HitPoint, _terrainBrush.Diameter, _terrainBrush.Rotation);
            if (currentMode == DeformMode.Flatten)
                brushProjector.ShowTargetHeight(Surface.WorldBounds.min.y + _terrainBrush.Target * Surface.Size.y);
            else
                brushProjector.HideTargetHeight();
        }

        /// <summary>Starts a stroke when Apply is pressed over the terrain, paints while it is held and ends it on release.</summary>
        private void HandleStroke()
        {
            if (_input.Apply.WasPressedThisFrame() && _mouseSelection.IsHittingTerrain)
                BeginStroke();

            if (_strokeActive && _input.Apply.IsPressed() && _mouseSelection.IsHittingTerrain)
                ApplyStroke(_mouseSelection.HitPoint);

            if (_strokeActive && !_input.Apply.IsPressed())
                EndStroke();
        }

        /// <summary>Starts collecting a stroke into one undo step.</summary>
        private void BeginStroke()
        {
            _strokeActive = true;
            _hasLastDab = false;
            Surface.BeginStroke();
            StrokeStarted?.Invoke(ActiveMode);
        }

        /// <summary>Finishes the stroke: records its undo step and tells listeners what changed.</summary>
        private void EndStroke()
        {
            _strokeActive = false;
            TerrainStrokeRecord record = Surface.EndStroke();
            if (record == null)
                return;

            RecordStep(record);
            if (record.ChangesHeights)
                RefreshBaseBoard(record.WorldBounds);
            RaiseChanged(new TerrainChange(TerrainChangeSource.Stroke, record.Channels, record.WorldBounds));
        }

        /// <summary>
        /// Applies this frame's part of the stroke. Dabs are placed every Brush Spacing along the path since the last frame
        /// and share the frame's strength, so fast mouse moves leave no gaps. The terrains get the frame's changes in one
        /// upload.
        /// </summary>
        private void ApplyStroke(Vector3 point)
        {
            ITerrainTool tool = PrepareTool(out float frameStrength, out bool multiplicative);
            if (tool == null)
                return;

            float diameter = _terrainBrush.Diameter;
            int dabs = 1;
            if (_hasLastDab)
            {
                Vector3 path = point - _lastDab;
                path.y = 0f;
                float spacing = Mathf.Max(diameter * brushSpacing, Surface.HeightCellSize.x * 0.5f);
                dabs = Mathf.Clamp(Mathf.CeilToInt(path.magnitude / spacing), 1, maxDabsPerFrame);
            }
            float dabStrength = multiplicative ? 1f - Mathf.Pow(1f - Mathf.Clamp01(frameStrength), 1f / dabs) : frameStrength / dabs;

            bool reachedEdge = false;
            Bounds edgeArea = default;
            for (int i = 1; i <= dabs; i++)
            {
                Vector3 position = _hasLastDab ? Vector3.Lerp(_lastDab, point, (float)i / dabs) : point;
                BrushDab dab = new BrushDab(position, diameter, _terrainBrush.CurrentShape, dabStrength, _terrainBrush.Rotation);
                if (Surface.Apply(tool, dab) && tool.Channel == TerrainChannel.Heights && IsBrushAtEdge(dab))
                {
                    Bounds dabArea = GetDabArea(dab);
                    if (reachedEdge)
                        edgeArea.Encapsulate(dabArea);
                    else
                        edgeArea = dabArea;
                    reachedEdge = true;
                }
            }
            _lastDab = point;
            _hasLastDab = true;
            Surface.FlushUploads();

            // Keep the walls glued to the terrain edge while it is being sculpted, not just when the stroke ends
            if (reachedEdge)
                RefreshBaseBoard(edgeArea);
        }

        /// <summary>Rebuilds the base board walls, when there is a base board.</summary>
        private void RefreshBaseBoard()
        {
            if (_terrainBaseBoard != null)
                _terrainBaseBoard.UpdateTerrainBase();
        }

        /// <summary>Rebuilds the base board walls of the tiles in a world-space area, when there is a base board.</summary>
        private void RefreshBaseBoard(Bounds area)
        {
            if (_terrainBaseBoard != null)
                _terrainBaseBoard.UpdateTerrainBase(area);
        }

        /// <summary>True when a dab touches the terrain's outer edge, where the base board walls are.</summary>
        private bool IsBrushAtEdge(in BrushDab dab)
        {
            Bounds bounds = Surface.WorldBounds;
            Vector3 local = dab.Center - bounds.min;
            Vector3 size = bounds.size;
            float radius = dab.Diameter * 0.5f * 1.42f; // rotated square corners reach a bit further
            return local.x - radius <= 0f || local.z - radius <= 0f || local.x + radius >= size.x || local.z + radius >= size.z;
        }

        /// <summary>World-space box a dab can change (ignoring height), with a heightmap cell to spare.</summary>
        private Bounds GetDabArea(in BrushDab dab)
        {
            float radius = dab.Diameter * 0.5f * 1.42f + Surface.HeightCellSize.x; // rotated square corners reach a bit further
            return new Bounds(dab.Center, new Vector3(radius * 2f, 0f, radius * 2f));
        }

        /// <summary>
        /// Configures the tool for the active mode and converts the strength slider into this frame's amount.
        /// </summary>
        /// <param name="strength">This frame's strength, in the tool's own units.</param>
        /// <param name="multiplicative">
        /// True when the strength is a blend factor (smoothing), which has to be split between dabs differently from an amount.
        /// </param>
        /// <returns>The tool to run, or null when the mode has none.</returns>
        private ITerrainTool PrepareTool(out float strength, out bool multiplicative)
        {
            float amount = _terrainBrush.Strength;
            float target = _terrainBrush.Target;
            float deltaTime = Mathf.Min(Time.deltaTime, MaxStrokeDeltaTime);
            multiplicative = false;

            if (_customTool != null)
            {
                strength = amount * deltaTime;
                return _customTool;
            }

            bool erase = eraseVegetation != _input.Invert.IsPressed();
            DeformMode mode = ActiveMode;
            switch (mode)
            {
                case DeformMode.Raise:
                    strength = HeightStep(amount, deltaTime);
                    return _raiseLowerTool;
                case DeformMode.Lower:
                    strength = -HeightStep(amount, deltaTime);
                    return _raiseLowerTool;
                case DeformMode.Flatten:
                    _flattenTool.TargetHeight = target;
                    strength = HeightStep(amount, deltaTime);
                    return _flattenTool;
                case DeformMode.Smooth:
                    _smoothTool.Radius = Mathf.Lerp(smoothRadius.x, smoothRadius.y, amount);
                    strength = 1f - Mathf.Exp(-smoothRate * deltaTime); // same result at any frame rate
                    multiplicative = true;
                    return _smoothTool;
                case DeformMode.Paint:
                    _paintTool.Layer = _terrainBrush.PaintLayer;
                    _paintTool.TargetOpacity = target;
                    strength = Mathf.Lerp(paintRate.x, paintRate.y, amount) * deltaTime;
                    return _paintTool;
                case DeformMode.Trees:
                    strength = Mathf.Lerp(treeRate.x, treeRate.y, amount) * deltaTime;
                    int treeType = _terrainBrush.TreeType;
                    if (erase)
                    {
                        _treeEraseTool.TreeType = eraseSelectedTypeOnly ? treeType : -1;
                        return _treeEraseTool;
                    }
                    _treePaintTool.TreeType = treeType;
                    _treePaintTool.Spacing = vegetationPalette != null ? vegetationPalette.GetTreeSpacing(treeType, target) : Mathf.Lerp(6f, 1.5f, target);
                    _treePaintTool.HeightScale = vegetationPalette != null ? vegetationPalette.GetTreeHeightScale(treeType) : new Vector2(0.8f, 1.2f);
                    return _treePaintTool;
                case DeformMode.Details:
                    int detailType = _terrainBrush.DetailType;
                    if (erase)
                    {
                        _detailEraseTool.DetailType = eraseSelectedTypeOnly ? detailType : -1;
                        strength = 1f;
                        return _detailEraseTool;
                    }
                    _detailTool.DetailType = detailType;
                    _detailTool.TargetDensity = target;
                    strength = Mathf.Lerp(detailRate.x, detailRate.y, amount) * deltaTime;
                    return _detailTool;
                case DeformMode.DigHoles:
                case DeformMode.FillHoles:
                    _holeTool.Dig = mode == DeformMode.DigHoles;
                    // The strength sets how much of a soft brush's fading edge cuts as well
                    _holeTool.MinWeight = Mathf.Lerp(0.95f, 0.05f, amount);
                    strength = 1f;
                    return _holeTool;
                default:
                    Debug.LogError("TerrainEditor: no tool for mode " + mode);
                    strength = 0f;
                    return null;
            }
        }

        /// <summary>
        /// Normalized height change this frame. The strength is squared so the low end of the slider gives fine control.
        /// </summary>
        private float HeightStep(float amount, float deltaTime)
        {
            float metersPerSecond = Mathf.Lerp(heightSpeed.x, heightSpeed.y, amount * amount);
            return metersPerSecond * deltaTime / Mathf.Max(Surface.Size.y, 0.001f);
        }

        /// <summary>Tells code and inspector listeners about a finished edit.</summary>
        private void RaiseChanged(TerrainChange change)
        {
            TerrainChanged?.Invoke(change);
            onTerrainChanged.Invoke();
        }

        /// <summary>
        /// Runs your own tool instead of the built-in one for the current mode, until called with null.
        /// Its strength is the brush strength slider times the frame time.
        /// </summary>
        public void SetCustomTool(ITerrainTool tool)
        {
            if (_strokeActive)
                EndStroke();
            _customTool = tool;
        }

        /// <summary>
        /// Enables or disables editing and the brush preview (the UI panels call this when they open/close).
        /// </summary>
        public void SetEditorEnabled(bool isEnabled)
        {
            if (!isEnabled && _strokeActive)
                EndStroke();

            editorEnabled = isEnabled;
            if (brushProjector != null && !isEnabled)
                brushProjector.Hide();
            if (tilePreview != null && !isEnabled)
                tilePreview.Hide();
        }

        /// <summary>Undoes the last edit.</summary>
        public void Undo()
        {
            if (_strokeActive)
                EndStroke();
            AfterHistoryStep(_undoRedo.Undo(), TerrainChangeSource.Undo);
        }

        /// <summary>Redoes the last undone edit.</summary>
        public void Redo()
        {
            if (_strokeActive)
                EndStroke();
            AfterHistoryStep(_undoRedo.Redo(), TerrainChangeSource.Redo);
        }

        /// <summary>Updates the base board and tells listeners what an undo or redo changed.</summary>
        private void AfterHistoryStep(ITerrainUndoAction action, TerrainChangeSource source)
        {
            if (action == null)
                return;
            TerrainStrokeRecord record = action as TerrainStrokeRecord;
            TileEdit tileEdit = action as TileEdit;
            TerrainChannels channels = record != null ? record.Channels : TerrainChannels.All;
            Bounds bounds = Surface.WorldBounds;
            if (record != null)
                bounds = record.WorldBounds;
            else if (tileEdit != null)
                bounds = tileEdit.WorldBounds;

            // Adding and removing tiles rebuilds the walls already; seams they evened out can reach the neighbours
            if (record != null && (channels & TerrainChannels.Heights) != 0)
                RefreshBaseBoard(bounds);
            else if (tileEdit != null)
                RefreshBaseBoard();
            RaiseChanged(new TerrainChange(source, channels, bounds));
        }

        #region Editing settings
        /// <summary>
        /// Changes the editing mode. Holding Invert (Shift) swaps Raise and Lower, digging and filling holes, and adding
        /// and removing tiles, while it is held.
        /// </summary>
        public void SetDeformMode(DeformMode mode)
        {
            currentMode = mode;
        }

        /// <summary>Selects a brush shape by its index in the brush settings.</summary>
        public void SetBrush(int brushIndex)
        {
            _terrainBrush.SetBrush(brushIndex);
        }

        /// <summary>Sets the brush size from a slider value [0,1].</summary>
        public void SetBrushSize(float size)
        {
            _terrainBrush.SetSize(size);
        }

        /// <summary>Sets the brush rotation in degrees.</summary>
        public void SetBrushRotation(float degrees)
        {
            _terrainBrush.SetRotation(degrees);
        }

        /// <summary>Sets the brush strength from a slider value [0,1].</summary>
        public void SetBrushStrength(float strength)
        {
            _terrainBrush.SetStrength(strength);
        }

        /// <summary>Sets the flatten height / paint opacity / tree and grass density [0,1].</summary>
        public void SetBrushTarget(float target)
        {
            _terrainBrush.SetTarget(target);
        }

        /// <summary>Selects the terrain layer the Paint mode paints with; falls back to the base layer when it doesn't exist.</summary>
        public void SetPaintLayer(int layer)
        {
            int layerCount = Surface.LayerCount;
            if (layer < 0 || layer >= layerCount)
            {
                Debug.LogWarning("TerrainEditor: paint layer " + layer + " doesn't exist (layers: " + layerCount + "), using the base layer.");
                layer = 0;
            }
            _terrainBrush.SetPaintLayer(layer);
        }

        /// <summary>Selects the tree type the Trees mode places.</summary>
        public void SetTreeType(int treeType)
        {
            _terrainBrush.SetTreeType(treeType);
        }

        /// <summary>Selects the grass/detail type the Details mode paints.</summary>
        public void SetDetailType(int detailType)
        {
            _terrainBrush.SetDetailType(detailType);
        }

        /// <summary>Makes the Trees and Details modes erase everything under the brush instead of painting.</summary>
        public void SetVegetationErase(bool erase)
        {
            eraseVegetation = erase;
        }

        /// <summary>Limits erasing to the selected tree or grass type (off: every type is erased).</summary>
        public void SetEraseSelectedTypeOnly(bool selectedOnly)
        {
            eraseSelectedTypeOnly = selectedOnly;
        }

        /// <summary>
        /// Resets every tile to the starting height and base texture, removes all trees and grass and clears the undo
        /// history. Added tiles stay.
        /// </summary>
        public void ResetTerrain()
        {
            if (_strokeActive)
                EndStroke();
            Surface.Fill(startingHeightPercent);
            Surface.ClearVegetation();
            ClearHistory();
            RefreshBaseBoard();
            RaiseChanged(new TerrainChange(TerrainChangeSource.Reset, TerrainChannels.All, Surface.WorldBounds));
        }
        #endregion
    }
}
