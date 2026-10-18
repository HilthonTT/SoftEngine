using Silk.NET.OpenGL;
using System.Numerics;
using System.Reflection;

namespace SoftEngine.Gpu;

public sealed class GpuProgram : IDisposable
{
    private const string VersionHeader = "#version 330 core\n";

    private readonly GL _gl;

    private readonly Dictionary<string, (int Location, GLEnum Type)> _uniforms = [];

    private uint _handle;

    private GpuProgram(GL gl, uint handle)
    {
        _gl = gl;
        _handle = handle;

        Introspect();
    }

    private unsafe void Introspect()
    {
        _gl.GetProgram(_handle, ProgramPropertyARB.ActiveUniforms, out var count);
        _gl.GetProgram(_handle, ProgramPropertyARB.ActiveUniformMaxLength, out var maxLength);

        for (uint i = 0; i < count; i++)
        {
            var buffer = new byte[System.Math.Max(maxLength, 1)];
            UniformType type;
            uint written;

            fixed (byte* pointer = buffer)
            {
                _gl.GetActiveUniform(_handle, i, (uint)buffer.Length, out var length, out _, out type, pointer);
                written = (uint)length;
            }

            var name = System.Text.Encoding.UTF8.GetString(buffer, 0, (int)System.Math.Min(written, (uint)buffer.Length));

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var bracket = name.IndexOf('[', StringComparison.Ordinal);
            if (bracket >= 0)
            {
                name = name[..bracket];
            }

            var location = _gl.GetUniformLocation(_handle, name);

            if (location >= 0)
            {
                _uniforms[name] = (location, (GLEnum)type);
            }
        }
    }

    public uint Handle => _handle;

    public static GpuProgram Create(GL gl, string vertexResource, string fragmentResource, bool includeCommon)
    {
        ArgumentNullException.ThrowIfNull(gl, nameof(gl));

        var prelude = includeCommon ? VersionHeader + ReadResource("common.glsl") : VersionHeader;

        var vertex = Compile(gl, ShaderType.VertexShader, prelude + ReadResource(vertexResource), vertexResource);
        var fragment = Compile(gl, ShaderType.FragmentShader, prelude + ReadResource(fragmentResource), fragmentResource);

        var handle = gl.CreateProgram();

        gl.AttachShader(handle, vertex);
        gl.AttachShader(handle, fragment);
        gl.LinkProgram(handle);

        gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out var linked);

        gl.DetachShader(handle, vertex);
        gl.DetachShader(handle, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        if (linked == 0)
        {
            var log = gl.GetProgramInfoLog(handle);
            gl.DeleteProgram(handle);

            throw new InvalidOperationException(
                $"Linking {vertexResource} with {fragmentResource} failed: {log}");
        }

        return new GpuProgram(gl, handle);
    }

    private static uint Compile(GL gl, ShaderType type, string source, string name)
    {
        var shader = gl.CreateShader(type);

        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);

        if (compiled != 0)
        {
            return shader;
        }

        var log = gl.GetShaderInfoLog(shader);
        gl.DeleteShader(shader);

        throw new InvalidOperationException($"Compiling {name} failed: {log}");
    }

    private static string ReadResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var full = $"{typeof(GpuProgram).Namespace}.Shaders.{name}";

        using var stream = assembly.GetManifestResourceStream(full)
            ?? throw new InvalidOperationException($"Shader resource '{full}' is missing from the assembly.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Use() => _gl.UseProgram(_handle);

    private int Location(string name) =>
        _uniforms.TryGetValue(name, out var uniform) ? uniform.Location : -1;

    private void SetScalar(string name, int asInt, float asFloat)
    {
        if (!_uniforms.TryGetValue(name, out var uniform))
        {
            return;
        }

        if (uniform.Type is GLEnum.Float or GLEnum.Double)
        {
            _gl.Uniform1(uniform.Location, asFloat);
        }
        else
        {
            _gl.Uniform1(uniform.Location, asInt);
        }
    }

    public void Set(string name, int value) => SetScalar(name, value, value);

    public void Set(string name, float value) => SetScalar(name, (int)value, value);

    public void Set(string name, bool value) => SetScalar(name, value ? 1 : 0, value ? 1f : 0f);

    public void Set(string name, Vector2 value) => _gl.Uniform2(Location(name), value.X, value.Y);

    public void Set(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);

    public void Set(string name, Vector4 value) => _gl.Uniform4(Location(name), value.X, value.Y, value.Z, value.W);

    public unsafe void Set(string name, in Matrix4x4 value)
    {
        var location = Location(name);

        if (location < 0)
        {
            return;
        }

        fixed (Matrix4x4* pointer = &value)
        {
            _gl.UniformMatrix4(location, 1, false, (float*)pointer);
        }
    }

    public unsafe void SetMatrix3(string name, in Matrix4x4 value)
    {
        var location = Location(name);

        if (location < 0)
        {
            return;
        }

        Span<float> packed =
        [
            value.M11, value.M12, value.M13,
            value.M21, value.M22, value.M23,
            value.M31, value.M32, value.M33,
        ];

        fixed (float* pointer = packed)
        {
            _gl.UniformMatrix3(location, 1, false, pointer);
        }
    }

    public unsafe void SetArray(string name, ReadOnlySpan<Vector3> values)
    {
        var location = Location(name);

        if (location < 0 || values.Length == 0)
        {
            return;
        }

        fixed (Vector3* pointer = values)
        {
            _gl.Uniform3(location, (uint)values.Length, (float*)pointer);
        }
    }

    public unsafe void SetArray(string name, ReadOnlySpan<Vector4> values)
    {
        var location = Location(name);

        if (location < 0 || values.Length == 0)
        {
            return;
        }

        fixed (Vector4* pointer = values)
        {
            _gl.Uniform4(location, (uint)values.Length, (float*)pointer);
        }
    }

    public unsafe void SetArray(string name, ReadOnlySpan<Vector2> values)
    {
        var location = Location(name);

        if (location < 0 || values.Length == 0)
        {
            return;
        }

        fixed (Vector2* pointer = values)
        {
            _gl.Uniform2(location, (uint)values.Length, (float*)pointer);
        }
    }

    public unsafe void SetArray(string name, ReadOnlySpan<Matrix4x4> values)
    {
        var location = Location(name);

        if (location < 0 || values.Length == 0)
        {
            return;
        }

        fixed (Matrix4x4* pointer = values)
        {
            _gl.UniformMatrix4(location, (uint)values.Length, false, (float*)pointer);
        }
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        _gl.DeleteProgram(_handle);
        _handle = 0;
    }
}
