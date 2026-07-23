using SoftEngine.Core.Geometry;

namespace SoftEngine.WinForms.Debugging;

/// <summary>One row of the graphics object table.</summary>
internal sealed record SceneObjectRow(
    int Id,
    string Type,
    string Detail,
    long SizeBytes,
    int VertexCount,
    int TriangleCount,
    int Width,
    int Height,
    Mesh? Mesh)
{
    public string Identifier => $"obj:{Id}";

    /// <summary>Only meshes can be switched off; everything else is part of the frame by definition.</summary>
    public bool CanToggle => Mesh is not null;

    public bool Active => Mesh?.Visible ?? true;
}
