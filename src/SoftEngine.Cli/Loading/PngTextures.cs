using SoftEngine.Core.Imaging;
using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Loading;

internal sealed class PngTextures
{
    private readonly Dictionary<string, Texture?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public int Skipped { get; private set; }

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

    public Texture? FromBytes(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.IsEmpty)
        {
            return null;
        }

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
            Skipped++;
            return null;
        }
    }
}
