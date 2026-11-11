using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Vivarium
{
    internal static class Program
    {
        public static bool DevTools;
        public static bool OpenSettingsOnStart;

        private static AppController _controller;
        private static BridgeWindow _bridge;
        private static Mutex _mutex;
        private static bool _quitting;

        [STAThread]
        private static void Main(string[] args)
        {
            Interop.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            bool autostart = Has(args, "--autostart");
            bool exitRequested = Has(args, "--exit");
            bool settings = Has(args, "--settings");
            DevTools = Has(args, "--devtools");

            bool created;
            _mutex = new Mutex(true, "Local\\Vivarium.SingleInstance", out created);
            if (!created)
            {
                IntPtr hwnd = Native.FindWindow(null, "Vivarium.Bridge");
                if (hwnd != IntPtr.Zero)
                {
                    Interop.PostToBridge(hwnd, exitRequested ? Interop.WM_VIVARIUM_EXIT : Interop.WM_VIVARIUM_SETTINGS);
                }
                _mutex.Dispose();
                return;
            }

            if (exitRequested)
            {
                _mutex.Dispose();
                return;
            }

            string root = FindAppRoot();
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vivarium");
            Log.Init(data);

            try
            {
                string browser = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
                Log.I("WebView2 runtime " + browser);
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "Vivarium needs the Microsoft Edge WebView2 Runtime, which is not installed.\n\n" +
                    "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start Vivarium again.",
                    "Vivarium", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _mutex.Dispose();
                return;
            }

            _controller = new AppController(root, data);
            _bridge = new BridgeWindow();
            _bridge.OpenSettingsRequested += delegate { if (_controller != null) _controller.OpenSettings(); };
            _bridge.ExitRequested += delegate { Quit(); };
            _bridge.NextRequested += delegate { if (_controller != null) _controller.NextWallpaper(); };

            bool firstRun = _controller.FirstRun;
            string requested = ArgValue(args, "--wallpaper");
            if (!string.IsNullOrEmpty(requested)) _controller.Store.Settings.wallpaper = requested;

            OpenSettingsOnStart = settings || (!autostart && firstRun);
            _controller.Start();

            if (OpenSettingsOnStart)
            {
                System.Windows.Forms.Timer starter = new System.Windows.Forms.Timer();
                starter.Interval = 250;
                starter.Tick += delegate
                {
                    if (_quitting) { starter.Stop(); starter.Dispose(); return; }
                    if (_controller == null || _controller.Env == null) return;
                    starter.Stop();
                    starter.Dispose();
                    _controller.OpenSettings();
                };
                starter.Start();
            }

            if (!_quitting) Application.Run(new ApplicationContext());

            if (_bridge != null)
            {
                _bridge.Dispose();
                _bridge = null;
            }
            if (_controller != null)
            {
                _controller.Dispose();
                _controller = null;
            }
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            _mutex.Dispose();
        }

        public static void Quit()
        {
            _quitting = true;
            if (_controller != null)
            {
                AppController controller = _controller;
                _controller = null;
                controller.Dispose();
            }
            Application.Exit();
        }

        private static string FindAppRoot()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string dir = exeDir;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, "wallpapers"))) return dir;
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return exeDir;
        }

        private static bool Has(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string ArgValue(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.E("ui thread exception", e.Exception);
            if (_quitting) return;
            MessageBox.Show(
                "Vivarium hit an unexpected error.\n\n" + (e.Exception != null ? e.Exception.Message : "Unknown error") +
                "\n\nThe wallpaper keeps running. Details are in the log.",
                "Vivarium", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            Log.E("unhandled exception", ex);
        }
    }
}
