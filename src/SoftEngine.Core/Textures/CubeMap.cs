using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Textures;

/// <summary>
/// An environment sampled by direction rather than by UV: six square textures, one per
/// face of a cube centred on the viewer, addressed by whichever axis a direction points
/// most strongly along.
///
/// It is the cheapest thing that answers "what is out there, that way" — which is the
/// question both a skybox and an ambient term are asking. A skybox asks it once per
/// background pixel, along the ray through that pixel; ambient lighting asks it about the
/// hemisphere around a surface normal, which is what <see cref="Shading.AmbientCube"/>
/// precomputes from one of these.
///
/// The face layout is the usual one (the same as OpenGL's and Direct3D's), so an
/// environment authored for a GPU renderer drops straight in.
///
/// A cube may additionally carry the same six faces in linear floats — see
/// <see cref="SampleRadiance"/> — which is how an environment loaded from an
/// <see cref="Imaging.HdrImage"/> keeps a sun that is thousands of times brighter than the sky
/// around it. The byte faces stay either way, because a skybox drawn on screen, a texture
/// handed to a graphics adapter and a picker's thumbnail all still want bytes.
/// </summary>
public sealed class CubeMap
{
    private readonly Texture[] _faces;

    /// <summary>
    /// Optional linear-light faces, three floats per texel, parallel to <see cref="_faces"/> and
    /// at the same resolutions. Null for an environment that only ever had bytes.
    /// </summary>
    private readonly float[][]? _radiance;

    public CubeMap(Texture[] faces)
        : this(faces, null)
    {
    }

    /// <summary>
    /// A cube map whose lighting is read from <paramref name="radiance"/> — six arrays of
    /// linear RGB floats, one per face, each three floats per texel of the matching
    /// <paramref name="faces"/> entry.
    /// </summary>
    public CubeMap(Texture[] faces, float[][]? radiance)
    {
        ArgumentNullException.ThrowIfNull(faces, nameof(faces));

        if (faces.Length != 6)
        {
            throw new ArgumentException($"A cube map has six faces, got {faces.Length}.", nameof(faces));
        }

        foreach (var face in faces)
        {
            ArgumentNullException.ThrowIfNull(face, nameof(face));
        }

        if (radiance is not null)
        {
            if (radiance.Length != 6)
            {
                throw new ArgumentException($"A cube map has six faces, got {radiance.Length} radiance faces.", nameof(radiance));
            }

            for (var f = 0; f < 6; f++)
            {
                ArgumentNullException.ThrowIfNull(radiance[f], nameof(radiance));

                var expected = faces[f].Width * faces[f].Height * 3;

                if (radiance[f].Length != expected)
                {
                    throw new ArgumentException(
                        $"Radiance face {(CubeFace)f} has {radiance[f].Length} floats; " +
                        $"{faces[f].Width}×{faces[f].Height} RGB needs {expected}.",
                        nameof(radiance));
                }
            }
        }

        _faces = faces;
        _radiance = radiance;
    }

    /// <summary>The six faces, indexed by <see cref="CubeFace"/>.</summary>
    public Texture this[CubeFace face] => _faces[(int)face];

    /// <summary>Bilinear rather than nearest sampling, which a low-resolution sky needs.</summary>
    public TextureFiltering Filtering { get; set; } = TextureFiltering.Bilinear;

    /// <summary>
    /// Whether this environment carries linear floats, and so has a range above white for
    /// <see cref="SampleRadiance"/> to return and the prefilter to convolve.
    /// </summary>
    public bool IsHighDynamicRange => _radiance is not null;

    /// <summary>
    /// The colour in a direction. The direction does not need to be normalized — only its
    /// largest component and the ratios to the other two matter.
    /// </summary>
    public ColorRGB Sample(Vector3 direction)
    {
        var (face, u, v) = Project(direction);
        var texture = _faces[(int)face];

        // Cube-map V runs downward from the top of the face; the texture's rows are stored
        // the same way, so v maps straight to a row here.
        return Filtering == TextureFiltering.Bilinear
            ? Bilinear(texture, u, v)
            : Nearest(texture, u, v);
    }

    /// <summary>
    /// The light arriving from a direction: the float faces when there are any, and otherwise
    /// exactly what <see cref="Sample"/> returns, decoded.
    ///
    /// Every consumer that treats the environment as light rather than as a picture goes through
    /// here — the skybox pass, <see cref="AmbientCube"/>, and the prefilter behind
    /// <see cref="PrefilteredEnvironment"/>. The fallback filters the sRGB bytes and decodes the
    /// blend, rather than decoding first and blending in linear light, because that is the order
    /// those three consumers already used and the difference is a fraction of a byte on a
    /// gradient — worth less than making every existing frame move.
    /// </summary>
    public LinearColor SampleRadiance(Vector3 direction)
    {
        if (_radiance is null)
        {
            return Sample(direction);
        }

        var (face, u, v) = Project(direction);
        var texture = _faces[(int)face];

        return Filtering == TextureFiltering.Bilinear
            ? BilinearRadiance(_radiance[(int)face], texture.Width, texture.Height, u, v)
            : NearestRadiance(_radiance[(int)face], texture.Width, texture.Height, u, v);
    }

    /// <summary>
    /// The linear floats of one face, three per texel, or null when this cube has none. Exposed
    /// for a backend that has to upload the environment somewhere else — a float texture on a
    /// graphics adapter — rather than resample it and disagree by a texel.
    /// </summary>
    public float[]? Radiance(CubeFace face) => _radiance?[(int)face];

    /// <summary>
    /// Addressing clamps rather than wrapping, unlike an ordinary texture. A cube face's
    /// neighbour along u is the next face round, not the far edge of this one — wrapping
    /// would draw a stripe of the sky behind you along every seam.
    /// </summary>
    private static ColorRGB Nearest(Texture texture, float u, float v)
    {
        var x = System.Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
        var y = System.Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);

        return ColorRGB.FromPacked(texture.Pixels[x + y * texture.Width]);
    }

    private static ColorRGB Bilinear(Texture texture, float u, float v)
    {
        var width = texture.Width;
        var height = texture.Height;

        var fx = u * width - 0.5f;
        var fy = v * height - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);

        var tx = fx - x0;
        var ty = fy - y0;

        var xa = System.Math.Clamp(x0, 0, width - 1);
        var xb = System.Math.Clamp(x0 + 1, 0, width - 1);
        var ya = System.Math.Clamp(y0, 0, height - 1) * width;
        var yb = System.Math.Clamp(y0 + 1, 0, height - 1) * width;

        var pixels = texture.Pixels;

        return Blend(
            Blend(ColorRGB.FromPacked(pixels[xa + ya]), ColorRGB.FromPacked(pixels[xb + ya]), tx),
            Blend(ColorRGB.FromPacked(pixels[xa + yb]), ColorRGB.FromPacked(pixels[xb + yb]), tx),
            ty);
    }

    private static ColorRGB Blend(ColorRGB a, ColorRGB b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static LinearColor NearestRadiance(float[] radiance, int width, int height, float u, float v)
    {
        var x = System.Math.Clamp((int)(u * width), 0, width - 1);
        var y = System.Math.Clamp((int)(v * height), 0, height - 1);

        return At(radiance, x + y * width);
    }

    private static LinearColor BilinearRadiance(float[] radiance, int width, int height, float u, float v)
    {
        var fx = u * width - 0.5f;
        var fy = v * height - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);

        var tx = fx - x0;
        var ty = fy - y0;

        var xa = System.Math.Clamp(x0, 0, width - 1);
        var xb = System.Math.Clamp(x0 + 1, 0, width - 1);
        var ya = System.Math.Clamp(y0, 0, height - 1) * width;
        var yb = System.Math.Clamp(y0 + 1, 0, height - 1) * width;

        var top = LinearColor.Lerp(At(radiance, xa + ya), At(radiance, xb + ya), tx);
        var bottom = LinearColor.Lerp(At(radiance, xa + yb), At(radiance, xb + yb), tx);

        return LinearColor.Lerp(top, bottom, ty);
    }

    private static LinearColor At(float[] radiance, int texel)
    {
        var i = texel * 3;
        return new LinearColor(radiance[i], radiance[i + 1], radiance[i + 2]);
    }

    /// <summary>
    /// Which face a direction lands on, and where. The major axis picks the face; the
    /// other two components, divided by it, give coordinates in [0, 1] across it.
    /// </summary>
    public static (CubeFace Face, float U, float V) Project(Vector3 direction)
    {
        var absX = MathF.Abs(direction.X);
        var absY = MathF.Abs(direction.Y);
        var absZ = MathF.Abs(direction.Z);

        CubeFace face;
        float major, uc, vc;

        if (absX >= absY && absX >= absZ)
        {
            major = absX;
            if (direction.X > 0f)
            {
                face = CubeFace.PositiveX;
                uc = -direction.Z;
                vc = -direction.Y;
            }
            else
            {
                face = CubeFace.NegativeX;
                uc = direction.Z;
                vc = -direction.Y;
            }
        }
        else if (absY >= absZ)
        {
            major = absY;
            if (direction.Y > 0f)
            {
                face = CubeFace.PositiveY;
                uc = direction.X;
                vc = direction.Z;
            }
            else
            {
                face = CubeFace.NegativeY;
                uc = direction.X;
                vc = -direction.Z;
            }
        }
        else
        {
            major = absZ;
            if (direction.Z > 0f)
            {
                face = CubeFace.PositiveZ;
                uc = direction.X;
                vc = -direction.Y;
            }
            else
            {
                face = CubeFace.NegativeZ;
                uc = -direction.X;
                vc = -direction.Y;
            }
        }

        if (major < 1e-20f)
        {
            return (CubeFace.PositiveY, 0.5f, 0.5f);
        }

        var inverse = 0.5f / major;

        return (face, uc * inverse + 0.5f, vc * inverse + 0.5f);
    }

    /// <summary>
    /// The direction a point on a face looks along — the inverse of <see cref="Project"/>,
    /// and what generating a cube map procedurally needs. Not normalized.
    /// </summary>
    public static Vector3 Direction(CubeFace face, float u, float v)
    {
        var uc = 2f * u - 1f;
        var vc = 2f * v - 1f;

        return face switch
        {
            CubeFace.PositiveX => new Vector3(1f, -vc, -uc),
            CubeFace.NegativeX => new Vector3(-1f, -vc, uc),
            CubeFace.PositiveY => new Vector3(uc, 1f, vc),
            CubeFace.NegativeY => new Vector3(uc, -1f, -vc),
            CubeFace.PositiveZ => new Vector3(uc, -vc, 1f),
            _ => new Vector3(-uc, -vc, -1f),
        };
    }

    /// <summary>
    /// Builds a cube map by evaluating <paramref name="shade"/> along the direction through
    /// the centre of every texel. The way to get an environment without an asset to load.
    /// </summary>
    public static CubeMap Generate(int resolution, Func<Vector3, ColorRGB> shade)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution);
        ArgumentNullException.ThrowIfNull(shade, nameof(shade));

        var faces = new Texture[6];

        for (var f = 0; f < 6; f++)
        {
            var pixels = new int[resolution * resolution];

            for (var y = 0; y < resolution; y++)
            {
                var v = (y + 0.5f) / resolution;

                for (var x = 0; x < resolution; x++)
                {
                    var u = (x + 0.5f) / resolution;
                    var direction = Vector3.Normalize(Direction((CubeFace)f, u, v));

                    pixels[x + y * resolution] = shade(direction).Color;
                }
            }

            faces[f] = new Texture(resolution, resolution, pixels);
        }

        return new CubeMap(faces);
    }

    /// <summary>
    /// The same as <see cref="Generate"/>, but keeping what <paramref name="shade"/> returns:
    /// the floats become the cube's radiance and a clipped sRGB encoding of them becomes its
    /// byte faces.
    ///
    /// This is the procedural counterpart to loading a panorama — the way to get a sky with a
    /// sun in it, where "a sun" means a small disc some thousands of times brighter than
    /// everything around it and not merely the whitest white available.
    /// </summary>
    public static CubeMap GenerateRadiance(int resolution, Func<Vector3, LinearColor> shade)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution);
        ArgumentNullException.ThrowIfNull(shade, nameof(shade));

        var faces = new Texture[6];
        var radiance = new float[6][];

        for (var f = 0; f < 6; f++)
        {
            var pixels = new int[resolution * resolution];
            var floats = new float[resolution * resolution * 3];

            for (var y = 0; y < resolution; y++)
            {
                var v = (y + 0.5f) / resolution;

                for (var x = 0; x < resolution; x++)
                {
                    var u = (x + 0.5f) / resolution;
                    var direction = Vector3.Normalize(Direction((CubeFace)f, u, v));

                    var light = shade(direction);
                    var texel = x + y * resolution;

                    floats[texel * 3] = light.R;
                    floats[texel * 3 + 1] = light.G;
                    floats[texel * 3 + 2] = light.B;

                    pixels[texel] = light.ToColorRGB().Color;
                }
            }

            faces[f] = new Texture(resolution, resolution, pixels);
            radiance[f] = floats;
        }

        return new CubeMap(faces, radiance);
    }
}
