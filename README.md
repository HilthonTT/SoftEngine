# SoftEngine

A software 3D rendering engine written in C# — the full pipeline (transforms, projection, culling, rasterization) runs on the CPU rather than the GPU, with no graphics API dependency.

> **Status:** early-stage / work in progress. The core math and pipeline building blocks exist, but there is not yet a runnable sample application or a triangle rasterizer — see [Roadmap](#roadmap).

## Features

- **Mesh model** — `IMesh`/`Mesh` with vertices, triangle indices, per-triangle colors, and automatic vertex-normal calculation ([`Geometry/`](src/SoftEngine.Core/Geometry))
- **Primitives** — `Cube` and a recursively-subdivided `IcoSphere` ([`Geometry/Primitives/`](src/SoftEngine.Core/Geometry/Primitives))
- **Scene graph** — `IWorld`/`SimpleWorld` holding meshes and lights ([`Scenes/`](src/SoftEngine.Core/Scenes))
- **Camera & projection** — `ICamera` view matrix and a `PerspectiveProjection` built on `System.Numerics.Matrix4x4` ([`Scenes/Cameras/`](src/SoftEngine.Core/Scenes/Cameras), [`Scenes/Projections/`](src/SoftEngine.Core/Scenes/Projections))
- **Lighting** — `PointLight` and Lambertian (N·L) shading ([`Scenes/Lights/`](src/SoftEngine.Core/Scenes/Lights), [`Shading/LambertLighting.cs`](src/SoftEngine.Core/Shading/LambertLighting.cs))
- **Vertex pipeline** — a pooled `VertexBuffer` carrying world/view/projected/normal data per vertex, plus per-triangle frustum and far-plane culling ([`Buffers/`](src/SoftEngine.Core/Buffers), [`Geometry/Triangle.cs`](src/SoftEngine.Core/Geometry/Triangle.cs))
- **Frame buffer** — a `FrameBuffer` with an int color buffer, z-buffer, NDC-to-screen mapping, and a 3D Bresenham line drawer ([`Buffers/FrameBuffer.cs`](src/SoftEngine.Core/Buffers/FrameBuffer.cs))
- **Diagnostics** — `RenderStats` tracks triangle/pixel counts and per-frame calculation vs. paint timing ([`Diagnostics/RenderStats.cs`](src/SoftEngine.Core/Diagnostics/RenderStats.cs))

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Building

```bash
dotnet build SoftEngine.slnx
```

There is currently no executable/sample project referencing `SoftEngine.Core`; it builds as a standalone class library.

## Roadmap

- Triangle rasterizer (scanline fill using the existing z-buffer)
- A runnable sample app / windowed output target
- Replace `Rotation3D` (Euler angles) with a quaternion-based rotation

## License

[MIT](LICENSE) © Hilthon
