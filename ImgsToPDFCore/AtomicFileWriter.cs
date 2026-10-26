using System;
using System.IO;

namespace ImgsToPDFCore {
    /// <summary>
    /// 事务性 / 原子文件写出机制：
    /// 先写同目录下的临时文件（.tmp），全部成功后再原子替换目标文件，失败则自动清理临时文件。
    /// 避免中途失败产生半成品损坏旧文件，并捕获 IO 异常统一上报友好提示。
    /// </summary>
    internal static class AtomicFileWriter {
        /// <summary>写出 PDF 时使用的临时文件后缀：先写它，成功后再替换目标文件，失败则删除。</summary>
        public const string TempFileSuffix = ".tmp";

        /// <summary>
        /// 输出文件的统一写法：先写同目录下的临时文件，全部成功后再替换目标文件。好处：
        /// - 中途失败（磁盘满、编码异常等）不会在目标路径留下半成品 PDF，也不会破坏上一次生成的结果；
        /// - "无法写入"（被别的程序占用、只读、无权限）会变成一行可读的上报，
        ///   而不是让进程直接崩掉、用户只看到一句无意义的异常信息。
        /// </summary>
        /// <param name="pathToSave">最终输出路径</param>
        /// <param name="write">真正写文件的回调（内部需自行关闭 Document/PdfCopy 以刷新数据）</param>
        public static bool WriteToFile(string pathToSave, Action<FileStream> write) {
            string tempPath = pathToSave + TempFileSuffix;
            FileStream fs;
            try {
                fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
            }
            catch (Exception ex) {
                CommonUtils.ReportFailure($"Failed to create the output file '{pathToSave}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }
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
                if (!committed) {
                    DiscardOutputFile(tempPath);
                }
            }
        }

        /// <summary>
        /// 用临时文件替换最终输出。目标已存在时优先用 File.Replace（原子替换），
        /// 个别文件系统/网络共享不支持时退化为"删除后改名"。
        /// </summary>
        public static void CommitOutputFile(string tempPath, string finalPath) {
            if (File.Exists(finalPath)) {
                try {
                    File.Replace(tempPath, finalPath, null);
                    return;
                }
                catch (IOException) {
                    // 交给下面的"删除 + 改名"兜底（例如部分网络共享不支持 Replace）
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
