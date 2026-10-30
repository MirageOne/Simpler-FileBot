using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace SimplerFileBot.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        try { InitializeComponent(); }
        catch (Exception error) { ReportStartupFailure(error); throw; }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception error) { ReportStartupFailure(error); throw; }
    }

    private static void ReportStartupFailure(Exception error)
    {
        var message = "Simpler FileBot could not start.";
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Simpler FileBot");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup-error.log");
            File.WriteAllText(path, error.ToString());
            message += $" Details were saved to {path}.";
        }
        catch { message += $" {error.Message}"; }
        if (Environment.GetEnvironmentVariable("CI") != "true")
            MessageBoxW(0, message, "Simpler FileBot", 0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint window, string text, string caption, uint type);
}
