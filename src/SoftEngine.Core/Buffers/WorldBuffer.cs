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

    /// <summary>
    /// Whether this buffer can still hold the given world, so the renderer can reuse it
    /// frame after frame instead of allocating one vertex buffer per mesh per frame.
    /// </summary>
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

    /// <summary>
    /// Drops what the previous frame left in these buffers, so a mesh the coming frame culls
    /// cannot be read as though it had been transformed for it.
    ///
    /// <para>
    /// It used to carry more than that: the pipeline memoized transforms behind zero-value
    /// sentinels (<c>Proj == Vector4.Zero</c>, <c>Norm == Vector3.Zero</c>), and a zeroed
    /// <see cref="Buffers.Vertices"/> was what told it a vertex still had to be computed. The
    /// cull phase now computes every vertex of every surviving mesh up front and
    /// unconditionally — that is what makes it safe to divide across threads, and why the
    /// painters no longer transform anything while the fill runs in parallel. See
    /// <c>Renderer.CullPhase.cs</c>.
    /// </para>
    /// </summary>
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
