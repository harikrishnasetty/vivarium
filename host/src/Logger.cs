using System;
using System.IO;

namespace Vivarium
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static void Init(string dataDir)
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                _path = Path.Combine(dataDir, "vivarium.log");
                try
                {
                    FileInfo fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        string old = Path.Combine(dataDir, "vivarium.old.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(_path, old);
                    }
                }
                catch { }
            }
            catch { }
        }

        public static void I(string message) { Write("INFO ", message); }
        public static void W(string message) { Write("WARN ", message); }
        public static void E(string message) { Write("ERROR", message); }

        public static void E(string message, Exception ex)
        {
            Write("ERROR", message + " :: " + (ex == null ? "?" : ex.GetType().Name + ": " + ex.Message));
            if (ex != null && ex.StackTrace != null) Write("ERROR", ex.StackTrace);
        }

        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
            lock (Gate)
            {
                try
                {
                    if (_path != null) File.AppendAllText(_path, line + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
