using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PrinterToolkit
{
    /// <summary>Win32 interop: raw printing (winspool), window discovery and window capture.</summary>
    internal static class Native
    {
        public const uint WM_CLOSE = 0x0010;
        public const uint BM_CLICK = 0x00F5;
        public const uint TCM_GETITEMCOUNT = 0x1304;
        public const uint PSM_SETCURSEL = 0x0400 + 101;
        public const uint PSM_GETCURRENTPAGEHWND = 0x0400 + 118;
        public const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        // ---------------- winspool RAW printing ----------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class DOCINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string name, out IntPtr h, IntPtr pd);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr h);
        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int StartDocPrinter(IntPtr h, int level, [In] DOCINFO di);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr h);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr h);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr h);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr h, byte[] buf, int count, out int written);

        /// <summary>Sends raw bytes (ZPL / EPL / TSPL / ESC-POS) straight to the printer queue.</summary>
        public static void SendRaw(string printer, byte[] data, string docName)
        {
            IntPtr h;
            if (!OpenPrinter(printer, out h, IntPtr.Zero))
                throw new InvalidOperationException("OpenPrinter failed (Win32 error " + Marshal.GetLastWin32Error() + ").");
            try
            {
                var di = new DOCINFO { pDocName = docName, pDataType = "RAW" };
                if (StartDocPrinter(h, 1, di) == 0)
                {
                    // v4 / XPS class drivers require the XPS_PASS data type for pass-through
                    di.pDataType = "XPS_PASS";
                    if (StartDocPrinter(h, 1, di) == 0)
                        throw new InvalidOperationException("StartDocPrinter failed (Win32 error " + Marshal.GetLastWin32Error() + ").");
                }
                try
                {
                    StartPagePrinter(h);
                    int written;
                    if (!WritePrinter(h, data, data.Length, out written))
                        throw new InvalidOperationException("WritePrinter failed (Win32 error " + Marshal.GetLastWin32Error() + ").");
                    EndPagePrinter(h);
                }
                finally { EndDocPrinter(h); }
            }
            finally { ClosePrinter(h); }
        }

        // ---------------- window helpers ----------------
        public static string WindowText(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static List<IntPtr> TopWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows((h, l) => { list.Add(h); return true; }, IntPtr.Zero);
            return list;
        }

        public static IntPtr FindChildByClass(IntPtr parent, string cls)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, (h, l) =>
            {
                if (ClassName(h) == cls) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Captures a window to a bitmap using PrintWindow (works even if partially covered).</summary>
        public static Bitmap CaptureWindow(IntPtr h)
        {
            RECT r;
            GetWindowRect(h, out r);
            int w = Math.Max(1, r.Right - r.Left), hgt = Math.Max(1, r.Bottom - r.Top);
            var bmp = new Bitmap(w, hgt, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(h, hdc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(hdc);
                if (!ok)
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, hgt));
            }
            return bmp;
        }

        public static void EnableDarkTitleBar(IntPtr handle)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(handle, 19, ref on, 4);
            }
            catch { }
        }

        public class SidebarButtonInfo
        {
            public IntPtr Handle;
            public string Name;
            public int Top;
        }

        public static List<SidebarButtonInfo> FindSidebarButtons(IntPtr dlg)
        {
            var list = new List<SidebarButtonInfo>();
            RECT dlgRect;
            if (!GetWindowRect(dlg, out dlgRect)) return list;
            int dlgWidth = Math.Max(1, dlgRect.Right - dlgRect.Left);

            EnumChildWindows(dlg, (h, l) =>
            {
                if (IsWindowVisible(h) && ClassName(h) == "Button")
                {
                    RECT r;
                    GetWindowRect(h, out r);
                    int relX = r.Left - dlgRect.Left;
                    int w = r.Right - r.Left;
                    int hgt = r.Bottom - r.Top;
                    string text = WindowText(h).Trim();

                    // Navigation sidebar button heuristics:
                    // 1. Located on the left side (< 30% dialog width)
                    // 2. Button dimensions suitable for sidebar tabs (w >= 70, hgt between 18 and 60)
                    // 3. Has non-empty text
                    // 4. Exclude common non-nav buttons (OK, Cancel, Apply, Help, Print test page, Logo, About)
                    if (relX < dlgWidth * 0.30 && w >= 70 && hgt >= 18 && hgt <= 60 && !string.IsNullOrEmpty(text))
                    {
                        if (!IsExcludedSidebarButton(text))
                        {
                            list.Add(new SidebarButtonInfo
                            {
                                Handle = h,
                                Name = text.Replace("&", "").Trim(),
                                Top = r.Top
                            });
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            list.Sort((a, b) => a.Top.CompareTo(b.Top));
            return list;
        }

        private static bool IsExcludedSidebarButton(string text)
        {
            string t = text.Trim();
            if (t.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Cancel", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Apply", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("&Apply", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Help", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Print test page", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Print preview", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Logo", StringComparison.OrdinalIgnoreCase) ||
                (t.Equals("About", StringComparison.OrdinalIgnoreCase) && t.Length <= 5))
            {
                return true;
            }
            return false;
        }
    }
}
