using System;
using System.Runtime.InteropServices;

namespace Twinstance.Platform
{
    /// <summary>
    /// Every import is pinned to System32. Without it the loader would search the
    /// application directory first, so anything that could drop a user32.dll next to
    /// Twinstance.exe would be loaded in preference to the real one. That matters more here
    /// than in most apps: this exe is registered as a protocol handler, so it is started by
    /// Windows on a URL and already sits in the shape antivirus engines look at closely.
    /// </summary>
    internal static class NativeMethods
    {
        private const DllImportSearchPath Sys32 = DllImportSearchPath.System32;

        [DllImport("user32.dll"), DefaultDllImportSearchPaths(Sys32)]
        internal static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll"), DefaultDllImportSearchPaths(Sys32)]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll"), DefaultDllImportSearchPaths(Sys32)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll"), DefaultDllImportSearchPaths(Sys32)]
        internal static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

        [DllImport("user32.dll"), DefaultDllImportSearchPaths(Sys32)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll"), DefaultDllImportSearchPaths(Sys32)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode), DefaultDllImportSearchPaths(Sys32)]
        internal static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

        // ExtractAssociatedIcon only ever yields 32x32, which looks like porridge once a badge
        // is composited over it at taskbar size. PrivateExtractIcons asks for an exact size and
        // returns the real 256px entry when the executable has one.
        [DllImport("user32.dll", CharSet = CharSet.Unicode), DefaultDllImportSearchPaths(Sys32)]
        internal static extern int PrivateExtractIcons(
            string fileName, int index, int cx, int cy, IntPtr[] icons, int[] ids, int count, uint flags);

        [DllImport("user32.dll"), DefaultDllImportSearchPaths(Sys32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        internal const uint GW_HWNDNEXT     = 2;
        internal const uint IMAGE_ICON      = 1;
        internal const uint LR_LOADFROMFILE = 0x0010;
        internal const uint LR_SHARED       = 0x8000;
        internal const uint WM_SETICON      = 0x0080;
        internal const int  ICON_SMALL      = 0;
        internal const int  ICON_BIG        = 1;
    }
}
