using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Vivarium
{
    /*
     * Owns the per-monitor wallpaper windows and keeps them in sync with the
     * desktop: WorkerW attachment, icon layout, fullscreen pause, monitor changes
     * and Explorer restarts.
     */
    internal sealed class WallpaperService : IDisposable
    {
        private readonly AppController _app;
        private readonly List<WallpaperWindow> _windows = new List<WallpaperWindow>();
        private readonly Dictionary<int, bool> _fullscreen = new Dictionary<int, bool>();
        private readonly Timer _timer;
        private List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private List<IconInfo> _icons = new List<IconInfo>();
        private IntPtr _workerW = IntPtr.Zero;
        private string _monitorSignature = "";
        private string _lastIconSignature = "";
        private int _tick;
        private bool _userPaused;
        private bool _disposed;

        public WallpaperService(AppController app)
        {
            _app = app;
            _timer = new Timer();
            _timer.Interval = 500;
            _timer.Tick += OnTick;
        }

        public bool UserPaused
        {
            get { return _userPaused; }
        }

        public List<MonitorInfo> Monitors
        {
            get { return _monitors; }
        }

        public void Start()
        {
            _monitors = Interop.GetMonitors();
            _monitorSignature = SignatureOf(_monitors);
            _workerW = FindWallpaperParent();
            RebuildWindows();
            _timer.Start();
            Log.I("wallpaper service started with " + _monitors.Count + " monitor(s)");
        }

        /* ---------------------------------------------------------------- */
        /* Window lifecycle                                                  */
        /* ---------------------------------------------------------------- */

        private List<MonitorInfo> SelectedMonitors()
        {
            List<MonitorInfo> result = new List<MonitorInfo>();
            string choice = _app.Store.Settings.system.monitors;
            if (string.IsNullOrEmpty(choice) || choice == "all")
            {
                result.AddRange(_monitors);
                return result;
            }
            int index;
            if (int.TryParse(choice, out index))
            {
                for (int i = 0; i < _monitors.Count; i++)
                {
                    if (_monitors[i].Index == index) result.Add(_monitors[i]);
                }
            }
            if (result.Count == 0) result.AddRange(_monitors);
            return result;
        }

        public void RebuildWindows()
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                try { _windows[i].Dispose(); }
                catch { }
            }
            _windows.Clear();
            _fullscreen.Clear();
            _lastIconSignature = "";

            List<MonitorInfo> selected = SelectedMonitors();
            for (int i = 0; i < selected.Count; i++)
            {
                MonitorInfo monitor = selected[i];
                WallpaperWindow window = new WallpaperWindow(_app, monitor);
                window.EnsureHandle();
                window.AttachTo(_workerW);
                try { window.Show(); }
                catch (Exception ex) { Log.E("show wallpaper window", ex); }
                _windows.Add(window);
                window.Initialize(_app.Env);
            }
        }

        public WallpaperWindow WindowFor(int monitorIndex)
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                if (_windows[i].Monitor.Index == monitorIndex) return _windows[i];
            }
            return null;
        }

        private void AttachAll()
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                try
                {
                    _windows[i].AttachTo(_workerW);
                    _windows[i].PositionIn(_workerW);
                }
                catch (Exception ex)
                {
                    Log.E("re-attach window", ex);
                }
            }
        }

        /* ---------------------------------------------------------------- */
        /* WorkerW discovery                                                 */
        /* ---------------------------------------------------------------- */

        public static IntPtr FindWallpaperParent()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr result;
                try
                {
                    /* Ask Explorer to spawn the WorkerW that lives behind the icons. */
                    Native.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, Native.SMTO_NORMAL, 1200, out result);
                    Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), Native.SMTO_NORMAL, 1200, out result);
                }
                catch (Exception ex)
                {
                    Log.W("workerw handshake: " + ex.Message);
                }
            }

            IntPtr worker = IntPtr.Zero;
            try
            {
                Native.EnumWindows(delegate(IntPtr hwnd, IntPtr lparam)
                {
                    IntPtr defView = Native.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero)
                    {
                        IntPtr sibling = Native.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                        if (sibling != IntPtr.Zero)
                        {
                            worker = sibling;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.W("workerw enum: " + ex.Message);
            }

            if (worker == IntPtr.Zero) worker = progman;
            if (worker == IntPtr.Zero) Log.W("no WorkerW or Progman found");
            return worker;
        }

        /* ---------------------------------------------------------------- */
        /* Timer work                                                        */
        /* ---------------------------------------------------------------- */

        private void OnTick(object sender, EventArgs e)
        {
            if (_disposed) return;
            _tick++;

            if (_tick % 2 == 0) RefreshFullscreen();
            if (_tick % 6 == 0) Reconcile();

            bool active = !_userPaused;
            if (active)
            {
                bool anyVisible = false;
                for (int i = 0; i < _windows.Count; i++)
                {
                    bool fs;
                    if (!_fullscreen.TryGetValue(_windows[i].Monitor.Index, out fs) || !fs)
                    {
                        anyVisible = true;
                        break;
                    }
                }
                active = anyVisible;
            }

            if (active) ReadIconsAndBroadcast();
        }

        private void Reconcile()
        {
            try
            {
                List<MonitorInfo> now = Interop.GetMonitors();
                string signature = SignatureOf(now);
                if (signature != _monitorSignature)
                {
                    Log.I("monitor layout changed");
                    _monitors = now;
                    _monitorSignature = signature;
                    _workerW = FindWallpaperParent();
                    RebuildWindows();
                    return;
                }

                bool needAttach = _workerW == IntPtr.Zero || !Native.IsWindow(_workerW);
                if (!needAttach)
                {
                    for (int i = 0; i < _windows.Count; i++)
                    {
                        if (Native.GetParent(_windows[i].Handle) != _workerW)
                        {
                            needAttach = true;
                            break;
                        }
                    }
                }

                if (needAttach)
                {
                    Log.I("desktop changed, re-attaching wallpaper");
                    _workerW = FindWallpaperParent();
                    if (_workerW != IntPtr.Zero) AttachAll();
                }
            }
            catch (Exception ex)
            {
                Log.W("reconcile: " + ex.Message);
            }
        }

        private void RefreshFullscreen()
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                WallpaperWindow window = _windows[i];
                bool fs = IsFullscreen(window.Monitor);
                _fullscreen[window.Monitor.Index] = fs;
                window.SetPaused(_userPaused || fs);
            }
        }

        private bool IsFullscreen(MonitorInfo monitor)
        {
            try
            {
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                for (int i = 0; i < _windows.Count; i++)
                {
                    if (_windows[i].Handle == foreground) return false;
                }
                if (!Native.IsWindowVisible(foreground) || Native.IsIconic(foreground)) return false;
                if (Native.IsCloaked(foreground)) return false;

                string cls = Native.ClassNameOf(foreground);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                    cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow") return false;

                Native.RECT rect;
                if (!Native.GetWindowRect(foreground, out rect)) return false;
                IntPtr owner = Native.MonitorFromWindow(foreground, Native.MONITOR_DEFAULTTONEAREST);
                if (owner != monitor.Handle) return false;

                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w <= 0 || h <= 0) return false;
                return w >= monitor.W - 4 && h >= monitor.H - 4 &&
                       rect.Left <= monitor.X + 4 && rect.Top <= monitor.Y + 4;
            }
            catch
            {
                return false;
            }
        }

        /* ---------------------------------------------------------------- */
        /* Icons                                                             */
        /* ---------------------------------------------------------------- */

        private void ReadIconsAndBroadcast()
        {
            List<IconInfo> icons = IconLayoutReader.Read();
            string signature = SignatureOfIcons(icons);
            _icons = icons;
            if (signature == _lastIconSignature) return;
            _lastIconSignature = signature;
            for (int i = 0; i < _windows.Count; i++) SendIcons(_windows[i]);
        }

        public List<Dictionary<string, object>> IconsFor(MonitorInfo monitor)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            for (int i = 0; i < _icons.Count; i++)
            {
                IconInfo icon = _icons[i];
                int cx = icon.X + icon.W / 2;
                int cy = icon.Y + icon.H / 2;
                if (cx < monitor.X - 24 || cx > monitor.X + monitor.W + 24) continue;
                if (cy < monitor.Y - 24 || cy > monitor.Y + monitor.H + 24) continue;

                Dictionary<string, object> item = new Dictionary<string, object>();
                item["id"] = icon.Id;
                item["seed"] = icon.Seed;
                item["x"] = icon.X - monitor.X;
                item["y"] = icon.Y - monitor.Y;
                item["w"] = icon.W;
                item["h"] = icon.H;
                list.Add(item);
            }
            return list;
        }

        public void SendIcons(WallpaperWindow window)
        {
            if (window == null) return;
            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["t"] = "icons";
            msg["icons"] = IconsFor(window.Monitor);
            window.PostJson(Json.Serialize(msg));
        }

        private static string SignatureOfIcons(List<IconInfo> icons)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < icons.Count; i++)
            {
                IconInfo icon = icons[i];
                sb.Append(icon.Id).Append(':').Append(icon.X).Append(',').Append(icon.Y)
                  .Append(',').Append(icon.W).Append(',').Append(icon.H).Append(';');
            }
            return sb.ToString();
        }

        /* ---------------------------------------------------------------- */
        /* Commands from the app                                             */
        /* ---------------------------------------------------------------- */

        public void ApplyWallpaper(string id)
        {
            string url = _app.CurrentUrl();
            _lastIconSignature = "";
            for (int i = 0; i < _windows.Count; i++) _windows[i].Navigate(url);
            RefreshFullscreen();
            ReadIconsAndBroadcast();
        }

        public void ApplyParams(string id, Dictionary<string, object> parameters)
        {
            if (id != _app.CurrentWallpaperId) return;
            for (int i = 0; i < _windows.Count; i++) _windows[i].ApplyParams(parameters);
        }

        public void ApplySystem()
        {
            _monitors = Interop.GetMonitors();
            _monitorSignature = SignatureOf(_monitors);

            List<MonitorInfo> selected = SelectedMonitors();
            bool same = selected.Count == _windows.Count;
            if (same)
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    bool found = false;
                    for (int k = 0; k < _windows.Count; k++)
                    {
                        if (_windows[k].Monitor.Index == selected[i].Index) { found = true; break; }
                    }
                    if (!found) { same = false; break; }
                }
            }
            if (!same)
            {
                RebuildWindows();
                return;
            }
            _lastIconSignature = "";
            for (int i = 0; i < _windows.Count; i++) _windows[i].Reload();
        }

        public void ReloadAll()
        {
            _lastIconSignature = "";
            for (int i = 0; i < _windows.Count; i++) _windows[i].Reload();
        }

        public void RefreshMedia()
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                _windows[i].RefreshMappings();
                _windows[i].ApplyMedia();
            }
        }

        public void SetUserPaused(bool paused)
        {
            _userPaused = paused;
            RefreshFullscreen();
            if (!paused) ReadIconsAndBroadcast();
        }

        public void ToggleUserPause()
        {
            SetUserPaused(!_userPaused);
        }

        private static string SignatureOf(List<MonitorInfo> monitors)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < monitors.Count; i++) sb.Append(monitors[i].Signature()).Append('|');
            return sb.ToString();
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
            for (int i = 0; i < _windows.Count; i++)
            {
                try { _windows[i].Dispose(); }
                catch { }
            }
            _windows.Clear();
        }
    }
}
