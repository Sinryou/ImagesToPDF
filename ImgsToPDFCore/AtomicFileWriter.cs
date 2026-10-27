using System;
using System.Collections.Concurrent;
using System.IO;

namespace ImgsToPDF.Core {
    /// <summary>
    /// 事务性 / 原子文件写出机制：
    /// 先写同目录下的临时文件（.tmp），全部成功后再原子替换目标文件，失败则自动清理临时文件。
    /// 避免中途失败产生半成品损坏旧文件，并捕获 IO 异常统一上报友好提示。
    /// </summary>
    internal static class AtomicFileWriter {
        /// <summary>写出 PDF 时使用的临时文件后缀：先写它，成功后再替换目标文件，失败则删除。</summary>
        public const string TempFileSuffix = ".tmp";

        /// <summary>
        /// 本次进程内正在写出的临时文件。用于进程被强杀（GUI 关闭、看门狗超时、未处理异常）时
        /// 在退出前统一清理，避免在用户图片目录留下 *.pdf.tmp。
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> ActiveTempFiles = new(StringComparer.OrdinalIgnoreCase);

        static AtomicFileWriter() {
            // 兜底：无论进程以何种方式退出（未处理异常、Environment.Exit），都尽量清掉写了一半的临时文件。
            // 看门狗（Program.StartParentWatchdogIfRequested）还会在强制退出前显式调用一次。
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupActiveTempFiles();
        }

        /// <summary>
        /// 输出文件的统一写法：先写同目录下的临时文件，全部成功后再替换目标文件。好处：
        /// - 中途失败（磁盘满、编码异常等）不会在目标路径留下半成品 PDF，也不会破坏上一次生成的结果；
        /// - “无法写入”（被别的程序占用、只读、无权限）会变成一行可读的上报，
        ///   而不是让进程直接崩掉、用户只看到一句无意义的异常信息。
        /// </summary>
        /// <param name="pathToSave">最终输出路径</param>
        /// <param name="write">真正写文件的回调（内部需自行关闭 Document/PdfCopy 以刷新数据）</param>
        public static bool WriteToFile(string pathToSave, Action<FileStream> write) {
            string tempPath = pathToSave + TempFileSuffix;
            FileStream fs;
            try {
                // FileShare.Delete 是关键：允许看门狗/进程退出时的清理逻辑在写入过程中就把临时文件
                // 标记为删除；随后进程被 Environment.Exit 强杀、句柄关闭时，系统会自动完成删除。
                // 不指定 Delete 的话，强杀路径下文件会一直残留（这正是之前 *.pdf.tmp 的来源）。
                fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
            }
            catch (Exception ex) {
                CommonUtils.ReportFailure($"Failed to create the output file '{pathToSave}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            ActiveTempFiles[tempPath] = 0;
            bool committed = false;
            try {
                write(fs);
                CSGlobal.ThrowIfCancelled();
                CommitOutputFile(tempPath, pathToSave);
                committed = true;
                return true;
            }
            catch (Exception ex) {
                CommonUtils.ReportFailure($"Failed to write the output file '{pathToSave}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally {
                // iText 的 Document/PdfCopy 关闭时会一并关闭流，这里重复 Dispose 无副作用
                fs.Dispose();
                ActiveTempFiles.TryRemove(tempPath, out _);
                if (!committed) {
                    DiscardOutputFile(tempPath);
                }
            }
        }

        /// <summary>
        /// 删除本次进程内所有尚未完成的临时文件。
        /// 供父进程看门狗与 ProcessExit 调用：即使主线程还卡在写文件/关闭文档上，
        /// 私有句柄带 FileShare.Delete，这里也能把文件标记为删除并让系统在进程退出时真正清掉。
        /// </summary>
        public static void CleanupActiveTempFiles() {
            foreach (string tempPath in ActiveTempFiles.Keys) {
                DiscardOutputFile(tempPath);
            }
            ActiveTempFiles.Clear();
        }

        /// <summary>
        /// 用临时文件替换最终输出。目标已存在时优先用 File.Replace（原子替换），
        /// 个别文件系统/网络共享不支持时退化为“删除后改名”。
        /// </summary>
        public static void CommitOutputFile(string tempPath, string finalPath) {
            if (File.Exists(finalPath)) {
                try {
                    File.Replace(tempPath, finalPath, null);
                    return;
                }
                catch (IOException) {
                    // 交给下面的“删除 + 改名”兜底（例如部分网络共享不支持 Replace、目标被阅读器占用）
                }
                catch (UnauthorizedAccessException) {
                    // 目标只读或 ACL 受限：同样尝试兜底，失败会在外层统一上报可读错误
                }
                catch (PlatformNotSupportedException) {
                }
                File.Delete(finalPath);
            }
            File.Move(tempPath, finalPath);
        }

        /// <summary>
        /// 删除失败时残留的临时文件（删除本身再失败也不抛异常，以免盖掉真正要上报的错误）
        /// </summary>
        public static void DiscardOutputFile(string tempPath) {
            try {
                if (File.Exists(tempPath)) {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[ImgsToPDFCore] Failed to delete temp file '{tempPath}': {ex.Message}");
            }
        }
    }
}
