using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImgsToPDFCore
{
    internal class CommonUtils
    {
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
        /// 解压缩(支持rar，zip)
        /// </summary>
        /// <param name="fromFilePath">待解压文件全路径</param>
        /// <param name="outFileDirectory">解压文件后目录</param>
        public static bool Decompress(string fromFilePath, string outFileDirectory) {
            using (var archive = ArchiveFactory.Open(fromFilePath)) {
                if (archive.Entries.Where(p => !p.IsDirectory).First().IsEncrypted) {
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
            using (var archive = ArchiveFactory.Open(fromFilePath, new ReaderOptions { Password = password })) {
                return ExtraArchive(archive, outFileDirectory);
            }
        }
    }
}
