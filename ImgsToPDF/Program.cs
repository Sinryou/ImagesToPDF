using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace ImgsToPDF {
    internal static class Program {
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

            // 语言等用户设置保存在“按程序集版本分目录”的 user.config 里，
            // 升级到新版本后读取到的是默认值。这里在升级后的首次启动做一次迁移。
            UpgradeUserSettings();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }

        /// <summary>
        /// 迁移上一个版本的用户设置（例如语言）。
        /// 只在新版本首次启动时执行一次：UpgradeRequired 标志本身也保存在设置里，
        /// 否则每次启动都会拿旧版本的值覆盖用户在当前版本改过的设置。
        /// </summary>
        private static void UpgradeUserSettings() {
            try {
                if (!Properties.Settings.Default.UpgradeRequired) {
                    return;
                }
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex) {
                // 配置损坏/无写权限时不能阻止程序启动：本次用默认值，下次启动再尝试迁移
                Debug.WriteLine($"[ImgsToPDF] Failed to upgrade user settings: {ex.Message}");
            }
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
                            if (NativeMethods.IsIconic(hWnd)) {
                                NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
                            }
                            NativeMethods.SetForegroundWindow(hWnd);
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
