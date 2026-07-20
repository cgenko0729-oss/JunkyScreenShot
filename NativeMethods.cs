using System;
using System.Runtime.InteropServices;

namespace JunkyScreenShot
{
    /// <summary>
    /// All Win32 API declarations in one place: global hotkey, window detection, GDI cleanup.
    /// </summary>
    internal static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint VK_F1 = 0x70;

        // DwmGetWindowAttribute attributes
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9; // visible window bounds without drop shadow
        public const int DWMWA_CLOAKED = 14;              // non-zero for invisible (cloaked) UWP windows

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;

            public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
        }

        // ---- Global hotkey ----

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ---- Window detection ----

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        // ---- GDI ----

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
