using ImgsToPDF.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;

namespace ImgsToPDF {
    /// <summary>
    /// 文件系统与目录树扫描服务。
    /// 负责按自然排序规则进行路径比较，以非递归 DFS 显式栈遍历文件夹并过滤重解析点与受支持的图片类型。
    /// </summary>
    internal static class FolderScanner {
        /// <summary>
        /// 遵循 Windows 资源管理器自然排序规范的字符串比较器
        /// </summary>
        public sealed class NaturalPathComparer : IComparer<string> {
            public static readonly NaturalPathComparer Instance = new();
            public int Compare(string x, string y) {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                return NativeMethods.StrCmpLogicalW(x, y);
            }
        }

        /// <summary>
        /// 检查指定文件夹中是否包含至少一张受支持的图片文件。
        /// 使用 EnumerateFiles 惰性枚举，遇到第一张有效图片即返回 true，避免全量遍历产生的性能损耗。
        /// </summary>
        public static bool ContainsSupportedImage(string directoryPath) {
            try {
                return Directory.EnumerateFiles(directoryPath)
                    .Any(f => ImageContracts.ImageExtensions.Contains(Path.GetExtension(f)));
            }
            catch (Exception ex) {
                Debug.WriteLine($"[ImgsToPDF] Failed to enumerate files in '{directoryPath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 递归遍历目录树，收集所有包含受支持图片的文件夹路径。
        /// 具备以下防御与优化能力：
        /// 1. 采用栈式迭代（DFS）代替方法递归，彻底规避极端目录层级下的栈溢出风险（StackOverflowException）；
        /// 2. 检查并跳过重解析点（ReparsePoint，如 NTFS Junction、符号链接），同时维护规范化路径的已访问集合，杜绝死循环；
        /// 3. 全面捕获 UnauthorizedAccessException、SecurityException、PathTooLongException 及 IOException，
        ///    遇到无权限或损坏的系统/受限文件夹时跳过并记录调试日志，避免整次扫描任务直接崩溃；
        /// 4. 仅收集实际包含图片的文件夹，过滤空文件夹及纯文本/纯子文件夹目录，避免拉起无意义的 Core 进程或生成单页空白 PDF。
        /// </summary>
        public static List<string> RecursiveFolder(string rootPath, CancellationToken cancellationToken = default) {
            var imageDirs = new List<string>();
            if (string.IsNullOrWhiteSpace(rootPath)) {
                return imageDirs;
            }

            DirectoryInfo rootDirInfo;
            try {
                rootDirInfo = new DirectoryInfo(Path.GetFullPath(rootPath));
                if (!rootDirInfo.Exists) {
                    return imageDirs;
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"[ImgsToPDF] Invalid root path '{rootPath}': {ex.Message}");
                return imageDirs;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<DirectoryInfo>();
            stack.Push(rootDirInfo);

            while (stack.Count > 0) {
                cancellationToken.ThrowIfCancellationRequested();

                DirectoryInfo currentDir = stack.Pop();

                // 统一去除末尾路径分隔符，防止 "C:\Dir\" 与 "C:\Dir" 在 HashSet 中被视为不同路径
                string normalizedPath = currentDir.FullName.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

                if (!visited.Add(normalizedPath)) {
                    continue;
                }

                // 1. 若当前目录包含受支持的图片，则加入待处理列表
                if (ContainsSupportedImage(currentDir.FullName)) {
                    imageDirs.Add(currentDir.FullName);
                }

                // 2. 惰性枚举子目录并压栈
                try {
                    foreach (DirectoryInfo childFolder in currentDir.EnumerateDirectories()) {
                        try {
                            // 跳过重解析点（符号链接、Junction 联接点等），防止死循环
                            if ((childFolder.Attributes & FileAttributes.ReparsePoint) != 0) {
                                Debug.WriteLine($"[ImgsToPDF] Skipped reparse point directory: '{childFolder.FullName}'");
                                continue;
                            }
                            stack.Push(childFolder);
                        }
                        catch (Exception ex) {
                            Debug.WriteLine($"[ImgsToPDF] Failed to inspect child directory '{childFolder.FullName}': {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException || ex is IOException) {
                    Debug.WriteLine($"[ImgsToPDF] Skipped inaccessible subdirectories of '{currentDir.FullName}': {ex.Message}");
                }
                catch (Exception ex) {
                    Debug.WriteLine($"[ImgsToPDF] Unexpected error getting subdirectories of '{currentDir.FullName}': {ex.Message}");
                }
            }

            // 排序输出
            imageDirs.Sort(NaturalPathComparer.Instance);
            return imageDirs;
        }
    }
}
