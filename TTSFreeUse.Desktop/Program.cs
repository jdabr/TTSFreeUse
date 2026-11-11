using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using TTSFreeUse.Api;
using TTSFreeUse.Core.Jobs;

namespace TTSFreeUse.Desktop
{
    /// <summary>
    /// The installed Windows app. Starts TTSFreeUse in the background (no
    /// console window), opens it in the default browser, and shows a tray icon
    /// to reopen the page, open the audio folder, or quit.
    /// </summary>
    internal static class Program
    {
        // Also used by the installer to ask the user to close the app first.
        private const string MutexName = "TTSFreeUse.Desktop";

        [STAThread]
        private static int Main(string[] args)
        {
            // Options for testing: --port 5199, --no-browser
            var port = ReadPort(args) ?? TTSFreeUseApp.DefaultPort;
            var openBrowser = !args.Contains("--no-browser");
            var url = $"http://localhost:{port}/";

            // Already running? Just bring the page up again.
            using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                OpenInBrowser(url);
                return 0;
            }

            ApplicationConfiguration.Initialize();

            WebApplication app;
            try
            {
                app = TTSFreeUseApp.Create(args, new WebApplicationOptions
                {
                    Args = new[] { $"--urls={url}" },
                    // Settings and the web page are installed next to the exe;
                    // the working directory of a shortcut can be anything.
                    ContentRootPath = AppContext.BaseDirectory,
                    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
                });
                app.StartAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                var portInUse = ex is AddressInUseException || ex.InnerException is AddressInUseException;
                var message = portInUse
                    ? $"TTSFreeUse couldn't start because port {port} is already in use by another program.\n\n" +
                      "If you're running a developer copy (dotnet run), close it and try again."
                    : $"TTSFreeUse couldn't start:\n\n{ex.Message}";
                MessageBox.Show(message, "TTSFreeUse", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            using var tray = CreateTrayIcon(app, url);
            if (openBrowser)
                OpenInBrowser(url);
            else
                tray.ShowBalloonTip(3000, "TTSFreeUse is running", "Click the tray icon to open it.", ToolTipIcon.Info);

            Application.Run();

            tray.Visible = false;
            // Give a conversion in progress a moment to clean up its temp files.
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                app.StopAsync(timeout.Token).GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return 0;
        }

        private static NotifyIcon CreateTrayIcon(WebApplication app, string url)
        {
            var menu = new ContextMenuStrip();
            var open = menu.Items.Add("Open TTSFreeUse", null, (_, _) => OpenInBrowser(url));
            open.Font = new Font(open.Font, FontStyle.Bold);
            menu.Items.Add("Open audio folder", null, (_, _) =>
            {
                var folder = ((AudioLibrary)app.Services.GetService(typeof(AudioLibrary))!).Directory;
                Directory.CreateDirectory(folder);
                // Full path, so an "explorer.exe" in the working folder is never started instead.
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo(explorer, $"\"{folder}\"") { UseShellExecute = true });
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit TTSFreeUse", null, (_, _) => Application.Exit());

            var tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "TTSFreeUse",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    OpenInBrowser(url);
            };
            return tray;
        }

        private static Icon LoadIcon()
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("app.ico");
            return stream != null
                ? new Icon(stream, SystemInformation.SmallIconSize)
                : SystemIcons.Application;
        }

        private static void OpenInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show($"Open {url} in your web browser to use TTSFreeUse.", "TTSFreeUse");
            }
        }

        private static int? ReadPort(string[] args)
        {
            var i = Array.IndexOf(args, "--port");
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var port) && port is > 0 and < 65536
                ? port
                : null;
        }
    }
}
