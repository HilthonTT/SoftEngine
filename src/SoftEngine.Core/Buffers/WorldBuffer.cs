using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Buffers;

namespace SoftEngine.Core.Buffers;

public sealed class WorldBuffer : IDisposable
{
    private readonly int _size;
    private readonly static ArrayPool<VertexBuffer> _vertexBuffer3Bag = ArrayPool<VertexBuffer>.Shared;

    public VertexBuffer[] VertexBuffers { get; set; } = [];

    public WorldBuffer(IWorld world)
    {
        List<IMesh> meshes = world.Meshes;
        _size = meshes.Count;

        VertexBuffers = _vertexBuffer3Bag.Rent(_size);

        for (int i = 0; i < _size; i++)
        {
            VertexBuffers[i] = new(meshes[i].Vertices.Length);
        }
    }

    public bool Fits(IWorld world)
    {
        List<IMesh> meshes = world.Meshes;

        if (meshes.Count != _size)
        {
            return false;
        }

        for (int i = 0; i < _size; i++)
        {
            if (VertexBuffers[i].Size < meshes[i].Vertices.Length)
            {
                return false;
            }
        }

        return true;
    }

    public void Reset()
    {
        for (int i = 0; i < _size; i++)
        {
            var vertexBuffer = VertexBuffers[i];
            Array.Clear(vertexBuffer.Vertices, 0, vertexBuffer.Size);
            vertexBuffer.ResetClipped();
            vertexBuffer.Mesh = null;
        }
    }

    public void Dispose()
    {
        int nv = VertexBuffers.Length;

        for (int i = 0; i < nv; i++)
        {
            VertexBuffers[i]?.Dispose();
        }
        _vertexBuffer3Bag.Return(VertexBuffers, true);
    }
}
