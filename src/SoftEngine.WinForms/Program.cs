namespace SoftEngine.WinForms;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    public static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // SetColorMode is marked experimental
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        Application.Run(new MainScreen());
    }
}