using System.Windows.Forms;

namespace AsrUi;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppPaths.EnsureDirectories();
        Application.Run(new MainForm());
    }
}
