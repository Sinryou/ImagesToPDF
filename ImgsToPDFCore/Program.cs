using ImgsToPDF.Shared;
using CommandLine;
using System;
using System.IO;
using System.Threading;

namespace ImgsToPDF.Core {
    internal class Program {
        /// <summary>
        /// 程序的所有命令行参数类型
        /// </summary>
        private class Options {
            [Option('d', "dir-path", Required = true, HelpText = "图片所在的文件夹路径。")]
            public string DirectoryPath { get; set; }

            [Option('l', "layout", Required = false, HelpText = "页面布局：Single 单页输出、DuplexLeftToRight 双页左至右、DuplexRightToLeft 双页右至左；也接受对应数值 0/1/2。")]
            public Layout Layout { get; set; }

            [Option('f', "fast", Required = false, HelpText = "是否以牺牲图片质量换取生成速度。")]
            public bool FastFlag { get; set; }

            [Option('m', "merge-pdfs", Required = false, HelpText = "用于合并pdf的单独功能")]
            public bool Merge { get; set; }
        }
        static void Main(string[] args) {
            // 显式指定 stderr/stdout 的编码。Core 由 GUI 以「重定向输出 + 无窗口」方式启动，
            // 此时 Console 默认使用 OEM 代码页，而 GUI 侧按 UTF-8 解码，
            // 导致错误信息里含中文的文件名/文件夹名变成乱码（用户无法辨认是哪张图出错）。
            Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            // 看门狗只应由 GUI 显式开启，且该标志不能继承给其它进程，因此读完立刻清掉。
            bool watchParent = string.Equals(
                Environment.GetEnvironmentVariable(IpcContracts.WatchParentEnvVar), "1", StringComparison.Ordinal);
            Environment.SetEnvironmentVariable(IpcContracts.WatchParentEnvVar, null);

            CSGlobal.IsGuiMode = watchParent;
            StartParentWatchdogIfRequested(watchParent);

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Run)
                .WithNotParsed(_ => {
                    Environment.ExitCode = 1;
                });
        }

        /// <summary>
        /// GUI 在启动 Core 时设置此环境变量以启用父进程看门狗。
        /// 命令行/脚本调用不会设置它，因此 stdin 被重定向（管道、cmd 的 &lt; NUL、CI 等）
        /// 不会被误当成「父进程已退出」而自杀。
        /// </summary>
        

        /// <summary>
        /// 父进程看门狗：由 GUI 拉起时（见 <see cref="IpcContracts.WatchParentEnvVar"/>），后台监听 Stdin 管道。
        /// 一旦父进程关闭或异常崩溃，OS 会自动关闭管道写端，ReadByte() 立即返回 -1。
        /// 先置取消标志让主线程走 finally 清理临时文件与解压目录，超时未退才强制自毁。
        /// 注意：这里刻意不用 Console.IsInputRedirected 作为判据——它无法区分
        /// 「GUI 拉起的匿名管道」与「任何其它非交互 stdin」，用它会让命令行调用直接失败。
        /// </summary>
        private static void StartParentWatchdogIfRequested(bool watchParent) {
            if (!watchParent || !Console.IsInputRedirected) {
                return;
            }
            new Thread(() => {
                try {
                    string line;
                    while ((line = Console.ReadLine()) != null) {
                        CSGlobal.DeliverStdinLine(line);
                    }

                }
                catch {
                    // 管道读取异常同样视为父进程已断开
                }

                CSGlobal.IsCancelled = true;
                CSGlobal.DeliverStdinLine(null);

                // 给主线程预留 2 秒宽限期以完成栈展开及 finally 清理（删除 .pdf.tmp 与解压目录）；
                // 若主线程正阻塞在 InputBox 密码弹窗等无法响应取消检查的位置，到期直接终止进程。
                for (int i = 0; i < 20; i++) {
                    if (CSGlobal.CleanupCompleted) {
                        return;   // 清理已完成，让主线程自然退出，保留真实退出码
                    }
                    Thread.Sleep(100);
                }

                // 宽限期已到但主线程仍在写文件：临时文件是以 FileShare.Delete 打开的，
                // 这里删除会把文件标记为“删除待完成”，进程退出、句柄关闭时系统即完成删除，
                // 从而避免在用户图片目录留下 *.pdf.tmp。
                AtomicFileWriter.CleanupActiveTempFiles();
                Environment.Exit(1);
            }) {
                IsBackground = true,
                Name = "ParentStdinWatchdog"
            }.Start();
        }
        /// <summary>
        /// 使用解析后的命令行参数进行操作。
        /// </summary>
        /// <param name="option">解析后的参数</param>
        static void Run(Options option) {
            var startedUtc = DateTime.UtcNow;
            try {
                string fullDirectoryPath = Path.GetFullPath(option.DirectoryPath);
                CSGlobal.luaEnv.AddBuildin("ffi", XLua.LuaDLL.Lua.LoadFFI);
                CSGlobal.luaEnv.AddBuildin("lfs", XLua.LuaDLL.Lua.LoadLFS);
                CSGlobal.luaEnv.AddBuildin("rapidjson", XLua.LuaDLL.Lua.LoadRapidJson);

                CSGlobal.luaEnv.DoString(@"config = require 'config';"); // 获取lua内的方法

                CSGlobal.luaConfig = CSGlobal.luaEnv.Global.Get<IConfig>("config");
                CSGlobal.luaConfig.PreProcess(fullDirectoryPath, option.Layout, option.FastFlag, option.Merge);

                VerifyPdfGenerated(startedUtc);
            }
            catch (Exception ex) {
                // 顶层兜底：把异常转成"一行可读的错误 + 非零退出码"。
                // 不能让 CLR 去打印这个异常：它多半来自 XLua（Lua 中抛出的 C# 异常会被包成 Lua 错误），
                // 而 finally 里会释放 Lua 状态，异常对象届时连 ToString() 都会失败，
                // 用户最终只看到"由于 Exception.ToString() 失败，因此无法打印异常字符串。"
                ReportException(ex);
            }
            finally {
                // 统一在这里清理临时目录（正常流程与 PreProcess 中途抛异常都走这里）。
                // PostProcess 内部会先检查目录是否存在，且 luaConfig 可能尚未赋值（Get 失败），故用 ?.
                try {
                    CSGlobal.luaConfig?.PostProcess();
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ImgsToPDFCore] Failed to clean up temp directory: {ex.Message}");
                }
                finally {
                    // 无论清理是否成功都置位：临时文件/解压目录的清理已经尝试过，
                    // 此时不该再被看门狗当成"清理未完成"而强制结束进程（那会让退出码退化成 1）。
                    CSGlobal.CleanupCompleted = true;
                }
                CSGlobal.luaEnv.Dispose();
            }
        }

        /// <summary>
        /// 上报异常。刻意不使用 ex.ToString()：异常可能来自 XLua，
        /// 其格式化依赖 Lua 状态，一旦状态不可用就会二次抛异常，反而什么信息都留不下。
        /// </summary>
        private static void ReportException(Exception ex) {
            try {
                Console.Error.WriteLine($"[ImgsToPDFCore] {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception) {
                Console.Error.WriteLine("[ImgsToPDFCore] Unhandled error (the error message is unavailable).");
            }
            Environment.ExitCode = 1;
        }

        /// <summary>
        /// 结果不变量校验：PreProcess 正常返回，就意味着本次已经产出了 PDF。
        /// 有些失败路径会让进程以 0 退出（Lua 里吞掉异常、漏调 ReportFailure 等），
        /// 上层 GUI 会把它当成"生成成功"，这里做最后一道兜底。
        /// 只认本次新写出的文件（比较写入时间），避免被上一次遗留的旧 PDF 蒙混过关。
        /// </summary>
        private static void VerifyPdfGenerated(DateTime startedUtc) {
            string expectedPath;
            try {
                expectedPath = CSGlobal.luaConfig?.PathToSave();
            }
            catch (Exception ex) {
                // 自定义 config.lua 的 PathToSave 可能在某些分支取不到值（例如合并分支没设置输出名），
                // 此时只提示、不判失败，避免误报
                Console.Error.WriteLine($"[ImgsToPDFCore] Could not determine the expected output path: {ex.Message}");
                return;
            }
            if (string.IsNullOrEmpty(expectedPath)) {
                return;
            }

            bool exists;
            try {
                exists = File.Exists(expectedPath);
                // 容忍文件系统时间戳精度，留 2 秒余量
                if (exists && File.GetLastWriteTimeUtc(expectedPath) >= startedUtc.AddSeconds(-2)) {
                    return;
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"[ImgsToPDFCore] Failed to inspect the output file '{expectedPath}': {ex.Message}");
                return;
            }

            // 已经上报过具体原因（例如"压缩包里没有图片"）时，不再补一条笼统的说明
            if (CommonUtils.FailureReported) {
                Environment.ExitCode = 1;
                return;
            }

            CommonUtils.ReportFailure(exists
                ? $"No new PDF was generated; the file at '{expectedPath}' is left over from an earlier run."
                : $"No PDF was generated at the expected path: {expectedPath}");
        }
    }
}
