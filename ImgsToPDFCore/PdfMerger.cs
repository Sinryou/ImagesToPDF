using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImgsToPDFCore {
    /// <summary>
    /// PDF 文件合并与书签树生成引擎。
    /// 支持基础合并、单层级书签合并及基于目录结构的多层级深度书签树合并。
    /// </summary>
    internal static class PdfMerger {
        /// <summary>
        /// 合并PDF文件
        /// </summary>
        /// <param name="inFiles">待合并文件列表</param>
        /// <param name="outFile">合并生成的文件名称</param>
        public static void PdfMerge(List<string> inFiles, string outFile) {
            CSGlobal.ThrowIfCancelled();
            // 1. 实例化比较器
            var comparer = new StringLenComparer();
            // 2. 调用 Sort 方法并传入比较器
            inFiles.Sort(comparer);
            AtomicFileWriter.WriteToFile(outFile, stream => {
                using var doc = new Document();
                using var pdf = new PdfCopy(doc, stream);
                doc.Open();
                inFiles.ForEach(file => {
                    CSGlobal.ThrowIfCancelled();
                    if (File.Exists(file)) {
                        using var reader = new PdfReader(file);
                        for (int i = 0; i < reader.NumberOfPages; i++) {
                            CSGlobal.ThrowIfCancelled();
                            var page = pdf.GetImportedPage(reader, i + 1);
                            pdf.AddPage(page);
                        }
                        // 读完即释放（与 PdfMergeWithDeepOutlines 一致）：让 PdfCopy 尽早写出并
                        // 释放该文件的已导入页对象，避免一次合并很多大 PDF 时内存峰值持续偏高
                        pdf.FreeReader(reader);
                    }
                });
            });
        }

        public static void PdfMergeWithHierarchicalOutlines(List<string> inFiles, string outFile) {
            CSGlobal.ThrowIfCancelled();
            // 1. 排序逻辑
            var comparer = new StringLenComparer();
            inFiles.Sort(comparer);

            // 用于缓存已经创建过的文件夹书签，避免重复创建
            var folderOutlineCache = new Dictionary<string, PdfOutline>();

            AtomicFileWriter.WriteToFile(outFile, stream => {
                using var doc = new Document();
                using var pdf = new PdfCopy(doc, stream);
                doc.Open();

                int currentPage = 1;
                PdfOutline root = pdf.RootOutline;

                foreach (var file in inFiles) {
                    CSGlobal.ThrowIfCancelled();
                    if (!File.Exists(file)) continue;

                    using var reader = new PdfReader(file);
                    int pageCount = reader.NumberOfPages;

                    // --- 核心逻辑：处理层级书签 ---

                    // 获取父文件夹名称 (例如 "第1话")
                    string folderName = Path.GetFileName(Path.GetDirectoryName(file));
                    // 获取文件名 (例如 "第1话.pdf")
                    string fileName = Path.GetFileNameWithoutExtension(file);

                    // 每个书签使用独立的 PdfAction（iTextSharp 的 action 实例不应被多个 outline 共享，
                    // 共享实例存在被重复注册/页码解析异常的风险）
                    PdfOutline parentNode = root;

                    // 如果文件夹名有效且不是根目录，则创建/获取一级书签
                    if (!string.IsNullOrEmpty(folderName)) {
                        if (!folderOutlineCache.ContainsKey(folderName)) {
                            // 创建一级目录节点，跳转到该文件夹第一个文件的第一页
                            var folderAction = PdfAction.GotoLocalPage(currentPage,
                                               new PdfDestination(PdfDestination.FITH), pdf);
                            var folderNode = new PdfOutline(root, folderAction, folderName);
                            folderOutlineCache[folderName] = folderNode;
                        }
                        parentNode = folderOutlineCache[folderName];
                    }

                    // 在父节点下创建具体文件的二级书签
                    // 如果文件名和文件夹名完全一样，可以考虑跳过这一级，直接用文件夹书签指向它
                    if (fileName != folderName) {
                        var fileAction = PdfAction.GotoLocalPage(currentPage,
                                           new PdfDestination(PdfDestination.FITH), pdf);
                        new PdfOutline(parentNode, fileAction, fileName);
                    }

                    // --- 书签逻辑结束 ---

                    // 复制页面
                    for (int i = 1; i <= pageCount; i++) {
                        CSGlobal.ThrowIfCancelled();
                        pdf.AddPage(pdf.GetImportedPage(reader, i));
                    }

                    currentPage += pageCount;
                    pdf.FreeReader(reader);
                }
            });
        }

        /// <summary>
        /// 手动实现相对路径获取（兼容 .NET Framework）
        /// </summary>
        public static string GetRelativePath(string rootPath, string fullPath) {
            // 确保路径以目录分隔符结尾，避免 abc 与 abcd 混淆
            if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())) {
                rootPath += Path.DirectorySeparatorChar;
            }

            Uri rootUri = new(rootPath);
            Uri fullUri = new(fullPath);

            // 计算相对路径
            Uri relativeUri = rootUri.MakeRelativeUri(fullUri);
            // 将 Uri 格式转回系统路径格式（处理斜杠方向和空格转义 %20）
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// 规范化目录路径：统一去掉末尾分隔符，便于作为字典的键（保留 "C:\" 这种盘符根）。
        /// </summary>
        public static string NormalizeDir(string path) {
            if (string.IsNullOrEmpty(path)) {
                return path;
            }
            string full;
            try {
                full = Path.GetFullPath(path);
            }
            catch (Exception) {
                full = path;
            }
            string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.EndsWith(":", StringComparison.Ordinal) ? trimmed + Path.DirectorySeparatorChar : trimmed;
        }

        /// <summary>
        /// 列出文件所在文件夹到 rootPath 之间（不含 rootPath 本身）的所有上级文件夹，从上层到下层。
        /// 文件不在 rootPath 之下时返回空列表（不为它建文件夹书签）。
        /// </summary>
        public static List<string> AncestorFolders(string rootPath, string filePath) {
            var folders = new List<string>();
            string root = NormalizeDir(rootPath);
            string dir = NormalizeDir(Path.GetDirectoryName(filePath));
            while (!string.IsNullOrEmpty(dir) && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) {
                folders.Add(dir);
                string parent = NormalizeDir(Path.GetDirectoryName(dir));
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase)) {
                    return [];   // 一直到盘符根都没遇到 root
                }
                dir = parent;
            }
            folders.Reverse();
            return folders;
        }

        /// <summary>
        /// 带多级书签地合并 PDF。
        /// 书签页码先由预扫描统一算好，文件夹书签锚定到"该文件夹自身 PDF 的首页"，
        /// 该文件夹没有同名 PDF 时才退化为"它子树里第一个文件的首页"。
        /// （旧实现用"第一次遇到该文件夹时的当前页"作锚点，排序把无数字目录排到前面时，
        /// 父文件夹书签会指到子文件夹的起始页，点进去完全不是那个章节。）
        /// </summary>
        public static void PdfMergeWithDeepOutlines(List<string> inFiles, string outFile, string rootPath) {
            CSGlobal.ThrowIfCancelled();
            inFiles.Sort(new StringLenComparer());
            string root = NormalizeDir(rootPath);

            // 1. 预扫描：算出每个源文件在合并结果中的起始页；读不出来的 PDF 直接跳过（单个坏文件不该毁掉整次合并）
            var plan = new List<(string File, int StartPage, int PageCount)>();
            int cursor = 1;
            foreach (var file in inFiles) {
                CSGlobal.ThrowIfCancelled();
                if (!File.Exists(file)) continue;
                try {
                    using var probe = new PdfReader(file);
                    int pageCount = probe.NumberOfPages;
                    if (pageCount <= 0) {
                        Console.Error.WriteLine($"[ImgsToPDFCore] Skipped empty PDF '{file}' while merging.");
                        continue;
                    }
                    plan.Add((file, cursor, pageCount));
                    cursor += pageCount;
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ImgsToPDFCore] Failed to read PDF '{file}': {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (plan.Count == 0) {
                CommonUtils.ReportFailure($"No readable PDF was found to merge under '{rootPath}'; no merged PDF was generated.");
                return;
            }

            // 2. 每个文件夹的书签锚点页
            var folderAnchor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, startPage, _) in plan) {
                foreach (var folder in AncestorFolders(root, file)) {
                    if (!folderAnchor.ContainsKey(folder)) {
                        folderAnchor[folder] = startPage;   // 先到先得 = 该子树里第一个文件的起始页
                    }
                }
                // 该文件就是所在文件夹的同名 PDF：以自身起始页为准，覆盖上面的子树推断值
                string dir = NormalizeDir(Path.GetDirectoryName(file));
                if (!string.IsNullOrEmpty(dir) &&
                    string.Equals(Path.GetFileNameWithoutExtension(file), Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)) {
                    folderAnchor[dir] = startPage;
                }
            }

            // 3. 写文件：先建书签树，再按顺序拷贝页面
            AtomicFileWriter.WriteToFile(outFile, stream => {
                using var doc = new Document();
                using var pdf = new PdfCopy(doc, stream);
                doc.Open();

                // 按"所在文件夹"分组，便于按页面顺序递归建树
                var filesByDir = plan
                    .GroupBy(p => NormalizeDir(Path.GetDirectoryName(p.File)), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(p => p.StartPage).ToList(), StringComparer.OrdinalIgnoreCase);
                var subFoldersByDir = folderAnchor.Keys
                    .Where(f => !string.Equals(f, root, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => NormalizeDir(Path.GetDirectoryName(f)), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                PdfOutline CreateOutline(PdfOutline parent, int page, string title) {
                    // 页码来自预扫描，一定 >= 1；GotoLocalPage 会预分配页引用，
                    // 因此可以在拷贝页面之前先把整棵书签树建好
                    PdfAction action = PdfAction.GotoLocalPage(Math.Max(1, page), new PdfDestination(PdfDestination.FITH), pdf);
                    return new PdfOutline(parent, action, title);
                }

                // 同一层内按锚点页排序建节点，书签面板的顺序就与正文顺序一致；
                // 递归进入子文件夹只会往子节点里加内容，不会打乱本层的顺序
                void BuildTree(PdfOutline parentNode, string dir) {
                    bool isRootLevel = string.Equals(dir, root, StringComparison.OrdinalIgnoreCase);
                    var entries = new List<(int Page, bool IsFolder, string Path)>();
                    if (subFoldersByDir.TryGetValue(dir, out var subFolders)) {
                        foreach (var folder in subFolders) {
                            entries.Add((folderAnchor[folder], true, folder));
                        }
                    }
                    if (filesByDir.TryGetValue(dir, out var files)) {
                        foreach (var (file, startPage, _) in files) {
                            // 文件名与所在文件夹同名时不再单独建文件书签：该文件夹书签已指向本文件首页
                            if (!isRootLevel &&
                                string.Equals(Path.GetFileNameWithoutExtension(file), Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)) {
                                continue;
                            }
                            entries.Add((startPage, false, file));
                        }
                    }
                    foreach (var entry in entries.OrderBy(e => e.Page)) {
                        if (entry.IsFolder) {
                            BuildTree(CreateOutline(parentNode, entry.Page, Path.GetFileName(entry.Path)), entry.Path);
                        }
                        else {
                            CreateOutline(parentNode, entry.Page, Path.GetFileNameWithoutExtension(entry.Path));
                        }
                    }
                }

                BuildTree(pdf.RootOutline, root);

                // 拷贝页面
                foreach (var (file, _, pageCount) in plan) {
                    CSGlobal.ThrowIfCancelled();
                    PdfReader reader;
                    try {
                        reader = new PdfReader(file);
                    }
                    catch (Exception ex) {
                        // 预扫描之后文件才出问题：页码已经排好，继续写会得到错位的书签，直接中止并丢弃临时文件
                        throw new InvalidOperationException($"Failed to reopen '{file}' while merging: {ex.Message}", ex);
                    }
                    using (reader) {
                        for (int i = 1; i <= pageCount; i++) {
                            CSGlobal.ThrowIfCancelled();
                            pdf.AddPage(pdf.GetImportedPage(reader, i));
                        }
                        pdf.FreeReader(reader);
                    }
                }
            });
        }
    }
}
