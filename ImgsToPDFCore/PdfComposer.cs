using ImgsToPDF.Shared;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImgsToPDF.Core {
    /// <summary>
    /// PDF 页面排版与文档生成引擎。
    /// 负责按单页或双页（小说模式）对图片进行几何变换、页面缩放、中缝处理并输出为 PDF 文档。
    /// </summary>
    internal static class PdfComposer {
        /// <summary>双页模式下两张图之间的中缝宽度（点/像素）。</summary>
        private const int DuplexPageGap = 10;

        /// <summary>
        /// 计算把图片按 EXIF 方向放进目标矩形 (x, y, width, height) 的仿射矩阵。
        /// 返回值对应 PDF 的 "a b c d e f cm"：x' = a*u + c*v + e，y' = b*u + d*v + f，
        /// 其中 (u, v) 为图片单位方格坐标（u 向右、v 向上，与 PDF 图像坐标一致）。
        /// 各方向由 EXIF 规范里“第 0 行/第 0 列”的定义推导：1 原样、2 水平镜像、3 旋转 180°、
        /// 4 垂直镜像、5 转置、6 顺时针 90°、7 反转置、8 顺时针 270°。
        /// </summary>
        public static float[] GetOrientationMatrix(ushort orientation, float x, float y, float width, float height) {
            return orientation switch {
                2 => [-width, 0, 0, height, x + width, y],
                3 => [-width, 0, 0, -height, x + width, y + height],
                4 => [width, 0, 0, -height, x, y + height],
                5 => [0, -height, -width, 0, x + width, y + height],
                6 => [0, -height, width, 0, x, y + height],
                7 => [0, height, width, 0, x, y],
                8 => [0, height, -width, 0, x + width, y],
                _ => [width, 0, 0, height, x, y],
            };
        }

        /// <summary>
        /// 用仿射矩阵把一张图片放到当前页的指定矩形内（EXIF 方向在此一并应用）。
        /// </summary>
        public static void DrawImage(PdfContentByte content, PdfImagePipeline.PageImage pageImage, float x, float y, float width, float height) {
            var matrix = GetOrientationMatrix(pageImage.Orientation, x, y, width, height);
            content.AddImage(pageImage.Image, matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]);
        }

        /// <summary>
        /// 把一张图片排成一页。图片通过 PdfContentByte 以仿射矩阵直接放置：
        /// 未开启 --fast 时原始字节（JPEG 等）无损直通，EXIF 方向也只在此处应用一次，无需重新编码。
        /// </summary>
        public static void AddPage(Document document, PdfWriter writer, PdfImagePipeline.PageImage pageImage) {
            var pageSizeToSave = CSGlobal.luaConfig.PageSizeToSave;
            float imageWidth = pageImage.Width;
            float imageHeight = pageImage.Height;

            iTextSharp.text.Rectangle pageSize;
            float drawWidth = imageWidth;
            float drawHeight = imageHeight;
            float x = 0f;
            float y = 0f;
            if (pageSizeToSave != null) {
                pageSize = pageSizeToSave;
                float scale = Math.Min(pageSize.Width / imageWidth, pageSize.Height / imageHeight);
                drawWidth = imageWidth * scale;
                drawHeight = imageHeight * scale;
                x = (pageSize.Width - drawWidth) / 2;
                y = (pageSize.Height - drawHeight) / 2;
            }
            else {
                pageSize = new iTextSharp.text.Rectangle(0, 0, imageWidth, imageHeight);
            }

            document.SetPageSize(pageSize);
            document.NewPage();
            DrawImage(writer.DirectContent, pageImage, x, y, drawWidth, drawHeight);
        }

        /// <summary>
        /// 把两张竖图并排排入同一页（小说模式的双页）。
        /// 不再把两张图拼成一张位图：拼图会额外占用一整幅画布的 GDI+ 内存，
        /// 且必然经历一次重新编码（画质损失 + 体积膨胀）。这里两张图各自按自身编码放置。
        /// </summary>
        public static void AddDuplexPage(Document document, PdfWriter writer, PdfImagePipeline.PageImage left, PdfImagePipeline.PageImage right) {
            var pageSizeToSave = CSGlobal.luaConfig.PageSizeToSave;

            float leftWidth = left.Width;
            float leftHeight = left.Height;
            float rightWidth = right.Width;
            float rightHeight = right.Height;
            float contentWidth = leftWidth + DuplexPageGap + rightWidth;
            float contentHeight = Math.Max(leftHeight, rightHeight);

            iTextSharp.text.Rectangle pageSize;
            float scale = 1f;
            float offsetX = 0f;
            float offsetY = 0f;
            if (pageSizeToSave != null) {
                pageSize = pageSizeToSave;
                scale = Math.Min(pageSize.Width / contentWidth, pageSize.Height / contentHeight);
                offsetX = (pageSize.Width - contentWidth * scale) / 2;
                offsetY = (pageSize.Height - contentHeight * scale) / 2;
            }
            else {
                pageSize = new iTextSharp.text.Rectangle(0, 0, contentWidth, contentHeight);
            }

            document.SetPageSize(pageSize);
            document.NewPage();

            var content = writer.DirectContent;
            // 两张图顶部对齐，与旧版拼接位图时的画法保持一致
            DrawImage(content, left,
                offsetX,
                offsetY + (contentHeight - leftHeight) * scale,
                leftWidth * scale,
                leftHeight * scale);
            DrawImage(content, right,
                offsetX + (leftWidth + DuplexPageGap) * scale,
                offsetY + (contentHeight - rightHeight) * scale,
                rightWidth * scale,
                rightHeight * scale);
        }

        /// <summary>
        /// 按配置的排序规则列出目录下的图片（目录不存在或读取失败时返回空列表）。
        /// OrderBy 会立即物化，因此连枚举期间抛出的异常也一并被兜住。
        /// </summary>
        public static List<string> CollectSortedImagePaths(string directoryPath) {
            var result = new List<string>();
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) {
                return result;
            }
            try {
                result.AddRange(Directory.EnumerateFiles(directoryPath)
                    .Where(p => ImageContracts.ImageExtensions.Contains(Path.GetExtension(p)))
                    .OrderBy(p => p, StringLenComparer.Instance));
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"[ImgsToPDFCore] Failed to enumerate '{directoryPath}': {ex.GetType().Name}: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 将指定文件夹下的图片合并为PDF文件
        /// </summary>
        /// <param name="directoryPath">文件夹路径</param>
        /// <param name="layout">合并方式</param>
        /// <param name="fastFlag">是否以图片质量换取生成速度</param>
        public static void ImagesToPDF(string directoryPath, Layout layout = Layout.Single, bool fastFlag = false) {
            CSGlobal.ThrowIfCancelled();
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) {
                return;   // 不存在文件夹则直接结束执行（保持原行为：不生成任何文件）
            }
            // 零页时补一页空白页，保持原有行为
            WriteDocument(CSGlobal.luaConfig.PathToSave(), layout, fastFlag, [CollectSortedImagePaths(directoryPath)], blankPageWhenEmpty: true);
        }

        /// <summary>
        /// 把多个文件夹（按给定顺序）里的图片依次写入同一个 PDF，返回实际写入的页数。
        /// 每个文件夹内部单独排序，双页模式下也不跨文件夹配对，因此章节之间不会串页。
        /// 返回 0 表示这些文件夹里一张图片都没有——此时不会创建输出文件，
        /// 由调用方（config.lua）负责上报失败。压缩包路径就靠这个返回值判断"整包无图片"。
        /// </summary>
        public static int ImagesToPDFFromFolders(string[] directoryPaths, Layout layout = Layout.Single, bool fastFlag = false) {
            CSGlobal.ThrowIfCancelled();
            if (directoryPaths == null || directoryPaths.Length == 0) {
                CommonUtils.ReportFailure("No source folder was provided; no PDF was generated.");
                return 0;
            }
            Array.Sort(directoryPaths, StringLenComparer.Instance);
            var folders = new List<List<string>>();
            foreach (var directoryPath in directoryPaths) {
                CSGlobal.ThrowIfCancelled();
                var images = CollectSortedImagePaths(directoryPath);
                if (images.Count > 0) {
                    folders.Add(images);
                }
            }
            if (folders.Count == 0) {
                return 0;
            }
            return WriteDocument(CSGlobal.luaConfig.PathToSave(), layout, fastFlag, folders, blankPageWhenEmpty: false);
        }

        /// <summary>
        /// 把指定的图片文件列表写入同一个 PDF，返回实际写入的页数。
        /// </summary>
        public static int ImagesToPDFFromImageFiles(string[] imageFiles, Layout layout = Layout.Single, bool fastFlag = false) {
            CSGlobal.ThrowIfCancelled();
            if (imageFiles == null || imageFiles.Length == 0) {
                CommonUtils.ReportFailure("No image file was provided; no PDF was generated.");
                return 0;
            }
            var validImages = imageFiles.Where(File.Exists).OrderBy(p => p, StringLenComparer.Instance).ToList();
            if (validImages.Count == 0) {
                CommonUtils.ReportFailure("No valid existing image file was found; no PDF was generated.");
                return 0;
            }
            return WriteDocument(CSGlobal.luaConfig.PathToSave(), layout, fastFlag, [validImages], blankPageWhenEmpty: false);
        }

        /// <summary>
        /// 写出一份 PDF 并返回写入的页数（失败返回 0）。
        /// folders 的每个元素是一个文件夹内已排好序的图片列表，按顺序逐页写入。
        /// </summary>
        public static int WriteDocument(string pathToSave, Layout layout, bool fastFlag, List<List<string>> folders, bool blankPageWhenEmpty) {
            int pages = 0;
            bool ok = AtomicFileWriter.WriteToFile(pathToSave, fs => {
                var document = new Document(PageSize.A4, 0, 0, 0, 0);
                var writer = PdfWriter.GetInstance(document, fs);
                writer.SetFullCompression();
                document.Open();

                try {
                    foreach (var images in folders) {
                        WriteFolderPages(document, writer, images, layout, fastFlag);
                    }

                    // 如果零页，添加一页空页
                    // document.NewPage()不触发document.PageNumber计数增长，必须读writer.PageNumber
                    if (writer.PageNumber == 0 && blankPageWhenEmpty) {
                        document.NewPage();
                        document.Add(Chunk.NEWLINE);
                    }
                    pages = writer.PageNumber;
                }
                finally {
                    document.Close();
                }
            });
            return ok ? pages : 0;
        }

        /// <summary>
        /// 把一个文件夹里的图片写成一页页（单页模式一张一页；双页模式两张一页）
        /// </summary>
        private static void WriteFolderPages(Document document, PdfWriter writer, List<string> imagePaths, Layout layout, bool fastFlag) {
            if (layout != Layout.DuplexLeftToRight && layout != Layout.DuplexRightToLeft) {
                // 如果layout flag为0，单页来写
                foreach (var imagePath in imagePaths) {
                    CSGlobal.ThrowIfCancelled();
                    try {
                        var pageImage = PdfImagePipeline.LoadPageImage(imagePath, fastFlag);
                        if (pageImage != null) {
                            AddPage(document, writer, pageImage);
                        }
                    }
                    catch (Exception ex) {
                        Console.Error.WriteLine($"[ImgsToPDFCore] Failed to load image '{imagePath}': {ex.GetType().Name}: {ex.Message}");
                    }
                }
                return;
            }

            using var enumerator = imagePaths.GetEnumerator();
            while (enumerator.MoveNext()) {
                CSGlobal.ThrowIfCancelled();
                PdfImagePipeline.PageImage bm1;
                try {
                    bm1 = PdfImagePipeline.LoadPageImage(enumerator.Current, fastFlag);
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ImgsToPDFCore] Failed to load image '{enumerator.Current}': {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (bm1 == null) continue;

                // 横向图片（长插图页）单独占一页
                if (bm1.Width >= bm1.Height) {
                    AddPage(document, writer, bm1);
                    continue;
                }
                else if (!enumerator.MoveNext()) {
                    AddPage(document, writer, bm1);
                    break;
                }

                CSGlobal.ThrowIfCancelled();
                PdfImagePipeline.PageImage bm2;
                try {
                    bm2 = PdfImagePipeline.LoadPageImage(enumerator.Current, fastFlag);
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ImgsToPDFCore] Failed to load image '{enumerator.Current}': {ex.GetType().Name}: {ex.Message}");
                    AddPage(document, writer, bm1);
                    continue;
                }

                if (bm2 == null) {
                    AddPage(document, writer, bm1);
                    continue;
                }

                if (bm1.Height >= bm1.Width && bm2.Height >= bm2.Width) {   // 如果图片长大于宽且下一张也如此，把它们并排放在一页
                    PdfImagePipeline.PageImage picAtLeft = layout == Layout.DuplexLeftToRight ? bm1 : bm2;
                    PdfImagePipeline.PageImage picAtRight = layout == Layout.DuplexLeftToRight ? bm2 : bm1;
                    AddDuplexPage(document, writer, picAtLeft, picAtRight);
                }
                else {
                    AddPage(document, writer, bm1);
                    AddPage(document, writer, bm2);
                }
            }
        }
    }

    /// <summary>
    /// 给文件名排序的方法，不使用默认的排序方法，委托在 Lua (config.lua) 里实现
    /// </summary>
    internal sealed class StringLenComparer : IComparer<string> {
        public static readonly StringLenComparer Instance = new();

        private StringLenComparer() { }

        public int Compare(string x, string y) {
            return CSGlobal.luaConfig.FilePathComparer(x, y);
        }
    }
}
