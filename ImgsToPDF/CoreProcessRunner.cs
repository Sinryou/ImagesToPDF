using ImgsToPDF.Shared;
using ImgsToPDF.Localization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImgsToPDF {
    /// <summary>
    /// ImgsToPDFCore 子进程调度与跨进程通信（IPC）服务。
    /// 负责按可用物理内存及 CPU 核心动态并发拉起 Core 进程、注入父进程看门狗管道、
    /// 拦截密码输入请求并通过标准输入/输出完成 IPC 交互。
    /// </summary>
    internal static class CoreProcessRunner {
        /// <summary>
        /// 同时运行的 Core 进程数硬上限：每个进程都是完整 .NET 运行时，
        /// 高核数机器上不设上限会一次拉起几十个进程。
        /// </summary>
        public const int MaxCoreProcessConcurrency = 8;

        /// <summary>
        /// 单个 Core 进程的峰值内存预算（含 .NET 运行时、XLua/libwebp、
        /// 最大单张图片解码及编码缓冲）。按大图最坏情况估算，实际峰值通常低于此值。
        /// </summary>
        public const long PerCoreProcessMemoryBudget = 1L << 30; // 1 GB

        /// <summary>
        /// 按当前可用物理内存估算允许同时运行的 Core 进程数，
        /// 图片很大时避免同时解码过多大图导致内存耗尽。
        /// </summary>
        public static int GetConcurrencyLimitByMemory() {
            long availPhys = NativeMethods.GetAvailablePhysicalMemory();
            if (availPhys == long.MaxValue) {
                return int.MaxValue;
            }
            return (int)Math.Max(1, availPhys / PerCoreProcessMemoryBudget);
        }

        /// <summary>
        /// 综合 CPU 核心数、任务数与可用内存计算最大并发度
        /// </summary>
        public static int CalculateMaxConcurrency(int taskCount) {
            return Math.Max(1, Math.Min(
                Math.Min(Environment.ProcessorCount, taskCount),
                Math.Min(GetConcurrencyLimitByMemory(), MaxCoreProcessConcurrency)));
        }

        /// <summary>
        /// 构造传给 Core 进程的命令行参数
        /// </summary>
        public static string[] BuildCoreArgs(string dirPath, bool fastMode, Layout layout, bool mergePdfs = false) {
            if (mergePdfs) {
                return ["-d", dirPath, "--merge-pdfs"];
            }
            var args = new List<string> { "-d", dirPath, "-l", layout.ToString() };
            if (fastMode) {
                args.Add("--fast");
            }
            return [.. args];
        }

        /// <summary>
        /// 归类一次 Core 进程的执行结果。
        /// 失败（退出码非 0）：stderr 为空时补上退出码，避免弹出内容为空的对话框；
        /// 警告（退出码为 0 但 stderr 有内容）：例如个别图片解码失败被跳过，PDF 本身已生成。
        /// 两种情况都会带上对应的目录/压缩包路径，并发处理多个目录时才能定位是谁出的问题。
        /// </summary>
        public static void CollectProcessResult(string targetPath, string stderr, int exitCode, ConcurrentQueue<string> failureQueue, ConcurrentQueue<string> warningQueue) {
            string detail = stderr == null ? string.Empty : stderr.Trim();
            if (exitCode != 0) {
                if (detail.Length == 0) {
                    detail = string.Format(Strings.NoErrorOutput, exitCode);
                }
                failureQueue.Enqueue(targetPath + Environment.NewLine + detail);
            }
            else if (detail.Length > 0) {
                warningQueue.Enqueue(targetPath + Environment.NewLine + detail);
            }
        }

        /// <summary>
        /// 在后台生成 PDF；错误通过返回值收集，统一回到 UI 线程展示。
        /// 判定标准：Core 进程退出码非 0 才算失败；退出码为 0 但 stderr 有内容
        /// （例如个别图片无法解码被跳过）只算警告，不再把成功当成失败上报。
        /// </summary>
        public static async Task<(List<string> failures, List<string> warnings)> ExecuteBatchAsync(
            string directoryPath,
            bool recursive,
            bool fastMode,
            bool merge,
            Layout layout,
            string displayTargetPath,
            Func<string, Task<string>> promptPasswordAsync,
            Func<bool> isCancelled) {

            var fileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core", "ImgsToPDFCore.exe");
            var failureQueue = new ConcurrentQueue<string>();
            var warningQueue = new ConcurrentQueue<string>();
            string reportedTarget = displayTargetPath ?? directoryPath;

            if (recursive && Directory.Exists(directoryPath)) {
                // 递归收集子目录可能较慢（大量文件夹），放到后台执行
                var dirs = await Task.Run(() => FolderScanner.RecursiveFolder(directoryPath));
                if (dirs.Count == 0) {
                    failureQueue.Enqueue(directoryPath + Environment.NewLine + Strings.NoImagesFound);
                    return ([.. failureQueue], [.. warningQueue]);
                }

                int maxConcurrency = CalculateMaxConcurrency(dirs.Count);
                using var semaphore = new SemaphoreSlim(maxConcurrency);
                var tasks = dirs.Select(async dirPath => {
                    await semaphore.WaitAsync();
                    try {
                        if (isCancelled()) return;
                        var (_, stderr, exitCode) = await RunProcessAsync(fileName, BuildCoreArgs(dirPath, fastMode, layout), promptPasswordAsync);
                        CollectProcessResult(dirPath, stderr, exitCode, failureQueue, warningQueue);
                    }
                    finally {
                        semaphore.Release();
                    }
                });
                await Task.WhenAll(tasks);

                if (merge && !isCancelled()) {
                    var (_, stderr, exitCode) = await RunProcessAsync(fileName, BuildCoreArgs(directoryPath, fastMode: false, layout: Layout.Single, mergePdfs: true), promptPasswordAsync);
                    CollectProcessResult(directoryPath, stderr, exitCode, failureQueue, warningQueue);
                }
            }
            else if (!isCancelled()) {
                var (_, stderr, exitCode) = await RunProcessAsync(fileName, BuildCoreArgs(directoryPath, fastMode, layout), promptPasswordAsync);
                CollectProcessResult(reportedTarget, stderr, exitCode, failureQueue, warningQueue);
            }
            return ([.. failureQueue], [.. warningQueue]);
        }

        /// <summary>
        /// 运行给定的命令，返回得到的标准输出、标准错误及退出码。
        /// 监听标准输出并拦截密码请求指令（::__REQ_PASSWORD__::），通过回调通知 UI 线程弹出密码输入框并通过标准输入回传。
        /// </summary>
        /// <param name="fileName">需要运行的指令</param>
        /// <param name="args">命令行参数列表</param>
        /// <param name="promptPasswordAsync">密码输入交互回调</param>
        /// <returns>元组：(stdout: 标准输出, stderr: 标准错误, exitCode: 进程退出码)</returns>
        public static async Task<(string stdout, string stderr, int exitCode)> RunProcessAsync(
            string fileName,
            string[] args,
            Func<string, Task<string>> promptPasswordAsync) {

            using Process p = new();
            p.StartInfo.FileName = fileName;
            p.StartInfo.SetArguments(args);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;

            // 必须与 Core 内 Console.OutputEncoding 的设置保持一致：
            // 不指定时 .NET Framework 用 Encoding.Default（ANSI 码页）解码，
            // 而 Core 输出的是 UTF-8，含中文的路径会在错误提示里变成乱码。
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            p.StartInfo.StandardOutputEncoding = utf8NoBom;
            p.StartInfo.StandardErrorEncoding = utf8NoBom;

            // 显式告知 Core「我是 GUI 拉起的」，它才会开启父进程看门狗：
            // 该看门狗把 stdin 当作父进程存活管道，一旦读到 EOF 就取消任务并在宽限期后自毁，
            // 避免 GUI 退出后残留孤儿进程。
            p.StartInfo.EnvironmentVariables[IpcContracts.WatchParentEnvVar] = "1";

            p.Start();

            // 异步读取 stdout，拦截并处理密码请求指令；普通行追加到 stdoutBuffer
            var stdoutSb = new StringBuilder();
            var stdoutTask = Task.Run(async () => {
                
                try {
                    while (true) {
                        string line = await p.StandardOutput.ReadLineAsync();
                        if (line == null) {
                            break;
                        }
                        if (line.StartsWith(IpcContracts.ReqPasswordPrefix, StringComparison.Ordinal)) {
                            string archivePath = line.Substring(IpcContracts.ReqPasswordPrefix.Length);
                            string password = promptPasswordAsync != null ? await promptPasswordAsync(archivePath) : null;
                            try {
                                if (password != null) {
                                    byte[] bytes = Encoding.UTF8.GetBytes(password);
                                    string base64 = Convert.ToBase64String(bytes);
                                    await p.StandardInput.WriteLineAsync($"{IpcContracts.RespPasswordPrefix}{base64}");
                                    await p.StandardInput.FlushAsync();
                                }
                                else {
                                    await p.StandardInput.WriteLineAsync(IpcContracts.CancelToken);
                                    await p.StandardInput.FlushAsync();
                                }
                            }
                            catch {
                                // 进程已退出或管道已关闭
                            }
                        }
                        else {
                            stdoutSb.AppendLine(line);
                        }
                    }
                }
                catch {
                    // 读取异常（如子进程被强制终止导致管道损坏）
                }
                return stdoutSb.ToString();
            });

            var stderrTask = p.StandardError.ReadToEndAsync();
            var outputs = await Task.WhenAll(stdoutTask, stderrTask);

            // 适配 .NET Framework：使用 Task.Run 包装同步的 WaitForExit
            await Task.Run(() => p.WaitForExit());

            return (outputs[0], outputs[1], p.ExitCode);
        }
    }
}
