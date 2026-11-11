using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Vivarium
{
    internal sealed class AppController : IDisposable
    {
        public readonly string AppRoot;
        public readonly string DataDir;
        public readonly SettingsStore Store;
        public readonly ThemeCatalog Catalog;
        public CoreWebView2Environment Env;
        public WallpaperService Service;
        public SettingsWindow SettingsUi;
        public TrayController Tray;

        private readonly Timer _saveTimer;
        private bool _disposed;

        public event Action StateChanged;

        private const string BrowserArguments =
            "--autoplay-policy=no-user-gesture-required " +
            "--disable-background-timer-throttling " +
            "--disable-backgrounding-occluded-windows " +
            "--disable-renderer-backgrounding " +
            "--disable-features=CalculateNativeWinOcclusion,IntensiveWakeUpThrottling";

        /* Opt-in diagnostics: set VIVARIUM_DEBUG_PORT=9222 to expose the
           WebView2 DevTools protocol for local inspection. Never enabled by
           default, and only bound to loopback. */
        private static string BrowserArgs()
        {
            string port = Environment.GetEnvironmentVariable("VIVARIUM_DEBUG_PORT");
            int value;
            if (!string.IsNullOrEmpty(port) && int.TryParse(port, out value) && value > 0 && value < 65536)
            {
                return BrowserArguments + " --remote-debugging-port=" + value + " --remote-allow-origins=*";
            }
            return BrowserArguments;
        }

        public AppController(string appRoot, string dataDir)
        {
            AppRoot = appRoot;
            DataDir = dataDir;
            Store = new SettingsStore(dataDir);
            Catalog = new ThemeCatalog(appRoot);
            _saveTimer = new Timer();
            _saveTimer.Interval = 600;
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                Store.Save();
            };
        }

        public string Version
        {
            get { return "1.0.0"; }
        }

        public bool FirstRun
        {
            get { return !Store.Existed; }
        }

        public string CurrentWallpaperId
        {
            get { return Store.Settings.wallpaper; }
        }

        public async void Start()
        {
            try
            {
                Catalog.Load();
                Store.Load();

                if (Catalog.Get(Store.Settings.wallpaper) == null)
                {
                    Store.Settings.wallpaper = Catalog.Themes.Count > 0 ? Catalog.Themes[0].id : "moss-vale";
                }

                string userData = Path.Combine(DataDir, "WebView2");
                Directory.CreateDirectory(userData);
                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(
                    BrowserArguments, "en-US", null, false, new List<CoreWebView2CustomSchemeRegistration>());
                Env = await CoreWebView2Environment.CreateAsync(null, userData, options);
                if (_disposed) return;

                Service = new WallpaperService(this);
                Service.Start();
                Tray = new TrayController(this);
                Log.I("Vivarium " + Version + " started from " + AppRoot);
            }
            catch (Exception ex)
            {
                Log.E("startup", ex);
                MessageBox.Show(
                    "Vivarium could not start.\n\n" + ex.Message +
                    "\n\nSee the log for details:\n" + Path.Combine(DataDir, "vivarium.log"),
                    "Vivarium", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Program.Quit();
            }
        }

        /* ---------------------------------------------------------------- */
        /* WebView2 configuration                                            */
        /* ---------------------------------------------------------------- */

        public void ConfigureWebView(CoreWebView2 core, bool settingsWindow)
        {
            if (core == null) return;
            try
            {
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = Program.DevTools;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = Program.DevTools;
                core.Settings.IsSwipeNavigationEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsBuiltInErrorPageEnabled = true;
                core.SetVirtualHostNameToFolderMapping("vivarium.local", AppRoot, CoreWebView2HostResourceAccessKind.Allow);
                MapMedia(core, "video", Store.Settings.videoPath);
                MapMedia(core, "image", Store.Settings.imagePath);
            }
            catch (Exception ex)
            {
                Log.W("configure webview: " + ex.Message);
            }
        }

        private void MapMedia(CoreWebView2 core, string kind, string path)
        {
            string host = "vivarium-" + kind + ".local";
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        core.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.Allow);
                        return;
                    }
                }
                core.ClearVirtualHostNameToFolderMapping(host);
            }
            catch (Exception ex)
            {
                Log.W("media mapping " + kind + ": " + ex.Message);
            }
        }

        public Dictionary<string, object> MediaPayload()
        {
            Dictionary<string, object> media = new Dictionary<string, object>();
            media["video"] = MediaUrl("video", Store.Settings.videoPath);
            media["image"] = MediaUrl("image", Store.Settings.imagePath);
            return media;
        }

        private static object MediaUrl(string kind, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return null;
            return "https://vivarium-" + kind + ".local/" + Uri.EscapeDataString(name);
        }

        public string CurrentUrl()
        {
            return "https://vivarium.local/wallpapers/" + Uri.EscapeDataString(Store.Settings.wallpaper) + "/index.html";
        }

        public string BuildInit(WallpaperWindow window)
        {
            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["t"] = "init";
            msg["params"] = Catalog.MergedParams(Store.Settings.wallpaper, Store.Settings);
            msg["fps"] = Store.Settings.system.fps;
            msg["reduced"] = Store.Settings.system.reducedMotion;
            msg["icons"] = Service != null ? Service.IconsFor(window.Monitor) : new List<Dictionary<string, object>>();
            msg["media"] = MediaPayload();

            Dictionary<string, object> monitor = new Dictionary<string, object>();
            monitor["index"] = window.Monitor.Index;
            monitor["x"] = window.Monitor.X;
            monitor["y"] = window.Monitor.Y;
            monitor["w"] = window.Monitor.W;
            monitor["h"] = window.Monitor.H;
            msg["monitor"] = monitor;
            return Json.Serialize(msg);
        }

        /* ---------------------------------------------------------------- */
        /* Commands                                                          */
        /* ---------------------------------------------------------------- */

        public void ApplyWallpaperById(string id)
        {
            if (Catalog.Get(id) == null) return;
            Store.Settings.wallpaper = id;
            Store.Save();
            if (Service != null) Service.ApplyWallpaper(id);
            NotifyState();
        }

        public void SetParams(string id, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrEmpty(id) || parameters == null) return;
            Dictionary<string, object> saved;
            if (!Store.Settings.themeParams.TryGetValue(id, out saved) || saved == null)
            {
                saved = new Dictionary<string, object>();
                Store.Settings.themeParams[id] = saved;
            }
            foreach (KeyValuePair<string, object> pair in parameters) saved[pair.Key] = pair.Value;
            ScheduleSave();
            if (Service != null) Service.ApplyParams(id, Catalog.MergedParams(id, Store.Settings));
        }

        public void ResetParams(string id)
        {
            if (Store.Settings.themeParams.ContainsKey(id)) Store.Settings.themeParams.Remove(id);
            Store.Save();
            if (Service != null) Service.ApplyParams(id, Catalog.MergedParams(id, Store.Settings));
            NotifyState();
        }

        public void ApplySystem(Dictionary<string, object> incoming)
        {
            if (incoming == null) return;
            SystemSettings system = Store.Settings.system;
            bool rebuild = false;

            object value;
            if (incoming.TryGetValue("pauseFullscreen", out value)) system.pauseFullscreen = Convert.ToBoolean(value);
            if (incoming.TryGetValue("reducedMotion", out value)) system.reducedMotion = Convert.ToBoolean(value);
            if (incoming.TryGetValue("fps", out value))
            {
                int fps = Convert.ToInt32(value);
                if (fps != 24 && fps != 30 && fps != 60) fps = 30;
                if (system.fps != fps) { system.fps = fps; rebuild = true; }
            }
            if (incoming.TryGetValue("monitors", out value))
            {
                string monitors = Convert.ToString(value);
                if (string.IsNullOrEmpty(monitors)) monitors = "all";
                if (system.monitors != monitors) { system.monitors = monitors; rebuild = true; }
            }

            Store.Save();
            if (Service != null)
            {
                if (rebuild) Service.ApplySystem();
                else Service.ReloadAll();
            }
            NotifyState();
        }

        public bool SetStartup(bool enabled)
        {
            try
            {
                StartupManager.SetEnabled(enabled);
                Store.Settings.system.startup = StartupManager.IsEnabled();
                Store.Save();
                NotifyState();
                return true;
            }
            catch (Exception ex)
            {
                Log.E("startup toggle", ex);
                Toast("Could not change the startup setting: " + ex.Message, "error");
                return false;
            }
        }

        public void PickMedia(string kind)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                if (kind == "video")
                {
                    dialog.Title = "Choose a video wallpaper";
                    dialog.Filter = "Video files|*.mp4;*.webm;*.m4v;*.mov;*.ogv|All files|*.*";
                }
                else
                {
                    dialog.Title = "Choose an image wallpaper";
                    dialog.Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.avif|All files|*.*";
                }
                dialog.CheckFileExists = true;
                IWin32Window owner = (SettingsUi != null && SettingsUi.Visible) ? SettingsUi : null;
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;

                if (kind == "video") Store.Settings.videoPath = dialog.FileName;
                else Store.Settings.imagePath = dialog.FileName;
                Store.Save();
                if (Service != null) Service.RefreshMedia();
                if (Catalog.Get(kind) != null) ApplyWallpaperById(kind);
                else NotifyState();
                Toast(kind == "video" ? "Video wallpaper ready" : "Image wallpaper ready", "ok");
            }
        }

        public void ClearMedia(string kind)
        {
            if (kind == "video") Store.Settings.videoPath = "";
            else Store.Settings.imagePath = "";
            Store.Save();
            if (Service != null) Service.RefreshMedia();
            NotifyState();
        }

        public void NextWallpaper()
        {
            if (Catalog.Themes.Count == 0) return;
            int index = Catalog.IndexOf(Store.Settings.wallpaper);
            index = (index + 1) % Catalog.Themes.Count;
            ApplyWallpaperById(Catalog.Themes[index].id);
        }

        public void TogglePause()
        {
            if (Service == null) return;
            Service.ToggleUserPause();
            NotifyState();
        }

        public void SetPaused(bool paused)
        {
            if (Service == null) return;
            Service.SetUserPaused(paused);
            NotifyState();
        }

        public void OpenSettings()
        {
            if (Env == null) return;
            try
            {
                if (SettingsUi == null || SettingsUi.IsDisposed)
                {
                    SettingsUi = new SettingsWindow(this);
                    SettingsUi.Initialize();
                }
                SettingsUi.ShowSettings();
            }
            catch (Exception ex)
            {
                Log.E("open settings", ex);
            }
        }

        public void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                Process.Start(new ProcessStartInfo(DataDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.W("open data folder: " + ex.Message);
            }
        }

        public void OpenLog()
        {
            try
            {
                string path = Path.Combine(DataDir, "vivarium.log");
                if (File.Exists(path)) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
                else OpenDataFolder();
            }
            catch (Exception ex)
            {
                Log.W("open log: " + ex.Message);
            }
        }

        public void Toast(string message, string kind)
        {
            if (SettingsUi != null && !SettingsUi.IsDisposed) SettingsUi.SendToast(message, kind);
        }

        /* ---------------------------------------------------------------- */
        /* State for the settings UI                                         */
        /* ---------------------------------------------------------------- */

        public List<Dictionary<string, object>> ThemeList()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            for (int i = 0; i < Catalog.Themes.Count; i++) list.Add(Catalog.Themes[i].raw);
            return list;
        }

        public Dictionary<string, object> SystemState()
        {
            SystemSettings system = Store.Settings.system;
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["startup"] = StartupManager.IsEnabled();
            state["pauseFullscreen"] = system.pauseFullscreen;
            state["fps"] = system.fps;
            state["monitors"] = system.monitors;
            state["reducedMotion"] = system.reducedMotion;
            return state;
        }

        public List<Dictionary<string, object>> MonitorList()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            List<MonitorInfo> monitors = Service != null ? Service.Monitors : Interop.GetMonitors();
            for (int i = 0; i < monitors.Count; i++)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["index"] = monitors[i].Index;
                item["primary"] = monitors[i].Primary;
                item["w"] = monitors[i].W;
                item["h"] = monitors[i].H;
                item["label"] = (monitors[i].Primary ? "Primary" : "Display " + (monitors[i].Index + 1)) +
                                "  ·  " + monitors[i].W + "×" + monitors[i].H;
                list.Add(item);
            }
            return list;
        }

        public Dictionary<string, object> MediaState()
        {
            Dictionary<string, object> media = MediaPayload();
            Dictionary<string, object> paths = new Dictionary<string, object>();
            paths["video"] = string.IsNullOrEmpty(Store.Settings.videoPath) ? "" : Store.Settings.videoPath;
            paths["image"] = string.IsNullOrEmpty(Store.Settings.imagePath) ? "" : Store.Settings.imagePath;
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["urls"] = media;
            state["paths"] = paths;
            return state;
        }

        public void NotifyState()
        {
            if (SettingsUi != null && !SettingsUi.IsDisposed) SettingsUi.SendState();
            if (Tray != null) Tray.Refresh();
            Action handler = StateChanged;
            if (handler != null) handler();
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _saveTimer.Stop();
            _saveTimer.Dispose();
            try { Store.Save(); }
            catch { }
            if (Service != null)
            {
                Service.Dispose();
                Service = null;
            }
            if (SettingsUi != null)
            {
                try { SettingsUi.CloseForReal(); }
                catch { }
                SettingsUi = null;
            }
            if (Tray != null)
            {
                Tray.Dispose();
                Tray = null;
            }
        }
    }
}
