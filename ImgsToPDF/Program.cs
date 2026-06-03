using System;
using System.Threading;
using System.Windows.Forms;

namespace ImgsToPDF {
    internal static class Program {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main() {
            using Mutex mutex = new(
                true,
                @"ImgsToPDF",
                out bool isFirstInstance);

            if (!isFirstInstance) {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new ImgsToPDF());
        }
    }
}
