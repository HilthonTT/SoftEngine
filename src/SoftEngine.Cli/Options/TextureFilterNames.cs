using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Options;

/// <summary>
/// The spellings of <see cref="TextureFiltering"/> a person may type.
///
/// Separate from the enum because the aliases are a property of this front-end and not of the
/// engine: <c>point</c> and <c>none</c> mean nearest to anyone who has used another renderer, and
/// accepting them costs nothing. Shared by the parser — which rejects a name it does not know
/// rather than falling back — and by <see cref="RenderOptions.ResolveFiltering"/>.
/// </summary>
internal static class TextureFilterNames
{
    public static bool TryParse(string name, out TextureFiltering filtering)
    {
        switch (name.ToLowerInvariant())
        {
            case "nearest" or "point" or "none":
                filtering = TextureFiltering.Nearest;
                return true;

            case "bilinear" or "linear":
                filtering = TextureFiltering.Bilinear;
                return true;

            case "trilinear":
                filtering = TextureFiltering.Trilinear;
                return true;

            default:
                filtering = TextureFiltering.Bilinear;
                return false;
        }
    }
}
