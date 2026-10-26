using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImgsToPDF {
    /// <summary>
    /// Win32 原生 API P/Invoke 调用集中管理类
    /// </summary>
    internal static class NativeMethods {
        private const int IDC_HAND = 32649;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

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
            var status = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(status)) {
                return long.MaxValue;
            }
            return (long)status.ullAvailPhys;
        }
    }
}
