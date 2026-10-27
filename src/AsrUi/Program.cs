using System.Windows.Forms;

namespace AsrUi;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        AppPaths.ConfigureModelDirectory(settings.ModelDirectory);
        AppPaths.EnsureDirectories();
        Application.Run(new MainForm(settings));
    }
}
