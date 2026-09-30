# Runtime Terrain Editor

Edit Unity terrains while the game is running: raise, lower, flatten, smooth, texture painting, trees, grass and holes,
with a brush preview, undo/redo and save/load. Works on one terrain or on a grid of terrain tiles, and tiles can be
added and removed while playing.

Made for Unity 6 with URP and the Input System package.

## Folders

| Folder | What it is |
|---|---|
| `Scripts/` | The runtime terrain editor. This is what you build on. |
| `Prefabs/TerrainEditor.prefab` | Everything wired together, ready to drop into a scene. |
| `Input/TerrainEditorControls.inputactions` | The editor's controls as an Input Action Asset. |
| `Brushes/` | Default brush settings and brush shape textures. |
| `Materials/`, `Shaders/` | Brush preview and base board materials. |
| `Demo/` | An example scene with UI, a camera controller, terrain textures, trees and grass. Safe to delete once you have your own setup. |

## Quick start

1. Open a scene with a camera tagged `MainCamera` and a Terrain, or several terrain tiles. Without any terrain, the
   prefab's **TerrainGridGenerator** makes a grid of tiles when play starts.
2. Drag `Prefabs/TerrainEditor.prefab` into the scene.
3. Press Play and edit with the mouse (see Controls).

The terrain is edited through a runtime copy of its TerrainData, so play mode never changes your terrain asset
(turn off **Edit Runtime Copy** on TerrainEditor to change that). **Reset Terrain On Start** flattens it and clears
trees, grass and holes when play starts.

The demo scene `Demo/Scenes/RuntimeTerrainEditorDemo.unity` shows a full setup with UI, with a tab each for sculpting,
painting, trees and grass, and adding or removing tiles.

## Controls

All controls are actions in `Input/TerrainEditorControls.inputactions` (the "Terrain Editor" action map). Change the
bindings by opening that asset, or point the fields of the **TerrainEditorInput** component at actions of your own.
Pointer actions work with mouse, pen and touch. The demo camera and its save keys use their own asset,
`Demo/Input/DemoControls.inputactions`.

| Action | Default |
|---|---|
| Edit | Left mouse / touch |
| Invert (lower instead of raise, fill holes instead of digging, erase trees and grass, remove tiles instead of adding) | Hold Shift |
| Undo / Redo | Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z |
| Brush size | `[` `]` or Ctrl + scroll wheel |
| Brush rotation | `,` `.` or Alt + scroll wheel |
| Show / hide brush | H |

Fast strokes are filled in: dabs are spaced along the mouse path (**Brush Spacing** on TerrainEditor).

## Components on the prefab

| Component | Job |
|---|---|
| `TerrainEditor` | Runs the current tool along your strokes, keeps undo history, saves and loads. Your UI talks to this. |
| `TerrainEditorInput` | Connects the editor to its input actions (from `TerrainEditorControls.inputactions` by default). |
| `TerrainBrush` | The brush in use: shape, size, strength, rotation and its starting values. The shapes come from a Brush Settings asset (*Create > Runtime Terrain Editor > Brush Settings*), which play mode never changes. |
| `TerrainMouseSelection` | Finds where the pointer is on the terrain (on every tile, and inside holes so they can be filled). Set **Raycast Camera** if you don't use `MainCamera`. |
| `BrushProjector` (child) | Draws the brush on the terrain, and the flatten height as a disc. Optional. |
| `TerrainTilePreview` (child) | Shows the free spots where tiles can be added, and the tile that would be removed. Optional. |
| `TerrainLayerPalette` (child) | The TerrainLayers you can paint with; the first is the base layer. Leave empty to use the terrain's own layers. Optional. |
| `TerrainVegetationPalette` (child) | Icons, spacing and height ranges for the terrain's trees and grass, and functions to add more at runtime. Optional. |
| `TerrainBaseBoard` | Walls under the outer terrain edges so the map looks like a solid block. Optional. |
| `TerrainGridGenerator` | Makes a grid of terrain tiles when the scene has no terrain. Optional. |

## Trees and grass

Add trees and grass to the Terrain itself, like for editor painting: **Paint Trees > Edit Trees > Add Tree** and
**Paint Details > Edit Details > Add Grass Texture** (or Add Detail Mesh) in the Terrain inspector. The editor paints
with whatever the terrain has.

- Grass needs a **Detail Resolution** above 0 (Terrain settings), or it can't be painted.
- Trees need an **LODGroup** on the prefab so the terrain draws them with their own materials. Without one Unity expects
  the legacy Nature/Soft Occlusion shaders and the trees look black in URP. Leave colliders off the trees so they
  don't block the brush.
- **TerrainVegetationPalette** is optional. Its entries are matched to the terrain's trees by prefab and give UI icons,
  tree spacing and height ranges; grass entries only add icons (the grass texture is used otherwise).
- Trees mode scatters trees with a minimum spacing (the density slider packs them closer); Details mode paints grass
  density. Both erase while Invert is held, or with `SetVegetationErase(true)`.
- Erasing removes everything under the brush at once, of every tree or grass type. `SetEraseSelectedTypeOnly(true)`
  limits it to the selected type. The faint outer edge of soft brushes is left alone.
- Grass is only drawn within the Terrain's **Detail Distance** (80 m by default).
- Trees stay on the ground when you sculpt under them.

Adding tree or grass types while the game runs:

```csharp
TerrainVegetationPalette palette = editor.VegetationPalette;
int tree = palette.AddTreeType(new TerrainTreeType { prefab = palmPrefab, spacing = 5f, icon = palmIcon });
int grass = palette.AddDetailType(new TerrainDetailType { texture = flowerTexture, width = new Vector2(0.5f, 1f) });
editor.SetTreeType(tree);
palette.ApplyToTerrain(); // or: replace all of the terrain's types with the palette's lists
```

Adding a type keeps everything painted and returns its index (adding one the terrain already has returns the existing
index and updates its icon/spacing). `ApplyToTerrain` removes trees whose type no longer exists. Undo steps for trees
and grass are cleared when the types change, since they can't be replayed with other types. The demo's Foliage panel
shows new types the next time it opens.

## Holes

`DeformMode.DigHoles` cuts holes in the terrain, e.g. for cave or tunnel entrances, and `DeformMode.FillHoles` closes
them again (Invert swaps the two while held). Holes cut through the terrain's collider too, so players and physics fall
through. The brush strength decides how much of a soft brush's fading edge cuts as well. Holes are part of undo, reset
and save files.

## Several terrain tiles

The editor edits every terrain in its **Terrains** list, or every active terrain in the scene when the list is empty,
as one surface: strokes run across tile borders and one stroke is one undo step on all tiles.

- Tiles of the same size and heightmap resolution placed side by side on a grid (as Unity's *Create Neighbor Terrains*
  makes them) get their seams kept closed: the heights along shared edges stay equal, also after smoothing. Give them
  the same **Grouping ID** with **Auto Connect** on so their levels of detail match at the seams too.
- Give every tile the same layers, trees and grass. The palettes apply their changes to all tiles.
- Use a terrain layer **Tile Size** that divides the tile size (e.g. 10 m layers on 100 m tiles), or the textures
  don't line up at the seams.
- The base board only builds walls on sides that have no neighbouring tile.
- **TerrainGridGenerator** makes a grid when the scene has no terrain at all: set the tile count, size and resolutions,
  and a **Template** TerrainData to copy the layers, trees and grass from.
- The brush size slider is relative to one tile (1 = a tile's longest side), so brushes keep their size when tiles are
  added.

`editor.Surface.Tiles` lists the tiles; `editor.Surface.SampleHeight(position)` reads the ground height across all of them.

### Adding and removing tiles while playing

`DeformMode.AddTiles` shows a plus on every free spot next to the terrain; clicking one adds a tile there.
`DeformMode.RemoveTiles` outlines the tile under the pointer; clicking removes it. Invert swaps the two while held. The
demo's Tiles tab does exactly this.

```csharp
Terrain tile = editor.AddTile(new Vector3(250f, 0f, 50f)); // adds a tile in the free spot at that position, or returns null
editor.RemoveTile(tile);                                    // never removes the last tile
bool free = editor.CanAddTile(position);
```

- A new tile gets the size, resolutions, layers, trees, grass and look (material, distances, grouping) of the other
  tiles. Its ground starts at the starting height and blends into the edges of the tiles next to it, over **New Tile
  Blend** (a fraction of the tile) on TerrainEditor, so it continues the ground it joins.
- Tiles can only be added next to an existing tile, and only when the tiles form a grid.
- Adding and removing are undo steps. A removed tile is switched off and kept, with everything on it, while undo can
  bring it back; after that it is destroyed.
- Saves remember where the tiles are, so loading adds and removes tiles to match (see Saving and loading).
- `editor.TerrainChanged` reports them with `TerrainChangeSource.TileAdded` and `TileRemoved`.
- `editor.Surface.GetFreeSlots(list)` lists the free spots, `GetSlotPosition` and `GetTileAtSlot` map positions to
  them. `TerrainTileFactory` creates tile TerrainData and Terrain objects shaped like existing ones, if you build tiles
  yourself.

## Driving it from your own UI

Everything goes through `TerrainEditor.Instance`:

```csharp
TerrainEditor editor = TerrainEditor.Instance;
editor.SetEditorEnabled(true);                  // turn editing on or off
editor.SetDeformMode(DeformMode.Smooth);        // Raise, Lower, Flatten, Smooth, Paint, Trees, Details, DigHoles,
                                                // FillHoles, AddTiles, RemoveTiles
editor.SetBrush(2);                             // index into the brush settings' textures
editor.SetBrushSize(0.3f);                      // 0..1 slider value (1 = one tile's longest side)
editor.SetBrushRotation(45f);                   // degrees
editor.SetBrushStrength(0.5f);                  // 0..1
editor.SetBrushTarget(0.4f);                    // flatten height / paint opacity / tree and grass density, 0..1
editor.SetPaintLayer(1);                        // which terrain layer Paint uses
editor.SetTreeType(0);                          // which tree Trees places
editor.SetDetailType(0);                        // which grass Details paints
editor.SetVegetationErase(true);                // Trees and Details erase instead of paint
editor.SetEraseSelectedTypeOnly(true);          // ...only the selected tree/grass type (default: every type)
editor.Undo();
editor.Redo();
editor.ResetTerrain();
```

`editor.Brush` holds the current values (`Strength`, `SizeValue`, `Diameter`, `Target`, `BrushIndex`, ...) and its
`Changed` event fires whenever one changes (also from shortcuts), so sliders can follow it. Holding Invert swaps Raise and
Lower, digging and filling holes, and adding and removing tiles, while it is held; `editor.ActiveMode` is the mode that
applies right now, `editor.CurrentMode` the selected one.

Change the paintable layers at runtime with `editor.LayerPalette` (`AddLayer`, `CreateLayer`, `ReplaceLayer`,
`RemoveLayer`, `SetLayers`). Painted areas stay on the right texture when layers are added or removed.

## Saving and loading

```csharp
editor.SaveToFile(TerrainEditor.GetSavePath("MyMap"));   // Application.persistentDataPath/MyMap.rterrain
editor.LoadFromFile(TerrainEditor.GetSavePath("MyMap"));
byte[] bytes = editor.SaveToBytes();                      // for cloud saves, PlayerPrefs, networking...
editor.LoadFromBytes(bytes);
```

A save holds heights (16 bit), painted layers, grass, trees and holes of every tile, and where each tile is. Loading
adds and removes tiles until the terrain has the saved layout, then fills them. Saves from older versions, or whose tiles
don't line up with the terrain's grid, are matched to the tiles in order instead. Layers and vegetation are matched by
name, and saves from a terrain with another resolution or height are resampled to fit. Loading clears the undo history.
The whole file is checked before the terrain changes, so loading a damaged file returns false and leaves the terrain as
it was. `TerrainSaveFile.Read` and `Apply` do the same without a TerrainEditor.

Heightmaps: `ImportHeightmap(texture)` (grayscale, bright is high), `ImportHeightmapRaw(bytes)` and
`ExportHeightmapRaw()` (16-bit RAW, like Unity's terrain export). A heightmap covers all tiles; exports of a square grid
of tiles can be imported again. Imports can be undone.

## Reacting to edits

`editor.TerrainChanged` is raised after every finished edit (stroke, undo, redo, reset, load, import, tile added or
removed) with what changed (`TerrainChannels`: heights, splat, details, trees, holes) and a world-space box around it.
There is also an **On Terrain Changed** UnityEvent on the component, and `StrokeStarted`.

```csharp
editor.TerrainChanged += change =>
{
    if (change.Includes(TerrainChannels.Heights))
        navMeshSurface.UpdateNavMesh(navMeshSurface.navMeshData); // e.g. with the AI Navigation package
};
```

Put **SnapToTerrain** on props, buildings or spawn points to keep them on the ground when the terrain under them changes.

## Adding your own tools

Brush operations live in `Scripts/Sculpting` (namespace `RuntimeTerrainEditor.Sculpting`). Implement `ITerrainTool`,
then run it with `editor.SetCustomTool(myTool)` or call `editor.Surface.Apply(myTool, dab)` yourself.
`TerrainSurface` takes care of brush shapes and rotation, running the tool on every tile the brush touches, closing tile
seams, uploading changes to the terrain, collider updates and undo. See `RaiseLowerTool.cs` for the smallest grid tool,
`HoleTool.cs` for one that edits holes and `TreePaintTool.cs` for one that places objects.

A few things to know when writing tools:

- If you call `editor.Surface.Apply` inside your own `BeginStroke` / `EndStroke`, call `editor.Surface.FlushUploads()`
  once per frame so the terrain shows the changes before the stroke ends. Outside a stroke every dab uploads at once.
- A grass tool should call `context.Details.MarkChanged(type)` for each grass type it changes, so only those are
  uploaded. A tool that marks nothing uploads every type.
- Your own undo steps (`ITerrainUndoAction`) report their `SizeInBytes`, which counts against the history's memory
  budget (**Max Undo Memory MB** on the TerrainEditor).

## Performance settings

On the **TerrainEditor** component:

- **Max Undo Steps** and **Max Undo Memory MB** limit the undo history; the oldest steps are dropped first.
- **Sync Colliders During Strokes** and **Collider Sync Interval**: turn syncing off on big terrains to refresh
  colliders only when a stroke ends. The editor's own picking reads the heightmap, so it doesn't need the collider.
- **Brush Spacing** and **Max Dabs Per Frame** trade stroke smoothness for cost on fast strokes.
