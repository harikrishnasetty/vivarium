using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Vivarium
{
    public class SystemSettings
    {
        public bool startup { get; set; }
        public bool pauseFullscreen { get; set; }
        public int fps { get; set; }
        public string monitors { get; set; }
        public bool reducedMotion { get; set; }

        public SystemSettings()
        {
            pauseFullscreen = true;
            fps = 30;
            monitors = "all";
        }
    }

    public class Settings
    {
        public int version { get; set; }
        public string wallpaper { get; set; }
        public Dictionary<string, Dictionary<string, object>> themeParams { get; set; }
        public SystemSettings system { get; set; }
        public string videoPath { get; set; }
        public string imagePath { get; set; }

        public Settings()
        {
            version = 1;
            wallpaper = "moss-vale";
            themeParams = new Dictionary<string, Dictionary<string, object>>();
            system = new SystemSettings();
            videoPath = "";
            imagePath = "";
        }
    }

    public class SettingsStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        public Settings Settings;
        public bool Existed;

        public SettingsStore(string dataDir)
        {
            _path = Path.Combine(dataDir, "settings.json");
            Settings = new Settings();
            Existed = File.Exists(_path);
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string text = File.ReadAllText(_path);
                if (string.IsNullOrEmpty(text)) return;
                Settings loaded = _json.Deserialize<Settings>(text);
                if (loaded == null) return;
                if (loaded.themeParams == null) loaded.themeParams = new Dictionary<string, Dictionary<string, object>>();
                if (loaded.system == null) loaded.system = new SystemSettings();
                if (loaded.system.monitors == null) loaded.system.monitors = "all";
                if (loaded.system.fps != 24 && loaded.system.fps != 30 && loaded.system.fps != 60) loaded.system.fps = 30;
                if (loaded.videoPath == null) loaded.videoPath = "";
                if (loaded.imagePath == null) loaded.imagePath = "";
                if (loaded.wallpaper == null) loaded.wallpaper = "moss-vale";
                Settings = loaded;
            }
            catch (Exception ex)
            {
                Log.W("settings load failed: " + ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                string text = _json.Serialize(Settings);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, text);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                Log.W("settings save failed: " + ex.Message);
            }
        }
    }

    public class ThemeManifest
    {
        public string id;
        public string name;
        public string category;
        public string tagline;
        public Dictionary<string, object> raw;
        public Dictionary<string, object> defaults;

        public ThemeManifest()
        {
            id = "";
            name = "";
            category = "";
            tagline = "";
            raw = new Dictionary<string, object>();
            defaults = new Dictionary<string, object>();
        }
    }

    public class ThemeCatalog
    {
        private readonly string _wallpapersDir;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        public List<ThemeManifest> Themes = new List<ThemeManifest>();

        public ThemeCatalog(string appRoot)
        {
            _wallpapersDir = Path.Combine(appRoot, "wallpapers");
        }

        public void Load()
        {
            Themes.Clear();
            try
            {
                if (!Directory.Exists(_wallpapersDir))
                {
                    Log.W("wallpapers folder missing: " + _wallpapersDir);
                    return;
                }
                string[] dirs = Directory.GetDirectories(_wallpapersDir);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string name = Path.GetFileName(dirs[i]);
                    if (name.StartsWith("_")) continue;
                    string manifestPath = Path.Combine(dirs[i], "theme.json");
                    if (!File.Exists(manifestPath)) continue;
                    try
                    {
                        string text = File.ReadAllText(manifestPath);
                        Dictionary<string, object> dict = _json.Deserialize<Dictionary<string, object>>(text);
                        if (dict == null) continue;
                        ThemeManifest theme = new ThemeManifest();
                        theme.raw = dict;
                        theme.id = GetString(dict, "id", name);
                        theme.name = GetString(dict, "name", name);
                        theme.category = GetString(dict, "category", "Other");
                        theme.tagline = GetString(dict, "tagline", "");
                        theme.defaults = ExtractDefaults(dict);
                        Themes.Add(theme);
                    }
                    catch (Exception ex)
                    {
                        Log.W("theme '" + name + "' skipped: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.W("theme catalog: " + ex.Message);
            }
            Themes.Sort(delegate(ThemeManifest a, ThemeManifest b)
            {
                int c = string.Compare(a.category, b.category, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });
        }

        public ThemeManifest Get(string id)
        {
            for (int i = 0; i < Themes.Count; i++) if (Themes[i].id == id) return Themes[i];
            return null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Themes.Count; i++) if (Themes[i].id == id) return i;
            return -1;
        }

        public Dictionary<string, object> MergedParams(string id, Settings settings)
        {
            ThemeManifest theme = Get(id);
            Dictionary<string, object> merged = new Dictionary<string, object>();
            if (theme != null)
            {
                foreach (KeyValuePair<string, object> kv in theme.defaults) merged[kv.Key] = kv.Value;
            }
            Dictionary<string, object> saved;
            if (settings.themeParams.TryGetValue(id, out saved) && saved != null)
            {
                foreach (KeyValuePair<string, object> kv in saved) merged[kv.Key] = kv.Value;
            }
            return merged;
        }

        private static Dictionary<string, object> ExtractDefaults(Dictionary<string, object> manifest)
        {
            Dictionary<string, object> defaults = new Dictionary<string, object>();
            object rawParams;
            if (!manifest.TryGetValue("params", out rawParams)) return defaults;
            IEnumerable list = rawParams as IEnumerable;
            if (list == null) return defaults;
            foreach (object entry in list)
            {
                Dictionary<string, object> param = entry as Dictionary<string, object>;
                if (param == null) continue;
                object key;
                if (!param.TryGetValue("key", out key) || key == null) continue;
                object def;
                if (param.TryGetValue("default", out def)) defaults[Convert.ToString(key)] = def;
            }
            return defaults;
        }

        private static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            object value;
            if (dict.TryGetValue(key, out value) && value != null) return Convert.ToString(value);
            return fallback;
        }
    }
}
