using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Vivarium
{
    internal sealed class SettingsWindow : Form
    {
        private readonly AppController _app;
        private WebView2 _web;
        private bool _ready;
        private bool _allowClose;

        public SettingsWindow(AppController app)
        {
            _app = app;
            Text = "Vivarium";
            Width = 1220;
            Height = 830;
            MinimumSize = new Size(980, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 14, 15);
            Icon = AppIcon.Shared;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Interop.SetDarkTitleBar(Handle);
        }

        public async void Initialize()
        {
            try
            {
                _web = new WebView2();
                _web.Dock = DockStyle.Fill;
                _web.DefaultBackgroundColor = Color.FromArgb(10, 14, 15);
                _web.AllowExternalDrop = false;
                _web.TabStop = false;
                Controls.Add(_web);

                await _web.EnsureCoreWebView2Async(_app.Env);
                if (IsDisposed) return;

                CoreWebView2 core = _web.CoreWebView2;
                _app.ConfigureWebView(core, true);
                core.WebMessageReceived += OnWebMessage;
                core.NewWindowRequested += OnNewWindow;
                core.NavigationStarting += OnNavigationStarting;
                core.Navigate("https://vivarium.local/app/settings.html");
                _ready = true;
            }
            catch (Exception ex)
            {
                Log.E("settings webview", ex);
            }
        }

        public void ShowSettings()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            BringToFront();
            SendState();
        }

        public void CloseForReal()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Uri)) return;
            if (e.Uri.StartsWith("https://vivarium.local/", StringComparison.OrdinalIgnoreCase)) return;
            if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        }

        private void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        }

        private static void OpenExternal(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return;
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.W("open external: " + ex.Message);
            }
        }

        /* ---------------------------------------------------------------- */
        /* Messaging                                                         */
        /* ---------------------------------------------------------------- */

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                Dictionary<string, object> msg = Json.ParseObject(e.WebMessageAsJson);
                if (msg == null) return;
                string type = Json.GetString(msg, "t", "");

                if (type == "hello")
                {
                    SendState();
                }
                else if (type == "select")
                {
                    string id = Json.GetString(msg, "id", "");
                    if (id.Length > 0) _app.ApplyWallpaperById(id);
                }
                else if (type == "params")
                {
                    string id = Json.GetString(msg, "id", "");
                    object raw;
                    Dictionary<string, object> parameters = msg.TryGetValue("params", out raw) ? Json.AsObject(raw) : null;
                    _app.SetParams(id, parameters);
                }
                else if (type == "resetParams")
                {
                    _app.ResetParams(Json.GetString(msg, "id", ""));
                }
                else if (type == "system")
                {
                    object raw;
                    Dictionary<string, object> system = msg.TryGetValue("system", out raw) ? Json.AsObject(raw) : null;
                    _app.ApplySystem(system);
                }
                else if (type == "startup")
                {
                    _app.SetStartup(Json.GetBool(msg, "enabled", false));
                }
                else if (type == "pickMedia")
                {
                    _app.PickMedia(Json.GetString(msg, "kind", "video"));
                }
                else if (type == "clearMedia")
                {
                    _app.ClearMedia(Json.GetString(msg, "kind", "video"));
                }
                else if (type == "next")
                {
                    _app.NextWallpaper();
                }
                else if (type == "pause")
                {
                    _app.SetPaused(true);
                }
                else if (type == "resume")
                {
                    _app.SetPaused(false);
                }
                else if (type == "reload")
                {
                    if (_app.Service != null) _app.Service.ReloadAll();
                }
                else if (type == "openData")
                {
                    _app.OpenDataFolder();
                }
                else if (type == "openLog")
                {
                    _app.OpenLog();
                }
                else if (type == "quit")
                {
                    Program.Quit();
                }
            }
            catch (Exception ex)
            {
                Log.W("settings message: " + ex.Message);
            }
        }

        private void Send(Dictionary<string, object> message)
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null) return;
            try
            {
                _web.CoreWebView2.PostWebMessageAsJson(Json.Serialize(message));
            }
            catch (Exception ex)
            {
                Log.W("settings post: " + ex.Message);
            }
        }

        public void SendState()
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null) return;
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["t"] = "state";
            state["version"] = _app.Version;
            state["wallpaper"] = _app.CurrentWallpaperId;
            state["themes"] = _app.ThemeList();
            state["themeParams"] = _app.Store.Settings.themeParams;
            state["system"] = _app.SystemState();
            state["media"] = _app.MediaState();
            state["monitors"] = _app.MonitorList();
            state["paused"] = _app.Service != null && _app.Service.UserPaused;
            state["dataDir"] = _app.DataDir;
            Send(state);
        }

        public void SendToast(string message, string kind)
        {
            Dictionary<string, object> toast = new Dictionary<string, object>();
            toast["t"] = "toast";
            toast["message"] = message;
            toast["kind"] = kind;
            Send(toast);
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
