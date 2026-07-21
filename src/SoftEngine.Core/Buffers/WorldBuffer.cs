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
