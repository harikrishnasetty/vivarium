using System;
using System.Drawing;
using System.Windows.Forms;

namespace Vivarium
{
    internal sealed class TrayController : IDisposable
    {
        private readonly AppController _app;
        private readonly NotifyIcon _notify;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _pauseItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _wallpaperItem;
        private bool _disposed;

        public TrayController(AppController app)
        {
            _app = app;

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.Font = new Font("Segoe UI", 9f);

            ToolStripMenuItem open = new ToolStripMenuItem("Open Vivarium");
            open.Font = new Font(_menu.Font, FontStyle.Bold);
            open.Click += delegate { _app.OpenSettings(); };
            _menu.Items.Add(open);

            _wallpaperItem = new ToolStripMenuItem("Wallpaper");
            _menu.Items.Add(_wallpaperItem);

            ToolStripMenuItem next = new ToolStripMenuItem("Next wallpaper");
            next.Click += delegate { _app.NextWallpaper(); };
            _menu.Items.Add(next);

            _menu.Items.Add(new ToolStripSeparator());

            _pauseItem = new ToolStripMenuItem("Pause wallpaper");
            _pauseItem.Click += delegate { _app.TogglePause(); };
            _menu.Items.Add(_pauseItem);

            _startupItem = new ToolStripMenuItem("Start with Windows");
            _startupItem.Click += delegate { _app.SetStartup(!StartupManager.IsEnabled()); };
            _menu.Items.Add(_startupItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem data = new ToolStripMenuItem("Open data folder");
            data.Click += delegate { _app.OpenDataFolder(); };
            _menu.Items.Add(data);

            ToolStripMenuItem log = new ToolStripMenuItem("View log");
            log.Click += delegate { _app.OpenLog(); };
            _menu.Items.Add(log);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit Vivarium");
            exit.Click += delegate { Program.Quit(); };
            _menu.Items.Add(exit);

            _notify = new NotifyIcon();
            _notify.Icon = AppIcon.Shared;
            _notify.Text = "Vivarium";
            _notify.Visible = true;
            _notify.ContextMenuStrip = _menu;
            _notify.DoubleClick += delegate { _app.OpenSettings(); };

            Refresh();
        }

        public void Refresh()
        {
            if (_disposed) return;

            bool paused = _app.Service != null && _app.Service.UserPaused;
            _pauseItem.Checked = paused;
            _pauseItem.Text = paused ? "Resume wallpaper" : "Pause wallpaper";
            _startupItem.Checked = StartupManager.IsEnabled();

            string name = _app.CurrentWallpaperId;
            ThemeManifest theme = _app.Catalog.Get(name);
            string label = theme != null ? theme.name : name;
            string text = "Vivarium — " + label;
            if (text.Length > 63) text = text.Substring(0, 63);
            _notify.Text = text;

            RebuildWallpaperMenu();
        }

        private void RebuildWallpaperMenu()
        {
            _wallpaperItem.DropDownItems.Clear();
            string lastCategory = null;
            string current = _app.CurrentWallpaperId;

            for (int i = 0; i < _app.Catalog.Themes.Count; i++)
            {
                ThemeManifest theme = _app.Catalog.Themes[i];
                if (theme.category != lastCategory)
                {
                    if (lastCategory != null) _wallpaperItem.DropDownItems.Add(new ToolStripSeparator());
                    ToolStripMenuItem header = new ToolStripMenuItem(theme.category.ToUpperInvariant());
                    header.Enabled = false;
                    _wallpaperItem.DropDownItems.Add(header);
                    lastCategory = theme.category;
                }
                ToolStripMenuItem item = new ToolStripMenuItem(theme.name);
                item.Tag = theme.id;
                item.Checked = theme.id == current;
                item.Click += delegate(object sender, EventArgs e)
                {
                    ToolStripMenuItem clicked = sender as ToolStripMenuItem;
                    if (clicked == null) return;
                    string id = Convert.ToString(clicked.Tag);
                    if (!string.IsNullOrEmpty(id)) _app.ApplyWallpaperById(id);
                };
                _wallpaperItem.DropDownItems.Add(item);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _notify.Visible = false;
                _notify.Dispose();
            }
            catch { }
            try { _menu.Dispose(); }
            catch { }
        }
    }
}
