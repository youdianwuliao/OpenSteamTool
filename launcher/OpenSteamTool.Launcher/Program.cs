namespace OpenSteamTool.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(Theme.Font(9f));
        Application.Run(new MainForm());
    }
}
