using CommandLine;
using System;
using System.IO;
using XLua;

namespace ImgsToPDFCore {
    internal class Program {
        /// <summary>
        /// 程序的所有命令行参数类型
        /// </summary>
        private class Options {
            [Option('d', "dir-path", Required = true, HelpText = "图片所在的文件夹路径。")]
            public string DirectoryPath { get; set; }

            [Option('l', "layout", Required = false, HelpText = "页面布局，0为单页输出，1为双页左至右，2为双页右至左。")]
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

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Run)
                .WithNotParsed(_ => {
                    Environment.ExitCode = 1;
                });
        }
        /// <summary>
        /// 使用解析后的命令行参数进行操作。
        /// </summary>
        /// <param name="option">解析后的参数</param>
        static void Run(Options option) {
            var startedUtc = DateTime.UtcNow;
            try {
                CSGlobal.luaEnv.AddBuildin("ffi", XLua.LuaDLL.Lua.LoadFFI);
                CSGlobal.luaEnv.AddBuildin("lfs", XLua.LuaDLL.Lua.LoadLFS);
                CSGlobal.luaEnv.AddBuildin("rapidjson", XLua.LuaDLL.Lua.LoadRapidJson);

                CSGlobal.luaEnv.DoString(@"config = require 'config';"); // 获取lua内的方法

                CSGlobal.luaConfig = CSGlobal.luaEnv.Global.Get<IConfig>("config");
                CSGlobal.luaConfig.PreProcess(option.DirectoryPath, option.Layout, option.FastFlag, option.Merge);

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
