using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImgsToPDF {
    internal static class Program {
        [DllImport("User32.dll", SetLastError = false, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessDPIAware();
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main() {
            Mutex obj = new Mutex(true, "Global\\ImgsToPDF", out bool isFirstInstance);
            if (isFirstInstance) {  // 仅保留第一个窗口实例 新建跳过
#if NETCOREAPP3_0_OR_GREATER
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
#else
                SetProcessDPIAware();   // 调用User32.dll中的WinAPI来解决Winform的DPI问题
#endif
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ImgsToPDF());
                GC.KeepAlive(obj);
            }
        }
    }
}
