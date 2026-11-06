using System.IO;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;

namespace PhoneBridge.Host;

/// <summary>
/// Entry point. Flags: --smoke (init everything, exit 0 if the server and UI came up, 1 otherwise),
/// --smoke-wait N (stay up N seconds for scripted end-to-end tests, writes dev-pairing.json), --hidden (start in tray).
/// Env: PHONEBRIDGE_DATA (data dir, default %LOCALAPPDATA%\PhoneBridge), PHONEBRIDGE_UI_URL (dev server URL).
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var customData = Environment.GetEnvironmentVariable("PHONEBRIDGE_DATA");
        var dataDir = customData ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneBridge");
        Directory.CreateDirectory(dataDir);
        var log = new FileLogger(Path.Combine(dataDir, "logs", "host.log"));

        var smoke = args.Contains("--smoke");
        var wait = 0;
        var waitIndex = Array.IndexOf(args, "--smoke-wait");
        if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out var w)) (smoke, wait) = (true, w);

        using var mutex = new Mutex(true, "PhoneBridge.Host." + Core.Tokens.Hash(dataDir)[..12], out var first);
        if (!first)
        {
            log.Write("Another instance is running for this data dir: exiting.");
            return smoke ? 1 : 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new HostController(dataDir, log, customData is not null);
        var exitCode = 0;

        app.Startup += async (_, _) =>
        {
            try
            {
                var ok = await controller.StartAsync(showWindow: !smoke && !args.Contains("--hidden"));
                if (!smoke) return;
                log.Write($"SMOKE {(ok ? "OK" : "FAILED")}: https={controller.Server.HttpsPort} http={controller.Server.HttpPort} cert={controller.Server.CertSha256}");
                if (wait > 0)
                {
                    log.Write($"SMOKE pairing file: {controller.WriteDevPairingFile()}");
                    await Task.Delay(TimeSpan.FromSeconds(wait));
                }
                exitCode = ok ? 0 : 1;
                Shutdown(app, controller);
            }
            catch (Exception e)
            {
                log.Write($"[Error] Startup failed: {e.GetType().Name}: {e.Message}");
                exitCode = 1;
                if (!smoke)
                    MessageBox.Show($"PhoneBridge n'a pas pu démarrer :\n{e.Message}", "PhoneBridge", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(app, controller);
            }
        };
        app.Exit += (_, _) => controller.Dispose();
        app.Run();
        if (smoke) File.Delete(Path.Combine(dataDir, "dev-pairing.json"));
        return exitCode;
    }

    static void Shutdown(Application app, HostController controller) => controller.Quit();
}
