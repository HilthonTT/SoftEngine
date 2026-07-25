# SoftEngine

A **software 3D rasterizer** written in C#. The entire pipeline — model transforms, projection, culling, clipping, scanline rasterization, z-buffering and shading — runs on the CPU with no GPU or graphics-API dependency. A WinForms front-end renders live into a bitmap so you can orbit models, switch shading modes, and watch per-frame render statistics.

![Skull model (31k triangles) rendered with Gouraud shading](docs/screenshots/skull.png)

| ![Elephant model (26k triangles, 5 meshes) with Gouraud shading](docs/screenshots/elephant.png) | ![Parrot model (7k triangles) with Gouraud shading](docs/screenshots/parrot.png) |
| :--: | :--: |
| Elephant — 26k triangles across 5 meshes | Parrot — 7k triangles |

## What it does

- Loads and renders 3D models (Wavefront `.obj`, Collada `.dae`) and procedural primitives in real time.
- Rasterizes triangles with a generic scanline filler and a depth (z) buffer.
- Supports several shading modes — wireframe, solid, flat, Gouraud, Phong, textured and full material — selectable at runtime.
- Lights a scene with **any number of coloured lights** — directional, point with distance falloff, and spot.
- Rasterizes into an **HDR linear float target**, so highlights brighter than white survive to the post-process stack.
- Surrounds the scene with an **environment cube map**, drawn as a skybox and reduced to the ambient light the painters use.
- Casts **shadows** with a shadow-map pass rendered from the light's point of view, and **screen-space ambient occlusion** for the contact detail a shadow map cannot resolve.
- Shades **materials**: albedo, tangent-space normal and specular maps, loaded from a model's `.mtl`.
- Runs a **post-process stack** over the finished frame — bloom, tone mapping, FXAA, vignette.
- Anti-aliases by **supersampling**: render at a multiple of the display resolution and average down.
- Provides an interactive arc-ball camera, WASD fly controls, gizmos (world axes, ground grid), and a live stats overlay.
- Ships a **graphics debugger** — event list, object table and per-pixel history — built on the renderer's own instrumentation.

| ![Shadow mapping](docs/screenshots/shadows.png) | ![Normal mapping](docs/screenshots/normal-mapping.png) |
| :--: | :--: |
| Shadow mapping — one depth pass from the light | Normal mapping — the same two cubes, 12 triangles each; only the left one has a normal map |

## Shading modes

| Mode | Painter | Description |
| --- | --- | --- |
| **None** | — | Geometry only (combine with the wireframe overlay to see edges). |
| **Classic** | `ClassicPainter` | Flat per-triangle base color, no lighting. |
| **Flat** | `FlatPainter` | One Lambert (N·L) light per triangle from its centroid normal. |
| **Gouraud** | `GouraudPainter` | Per-vertex Lambert light interpolated across the triangle. |
| **Phong** | `PhongPainter` | Per-pixel Blinn-Phong from an interpolated world position and normal. |
| **Textured** | `TexturedPainter` | Perspective-correct texturing with Gouraud lighting, bilinear filtering and mip-maps. |
| **Material** | `MaterialPainter` | Per-pixel albedo, normal and specular maps over Blinn-Phong — the full material path. |

A `WireFramePainter` overlay (Liang–Barsky homogeneous line clipping) can be drawn on top of any mode.

## Lighting

A scene's lights are a list, and every lit painter sums over all of them. Each carries a
colour as well as an intensity, so a warm key and a cool fill produce a surface whose lit
side and shadowed side differ in hue rather than only in brightness — and the sum is never
clamped, because two lights on one surface really do deliver twice the light.

| Light | Falls off | Notes |
| --- | --- | --- |
| [`DirectionalLight`](src/SoftEngine.Core/Scenes/Lights/DirectionalLight.cs) | Never | Parallel rays; the sun. |
| [`PointLight`](src/SoftEngine.Core/Scenes/Lights/PointLight.cs) | With distance, if given a `Range` | Windowed inverse-square, reaching exactly zero at the range rather than trailing off forever. `Range` is infinite by default, which is the no-falloff behaviour the engine had before. |
| [`SpotLight`](src/SoftEngine.Core/Scenes/Lights/SpotLight.cs) | With distance and angle | Two cone angles, so the beam's edge ramps instead of stepping — a single angle aliases in the lighting, where no amount of supersampling can reach it. |

The `ILight` interface is not what a shader talks to. Every light is flattened once per
frame into a [`ShaderLight`](src/SoftEngine.Core/Shading/ShaderLight.cs) — a struct of plain
floats — and the frame's set into a [`LightSet`](src/SoftEngine.Core/Shading/LightSet.cs),
so the per-pixel loop is a branch on a field rather than a virtual call that also forfeits
inlining. The array is reused across frames.

Only the first light casts a shadow: the shadow map is one depth buffer taken from one point
of view, so a second shadowed light would need a second pass and a second buffer.

## High dynamic range

An 8-bit render target cannot hold a value above white. A specular glint five times paper
white and one exactly at it are the same pixel by the time anything downstream sees them —
which means bloom deciding what is "bright" and tone mapping "compressing the range" are both
working on an image whose brights were flattened before they arrived.

`Scene.HighDynamicRange` rasterizes into a linear float buffer instead
([`FrameBuffer.SetHighDynamicRange`](src/SoftEngine.Core/Buffers/FrameBuffer.cs)). A shader
returns a [`LinearColor`](src/SoftEngine.Core/Shading/LinearColor.cs) — linear light with no
ceiling — and the value is clamped once, at the very end of the frame, after the effects have
had it. It pairs with `GammaCorrect`, which is the path where the shaders produce light rather
than pre-encoded bytes and so the only one with a range above white to keep.

Two things moved into linear light along with it, because that is where mixing light is
defined: **alpha blending** and **fog**. Half of a full-intensity channel encodes to about
188, not to 128 — the latter is a good deal darker than half the light.

## Environment and ambient

`Scene.Environment` is a [`CubeMap`](src/SoftEngine.Core/Geometry/CubeMap.cs), and it does two
jobs.

[`SkyRenderer`](src/SoftEngine.Core/Pipeline/SkyRenderer.cs) draws it behind the scene. There
is no cube to rasterize: a skybox drawn as geometry is really just a way of getting a
direction interpolated per pixel, and here the direction comes straight from the pixel's
position through the inverse projection — no seams where the cube's own triangles meet, and
nothing that can be clipped by the near plane. It runs *between* the opaque and transparent
passes, filling only pixels whose depth is still the cleared value: after the opaque fill so
it shades nothing that was covered, and before the transparent one because that blends
without writing depth, so a sky drawn last would paint over the glass rather than behind it.

The same map also becomes the ambient term, as an
[`AmbientCube`](src/SoftEngine.Core/Shading/AmbientCube.cs) — six directional averages instead
of one constant. A flat ambient says a ceiling and a floor in the same room receive the same
light from their surroundings, which is never true: one faces the sky and the other faces the
ground. Averaging each face and blending the three a normal points toward is the cheapest
correction that says otherwise. The per-pixel painters evaluate it with the *shading* normal,
so a normal map shapes the ambient the same way it shapes the lights.

[`SkyBox.Gradient`](src/SoftEngine.Core/Geometry/SkyBox.cs) generates one procedurally — zenith,
horizon, ground and a sun disc — so a scene can have a sky, and therefore directional ambient,
with no asset to load.

## Shadow mapping

Shadows come from a second, depth-only render of the world from where the light stands
([`Pipeline/Shadows/ShadowMapRenderer.cs`](src/SoftEngine.Core/Pipeline/Shadows/ShadowMapRenderer.cs)).
Shading a point then means projecting it with the same matrix and comparing: if something
nearer to the light already occupies that direction, the point is in shadow.

- The light gets an **orthographic** projection ([`OrthographicProjection`](src/SoftEngine.Core/Scenes/Projections/OrthographicProjection.cs)) sized to a sphere around the whole world, so the map covers the scene from any angle. Point lights are approximated as directional — accurate while the light sits outside the scene.
- The pass is the main pipeline with everything that doesn't affect occlusion removed: no colour, no lighting, no varyings. It uses a **bounding-box edge-function rasterizer**, split into contiguous bands of rows so it parallelizes without locking the depth buffer.
- **Phong and Material** sample the map per pixel; **Flat, Gouraud and Textured** fold the visibility into their per-vertex intensity. Ambient light is never shadowed, so a shadowed surface darkens rather than going black.
- **Bias is measured in shadow-map texels of depth**, not raw normalized depth — one texel of error means the same thing in a 2-unit skull and a 1500-unit elephant, so it does not need retuning per scene or per resolution. A slope term scales it with the light's incidence angle.
- Transparent and hidden meshes are excluded: something you can see through should not block the light, and a mesh dropped from the frame should not leave its shadow behind.

`Scene.Shadows` ([`ShadowSettings`](src/SoftEngine.Core/Scenes/ShadowSettings.cs)) controls resolution, bias, 3×3 PCF filtering and shadow strength.

## Materials and normal mapping

Every mesh carries a [`Material`](src/SoftEngine.Core/Geometry/Material.cs): a diffuse
colour, an albedo map, a tangent-space normal map, a specular mask, and the two numbers
that shape its highlight. `Mesh.Texture` is the albedo map under its older name, so nothing
that predates materials had to change.

Normal mapping adds surface detail finer than a vertex without adding a single triangle. It
needs a per-vertex UV frame, which [`TangentBuilder`](src/SoftEngine.Core/Geometry/TangentBuilder.cs)
derives by solving each triangle's edges against its UV deltas and accumulating the result
per vertex — including the ±1 handedness that mirrored UV islands flip. Frames are built
in the painter's `Prepare`, before the parallel paint phase, exactly as mip chains are.

The OBJ importer reads `Kd`, `Ks`, `Ns`, `d`/`Tr`, `map_Kd`, `map_Ks` and the normal map
under all of its spellings (`map_Bump`, `bump`, `norm`).
[`NormalMapBuilder`](src/SoftEngine.Core/Geometry/NormalMapBuilder.cs) converts a height map
into a normal map, since `map_Bump` was specified as one and is used as the other.

## Post-processing

![Post-processing](docs/screenshots/post-processing.png)

[`PostProcessStack`](src/SoftEngine.Core/Pipeline/PostProcess/PostProcessStack.cs) runs
full-screen effects over the finished render target. It owns the conversion at both ends, so
effects only ever see linear float RGB — the space blurring and adding light are actually
defined in. Against an HDR surface the image arrives already linear and unbounded and no
decode happens at all; against an 8-bit one the stack decodes sRGB on the way in, and nothing
downstream can recover highlights the rasterizer already clipped.

| Effect | What it does |
| --- | --- |
| **SSAO** | Reconstructs a position and a normal per pixel from the depth buffer, samples a hemisphere around each, and darkens by the fraction of samples something else is in front of. |
| **Bloom** | Bright-passes and blurs at a quarter resolution, then adds the result back — a wide blur is what sells the effect, and it costs a sixteenth of the samples downsampled. |
| **Tone map** | Exposure plus a Reinhard or ACES filmic curve, so values above white roll off instead of flattening into one clipped patch. |
| **FXAA** | Detects luminance edges, works out which way each runs, and blurs across it — never along it. Fixed screen-sized cost rather than a multiple of the whole render. |
| **Vignette** | Darkens the frame toward its corners, normalized so it behaves the same at any aspect ratio. |

### Ambient occlusion

[`SsaoEffect`](src/SoftEngine.Core/Pipeline/PostProcess/SsaoEffect.cs) finds the creases and
contact points a shadow map at any sane resolution cannot resolve, from nothing but the depth
buffer the frame already has. The depth buffer is a partial model of the scene: one point in
space per pixel, and — by differencing its neighbours — the surface's orientation there. A
point is occluded to the extent that other points sit above its own tangent plane nearby.

Effects that want this declare `NeedsDepth`, and the stack then reads the z-buffer back as
*view-space distance* ([`FrameBuffer.ReadViewDepth`](src/SoftEngine.Core/Buffers/FrameBuffer.cs))
rather than as the quantized device depth the depth test uses — a screen-space effect's radius
is measured in world units, so it needs the distance, not something merely monotonic in it.
The read costs a full-screen pass, so it only happens when something enabled asked for it, and
it is only possible under a perspective projection.

Two honest limitations. It only knows what is on screen, so geometry outside the frame or
hidden behind something nearer occludes nothing — inherent to the technique. And it multiplies
the finished image, which darkens direct light along with ambient; separating the two in a
forward renderer means carrying the ambient in a buffer of its own through the whole frame.
`Radius` is a world-space distance and is the one number that must be scaled to the scene.

## Rendering pipeline

```
model ──worldMatrix──▶ world ──viewMatrix──▶ view ──projectionMatrix──▶ clip ──/w──▶ NDC ──▶ screen
```

Per frame, the `Renderer` ([`Pipeline/Renderer.cs`](src/SoftEngine.Core/Pipeline/Renderer.cs)):

1. Clears the color and z-buffers.
2. Renders the shadow map from the light, if the scene casts shadows — before any painter prepares, since every shade that follows reads it.
3. Transforms each mesh's vertices into view space (pooled `VertexBuffer` per mesh).
4. Rejects triangles behind the far plane, back-facing triangles (optional culling), and triangles outside the view frustum.
5. Projects survivors into clip space, maps to screen space, and bins them into the screen tiles they cover.
6. Fills the tiles in parallel through the active painter.
7. Draws the sky into whatever pixels the opaque pass left untouched.
8. Blends the transparent triangles over the result, farthest first.
9. Draws optional gizmos (XZ grid, world axes).
10. Runs the post-process stack over the finished image, and encodes it for presentation.

The rasterizer ([`Rasterization/ScanlineRasterizer.cs`](src/SoftEngine.Core/Rasterization/ScanlineRasterizer.cs)) sorts a triangle's vertices by Y, splits it at the middle vertex, and walks two half-triangles, interpolating depth plus an arbitrary *varying* payload. Painters only supply a **varying** type and a **shader** — both are `struct` generics, so the JIT devirtualizes and inlines the per-pixel shade call with no allocation on the hot path.

## Tiled rasterization

The fill phase is split by **screen tile**, not by triangle: [`TileBinner`](src/SoftEngine.Core/Rasterization/TileBinner.cs)
sorts each frame's triangles into the 32×32-pixel tiles their bounding boxes touch, and one
worker owns each tile ([`ScreenTile`](src/SoftEngine.Core/Rasterization/ScreenTile.cs)). Because
tiles never overlap, the z-buffer needs no locking — and because a triangle only reaches the
tiles it covers, its per-triangle setup is paid once per tile instead of once per core.

Three things fall out of owning a rectangle of pixels:

- **Coarse depth rejection.** Before drawing a triangle into a tile, its nearest depth is
  compared against the farthest depth currently stored anywhere in that tile. If it is behind,
  the triangle is dropped whole — no rows walked, no pixels tested. The bound is re-read every
  few triangles, and a scan that buys no rejection doubles the interval to the next one, so a
  scene with no depth complexity stops paying for it. `Settings.HierarchicalZ` turns it off.
- **Vectorized depth tests.** Runs of `Vector<int>.Count` pixels are depth-tested at once, and
  a run entirely behind the z-buffer is skipped without interpolating or shading any of it.
  Depth is evaluated as an affine function of x rather than a running sum, so the vector test
  and the scalar loop agree exactly.
- **Contiguous memory.** A tile walks the framebuffer the way it is laid out, rather than
  every n-th row.

Measured at 1280×720 on eight hardware threads, the three together render a dense model about
**1.5–1.8×** faster than the row-interleaved fill they replace, and a scene with heavy overdraw
about **2×** faster.

## Supersampling

[`SuperSampler`](src/SoftEngine.Core/Pipeline/SuperSampler.cs) resolves a render target drawn
at an integer multiple of the display resolution back down to it. It is the one kind of
anti-aliasing that asks nothing of the rasterizer — the whole pipeline just runs larger — so
unlike FXAA it smooths specular glints and texture shimmer as well as silhouettes. Colours are
averaged in linear light, and alpha along with them, which leaves the edge pixels of a shape
over the cleared background correctly premultiplied for presentation. A 2× frame fills four
times the pixels, which is exactly what it costs.

## Interactive app

The WinForms app ([`SoftEngine.WinForms`](src/SoftEngine.WinForms)) renders the scene into a 32-bpp bitmap that is blitted to a `Panel3D`.

| Control | Action |
| --- | --- |
| **Left-drag** | Orbit the arc-ball camera |
| **Right-drag** | Pan; **left+right-drag** dollies |
| **Mouse wheel** | Move the camera in/out — the status bar's zoom percentage follows it (100% is the framing a world loads with) |
| **W / A / S / D** | Fly the camera forward / left / back / right (**Q**/**E** for down/up). Hold **Shift** to move faster, **Ctrl** for fine steps; the step scales with the camera's distance, so it works on a 2-unit skull and a 1500-unit elephant alike |
| **Left-click the viewport** | Probe that pixel — its full write history appears in the Pixel History panel (**Esc** clears it) |
| **Load model…** | Pick a bundled world (skull, parrot, elephant, teapot, cubes, spheres, towns, shadows, normal mapping…) or open an OBJ/Collada file from disk |
| **Shading radios** | Switch between None / Classic / Flat / Gouraud / Phong / Textured / Material |
| **Display checkboxes** | Toggle wireframe triangles, back-face culling, XZ grid, world axes, fog, shadows, sky, gamma-correct light, HDR target, texture filtering, 2× supersampling |
| **Post-processing checkboxes** | Toggle ambient occlusion, bloom, tone mapping, FXAA and vignette independently |

A stats overlay reports triangle counts (total / back-facing / out-of-view / behind), pixel counts (drawn / z-rejected), and calculation vs. paint timing per frame.

## Graphics debugger

The front-end doubles as a small graphics debugger, modelled on [Rasterizr Studio](https://github.com/tgjones/rasterizr). Because the whole pipeline runs on the CPU, the panels show what the renderer actually did rather than what a driver reported.

| Panel | Shows |
| --- | --- |
| **Graphics Event List** | Every step of the frame in pipeline order — viewport and depth-range setup, buffer clears, the shadow-map pass, the view and projection matrices, then per mesh: vertex transform, cull results and the draw call, then the post-process pass and the present. |
| **Graphics Object Table** | Every object the frame touched — render target, depth buffer, camera, projection, painter, shadow map, post-process stack, lights, meshes and textures — with its size, vertex/triangle counts and dimensions. Meshes carry an **active** checkbox that drops them from the frame. |
| **Pixel History** | For the selected pixel: the clear, then each triangle that tried to write it — including the ones the depth test rejected — with the input-assembler and transformed vertex data, the depth comparison, and the previous → resulting colour, ending with the post-process pass's before → after. |

Identifiers are shared: `obj:7` in the event list is `obj:7` in the object table, and clicking an entry in the pixel history selects both.

Recording is driven from `RenderDiagnostics` on the renderer ([`Diagnostics/`](src/SoftEngine.Core/Diagnostics)):

- Events are stored as a `readonly record struct` with a numeric payload in a reused buffer, and formatted only for the rows the list actually draws — a busy scene emits thousands of events per frame and capturing them allocates nothing.
- The pixel probe is a single int compare inside `FrameBuffer.PutPixel`, off (`-1`) unless a pixel is selected. The "what is drawing" context is thread-static: each paint worker owns a disjoint set of screen rows, so the one worker that owns the probed pixel is also the one that tags its writes, and they stay in draw order.
- Triangle vertices are snapshotted only when a write actually lands on the probed pixel, never per triangle.

Both can be switched off from the **View** menu, along with each panel.

## Project layout

```
src/
├── SoftEngine.Core/        # engine, no UI dependency (net10.0 class library)
│   ├── Buffers/            # FrameBuffer (color + z-buffer + pixel probe), pooled Vertex/World buffers
│   ├── Diagnostics/        # render stats, graphics event log, pixel history
│   ├── Geometry/           # IMesh/Mesh, Material, Triangle, tangents, primitives, OBJ/Collada importers
│   ├── Pipeline/           # Renderer, settings, homogeneous clipping, sky pass
│   │   ├── PostProcess/    # effect stack: SSAO, bloom, tone map, FXAA, vignette
│   │   └── Shadows/        # depth-only shadow-map pass
│   ├── Rasterization/      # scanline filler, painters, shaders, varyings, texture sampling
│   ├── Scenes/             # world, camera, projections, lights, fog and shadow settings
│   └── Shading/            # linear colour, light sets, ambient cube, sRGB conversion, shadow map
└── SoftEngine.WinForms/    # interactive front-end (net10.0-windows)
    ├── Debugging/          # event list, object table and pixel history panels
    └── Dialogs/            # model picker
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows (the interactive app uses WinForms; `SoftEngine.Core` itself is platform-neutral).

## Build & run

```bash
# build everything
dotnet build SoftEngine.slnx

# run the interactive app
dotnet run --project src/SoftEngine.WinForms
```

## Performance notes

The renderer avoids managed-heap traffic on the pixel hot path:

- **`ColorRGB` is a `readonly struct`** — shaders can produce a color per pixel without allocating.
- **Struct-based varyings and shaders** let the JIT inline the shade call instead of dispatching through an interface.
- **`ArrayPool`-backed vertex buffers** are rented per frame rather than allocated.
- **Tile bins are flat arrays filled by a counting sort**, reused across frames, so binning tens of thousands of triangles allocates nothing either.
- **The frame's lights are flattened into structs once**, so the per-pixel loop over them branches on a field instead of dispatching through `ILight`.

One cost was accepted rather than avoided: shaders now return `LinearColor`, so a shader with
no HDR range to give converts a `ColorRGB` in and the framebuffer converts it back out —
six lookup-table reads per pixel that the old packed-byte path did not pay. Both tables are
small enough to stay in L1, and the alternative was two parallel shading paths.

Work is measured rather than assumed: the numbers above come from a headless harness that
renders fixed scenes — dense models, heavy overdraw, a few huge triangles — and reports the
median frame time.

## Roadmap

- Cascaded shadow maps, so a large scene doesn't spend one uniform resolution on all of it.
- Physically-based shading (metallic–roughness), which the environment map is already most of the way to supporting.
- A glTF 2.0 importer, which would bring PBR materials and skinning in one well-specified format.
- Replace `Rotation3D` (Euler angles) with quaternion-based rotation.
- Frame capture history, so the debugger can step back through earlier frames.
- Buffer visualizers in the debugger: depth, normals, overdraw, mip level, the shadow map itself.

## Credits

Inspired by David Rousset's tutorial series
[*Learning how to write a 3D soft engine from scratch in C#, TypeScript or JavaScript*](https://www.davrous.com/2013/06/13/tutorial-series-learning-how-to-write-a-3d-soft-engine-from-scratch-in-c-typescript-or-javascript/),
which this project started from before growing its own pipeline, rasterizer, and shading system.

## License

[MIT](LICENSE) © Hilthon
