using Silk.NET.OpenGL;
using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Gpu;

public sealed class GpuMesh : IDisposable
{
    public const int Stride = 12;

    private readonly GL _gl;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _colorBuffer;
    private uint _colorTexture;

    private int _vertexCapacity;
    private int _indexCapacity;

    public GpuMesh(GL gl)
    {
        _gl = gl;

        _vertexArray = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _indexBuffer = gl.GenBuffer();

        BindLayout();
    }

    public int IndexCount { get; private set; }

    public bool HasTriangleColors => _colorTexture != 0;

    public Vector3[]? Source { get; private set; }

    private unsafe void BindLayout()
    {
        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);

        var stride = (uint)(Stride * sizeof(float));

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    public unsafe void Upload(IMesh mesh, float[] scratch, uint[] indexScratch, bool force)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        var vertices = mesh.Vertices;

        if (!force && ReferenceEquals(Source, vertices) && IndexCount == mesh.Triangles.Length * 3)
        {
            return;
        }

        Source = vertices;

        var count = vertices.Length;
        var normals = mesh.NormVertices;
        var texCoords = mesh.TexCoords;
        var tangents = mesh.Tangents;

        for (var i = 0; i < count; i++)
        {
            var at = i * Stride;

            var position = vertices[i];
            scratch[at] = position.X;
            scratch[at + 1] = position.Y;
            scratch[at + 2] = position.Z;

            var normal = i < normals.Length ? normals[i] : Vector3.Zero;
            scratch[at + 3] = normal.X;
            scratch[at + 4] = normal.Y;
            scratch[at + 5] = normal.Z;

            var uv = texCoords is not null && i < texCoords.Length ? texCoords[i] : Vector2.Zero;
            scratch[at + 6] = uv.X;

            scratch[at + 7] = 1f - uv.Y;

            var tangent = tangents is not null && i < tangents.Length ? tangents[i] : Vector4.Zero;
            scratch[at + 8] = tangent.X;
            scratch[at + 9] = tangent.Y;
            scratch[at + 10] = tangent.Z;
            scratch[at + 11] = tangent.W;
        }

        var triangles = mesh.Triangles;

        for (var t = 0; t < triangles.Length; t++)
        {
            var at = t * 3;
            indexScratch[at] = (uint)triangles[t].I0;
            indexScratch[at + 1] = (uint)triangles[t].I1;
            indexScratch[at + 2] = (uint)triangles[t].I2;
        }

        IndexCount = triangles.Length * 3;

        _gl.BindVertexArray(_vertexArray);

        var vertexBytes = (nuint)(count * Stride * sizeof(float));
        var indexBytes = (nuint)(IndexCount * sizeof(uint));

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        fixed (float* pointer = scratch)
        {
            if (count > _vertexCapacity)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, vertexBytes, pointer, BufferUsageARB.DynamicDraw);
                _vertexCapacity = count;
            }
            else
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, vertexBytes, pointer);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);

        fixed (uint* pointer = indexScratch)
        {
            if (IndexCount > _indexCapacity)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, indexBytes, pointer, BufferUsageARB.DynamicDraw);
                _indexCapacity = IndexCount;
            }
            else
            {
                _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, indexBytes, pointer);
            }
        }

        _gl.BindVertexArray(0);
    }

    public unsafe void UploadTriangleColors(IMesh mesh)
    {
        if (_colorTexture != 0)
        {
            return;
        }

        var colors = mesh.TriangleColors;

        if (colors.Length == 0)
        {
            return;
        }

        var bytes = new byte[colors.Length * 4];

        for (var i = 0; i < colors.Length; i++)
        {
            var at = i * 4;
            bytes[at] = colors[i].R;
            bytes[at + 1] = colors[i].G;
            bytes[at + 2] = colors[i].B;
            bytes[at + 3] = 255;
        }

        _colorBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.TextureBuffer, _colorBuffer);

        fixed (byte* pointer = bytes)
        {
            _gl.BufferData(BufferTargetARB.TextureBuffer, (nuint)bytes.Length, pointer, BufferUsageARB.StaticDraw);
        }

        _colorTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.TextureBuffer, _colorTexture);
        _gl.TexBuffer(TextureTarget.TextureBuffer, SizedInternalFormat.Rgba8, _colorBuffer);
        _gl.BindTexture(TextureTarget.TextureBuffer, 0);
    }

    public void BindTriangleColors(TextureUnit unit)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.TextureBuffer, _colorTexture);
    }

    public void Bind() => _gl.BindVertexArray(_vertexArray);

    public unsafe void Draw()
    {
        if (IndexCount == 0)
        {
            return;
        }

        _gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount, DrawElementsType.UnsignedInt, (void*)0);
    }

    public void Dispose()
    {
        if (_vertexArray != 0)
        {
            _gl.DeleteVertexArray(_vertexArray);
            _vertexArray = 0;
        }

        if (_vertexBuffer != 0)
        {
            _gl.DeleteBuffer(_vertexBuffer);
            _vertexBuffer = 0;
        }

        if (_indexBuffer != 0)
        {
            _gl.DeleteBuffer(_indexBuffer);
            _indexBuffer = 0;
        }

        if (_colorTexture != 0)
        {
            _gl.DeleteTexture(_colorTexture);
            _colorTexture = 0;
        }

        if (_colorBuffer != 0)
        {
            _gl.DeleteBuffer(_colorBuffer);
            _colorBuffer = 0;
        }
    }
}
