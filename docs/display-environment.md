# LBM Display Environment

## Scope

`LBM_1wayCST` builds a lightweight visual-only room under `DisplayEnvironmentRoot` at runtime.
The room uses the solver domain envelope (centre `0,1,0`, size `5,2,8 m`) without changing
`CavityBounds`, the lattice resolution, or inlet/outlet boundary objects.

Runtime hierarchy:

```text
DisplayEnvironmentRoot
├─ FloorVisual
├─ WallVisuals
│  ├─ CutawayWalls
│  └─ CameraSideWalls
├─ CeilingVisual
├─ IndoorUnitVisual
└─ OptionalFurniture
   ├─ TelevisionVisual
   ├─ TelevisionDeskVisual
   └─ AirPurifierVisual
```

The default cutaway shows the floor and the `+X`/`+Z` walls. The `-X`/`-Z` camera-side
walls and ceiling start hidden.

The top-level visual groups are authored in `LBM_1wayCST.unity`, so their active checkboxes can
be edited directly in the Unity Hierarchy before Play. Runtime renderers are generated beneath
those groups without replacing the authored active state:

- `FloorVisual`: floor
- `WallVisuals/CutawayWalls`: `+X`/`+Z` walls and window
- `WallVisuals/CameraSideWalls`: `-X`/`-Z` walls
- `CeilingVisual`: ceiling
- `IndoorUnitVisual`: 1-way indoor unit
- `OptionalFurniture`: TV, TV desk, and air purifier

The white shell slabs overlap by their `0.16 m` thickness at every edge, producing a continuous
box without corner gaps while preserving the solver's interior `5 x 2 x 8 m` envelope.
The floor uses a light oak parquet URP material with base-colour and normal textures.
The wall slabs use the plain white `WallShell_URP.mat`. Separate inward-facing render-only quads
use the benchmark's woven-wallpaper base-colour and normal textures through `Wall_URP.mat`, so
the exterior and wall-thickness faces stay white. Floor and ceiling materials remain separate.
The indoor unit keeps the imported FBX scale and original proportions. Its renderer centre remains
aligned with the primary inlet/outlet location, and its lower bound stays flush with the `y=2 m`
ceiling plane.

## Controls

- `F6`: show/hide the cutaway walls.
- `F7`: show/hide the ceiling.
- `F8`: show/hide the camera-side walls.
- `F9`: show/hide the TV and air purifier.
- The same operations are available from the `DisplayEnvironmentController` component's
  context menu.

## Solver isolation

The environment is assigned to the `DisplayEnvironment` GameObject layer and contains no
`Collider`, `Rigidbody`, `DeviceObstacles`, `Racks`, `ACSource`, or `LBMZouHeBox` component.
The solver scene cache searches for the four solver component types rather than generic
renderers, so the visual geometry is excluded from cell and boundary generation.

The `+Z` visual wall is divided around a centred three-panel balcony window opening. The window, TV, TV desk,
and air purifier are render-only objects. Both contour cameras render real-time shadows, and the 1-way indoor
unit does not cast shadows.

## Imported CTO_18 assets

User approval to copy and use these assets was provided on 2026-09-30.

- Source: `D:/2_FFD/0_Unity/CTO_18/Assets/Data/ThinQ/Fbx/Products/2-11_1way_system_airconditioner.fbx`
- Target: `Assets/Resources/DisplayEnvironment/Models/1WayIndoorUnit.fbx`
- Source: `D:/2_FFD/0_Unity/CTO_18/Assets/SHINCO/Data/ThinQ/Textures/color_texture.png`
- Target: `Assets/Resources/DisplayEnvironment/Textures/IndoorUnitColor.png`
- Source: `D:/2_FFD/0_Unity/CTO_18/Assets/SHINCO/Data/ThinQ/Textures/MR_texture.png`
- Target: `Assets/Resources/DisplayEnvironment/Textures/ProductMetallicSmoothness.png` (URP channel repack)

No CTO_18 scene, HomePlanner system, runtime script, prefab, animation controller, wind mesh,
or particle asset is included. The FBX imports without colliders, cameras, lights, or animation.
Its Built-in material mapping was replaced with `IndoorUnit_URP.mat`, which uses URP/Lit.

Additional selected assets:

- `Resources/Generated/House/Meshes/BalconyWindow3Window-1317694.asset`
- `shinco_test/Data/Small_eletronics/6-6_TV.fbx`
- `shinco_test/Data/Furniture/TVDesk/Livingroom_TV_Desk1.fbx`
- `Data/Common/Fbx/Parts/SurfaceUp.fbx`
- `shinco_test/For_animation/B.중소형가전/1_2_airpurifier_2.fbx`
- `HomePlanner/HouseGraph/Resources/Props/Common/Materials/Substance/Paper/wallpaper_woven_plain_graph_0/wallpaper_woven_plain_basecolor.tga`
- `HomePlanner/HouseGraph/Resources/Props/Common/Materials/Substance/Paper/wallpaper_woven_plain_graph_0/wallpaper_woven_plain_normal.tga`

The source house, product prefabs, HomePlanner scripts, animations, Timeline assets,
particle effects, baked lighting data, and reflection probe are excluded. All copied
materials are replaced at runtime with URP/Lit materials.

The TV, TV desk, and air purifier URP/Lit materials reuse the benchmark
`SHINCO/Data/ThinQ/Textures/color_texture.png` palette (stored locally as
`IndoorUnitColor.png`). Their original FBX UVs therefore select the intended white body,
dark screen/top, trim, and handle colours instead of receiving a uniform grey override.
The benchmark metallic/roughness palette is repacked for URP as
`ProductMetallicSmoothness.png` (metallic in R, smoothness in A) for the TV and air purifier.
