using SoftEngine.Core.Imaging;
using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Loading;

/// <summary>
/// The texture decoder this front-end supplies to the importers.
///
/// <para>
/// The Core deliberately does not decode images for import: a texture arrives in whatever format
/// an artist saved it in, and supporting that is the host's problem rather than the renderer's.
/// The WinForms viewer answers it with GDI+, which decodes everything and runs on Windows only.
/// This one answers it with the engine's own PNG codec, which decodes <em>one</em> format and
/// runs anywhere.
/// </para>
///
/// <para>
/// That is a real limitation and it is reported rather than hidden. A JPEG texture comes back as
/// null, which the importers already handle — the mesh keeps its UVs and its material factors and
/// loses the map — so a model with JPEG textures renders as untextured geometry instead of
/// failing to load. <see cref="Skipped"/> counts those so the program can say so.
/// </para>
/// </summary>
internal sealed class PngTextures
{
    private readonly Dictionary<string, Texture?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many images could not be decoded, so the caller can mention it once at the end.</summary>
    public int Skipped { get; private set; }

    /// <summary>Decodes an image beside a model, as the OBJ importer asks.</summary>
    public Texture? FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var texture = Decode(() => PngCodec.Load(path));

        _cache[path] = texture;

        return texture;
    }

    /// <summary>
    /// Decodes image bytes, as the glTF importer asks — its images can be a file beside the
    /// model, a <c>data:</c> URI or a stretch of the GLB's binary chunk, and only the first of
    /// those is a path anything could open.
    /// </summary>
    public Texture? FromBytes(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.IsEmpty)
        {
            return null;
        }

        // PngCodec reads from a path, so the bytes go through a temporary file rather than
        // growing a second entry point into the decoder. Textures are decoded once at load and
        // there are a handful of them, so the write costs nothing that matters here.
        var path = Path.Combine(Path.GetTempPath(), $"softengine-{Guid.NewGuid():N}.png");

        try
        {
            File.WriteAllBytes(path, encoded.Span);

            return Decode(() => PngCodec.Load(path));
        }
        catch (IOException)
        {
            Skipped++;
            return null;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a render over.
            }
        }
    }

    private Texture? Decode(Func<(int[] Pixels, int Width, int Height)> load)
    {
        try
        {
            var (pixels, width, height) = load();

            return new Texture(width, height, pixels);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            // Anything that is not a PNG this codec understands. The importers treat a null as
            // "no map", which is the degradation that keeps a model loadable.
            Skipped++;
            return null;
        }
    }
}
