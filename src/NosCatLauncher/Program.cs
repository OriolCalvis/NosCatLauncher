namespace NosCatLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(Path.Combine(AppContext.BaseDirectory, "launcher.json")));
    }
}
