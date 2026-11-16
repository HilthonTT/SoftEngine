namespace SoftEngine.WinForms;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001

        var arguments = ViewerArguments.Parse(args);

        if (arguments.Errors.Count > 0)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, arguments.Errors) + Environment.NewLine + Environment.NewLine +
                "SoftEngine.WinForms [file] [--workspace name] [--select n] [--time s] [--viewport WxH] [--screenshot out.png]",
                "SoftEngine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        Application.Run(new MainScreen(arguments));
        return Environment.ExitCode;
    }
}
