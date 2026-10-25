using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ImgsToPDFCore
{
    internal class CommonUtils
    {
        /// <summary>
        /// 本次是否已经上报过失败。供 Program 的结果校验使用：
        /// 已经给出具体原因时就不必再补一条笼统的"没有生成 PDF"。
        /// </summary>
        public static bool FailureReported { get; private set; }

        /// <summary>
        /// 上报"本次没有生成任何 PDF"这类失败。
        /// 写 stderr（GUI 以此判定本次任务出错并提示用户）并设置非零退出码
        /// （供从命令行直接调用 ImgsToPDFCore.exe 的脚本判断）。
        /// 不调用它的话，什么都没生成也会被上层当成生成成功。
        /// 同时供 Lua（config.lua）调用。
        /// </summary>
        public static void ReportFailure(string message) {
            FailureReported = true;
            Console.Error.WriteLine("[ImgsToPDFCore] " + message);
            Environment.ExitCode = 1;
        }

        /// <summary>
        /// 上报不影响继续执行的问题原因（例如某个压缩包条目解压失败）。
        /// 只写 stderr：GUI 会把它作为"已生成但有跳过项"的提示展示。
        /// </summary>
        private static void ReportReason(string message) {
            Console.Error.WriteLine("[ImgsToPDFCore] " + message);
        }

        /// <summary>
        /// 枚举压缩包内的文件条目。Entries 每枚举一次就会重新解析一次压缩包，
        /// 因此只物化一次，后面判空、查加密、解压都复用这一份。
        /// </summary>
        private static bool TryGetFileEntries(IArchive archive, out List<IArchiveEntry> entries, out string error) {
            entries = null;
            error = null;
            try {
                entries = [.. archive.Entries.Where(e => !e.IsDirectory)];
            }
            catch (Exception ex) {
                error = $"failed to read the archive entries: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
            if (entries.Count == 0) {
                error = "the archive contains no file entry";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 把文件条目解压到指定目录。失败时通过 error 给出第一个失败条目的具体原因，
        /// 而不是像原先那样只返回 false 把原因丢掉（上层只能猜成"密码为空或错误"）。
        /// </summary>
        private static bool ExtraArchive(IReadOnlyList<IArchiveEntry> entries, string outFileDirectory, out string error) {
            error = null;
            try {
                Directory.CreateDirectory(outFileDirectory);
            }
            catch (Exception ex) {
                error = $"failed to create the extraction folder '{outFileDirectory}': {ex.GetType().Name}: {ex.Message}";
                return false;
            }
            foreach (var entry in entries) {
                CSGlobal.ThrowIfCancelled();
                // 不再用 entry.Size > 0 过滤：部分格式（流式 tar 等）取不到条目大小，
                // 过滤会把正常文件（包括图片）静默丢掉
                try {
                    entry.WriteToDirectory(outFileDirectory, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                }
                catch (Exception ex) {
                    error = $"failed to extract entry '{entry.Key}': {ex.GetType().Name}: {ex.Message}";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 打开压缩包；文件不存在、格式不受支持（OpenArchive 返回 null）或打开异常时返回 null，
        /// 并通过 error 给出原因（原先这些原因被整体吞掉，IO 错误与"格式不支持"无法区分）
        /// </summary>
        private static IArchive TryOpenArchive(string fromFilePath, ReaderOptions options, out string error) {
            error = null;
            if (string.IsNullOrEmpty(fromFilePath)) {
                error = "no archive path was provided";
                return null;
            }
            if (!File.Exists(fromFilePath)) {
                error = $"the archive does not exist: {fromFilePath}";
                return null;
            }
            try {
                return options == null
                    ? ArchiveFactory.OpenArchive(fromFilePath)
                    : ArchiveFactory.OpenArchive(fromFilePath, options);
            }
            catch (Exception ex) {
                error = $"the archive could not be opened ({ex.GetType().Name}: {ex.Message}): {fromFilePath}";
                return null;
            }
        }

        /// <summary>
        /// 判断压缩包内是否含加密条目；不支持查询加密标志的实现返回 false（走密码流程兜底）
        /// </summary>
        private static bool ContainsEncryptedEntry(IArchive archive) {
            try {
                return archive.Entries.Any(e => !e.IsDirectory && e.IsEncrypted);
            }
            catch (NotImplementedException) {
                // 无法判断时按"未加密"处理：先直接解压，若确实加密会在解压阶段失败，
                // 调用方（config.lua）随后仍会进入密码输入流程兜底
                return false;
            }
        }

        /// <summary>
        /// 压缩包是否真的需要密码。供 config.lua 区分"解压失败"到底该不该向用户索要密码：
        /// 原先只要解压失败就会弹密码框，把"条目非法""磁盘错误"等情况误报成"密码错误"。
        /// 判断不出来时返回 false（不弹框，直接把真实原因上报为失败）。
        /// </summary>
        public static bool IsArchiveEncrypted(string fromFilePath) {
            CSGlobal.ThrowIfCancelled();
            var archive = TryOpenArchive(fromFilePath, null, out string openError);
            if (archive == null) {
                ReportReason(openError);
                return false;
            }
            using (archive) {
                return ContainsEncryptedEntry(archive);
            }
        }

        /// <summary>
        /// 解压缩(支持rar，zip)
        /// </summary>
        /// <param name="fromFilePath">待解压文件全路径</param>
        /// <param name="outFileDirectory">解压文件后目录</param>
        public static bool Decompress(string fromFilePath, string outFileDirectory) {
            var archive = TryOpenArchive(fromFilePath, null, out string openError);
            if (archive == null) {
                ReportReason(openError);
                return false;
            }
            using (archive) {
                // 加密包返回 false，由调用方（config.lua）走密码流程；
                // 是否真的需要密码由 IsArchiveEncrypted 判断，避免误报
                if (ContainsEncryptedEntry(archive)) {
                    // ReportReason($"the archive is encrypted, a password is required: {fromFilePath}");
                    return false;
                }
                if (!TryGetFileEntries(archive, out var entries, out string entryError)) {
                    ReportReason($"failed to extract '{fromFilePath}': {entryError}");
                    return false;
                }
                if (ExtraArchive(entries, outFileDirectory, out string error)) {
                    return true;
                }
                ReportReason($"failed to extract '{fromFilePath}': {error}");
                return false;
            }
        }

        /// <summary>
        /// 解压缩加密的包(不支持rar，支持zip)
        /// </summary>
        /// <param name="fromFilePath">待解压文件全路径</param>
        /// <param name="outFileDirectory">解压文件后目录</param>
        /// <param name="password">密码</param>
        public static bool Decompress(string fromFilePath, string outFileDirectory, string password) {
            var archive = TryOpenArchive(fromFilePath, new ReaderOptions { Password = password }, out string openError);
            if (archive == null) {
                ReportReason(openError);
                return false;
            }
            using (archive) {
                if (!TryGetFileEntries(archive, out var entries, out string entryError)) {
                    ReportReason($"failed to extract '{fromFilePath}' with the given password: {entryError}");
                    return false;
                }
                if (ExtraArchive(entries, outFileDirectory, out string error)) {
                    return true;
                }
                ReportReason($"failed to extract '{fromFilePath}' with the given password: {error}");
                return false;
            }
        }

        /// <summary>
        /// 请求解压密码。
        /// 若由 GUI 调起，通过 stdout 向 GUI 发起协议请求并等待回传；
        /// 若由命令行直接运行，在控制台中提示输入密码（支持终端掩码输入）。
        /// 返回 null 或空字符串表示用户取消或未提供密码。
        /// </summary>
        public static string PromptPassword(string archivePath) {
            CSGlobal.ThrowIfCancelled();
            if (!CSGlobal.IsGuiMode) {
                return PromptConsolePassword(archivePath);
            }

            // GUI 模式：向标准输出写入约定的密码请求指令
            Console.WriteLine($"::__REQ_PASSWORD__::{archivePath}");
            Console.Out.Flush();

            // 等待 GUI 回传（最长等待 10 分钟）
            string response = CSGlobal.WaitForStdinLine(TimeSpan.FromMinutes(10));
            CSGlobal.ThrowIfCancelled();

            if (string.IsNullOrEmpty(response) || response == "::__CANCEL__::") {
                return null;
            }

            const string prefix = "::__RESP_PASSWORD__::";
            if (response.StartsWith(prefix)) {
                string base64 = response.Substring(prefix.Length);
                try {
                    byte[] bytes = Convert.FromBase64String(base64);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ImgsToPDFCore] Failed to decode password from GUI: {ex.Message}");
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// 在终端/控制台提示用户输入密码。
        /// 若处于非重定向的交互式控制台，支持 '*' 掩码输入、退格修正及 Enter 提交。
        /// 若输入被重定向（如自动化脚本/管道），回退到 Console.ReadLine()。
        /// </summary>
        private static string PromptConsolePassword(string archivePath) {
            CSGlobal.ThrowIfCancelled();
            string archiveName = Path.GetFileName(archivePath);

            if (Console.IsInputRedirected) {
                Console.Error.WriteLine($"[ImgsToPDFCore] Reading password for '{archiveName}' from redirected standard input...");
                return Console.ReadLine();
            }

            Console.Write($"Enter password for '{archiveName}': ");
            var sb = new StringBuilder();
            while (true) {
                CSGlobal.ThrowIfCancelled();
                ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
                if (keyInfo.Key == ConsoleKey.Enter) {
                    Console.WriteLine();
                    break;
                }
                if (keyInfo.Key == ConsoleKey.Escape) {
                    Console.WriteLine();
                    return null; // 用户取消
                }
                if (keyInfo.Key == ConsoleKey.Backspace) {
                    if (sb.Length > 0) {
                        sb.Length--;
                        Console.Write("\b \b");
                    }
                }
                else if (!char.IsControl(keyInfo.KeyChar)) {
                    sb.Append(keyInfo.KeyChar);
                    Console.Write("*");
                }
            }
            return sb.ToString();
        }
    }
}
