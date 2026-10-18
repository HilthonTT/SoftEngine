using SoftEngine.Core.Geometry;

namespace SoftEngine.WinForms.Debugging;

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

    public bool CanToggle => Mesh is not null;

    public bool Active => Mesh?.Visible ?? true;
}
