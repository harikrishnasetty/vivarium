using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Vivarium
{
    public class MonitorInfo
    {
        public int Index;
        public IntPtr Handle;
        public int X;
        public int Y;
        public int W;
        public int H;
        public int WorkX;
        public int WorkY;
        public int WorkW;
        public int WorkH;
        public bool Primary;

        public string Signature()
        {
            return Index + ":" + X + "," + Y + "," + W + "," + H;
        }
    }

    internal static class Interop
    {
        public const int WM_VIVARIUM_SETTINGS = 0x8000 + 1;
        public const int WM_VIVARIUM_EXIT = 0x8000 + 2;
        public const int WM_VIVARIUM_NEXT = 0x8000 + 3;

        public const int HWND_BOTTOM = 1;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOZORDER = 0x0004;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Native.RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref Native.POINT lpPoints, int cPoints);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch
            {
                /* older Windows without per-monitor v2 */
            }
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                /* nothing else to try */
            }
        }

        public static List<MonitorInfo> GetMonitors()
        {
            List<MonitorInfo> list = new List<MonitorInfo>();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMonitor, IntPtr hdc, ref Native.RECT rect, IntPtr data)
                {
                    Native.MONITORINFO info = new Native.MONITORINFO();
                    info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
                    if (Native.GetMonitorInfo(hMonitor, ref info))
                    {
                        MonitorInfo m = new MonitorInfo();
                        m.Handle = hMonitor;
                        m.X = info.rcMonitor.Left;
                        m.Y = info.rcMonitor.Top;
                        m.W = info.rcMonitor.Right - info.rcMonitor.Left;
                        m.H = info.rcMonitor.Bottom - info.rcMonitor.Top;
                        m.WorkX = info.rcWork.Left;
                        m.WorkY = info.rcWork.Top;
                        m.WorkW = info.rcWork.Right - info.rcWork.Left;
                        m.WorkH = info.rcWork.Bottom - info.rcWork.Top;
                        m.Primary = (info.dwFlags & 1) != 0;
                        if (m.W > 0 && m.H > 0) list.Add(m);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.W("monitor enumeration: " + ex.Message);
            }
            list.Sort(delegate(MonitorInfo a, MonitorInfo b)
            {
                if (a.X != b.X) return a.X.CompareTo(b.X);
                return a.Y.CompareTo(b.Y);
            });
            for (int i = 0; i < list.Count; i++) list[i].Index = i;
            return list;
        }

        public static void MapPointToParent(ref Native.POINT point, IntPtr parent)
        {
            try
            {
                MapWindowPoints(IntPtr.Zero, parent, ref point, 1);
            }
            catch (Exception ex)
            {
                Log.W("MapWindowPoints: " + ex.Message);
            }
        }

        public static void SetDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int value = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            }
            catch
            {
                /* older builds ignore this */
            }
        }

        public static void PostToBridge(IntPtr hwnd, int message)
        {
            try
            {
                PostMessage(hwnd, message, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                /* bridge is gone */
            }
        }
    }
}
