namespace SoftEngine.WinForms.Dialogs;

/// <summary>A built-in demo world, or a model file somewhere on the machine.</summary>
internal sealed record ModelChoice(string? DemoId, string? FilePath);
