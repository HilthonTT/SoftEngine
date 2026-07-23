# SoftEngine

A **software 3D rasterizer** written in C#. The entire pipeline — model transforms, projection, culling, clipping, scanline rasterization, z-buffering and shading — runs on the CPU with no GPU or graphics-API dependency. A WinForms front-end renders live into a bitmap so you can orbit models, switch shading modes, and watch per-frame render statistics.

![Skull model (31k triangles) rendered with Gouraud shading](docs/screenshots/skull.png)

| ![Elephant model (26k triangles, 5 meshes) with Gouraud shading](docs/screenshots/elephant.png) | ![Parrot model (7k triangles) with Gouraud shading](docs/screenshots/parrot.png) |
| :--: | :--: |
| Elephant — 26k triangles across 5 meshes | Parrot — 7k triangles |

## What it does

- Loads and renders 3D models (Wavefront `.obj`, Collada `.dae`) and procedural primitives in real time.
- Rasterizes triangles with a generic scanline filler and a depth (z) buffer.
- Supports several shading modes — wireframe, solid, flat, Gouraud, Phong and textured — selectable at runtime.
- Provides an interactive arc-ball camera, WASD fly controls, gizmos (world axes, ground grid), and a live stats overlay.
- Ships a **graphics debugger** — event list, object table and per-pixel history — built on the renderer's own instrumentation.

## Shading modes

| Mode | Painter | Description |
| --- | --- | --- |
| **None** | — | Geometry only (combine with the wireframe overlay to see edges). |
| **Classic** | `ClassicPainter` | Flat per-triangle base color, no lighting. |
| **Flat** | `FlatPainter` | One Lambert (N·L) intensity per triangle from its centroid normal. |
| **Gouraud** | `GouraudPainter` | Per-vertex Lambert intensity interpolated across the triangle. |

A `WireFramePainter` overlay (Liang–Barsky homogeneous line clipping) can be drawn on top of any mode.

## Rendering pipeline

```
model ──worldMatrix──▶ world ──viewMatrix──▶ view ──projectionMatrix──▶ clip ──/w──▶ NDC ──▶ screen
```

Per frame, the `Renderer` ([`Pipeline/Renderer.cs`](src/SoftEngine.Core/Pipeline/Renderer.cs)):

1. Clears the color and z-buffers.
2. Transforms each mesh's vertices into view space (pooled `VertexBuffer` per mesh).
3. Rejects triangles behind the far plane, back-facing triangles (optional culling), and triangles outside the view frustum.
4. Projects survivors into clip space, maps to screen space, and hands them to the active painter.
5. Draws optional gizmos (XZ grid, world axes).

The rasterizer ([`Rasterization/ScanlineRasterizer.cs`](src/SoftEngine.Core/Rasterization/ScanlineRasterizer.cs)) sorts a triangle's vertices by Y, splits it at the middle vertex, and walks two half-triangles, interpolating depth plus an arbitrary *varying* payload. Painters only supply a **varying** type and a **shader** — both are `struct` generics, so the JIT devirtualizes and inlines the per-pixel shade call with no allocation on the hot path.

## Interactive app

The WinForms app ([`SoftEngine.WinForms`](src/SoftEngine.WinForms)) renders the scene into a 32-bpp bitmap that is blitted to a `Panel3D`.

| Control | Action |
| --- | --- |
| **Left-drag** | Orbit the arc-ball camera |
| **Right-drag** | Pan; **left+right-drag** dollies |
| **Mouse wheel** | Move the camera in/out — the status bar's zoom percentage follows it (100% is the framing a world loads with) |
| **W / A / S / D** | Fly the camera forward / left / back / right (**Q**/**E** for down/up). Hold **Shift** to move faster, **Ctrl** for fine steps; the step scales with the camera's distance, so it works on a 2-unit skull and a 1500-unit elephant alike |
| **Left-click the viewport** | Probe that pixel — its full write history appears in the Pixel History panel (**Esc** clears it) |
| **Load model…** | Pick a bundled world (skull, parrot, elephant, teapot, cubes, spheres, towns…) or open an OBJ/Collada file from disk |
| **Shading radios** | Switch between None / Classic / Flat / Gouraud / Phong / Textured |
| **Checkboxes** | Toggle wireframe triangles, back-face culling, XZ grid, world axes |

A stats overlay reports triangle counts (total / back-facing / out-of-view / behind), pixel counts (drawn / z-rejected), and calculation vs. paint timing per frame.

## Graphics debugger

The front-end doubles as a small graphics debugger, modelled on [Rasterizr Studio](https://github.com/tgjones/rasterizr). Because the whole pipeline runs on the CPU, the panels show what the renderer actually did rather than what a driver reported.

| Panel | Shows |
| --- | --- |
| **Graphics Event List** | Every step of the frame in pipeline order — viewport and depth-range setup, buffer clears, the view and projection matrices, then per mesh: vertex transform, cull results and the draw call, ending with the present. |
| **Graphics Object Table** | Every object the frame touched — render target, depth buffer, camera, projection, painter, lights, meshes and textures — with its size, vertex/triangle counts and dimensions. Meshes carry an **active** checkbox that drops them from the frame. |
| **Pixel History** | For the selected pixel: the clear, then each triangle that tried to write it — including the ones the depth test rejected — with the input-assembler and transformed vertex data, the depth comparison, and the previous → resulting colour. |

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
│   ├── Geometry/           # IMesh/Mesh, Triangle, primitives, OBJ/Collada importers
│   ├── Pipeline/           # Renderer, settings, homogeneous clipping
│   ├── Rasterization/      # scanline filler, painters, shaders, varyings
│   ├── Scenes/             # world, camera, projection, lights
│   └── Shading/            # Lambert lighting
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

## Roadmap

- Cache per-mesh vertex buffers across frames for static scenes (avoid per-frame `VertexBuffer` allocation).
- Replace `Rotation3D` (Euler angles) with quaternion-based rotation.
- Frame capture history, so the debugger can step back through earlier frames.

## Credits

Inspired by David Rousset's tutorial series
[*Learning how to write a 3D soft engine from scratch in C#, TypeScript or JavaScript*](https://www.davrous.com/2013/06/13/tutorial-series-learning-how-to-write-a-3d-soft-engine-from-scratch-in-c-typescript-or-javascript/),
which this project started from before growing its own pipeline, rasterizer, and shading system.

## License

[MIT](LICENSE) © Hilthon
