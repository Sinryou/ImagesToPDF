using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImgsToPDF {
    /// <summary>
    /// Win32 原生 API P/Invoke 调用集中管理类。
    /// 统一配置 System32 安全加载路径以防止 DLL 预加载/劫持漏洞，
    /// 并采用结构体栈上分配杜绝内存查询时的 GC 压力。
    /// </summary>
    internal static class NativeMethods {
        private const int IDC_HAND = 32649;
        public const int SW_RESTORE = 9;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public static MEMORYSTATUSEX Create() => new() {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
            };
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        /// <summary>
        /// 获取 Windows 现代系统原生平滑手形光标（IDC_HAND），避免 WinForms 默认 Cursors.Hand 在高分屏下出现锯齿像素化。
        /// </summary>
        public static readonly Cursor SystemHandCursor = GetSystemHandCursor();

        private static Cursor GetSystemHandCursor() {
            try {
                IntPtr handle = LoadCursor(IntPtr.Zero, IDC_HAND);
                return handle != IntPtr.Zero ? new Cursor(handle) : Cursors.Hand;
            }
            catch {
                return Cursors.Hand;
            }
        }

        /// <summary>
        /// 获取当前可用物理内存字节数（若查询失败返回 long.MaxValue）
        /// </summary>
        public static long GetAvailablePhysicalMemory() {
            var status = MEMORYSTATUSEX.Create();
            if (!GlobalMemoryStatusEx(ref status)) {
                return long.MaxValue;
            }
            return (long)status.ullAvailPhys;
        }
    }
}
