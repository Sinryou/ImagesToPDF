using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ImgsToPDF {
    internal static class Program {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main() {
            using Mutex mutex = new(true, @"Local\ImgsToPDF", out bool isFirstInstance);

            if (!isFirstInstance) {
                ActivateExistingInstance();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new ImgsToPDF());
        }

        private static void ActivateExistingInstance() {
            try {
                using var current = Process.GetCurrentProcess();
                foreach (var process in Process.GetProcessesByName(current.ProcessName)) {
                    using (process) {
                        if (process.Id == current.Id)
                            continue;
                        IntPtr hWnd = process.MainWindowHandle;
                        if (hWnd != IntPtr.Zero) {
                            if (IsIconic(hWnd)) {
                                ShowWindowAsync(hWnd, SW_RESTORE);
                            }
                            SetForegroundWindow(hWnd);
                            break;
                        }
                    }
                }
            }
            catch {
                // 忽略异常
            }
        }
    }
}
