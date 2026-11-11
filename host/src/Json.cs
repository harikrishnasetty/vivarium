using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Vivarium
{
    internal static class Json
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        static Json()
        {
            Serializer.MaxJsonLength = 16 * 1024 * 1024;
            Serializer.RecursionLimit = 100;
        }

        public static string Serialize(object value)
        {
            try
            {
                return Serializer.Serialize(value);
            }
            catch (Exception ex)
            {
                Log.W("json serialize: " + ex.Message);
                return "{}";
            }
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return null;
                return Serializer.Deserialize<Dictionary<string, object>>(text);
            }
            catch (Exception ex)
            {
                Log.W("json parse: " + ex.Message);
                return null;
            }
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static bool TryGet(Dictionary<string, object> dict, string key, out object value)
        {
            value = null;
            if (dict == null) return false;
            return dict.TryGetValue(key, out value);
        }

        public static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            object value;
            if (dict != null && dict.TryGetValue(key, out value) && value != null) return Convert.ToString(value);
            return fallback;
        }

        public static bool GetBool(Dictionary<string, object> dict, string key, bool fallback)
        {
            object value;
            if (dict != null && dict.TryGetValue(key, out value) && value != null)
            {
                try { return Convert.ToBoolean(value); }
                catch { return fallback; }
            }
            return fallback;
        }

        public static int GetInt(Dictionary<string, object> dict, string key, int fallback)
        {
            object value;
            if (dict != null && dict.TryGetValue(key, out value) && value != null)
            {
                try { return Convert.ToInt32(value); }
                catch { return fallback; }
            }
            return fallback;
        }
    }
}
