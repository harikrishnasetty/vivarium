using System;
using System.IO;
using Microsoft.Win32;

namespace Vivarium
{
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Vivarium";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(ValueName);
                    return value != null && Convert.ToString(value).Length > 0;
                }
            }
            catch (Exception ex)
            {
                Log.W("startup read: " + ex.Message);
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("Could not open the startup registry key.");
                if (enabled)
                {
                    string exe = System.Windows.Forms.Application.ExecutablePath;
                    key.SetValue(ValueName, "\"" + exe + "\" --autostart", RegistryValueKind.String);
                    Log.I("startup enabled: " + exe);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                    Log.I("startup disabled");
                }
            }
        }
    }
}
