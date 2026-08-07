# SoftEngine

[![CI](https://github.com/HilthonTT/SoftEngine/actions/workflows/ci.yml/badge.svg)](https://github.com/HilthonTT/SoftEngine/actions/workflows/ci.yml)
[![CodeQL](https://github.com/HilthonTT/SoftEngine/actions/workflows/codeql.yml/badge.svg)](https://github.com/HilthonTT/SoftEngine/actions/workflows/codeql.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A **software 3D rasterizer** in C#. The whole pipeline — transforms, culling, clipping, scanline
rasterization, z-buffering and shading — runs on the CPU with no graphics-API dependency. A
WinForms front-end renders live into a bitmap; a CLI renders headlessly to PNG; an OpenGL backend
can fill the same frame on a graphics adapter for comparison, and a path tracer can render it as
a reference.

![Skull model (31k triangles) rendered with Gouraud shading](docs/screenshots/skull.png)

| ![Elephant](docs/screenshots/elephant.png) | ![Parrot](docs/screenshots/parrot.png) |
| :--: | :--: |
| Elephant — 26k triangles, 5 meshes | Parrot — 7k triangles |

> **[DESIGN.md](DESIGN.md)** is the companion to this file: why each part is built the way it is,
> what the trade-offs were, and the traps already paid for.

## What it does

- Loads `.obj`, `.dae` (Collada) and **glTF 2.0** `.gltf`/`.glb`, plus procedural primitives.
- Tiled, vectorized scanline fill with a z-buffer, hierarchical-Z and **occlusion culling**.
- Eight shading modes from wireframe to **physically based** (Cook-Torrance metallic-roughness).
- Any number of coloured directional / point / spot lights, in an **HDR linear float target**.
- **Cascaded shadow maps**, SSAO, and an environment cube map as both skybox and ambient — loadable
  from a Radiance `.hdr` panorama that keeps its range.
- **Baked indirect light**: the path tracer measured into a grid of probes the rasterizer reads.
- Materials: albedo, normal, specular, metallic-roughness and emissive maps, plus **alpha cutouts**.
- **Scene graph**, keyframed **animation** with clip **blending**, and linear-blend **skinning**.
- Post-process stack: bloom, tone mapping, FXAA, vignette. Supersampling, **TAA** and **motion blur**.
- Transparency sorted per triangle, or **order-independent** and resolved per pixel.
- **Scene editing**: ray-cast picking, drag gizmos and Blender-style keyboard transforms, undoable.
- **JSON scene files**, a **headless CLI** (stills and sequences), and a **graphics debugger** with
  buffer views and frame history.

| ![Shadow mapping](docs/screenshots/shadows.png) | ![Normal mapping](docs/screenshots/normal-mapping.png) |
| :--: | :--: |
| Shadow mapping | Normal mapping — only the left cube has a normal map |

## Build & run

```bash
dotnet build SoftEngine.slnx
dotnet run --project src/SoftEngine.WinForms                  # interactive viewer
dotnet test tests/SoftEngine.Core.Tests                       # 775 tests
dotnet run -c Release --project bench/SoftEngine.Benchmarks   # Release, or you measure the debugger
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Windows for the viewer;
`SoftEngine.Core` itself is platform-neutral. The GPU backend wants an OpenGL 3.3 driver and falls
back with a reason when there isn't one.

## Interactive app

| Control | Action |
| --- | --- |
| **Left-drag / right-drag / wheel** | Orbit, pan (left+right dollies), move in and out |
| **W A S D / Q E** | Fly; **Shift** faster, **Ctrl** finer. The step scales with camera distance |
| **Numpad 1/3/7/9, 4/6/8/2** | Axis views and 15° turns, as close to Blender's as a Y-up world allows |
| **Left-click** | Probe the pixel *and* pick what is under it (**Esc** clears) |
| **Shift+A** | Add a primitive — plane, cube, UV/ico sphere, cylinder, cone, torus |
| **G / S** | Move / scale the selected mesh with the cursor; **X Y Z** constrain, click confirms, **Esc** cancels |
| **X** or **Del** | Delete the selected mesh |
| **Drag a gizmo handle** | Move, turn or stretch the picked mesh instead |
| **Ctrl+Z / Ctrl+Y** | Undo / redo; the Edit menu names the edit |
| **Ctrl+G** | Snap to a grid — whole units, 15°, tenths of scale |
| **Ctrl+S** | Save the scene as JSON (**File ▸ Open scene…** brings it back) |
| **F12 / F11 / F1** | Screenshot / focus the viewport / the full keyboard reference |

`G`, `S` and `X` act on the selection and only on it, so with nothing selected `S` still flies the
camera and `X` still turns the view. **Esc** deselects and hands them back.

There are about thirty-five bindings and only a third are menu items, so
**Help ▸ Keyboard and mouse** (**F1**) is the list — a control nobody can discover is a control
nobody has. Plus **Load model…** (bundled demos or a file from disk), **Bake indirect light**,
shading radios, buffer view, cascade count, gizmo mode, and display/post-processing checkboxes.

**Dropping a file on the window** opens it by extension: `.obj`/`.dae`/`.gltf`/`.glb` as a model,
`.json` as a scene, `.hdr`/`.pic` and ordinary images as a panorama. **File ▸ Open recent** keeps the
last ten. Window placement, open panels, splitter positions, the recent list and the render backend
live in `%APPDATA%\SoftEngine\viewer.json`; nothing in there may throw, so a corrupt file means
"use the defaults" and never a viewer that will not start.

**View ▸ Rendered by** picks CPU, GPU or path tracer. With nothing saved it opens on the CPU: the
viewer is a demonstration of a software rasterizer, and defaulting to the graphics card would quietly
show you something else.

## Headless rendering

```bash
dotnet run -c Release --project src/SoftEngine.Cli -- model.gltf -o frame.png -w 1920 -h 1080 -p pbr --ss 2
```

| | |
| --- | --- |
| `-w`, `-h`, `--ss` | resolution and supersampling |
| `-p`, `--post`, `--view`, `--filter` | painter, post effects, buffer view, texture filtering |
| `--yaw`, `--pitch`, `--zoom`, `--camera`, `-t` | where to stand, and how far into the animation |
| `--env`, `--environment-size`, `--hdr-sky` | light it with a panorama or the linear-light sky |
| `--shadows`, `--cascades` | shadow pass |
| `--oit` | resolve transparency per pixel instead of by sorting the triangles |
| `--bake`, `--bake-resolution`, `--bake-rays`, `--bake-bounces` | measure indirect light into probes first |
| `--trace`, `--samples`, `--bounces`, `--physical` | path-trace instead of rasterizing |
| `--frames`, `--fps`, `--turntable`, `--shutter` | render a numbered sequence |
| `--backend`, `--gpu`, `--cpu`, `--gpu-info` | where the frame is filled |
| `--scene`, `--stats` | apply a saved scene; print counts and timings |

```bash
dotnet run -c Release --project src/SoftEngine.Cli -- parrot.dae --frames 30 --fps 30 --turntable 360 --shutter 0.5 -o turntable.png
ffmpeg -framerate 30 -i turntable.%04d.png -pix_fmt yuv420p turntable.mp4
```

`--frames <n>` writes `frame.0000.png`, … advancing the animation by `1/--fps` and sweeping the
camera `--turntable` degrees across the run — four zero-padded digits, because every tool that reads
a sequence sorts the names as text. The camera walks the arc rather than the model turning inside it,
since spinning geometry inside its own lighting looks like the lighting is spinning. Three further
differences from the viewer: the framing is **solved** (`r / sin(fov/2)`) rather than guessed; the
camera is a **bearing**, not an accumulated gesture, so the same three numbers give the same frame on
every machine; and textures decode from **PNG only** — a model with JPEG maps renders untextured and
the program says how many it skipped.

## Layout

```
src/
├── SoftEngine.Core/        # engine, no UI dependency (net10.0)
│   ├── Acceleration/       # world triangles flattened, and the SAH BVH over them
│   ├── Animation/          # tracks, interpolation, channels, clips, playback, blending
│   ├── Baking/             # the irradiance bake and what it is allowed to spend
│   ├── Buffers/            # FrameBuffer, velocity, fragments, pooled vertex/world buffers
│   ├── Diagnostics/        # stats, event log, pixel history, frame captures
│   ├── Editing/            # undoable edits and the history the tools record into
│   ├── Geometry/           # IMesh, Material, primitives, OBJ/Collada — Gltf/, Skinning/
│   ├── Gizmos/             # grid, axes, skeleton, drag handles, modal transforms, snapping
│   ├── Imaging/            # PNG codec, Radiance .hdr reader
│   ├── Picking/            # ray, intersection, scene picker
│   ├── Pipeline/           # Renderer, clipping, sky — Culling/ Debugging/ PostProcess/
│   │                       #   Shadows/ Temporal/
│   ├── Rasterization/      # scanline filler, painters, shaders, varyings, sampling
│   ├── Scenes/             # world, camera, projections, lights — Graph/ Serialization/
│   ├── Shading/            # linear colour, light sets, ambient cube, GGX, BRDF LUT
│   └── Tracing/            # path tracer, the integrator it shares with the bake, sampler
├── SoftEngine.Gpu/         # OpenGL backend via Silk.NET, and Shaders/
├── SoftEngine.Cli/         # headless renderer (net10.0 console)
└── SoftEngine.WinForms/    # interactive front-end (net10.0-windows)

bench/SoftEngine.Benchmarks/   # headless frame-time harness
tests/SoftEngine.Core.Tests/   # xUnit suite, and Golden/ image baselines
```

## Testing

`dotnet test tests/SoftEngine.Core.Tests` — **775 tests**. Alongside the unit tests, seventeen
generated scenes are rendered headless at 320×180 and compared against committed PNG baselines, so a
change that alters the picture shows up in the diff as a picture. `SOFTENGINE_UPDATE_GOLDEN=1` is the
only way to re-record one, and a failing run drops the actual frame and a diff image beside the
baseline. See [DESIGN.md § Testing](DESIGN.md#testing-and-benchmarks) for how the comparison is
weighted and why re-recording on failure is the thing not to do.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). The short version: `dotnet format SoftEngine.slnx
--verify-no-changes`, `dotnet build`, `dotnet test`, and — because this is a renderer — say which
world and shading mode you looked at, with a screenshot for anything that changes the picture.

CI builds Debug and Release on Windows with warnings as errors, runs the tests, checks formatting
against [`.editorconfig`](.editorconfig), and uploads golden-image `.diff.png` artifacts when an
image test fails. If your change moves a golden baseline, re-record it deliberately
(`SOFTENGINE_UPDATE_GOLDEN=1 dotnet test`) and **say so in the pull request** — a silently updated
baseline is the one thing that makes that suite worthless.

Security reports go through [private advisories](SECURITY.md), not public issues. The file parsers
— glTF, Collada, OBJ, PNG, Radiance HDR — are where the real attack surface is.

## Credits

Started from David Rousset's
[*Learning how to write a 3D soft engine from scratch*](https://www.davrous.com/2013/06/13/tutorial-series-learning-how-to-write-a-3d-soft-engine-from-scratch-in-c-typescript-or-javascript/)
before growing its own pipeline, rasterizer and shading system.

## License

[MIT](LICENSE) © Hilthon
