using System.Collections.Generic;

namespace ImgsToPDF.Core {
    public enum Layout {
        Single,
        DuplexLeftToRight,
        DuplexRightToLeft
    }

    /// <summary>
    /// PDF 操作的外观门面（Facade）。
    /// 保留原有的全部公开接口与签名，供 XLua（config.lua）及程序其他部分直接调用，
    /// 内部逻辑已全面委派至专用子系统（PdfComposer、PdfMerger、PdfImagePipeline、AtomicFileWriter）。
    /// </summary>
    internal class PDFWrapper {
        /// <summary>
        /// 将指定文件夹下的图片合并为 PDF 文件
        /// </summary>
        public static void ImagesToPDF(string directoryPath, Layout layout = Layout.Single, bool fastFlag = false) {
            PdfComposer.ImagesToPDF(directoryPath, layout, fastFlag);
        }

        /// <summary>
        /// 把多个文件夹（按给定顺序）里的图片依次写入同一个 PDF，返回实际写入的页数。
        /// </summary>
        public static int ImagesToPDFFromFolders(string[] directoryPaths, Layout layout = Layout.Single, bool fastFlag = false) {
            return PdfComposer.ImagesToPDFFromFolders(directoryPaths, layout, fastFlag);
        }

        /// <summary>
        /// 把指定的图片文件列表写入同一个 PDF，返回实际写入的页数。
        /// </summary>
        public static int ImagesToPDFFromImageFiles(string[] imageFiles, Layout layout = Layout.Single, bool fastFlag = false) {
            return PdfComposer.ImagesToPDFFromImageFiles(imageFiles, layout, fastFlag);
        }

        /// <summary>
        /// 基础合并 PDF 文件
        /// </summary>
        public static void PdfMerge(List<string> inFiles, string outFile) {
            PdfMerger.PdfMerge(inFiles, outFile);
        }

        /// <summary>
        /// 基础合并 PDF 文件（兼容数组入参）
        /// </summary>
        public static void PdfMerge(string[] inFiles, string outFile) {
            PdfMerger.PdfMerge([.. inFiles], outFile);
        }

        /// <summary>
        /// 带单层级文件夹书签的 PDF 合并
        /// </summary>
        public static void PdfMergeWithHierarchicalOutlines(List<string> inFiles, string outFile) {
            PdfMerger.PdfMergeWithHierarchicalOutlines(inFiles, outFile);
        }

        /// <summary>
        /// 带多层级深度书签树的 PDF 合并
        /// </summary>
        public static void PdfMergeWithDeepOutlines(List<string> inFiles, string outFile, string rootPath) {
            PdfMerger.PdfMergeWithDeepOutlines(inFiles, outFile, rootPath);
        }
    }
}
