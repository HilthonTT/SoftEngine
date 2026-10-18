using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Textures;
using SoftEngine.WinForms.Rendering;

namespace SoftEngine.WinForms.Tests.Rendering;

public class PainterNamesTests
{
    public static TheoryData<PainterKind> Kinds() => [.. Enum.GetValues<PainterKind>()];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void CreatedPainter_IsNamedAndParsedBackAsItsKind(PainterKind kind)
    {
        var painter = PainterNames.Create(kind);

        Assert.Equal(kind, PainterNames.KindOf(painter));
        Assert.Equal(kind, PainterNames.Parse(PainterNames.NameOf(painter)));
    }

    [Theory]
    [InlineData("PBR", PainterKind.Pbr)]
    [InlineData("PhysicallyBased", PainterKind.Pbr)]
    [InlineData("phong", PainterKind.Phong)]
    [InlineData("None", PainterKind.None)]
    public void Parse_IgnoresCaseAndAcceptsTheLongPbrName(string name, PainterKind expected)
    {
        Assert.Equal(expected, PainterNames.Parse(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("raytraced")]
    public void Parse_FallsBackToGouraud(string? name)
    {
        Assert.Equal(PainterKind.Gouraud, PainterNames.Parse(name));
    }

    [Fact]
    public void NameOf_NoPainterIsNone()
    {
        Assert.Equal("None", PainterNames.NameOf(null));
    }

    [Fact]
    public void Create_GivesEachTexturedKindItsOwnPainter()
    {
        Assert.IsType<TexturedPainter>(PainterNames.Create(PainterKind.Textured));
        Assert.IsType<MaterialPainter>(PainterNames.Create(PainterKind.Material));
        Assert.IsType<PbrPainter>(PainterNames.Create(PainterKind.Pbr));
    }

    [Theory]
    [InlineData(true, true, TextureFiltering.Trilinear)]
    [InlineData(true, false, TextureFiltering.Bilinear)]
    [InlineData(false, true, TextureFiltering.Nearest)]
    [InlineData(false, false, TextureFiltering.Nearest)]
    public void Filtering_OnlyBlendsLevelsWhenFilteringIsOn(bool filtering, bool trilinear, TextureFiltering expected)
    {
        Assert.Equal(expected, PainterNames.Filtering(filtering, trilinear));
    }
}
