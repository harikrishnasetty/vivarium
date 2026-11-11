using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Vivarium
{
    public class IconInfo
    {
        public string Id;
        public uint Seed;
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    /*
     * Reads the live desktop icon layout from Explorer's SysListView32.
     * Cross-process ListView messages are used (LVM_GETITEMRECT / LVM_GETITEMTEXTW),
     * which is the documented, shell-version independent way to inspect the desktop.
     * Icon names are hashed locally and never stored or transmitted in clear text.
     */
    internal static class IconLayoutReader
    {
        private const int RemoteSize = 4096;
        private const int OffPoint = 0;
        private const int OffRect = 32;
        private const int OffItem = 128;
        private const int OffText = 512;
        private const int TextChars = 260;

        public static List<IconInfo> Read()
        {
            List<IconInfo> result = new List<IconInfo>();
            IntPtr defView = FindDesktopDefView();
            if (defView == IntPtr.Zero) return result;

            IntPtr listView = Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (listView == IntPtr.Zero) return result;

            int count = (int)Native.SendMessage(listView, Native.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0 || count > 600) return result;

            Native.RECT listRect;
            if (!Native.GetWindowRect(listView, out listRect)) return result;

            uint pid;
            Native.GetWindowThreadProcessId(listView, out pid);
            if (pid == 0) return result;

            IntPtr process = Native.OpenProcess(
                Native.PROCESS_VM_OPERATION | Native.PROCESS_VM_READ | Native.PROCESS_VM_WRITE | Native.PROCESS_QUERY_INFORMATION,
                false, pid);
            if (process == IntPtr.Zero)
            {
                Log.W("icon reader: OpenProcess failed for explorer pid " + pid);
                return result;
            }

            IntPtr remote = IntPtr.Zero;
            try
            {
                remote = Native.VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)RemoteSize, Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
                if (remote == IntPtr.Zero)
                {
                    Log.W("icon reader: VirtualAllocEx failed");
                    return result;
                }

                Dictionary<string, int> seen = new Dictionary<string, int>();
                for (int i = 0; i < count; i++)
                {
                    Native.RECT rect;
                    bool haveRect = ReadItemRect(listView, process, remote, i, out rect);
                    Native.POINT pos;
                    bool havePos = ReadItemPosition(listView, process, remote, i, out pos);

                    int x;
                    int y;
                    int w;
                    int h;
                    if (haveRect && rect.Right > rect.Left && rect.Bottom > rect.Top)
                    {
                        x = rect.Left;
                        y = rect.Top;
                        w = rect.Right - rect.Left;
                        h = rect.Bottom - rect.Top;
                    }
                    else if (havePos)
                    {
                        x = pos.X;
                        y = pos.Y;
                        w = 84;
                        h = 96;
                    }
                    else
                    {
                        continue;
                    }

                    if (w < 24) w = 24;
                    if (h < 24) h = 24;
                    if (w > 400) w = 400;
                    if (h > 400) h = 400;

                    string name = ReadItemText(listView, process, remote, i);
                    uint seed = Fnv1a(name);
                    string id = seed.ToString("x8");
                    int dup;
                    if (seen.TryGetValue(id, out dup))
                    {
                        dup++;
                        seen[id] = dup;
                        id = id + "-" + dup;
                    }
                    else
                    {
                        seen[id] = 1;
                    }

                    IconInfo info = new IconInfo();
                    info.Id = id;
                    info.Seed = seed;
                    info.X = listRect.Left + x;
                    info.Y = listRect.Top + y;
                    info.W = w;
                    info.H = h;
                    result.Add(info);
                }
            }
            catch (Exception ex)
            {
                Log.W("icon reader: " + ex.Message);
            }
            finally
            {
                if (remote != IntPtr.Zero) Native.VirtualFreeEx(process, remote, UIntPtr.Zero, Native.MEM_RELEASE);
                Native.CloseHandle(process);
            }

            return result;
        }

        private static bool ReadItemPosition(IntPtr listView, IntPtr process, IntPtr remote, int index, out Native.POINT point)
        {
            point = new Native.POINT();
            IntPtr ok = Native.SendMessage(listView, Native.LVM_GETITEMPOSITION, (IntPtr)index, Add(remote, OffPoint));
            if (ok == IntPtr.Zero) return false;
            byte[] buf = new byte[8];
            IntPtr read;
            if (!Native.ReadProcessMemory(process, Add(remote, OffPoint), buf, (IntPtr)8, out read)) return false;
            point.X = BitConverter.ToInt32(buf, 0);
            point.Y = BitConverter.ToInt32(buf, 4);
            return true;
        }

        private static bool ReadItemRect(IntPtr listView, IntPtr process, IntPtr remote, int index, out Native.RECT rect)
        {
            rect = new Native.RECT();
            byte[] seed = new byte[16];
            BitConverter.GetBytes(Native.LVIR_BOUNDS).CopyTo(seed, 0);
            IntPtr written;
            if (!Native.WriteProcessMemory(process, Add(remote, OffRect), seed, (IntPtr)16, out written)) return false;
            Native.SendMessage(listView, Native.LVM_GETITEMRECT, (IntPtr)index, Add(remote, OffRect));
            byte[] buf = new byte[16];
            IntPtr read;
            if (!Native.ReadProcessMemory(process, Add(remote, OffRect), buf, (IntPtr)16, out read)) return false;
            rect.Left = BitConverter.ToInt32(buf, 0);
            rect.Top = BitConverter.ToInt32(buf, 4);
            rect.Right = BitConverter.ToInt32(buf, 8);
            rect.Bottom = BitConverter.ToInt32(buf, 12);
            return true;
        }

        private static string ReadItemText(IntPtr listView, IntPtr process, IntPtr remote, int index)
        {
            try
            {
                Native.LVITEMW item = new Native.LVITEMW();
                item.mask = Native.LVIF_TEXT;
                item.iItem = index;
                item.iSubItem = 0;
                item.pszText = Add(remote, OffText);
                item.cchTextMax = TextChars;

                int size = Marshal.SizeOf(typeof(Native.LVITEMW));
                IntPtr local = Marshal.AllocHGlobal(size);
                byte[] bytes = new byte[size];
                try
                {
                    Marshal.StructureToPtr(item, local, false);
                    Marshal.Copy(local, bytes, 0, size);
                }
                finally
                {
                    Marshal.FreeHGlobal(local);
                }

                IntPtr written;
                if (!Native.WriteProcessMemory(process, Add(remote, OffItem), bytes, (IntPtr)size, out written)) return "";
                Native.SendMessage(listView, Native.LVM_GETITEMTEXTW, (IntPtr)index, Add(remote, OffItem));

                byte[] text = new byte[TextChars * 2];
                IntPtr read;
                if (!Native.ReadProcessMemory(process, Add(remote, OffText), text, (IntPtr)text.Length, out read)) return "";
                int len = 0;
                while (len + 1 < text.Length && !(text[len] == 0 && text[len + 1] == 0)) len += 2;
                return Encoding.Unicode.GetString(text, 0, len);
            }
            catch
            {
                return "";
            }
        }

        private static IntPtr FindDesktopDefView()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;

            IntPtr found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr lparam)
            {
                IntPtr dv = Native.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                {
                    found = dv;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static IntPtr Add(IntPtr basePtr, int offset)
        {
            return new IntPtr(basePtr.ToInt64() + offset);
        }

        private static uint Fnv1a(string text)
        {
            uint hash = 2166136261;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619;
            }
            return hash;
        }
    }
}
