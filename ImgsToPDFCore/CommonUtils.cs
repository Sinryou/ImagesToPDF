using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;
using System.Linq;

namespace ImgsToPDFCore
{
    internal class CommonUtils
    {
        /// <summary>
        /// 上报"本次没有生成任何 PDF"这类失败。
        /// 写 stderr（GUI 以此判定本次任务出错并提示用户）并设置非零退出码
        /// （供从命令行直接调用 ImgsToPDFCore.exe 的脚本判断）。
        /// 不调用它的话，什么都没生成也会被上层当成生成成功。
        /// 同时供 Lua（config.lua）调用。
        /// </summary>
        public static void ReportFailure(string message) {
            Console.Error.WriteLine("[ImgsToPDFCore] " + message);
            Environment.ExitCode = 1;
        }
        private static bool ExtraArchive(IArchive archive, string outFileDirectory) {
            if (!archive.Entries.Any()) { return false; }
            Directory.CreateDirectory(outFileDirectory);
            bool result = true;
            foreach (var entry in archive.Entries) {
                if (!entry.IsDirectory && entry.Size > 0) {
                    try {
                        entry.WriteToDirectory(outFileDirectory, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                    }
                    catch (Exception) {
                        result = false;
                        break;
                    }
                }
            }
            return result;
        }
        /// <summary>
        /// 打开压缩包；文件不存在、格式不受支持（OpenArchive 返回 null）或打开异常时返回 null
        /// </summary>
        private static IArchive TryOpenArchive(string fromFilePath, ReaderOptions options = null) {
            if (string.IsNullOrEmpty(fromFilePath) || !File.Exists(fromFilePath)) {
                return null;
            }
            try {
                return options == null
                    ? ArchiveFactory.OpenArchive(fromFilePath)
                    : ArchiveFactory.OpenArchive(fromFilePath, options);
            }
            catch (Exception) {
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
        /// 解压缩(支持rar，zip)
        /// </summary>
        /// <param name="fromFilePath">待解压文件全路径</param>
        /// <param name="outFileDirectory">解压文件后目录</param>
        public static bool Decompress(string fromFilePath, string outFileDirectory) {
            var archive = TryOpenArchive(fromFilePath);
            if (archive == null) {
                return false;
            }
            using (archive) {
                // 原实现用 First() 取第一个条目，空压缩包（只有目录条目）会抛 InvalidOperationException；
                // 且只检查第一个条目的加密标志，这里改为检查全部条目
                if (ContainsEncryptedEntry(archive)) {
                    return false;
                }
                return ExtraArchive(archive, outFileDirectory);
            }
        }
        /// <summary>
        /// 解压缩加密的包(不支持rar，支持zip)
        /// </summary>
        /// <param name="fromFilePath">待解压文件全路径</param>
        /// <param name="outFileDirectory">解压文件后目录</param>
        /// <param name="password">密码</param>
        public static bool Decompress(string fromFilePath, string outFileDirectory, string password) {
            var archive = TryOpenArchive(fromFilePath, new ReaderOptions { Password = password });
            if (archive == null) {
                return false;
            }
            using (archive) {
                return ExtraArchive(archive, outFileDirectory);
            }
        }
    }
}
