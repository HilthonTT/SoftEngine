using SoftEngine.WinForms;

namespace SoftEngine.WinForms.Tests;

public class ViewerArgumentsTests
{
    [Fact]
    public void NoArguments_AsksForNothing()
    {
        var args = ViewerArguments.Parse([]);

        Assert.Null(args.Path);
        Assert.Null(args.Workspace);
        Assert.Null(args.Select);
        Assert.Null(args.Viewport);
        Assert.Null(args.Screenshot);
        Assert.Empty(args.Errors);
    }

    [Fact]
    public void ABareFile_IsWhatToOpen()
    {
        var args = ViewerArguments.Parse([@"C:\models\teapot.obj"]);

        Assert.Equal(@"C:\models\teapot.obj", args.Path);
        Assert.Empty(args.Errors);
    }

    [Fact]
    public void EveryOption_IsRead()
    {
        var args = ViewerArguments.Parse(
            ["scene.json", "--workspace", "shading", "--select", "2", "--time", "1.5", "--viewport", "1280x720", "--screenshot", "out.png"]);

        Assert.Equal("scene.json", args.Path);
        Assert.Equal("Shading", args.Workspace);
        Assert.Equal(2, args.Select);
        Assert.Equal(1.5f, args.Time);
        Assert.Equal(new Size(1280, 720), args.Viewport);
        Assert.Equal("out.png", args.Screenshot);
        Assert.Empty(args.Errors);
    }

    [Theory]
    [InlineData("--workspace", "Sculpting")]
    [InlineData("--select", "-1")]
    [InlineData("--select", "two")]
    [InlineData("--time", "-1")]
    [InlineData("--time", "soon")]
    [InlineData("--viewport", "1280")]
    [InlineData("--viewport", "0x720")]
    [InlineData("--viewport", "99999x720")]
    public void ABadValue_IsReportedRatherThanGuessed(string option, string value)
    {
        var args = ViewerArguments.Parse([option, value]);

        Assert.Single(args.Errors);
    }

    [Fact]
    public void AnOptionWithNoValue_IsReported()
    {
        var args = ViewerArguments.Parse(["--screenshot"]);

        Assert.Null(args.Screenshot);
        Assert.Single(args.Errors);
    }

    [Fact]
    public void AnUnknownOption_IsReported()
    {
        var args = ViewerArguments.Parse(["--fullscreen"]);

        Assert.Contains("--fullscreen", args.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondFile_IsReported()
    {
        var args = ViewerArguments.Parse(["a.obj", "b.obj"]);

        Assert.Equal("a.obj", args.Path);
        Assert.Single(args.Errors);
    }
}
