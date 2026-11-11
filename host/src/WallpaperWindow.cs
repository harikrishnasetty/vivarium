using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Vivarium
{
    /*
     * One wallpaper window per monitor. The window is reparented into Explorer's
     * WorkerW container so it renders behind the desktop icons, then hosts a
     * WebView2 that runs the scene.
     */
    internal sealed class WallpaperWindow : Form
    {
        private readonly AppController _app;
        private WebView2 _web;
        private bool _webReady;
        private bool _paused;
        private bool _helloReceived;
        private int _navGeneration;
        private string _url;

        public MonitorInfo Monitor;
        public bool Attached;

        public WallpaperWindow(AppController app, MonitorInfo monitor)
        {
            _app = app;
            Monitor = monitor;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(monitor.X, monitor.Y, monitor.W, monitor.H);
            BackColor = Color.Black;
            Text = "Vivarium Wallpaper " + monitor.Index;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void EnsureHandle()
        {
            IntPtr unused = Handle;
        }

        public void AttachTo(IntPtr parent)
        {
            if (parent == IntPtr.Zero) return;
            IntPtr h = Handle;
            Native.SetParent(h, parent);

            long style = Native.GetWindowLongPtr(h, Native.GWL_STYLE).ToInt64();
            style &= ~0x00C00000L; /* WS_CAPTION */
            style &= ~0x00040000L; /* WS_THICKFRAME */
            style &= ~0x00020000L; /* WS_MINIMIZEBOX */
            style &= ~0x00010000L; /* WS_MAXIMIZEBOX */
            style |= 0x40000000L;  /* WS_CHILD */
            style |= 0x10000000L;  /* WS_VISIBLE */
            style |= 0x04000000L;  /* WS_CLIPSIBLINGS */
            Native.SetWindowLongPtr(h, Native.GWL_STYLE, new IntPtr(style));

            long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW;
            ex |= Native.WS_EX_NOACTIVATE;
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));

            PositionIn(parent);
            Native.SetWindowPos(h, new IntPtr(Interop.HWND_BOTTOM), 0, 0, 0, 0,
                Interop.SWP_NOMOVE | Interop.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            Attached = true;
            Log.I("wallpaper window attached: monitor " + Monitor.Index + " -> " + parent.ToString("x"));
        }

        public void PositionIn(IntPtr parent)
        {
            if (Handle == IntPtr.Zero) return;
            Native.POINT p = new Native.POINT();
            p.X = Monitor.X;
            p.Y = Monitor.Y;
            Interop.MapPointToParent(ref p, parent);
            Native.SetWindowPos(Handle, new IntPtr(Interop.HWND_BOTTOM), p.X, p.Y, Monitor.W, Monitor.H,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public async void Initialize(CoreWebView2Environment env)
        {
            try
            {
                _web = new WebView2();
                _web.Dock = DockStyle.Fill;
                _web.DefaultBackgroundColor = Color.Black;
                _web.AllowExternalDrop = false;
                _web.TabStop = false;
                Controls.Add(_web);

                await _web.EnsureCoreWebView2Async(env);
                if (IsDisposed) return;

                CoreWebView2 core = _web.CoreWebView2;
                _app.ConfigureWebView(core, false);
                core.WebMessageReceived += OnWebMessage;
                core.NavigationCompleted += OnNavigationCompleted;
                _webReady = true;
                Navigate(_app.CurrentUrl());
            }
            catch (Exception ex)
            {
                Log.E("wallpaper webview init (monitor " + Monitor.Index + ")", ex);
            }
        }

        public void Navigate(string url)
        {
            _url = url;
            _helloReceived = false;
            if (!_webReady || _web == null || _web.CoreWebView2 == null) return;
            try
            {
                _web.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                Log.E("navigate " + url, ex);
            }
        }

        public void Reload()
        {
            if (!_webReady || _web == null || _web.CoreWebView2 == null) return;
            _helloReceived = false;
            try
            {
                _web.CoreWebView2.Reload();
            }
            catch (Exception ex)
            {
                Log.E("reload", ex);
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                Log.W("navigation failed on monitor " + Monitor.Index + ": " + e.WebErrorStatus);
                return;
            }
            int generation = ++_navGeneration;
            Watchdog(generation);
        }

        private async void Watchdog(int generation)
        {
            await Task.Delay(1500);
            if (IsDisposed) return;
            if (generation != _navGeneration) return;
            if (_helloReceived) return;
            Log.W("renderer did not say hello on monitor " + Monitor.Index + ", sending init anyway");
            PostJson(_app.BuildInit(this));
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(json)) return;
                System.Collections.Generic.Dictionary<string, object> msg = Json.ParseObject(json);
                if (msg == null) return;
                object type;
                if (!msg.TryGetValue("t", out type)) return;
                string kind = Convert.ToString(type);
                if (kind == "hello")
                {
                    _helloReceived = true;
                    PostJson(_app.BuildInit(this));
                }
                else if (kind == "log")
                {
                    object text;
                    if (msg.TryGetValue("message", out text)) Log.I("renderer[" + _app.CurrentWallpaperId + "]: " + Convert.ToString(text));
                }
                else if (kind == "error")
                {
                    object text;
                    if (msg.TryGetValue("message", out text)) Log.W("renderer[" + _app.CurrentWallpaperId + "]: " + Convert.ToString(text));
                }
            }
            catch (Exception ex)
            {
                Log.W("wallpaper message: " + ex.Message);
            }
        }

        public void PostJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (!_webReady || _web == null || _web.CoreWebView2 == null) return;
            try
            {
                _web.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Log.W("post to renderer: " + ex.Message);
            }
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            PostJson(paused ? "{\"t\":\"paused\"}" : "{\"t\":\"resumed\"}");
            if (!paused) _app.Service.SendIcons(this);
        }

        public void ApplyParams(System.Collections.Generic.Dictionary<string, object> parameters)
        {
            if (parameters == null) return;
            System.Collections.Generic.Dictionary<string, object> msg = new System.Collections.Generic.Dictionary<string, object>();
            msg["t"] = "params";
            msg["params"] = parameters;
            PostJson(Json.Serialize(msg));
        }

        public void ApplyMedia()
        {
            System.Collections.Generic.Dictionary<string, object> msg = new System.Collections.Generic.Dictionary<string, object>();
            msg["t"] = "media";
            msg["media"] = _app.MediaPayload();
            PostJson(Json.Serialize(msg));
        }

        public void RefreshMappings()
        {
            if (!_webReady || _web == null || _web.CoreWebView2 == null) return;
            _app.ConfigureWebView(_web.CoreWebView2, false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_web != null)
                {
                    try { _web.Dispose(); }
                    catch { }
                    _web = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
