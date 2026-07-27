using SoftEngine.Core.Scenes.Cameras;
using System.Numerics;

namespace SoftEngine.Benchmarks;

/// <summary>
/// A camera that does not move. The interactive app's arc-ball lives in the WinForms project
/// and would drag a UI dependency in here; a benchmark wants a fixed viewpoint anyway, so that
/// every run frames exactly the same pixels.
/// </summary>
internal sealed class FixedCamera(Vector3 position, Vector3 target) : ICamera
{
    public Vector3 Position { get; set; } = position;

    public Vector3 Target { get; set; } = target;

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
}
