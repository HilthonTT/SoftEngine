using SoftEngine.Core.Geometry;
using System.Buffers;
using System.Numerics;

namespace SoftEngine.Core.Buffers;

public sealed class VertexBuffer : IDisposable
{
    private static readonly ArrayPool<Vertices> _verticeBag = ArrayPool<Vertices>.Create();

    public VertexBuffer(int vertexCount)
    {
        Size = vertexCount;
        Vertices = _verticeBag.Rent(vertexCount);
    }

    public IMesh? Mesh { get; set; }

    public Vertices[] Vertices { get; } = [];

    public int Size { get; }

    public Matrix4x4 WorldMatrix { get; set; }

    public void Dispose()
    {
        _verticeBag.Return(Vertices, true);
    }
}
