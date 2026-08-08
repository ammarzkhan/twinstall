using System;
using System.Collections.Generic;

namespace Twinstance.Platform
{
    /// <summary>
    /// Top-level windows in Z-order, front to back. The window on top belongs to the app the
    /// user touched most recently, and that ordering survives the browser stealing focus
    /// during a sign-in — which is what makes callback routing work.
    /// </summary>
    public static class WindowEnumerator
    {
        public static IList<int> ProcessIdsTopDown()
        {
            var pids = new List<int>();
            IntPtr h = NativeMethods.GetTopWindow(IntPtr.Zero);
            while (h != IntPtr.Zero)
            {
                if (NativeMethods.IsWindowVisible(h) && NativeMethods.GetWindowTextLength(h) > 0)
                {
                    // The return value is the thread id, and it is 0 on failure — in which
                    // case 'pid' holds nothing meaningful. Checking it is the difference
                    // between skipping a window we couldn't read and inventing a process.
                    int pid;
                    if (NativeMethods.GetWindowThreadProcessId(h, out pid) != 0
                        && pid != 0 && !pids.Contains(pid)) pids.Add(pid);
                }
                h = NativeMethods.GetWindow(h, NativeMethods.GW_HWNDNEXT);
            }
            return pids;
        }

        public static IList<IntPtr> VisibleWindowsForProcesses(ICollection<int> processIds)
        {
            var handles = new List<IntPtr>();
            if (processIds == null || processIds.Count == 0) return handles;

            IntPtr h = NativeMethods.GetTopWindow(IntPtr.Zero);
            while (h != IntPtr.Zero)
            {
                if (NativeMethods.IsWindowVisible(h) && NativeMethods.GetWindowTextLength(h) > 0)
                {
                    int pid;
                    if (NativeMethods.GetWindowThreadProcessId(h, out pid) != 0
                        && processIds.Contains(pid)) handles.Add(h);
                }
                h = NativeMethods.GetWindow(h, NativeMethods.GW_HWNDNEXT);
            }
            return handles;
        }
    }
}
