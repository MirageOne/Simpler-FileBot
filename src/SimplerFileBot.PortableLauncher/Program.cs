using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace SimplerFileBot.PortableLauncher;

internal static class Program
{
    private const string PayloadName = "SimplerFileBot.Portable.Payload.zip";
    private const string PortableDataEnvironmentVariable = "SIMPLER_FILEBOT_PORTABLE_DATA_DIR";

    [STAThread]
    private static int Main()
    {
        string? runtimeDirectory = null;
        string? dataDirectory = null;
        try
        {
            var launcherPath = Environment.ProcessPath ?? throw new InvalidOperationException("Launcher path is unavailable.");
            var launcherDirectory = Path.GetDirectoryName(launcherPath) ?? throw new InvalidOperationException("Launcher directory is unavailable.");
            dataDirectory = Path.Combine(launcherDirectory, "data");
            Directory.CreateDirectory(dataDirectory);

            runtimeDirectory = Path.Combine(Path.GetTempPath(), "Simpler-FileBot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runtimeDirectory);
            using (var payload = typeof(Program).Assembly.GetManifestResourceStream(PayloadName)
                ?? throw new InvalidOperationException("Portable application payload is missing."))
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                archive.ExtractToDirectory(runtimeDirectory);

            var appPath = Path.Combine(runtimeDirectory, "Simpler-FileBot.exe");
            if (!File.Exists(appPath)) throw new FileNotFoundException("Portable application executable is missing.", appPath);
            var startInfo = new ProcessStartInfo(appPath)
            {
                WorkingDirectory = runtimeDirectory,
                UseShellExecute = false
            };
            startInfo.Environment[PortableDataEnvironmentVariable] = dataDirectory;
            using var app = Process.Start(startInfo) ?? throw new InvalidOperationException("Portable application did not start.");
            app.WaitForExit();
            return app.ExitCode;
        }
        catch (Exception error)
        {
            var detail = $"HRESULT: 0x{error.HResult:X8}{Environment.NewLine}{error}";
            var message = "Simpler FileBot could not start.";
            try
            {
                var directory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "Simpler-FileBot");
                Directory.CreateDirectory(directory);
                var logPath = Path.Combine(directory, "launcher-error.log");
                File.WriteAllText(logPath, detail);
                message += $" Details were saved to {logPath}.";
            }
            catch { message += $" {error.Message}"; }
            if (Environment.GetEnvironmentVariable("CI") != "true")
            {
                try { MessageBoxW(0, message, "Simpler FileBot", 0x10); }
                catch { /* Preserve the launch error. */ }
            }
            return 1;
        }
        finally
        {
            if (runtimeDirectory is not null)
            {
                for (var attempt = 0; attempt < 5 && Directory.Exists(runtimeDirectory); attempt++)
                {
                    try { Directory.Delete(runtimeDirectory, true); }
                    catch (IOException) { Thread.Sleep(200); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(200); }
                }
            }
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint window, string text, string caption, uint type);
}
