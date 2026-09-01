namespace SoftEngine.WinForms.Demos;

internal sealed record DemoDefinition(string Id, string Display, Func<IProgress<float>?, WorldSetup> Build);
