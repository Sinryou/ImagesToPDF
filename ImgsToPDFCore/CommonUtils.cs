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
        /// 检查异常是否与密码缺失或加密解密失败相关（含 SharpCompress 的 CryptographicException 及嵌套异常）
        /// </summary>
        private static bool IsCryptographicException(Exception ex) {
            for (Exception cur = ex; cur != null; cur = cur.InnerException) {
                if (cur is SharpCompress.Common.CryptographicException ||
                    cur is System.Security.Cryptography.CryptographicException) {
                    return true;
                }
                string msg = cur.Message;
                if (!string.IsNullOrEmpty(msg) &&
                    (msg.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     msg.IndexOf("encrypt", StringComparison.OrdinalIgnoreCase) >= 0)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 枚举压缩包内的文件条目。Entries 每枚举一次就会重新解析一次压缩包，
        /// 因此只物化一次，后面判空、查加密、解压都复用这一份。
        /// </summary>
        private static bool TryGetFileEntries(IArchive archive, out List<IArchiveEntry> entries, out string error, out bool isPasswordRequired) {
            entries = null;
            error = null;
            isPasswordRequired = false;
            try {
                entries = [.. archive.Entries.Where(e => !e.IsDirectory)];
            }
            catch (Exception ex) {
                isPasswordRequired = IsCryptographicException(ex);
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
        private static bool ExtraArchive(IReadOnlyList<IArchiveEntry> entries, string outFileDirectory, out string error, out bool isPasswordRequired) {
            error = null;
            isPasswordRequired = false;
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
                    isPasswordRequired = IsCryptographicException(ex);
                    error = $"failed to extract entry '{entry.Key}': {ex.GetType().Name}: {ex.Message}";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 打开压缩包；文件不存在、格式不受支持（OpenArchive 返回 null）或打开异常时返回 null，
        /// 并通过 error 给出原因（若遇到头部加密无法打开，isPasswordRequired 将被置为 true）。
        /// </summary>
        private static IArchive TryOpenArchive(string fromFilePath, ReaderOptions options, out string error, out bool isPasswordRequired) {
            error = null;
            isPasswordRequired = false;
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
                if (IsCryptographicException(ex)) {
                    isPasswordRequired = true;
                    error = $"the archive is encrypted and requires a password: {fromFilePath}";
                }
                else {
                    error = $"the archive could not be opened ({ex.GetType().Name}: {ex.Message}): {fromFilePath}";
                }
                return null;
            }
        }

        /// <summary>
        /// 判断压缩包内是否含加密条目；支持检查 IArchive.IsEncrypted 属性及条目级加密。
        /// </summary>
        private static bool ContainsEncryptedEntry(IArchive archive) {
            try {
                if (archive.IsEncrypted) {
                    return true;
                }
                return archive.Entries.Any(e => !e.IsDirectory && e.IsEncrypted);
            }
            catch (Exception ex) when (IsCryptographicException(ex)) {
                return true;
            }
            catch (NotImplementedException) {
                // 无法判断时按"未加密"处理：先直接解压，若确实加密会在解压阶段失败，
                // 调用方（config.lua）随后仍会进入密码输入流程兜底
                return false;
            }
        }

        /// <summary>
        /// 压缩包是否真的需要密码（支持识别条目级加密以及 RAR/7z 头部加密 Header Encryption）。
        /// 供 config.lua 区分"解压失败"到底该不该向用户索要密码：
        /// 原先若为头部加密，无密码打开直接抛异常失败，会被当成损坏文件而跳过密码输入。
        /// </summary>
        public static bool IsArchiveEncrypted(string fromFilePath) {
            CSGlobal.ThrowIfCancelled();
            var archive = TryOpenArchive(fromFilePath, null, out string openError, out bool isPasswordRequired);
            if (isPasswordRequired) {
                return true; // 头部加密：打开时即确定需要密码
            }
            if (archive == null) {
                ReportReason(openError);
                return false;
            }
            using (archive) {
                return ContainsEncryptedEntry(archive);
            }
        }

        /// <summary>
        /// 解压缩（支持 zip、rar、7z 等格式）
        /// </summary>
        /// <param name="fromFilePath">待解压文件全路径</param>
        /// <param name="outFileDirectory">解压文件后目录</param>
        public static bool Decompress(string fromFilePath, string outFileDirectory) {
            var archive = TryOpenArchive(fromFilePath, null, out string openError, out bool isPasswordRequired);
            if (isPasswordRequired) {
                // 头部加密包，打开即需密码：直接返回 false，由调用方（config.lua）走密码流程，无需提前报错
                return false;
            }
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
                if (!TryGetFileEntries(archive, out var entries, out string entryError, out bool entryPwdRequired)) {
                    if (entryPwdRequired) {
                        return false;
                    }
                    ReportReason($"failed to extract '{fromFilePath}': {entryError}");
                    return false;
                }
                if (ExtraArchive(entries, outFileDirectory, out string error, out bool extractPwdRequired)) {
                    return true;
                }
                if (extractPwdRequired) {
                    return false;
                }
                ReportReason($"failed to extract '{fromFilePath}': {error}");
                return false;
            }
        }

        /// <summary>
        /// 解压缩加密的包（支持 zip、rar、7z 等格式）
        /// </summary>
        /// <param name="fromFilePath">待解压文件全路径</param>
        /// <param name="outFileDirectory">解压文件后目录</param>
        /// <param name="password">密码</param>
        public static bool Decompress(string fromFilePath, string outFileDirectory, string password) {
            var archive = TryOpenArchive(fromFilePath, new ReaderOptions { Password = password }, out string openError, out _);
            if (archive == null) {
                ReportReason(openError);
                return false;
            }
            using (archive) {
                if (!TryGetFileEntries(archive, out var entries, out string entryError, out _)) {
                    ReportReason($"failed to extract '{fromFilePath}' with the given password: {entryError}");
                    return false;
                }
                if (ExtraArchive(entries, outFileDirectory, out string error, out _)) {
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

        /// <summary>
        /// 图片清单解析结果
        /// </summary>
        public class ManifestData {
            public string OutputDir { get; set; }
            public string PdfFileName { get; set; }
            public string[] ImagePaths { get; set; }
        }

        /// <summary>
        /// 检查指定路径是否为图片清单文件（.txt 或 .list 格式且文件存在）
        /// </summary>
        public static bool IsManifestFile(string path) {
            if (string.IsNullOrEmpty(path)) return false;
            try {
                if (!File.Exists(path)) return false;
                string ext = Path.GetExtension(path);
                return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(ext, ".list", StringComparison.OrdinalIgnoreCase);
            }
            catch {
                return false;
            }
        }

        /// <summary>
        /// 解析图片清单文件。
        /// 支持通过 #OUTPUT: 自定义目标输出 PDF 路径；
        /// 其余非空、非以 # 开头的行视为图片绝对路径。
        /// 若未显式指定 #OUTPUT:，则以首张图片所在目录名作为 PDF 文件名。
        /// </summary>
        public static ManifestData ParseManifest(string manifestPath) {
            if (!IsManifestFile(manifestPath)) return null;

            string[] lines;
            try {
                lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"[ImgsToPDFCore] Failed to read manifest '{manifestPath}': {ex.Message}");
                return null;
            }

            string customOutput = null;
            var images = new List<string>();

            foreach (var rawLine in lines) {
                var line = rawLine?.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("#OUTPUT:", StringComparison.OrdinalIgnoreCase)) {
                    customOutput = line.Substring("#OUTPUT:".Length).Trim();
                    continue;
                }
                if (line.StartsWith("#")) continue;

                if (File.Exists(line)) {
                    images.Add(line);
                }
                else {
                    Console.Error.WriteLine($"[ImgsToPDFCore] Manifest image not found, skipped: {line}");
                }
            }

            if (images.Count == 0) return null;

            string outputDir;
            string pdfFileName;

            if (!string.IsNullOrEmpty(customOutput)) {
                outputDir = Path.GetDirectoryName(customOutput);
                pdfFileName = Path.GetFileNameWithoutExtension(customOutput);
            }
            else {
                string firstImg = images[0];
                outputDir = Path.GetDirectoryName(firstImg);
                pdfFileName = Path.GetFileName(outputDir);
                if (string.IsNullOrEmpty(pdfFileName)) {
                    pdfFileName = Path.GetFileNameWithoutExtension(firstImg);
                }
            }

            return new ManifestData {
                OutputDir = outputDir,
                PdfFileName = pdfFileName,
                ImagePaths = [.. images]
            };
        }
    }
}
