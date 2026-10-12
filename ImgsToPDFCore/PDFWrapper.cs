using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using WebPWrapper;

namespace ImgsToPDFCore {
    public enum Layout {
        Single,
        DuplexLeftToRight,
        DuplexRightToLeft
    }
    internal class PDFWrapper {
        /// <summary>双页模式下两张图之间的中缝宽度（点/像素）。</summary>
        private const int DuplexPageGap = 10;

        /// <summary>写出 PDF 时使用的临时文件后缀：先写它，成功后再替换目标文件，失败则删除。</summary>
        private const string TempFileSuffix = ".tmp";

        /// <summary>EXIF 中 Orientation 标签的 ID。</summary>
        private const int OrientationId = 0x0112;

        private static readonly HashSet<string> imageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif", ".webp" };
        private static readonly HashSet<string> imageExtensionsEXIFOrientation = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".tif", ".tiff" };

        /// <summary>
        /// 待排入 PDF 的一张图片：iTextSharp 图像对象 + 仍需在排版期应用的 EXIF 方向。
        /// 方向留到排版期而不是解码期，是为了让原始字节（尤其是 JPEG）能无损直通 PDF：
        /// 解码期旋转必然要重新编码，会带来画质损失与体积膨胀。
        /// </summary>
        private sealed class PageImage {
            public iTextSharp.text.Image Image;
            /// <summary>EXIF Orientation，1 表示无需变换（方向已在解码期烘焙进位图时同样为 1）。</summary>
            public ushort Orientation = 1;

            /// <summary>应用 EXIF 方向之后的显示宽度。</summary>
            public float Width => ExifSwapsAxes(Orientation) ? Image.PlainHeight : Image.PlainWidth;

            /// <summary>应用 EXIF 方向之后的显示高度。</summary>
            public float Height => ExifSwapsAxes(Orientation) ? Image.PlainWidth : Image.PlainHeight;
        }

        /// <summary>
        /// EXIF Orientation 中 5~8 属于转置类变换，显示时宽高互换。
        /// </summary>
        private static bool ExifSwapsAxes(ushort orientation) => orientation is 5 or 6 or 7 or 8;

        /// <summary>
        /// EXIF Orientation 到 RotateFlipType 的映射函数
        /// </summary>
        private static RotateFlipType GetRotateFlipType(ushort orientation) => orientation switch {
            1 => RotateFlipType.RotateNoneFlipNone,
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.RotateNoneFlipY,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        };

        /// <summary>
        /// 读取已加载图片的 EXIF Orientation；没有标记、元数据异常或取值非法时返回 1（不做变换）。
        /// </summary>
        private static ushort ReadOrientation(System.Drawing.Image image) {
            try {
                if (Array.IndexOf(image.PropertyIdList, OrientationId) == -1) {
                    return 1;
                }
                var property = image.GetPropertyItem(OrientationId);
                if (property?.Value == null || property.Value.Length < 2) {
                    return 1;
                }
                ushort orientation = BitConverter.ToUInt16(property.Value, 0);
                return orientation is >= 1 and <= 8 ? orientation : (ushort)1;
            }
            catch (Exception ex) {
                // 元数据损坏不应导致整张图片被跳过，按“无方向”处理
                System.Diagnostics.Debug.WriteLine($"[ImgsToPDFCore] Failed to read EXIF orientation: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// 只解析元数据读出 EXIF Orientation（不解码像素数据）。
        /// </summary>
        private static ushort ReadExifOrientation(byte[] imageBytes) {
            using var stream = new MemoryStream(imageBytes, writable: false);
            using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            return ReadOrientation(image);
        }

        /// <summary>
        /// 计算把图片按 EXIF 方向放进目标矩形 (x, y, width, height) 的仿射矩阵。
        /// 返回值对应 PDF 的 "a b c d e f cm"：x' = a*u + c*v + e，y' = b*u + d*v + f，
        /// 其中 (u, v) 为图片单位方格坐标（u 向右、v 向上，与 PDF 图像坐标一致）。
        /// 各方向由 EXIF 规范里“第 0 行/第 0 列”的定义推导：1 原样、2 水平镜像、3 旋转 180°、
        /// 4 垂直镜像、5 转置、6 顺时针 90°、7 反转置、8 顺时针 270°。
        /// </summary>
        private static float[] GetOrientationMatrix(ushort orientation, float x, float y, float width, float height) {
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
        private static void DrawImage(PdfContentByte content, PageImage pageImage, float x, float y, float width, float height) {
            var matrix = GetOrientationMatrix(pageImage.Orientation, x, y, width, height);
            content.AddImage(pageImage.Image, matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]);
        }

        /// <summary>
        /// 把一张图片排成一页。图片通过 PdfContentByte 以仿射矩阵直接放置：
        /// 未开启 --fast 时原始字节（JPEG 等）无损直通，EXIF 方向也只在此处应用一次，无需重新编码。
        /// </summary>
        private static void AddPage(Document document, PdfWriter writer, PageImage pageImage) {
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
            document.PageCount = document.PageNumber + 1;
            DrawImage(writer.DirectContent, pageImage, x, y, drawWidth, drawHeight);
        }

        /// <summary>
        /// 把两张竖图并排排入同一页（小说模式的双页）。
        /// 不再把两张图拼成一张位图：拼图会额外占用一整幅画布的 GDI+ 内存，
        /// 且必然经历一次重新编码（画质损失 + 体积膨胀）。这里两张图各自按自身编码放置。
        /// </summary>
        private static void AddDuplexPage(Document document, PdfWriter writer, PageImage left, PageImage right) {
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
            document.PageCount = document.PageNumber + 1;

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
        /// 输出文件的统一写法：先写同目录下的临时文件，全部成功后再替换目标文件。好处：
        /// - 中途失败（磁盘满、编码异常等）不会在目标路径留下半成品 PDF，也不会破坏上一次生成的结果；
        /// - "无法写入"（被别的程序占用、只读、无权限）会变成一行可读的上报，
        ///   而不是让进程直接崩掉、用户只看到一句无意义的异常信息。
        /// </summary>
        /// <param name="pathToSave">最终输出路径</param>
        /// <param name="write">真正写文件的回调（内部需自行关闭 Document/PdfCopy 以刷新数据）</param>
        private static bool WriteToFile(string pathToSave, Action<FileStream> write) {
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
        private static void CommitOutputFile(string tempPath, string finalPath) {
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
        private static void DiscardOutputFile(string tempPath) {
            try {
                if (File.Exists(tempPath)) {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[ImgsToPDFCore] Failed to delete temp file '{tempPath}': {ex.Message}");
            }
        }

        /// <summary>
        /// 按配置的排序规则列出目录下的图片（目录不存在或读取失败时返回空列表）。
        /// OrderBy 会立即物化，因此连枚举期间抛出的异常也一并被兜住。
        /// </summary>
        private static List<string> CollectSortedImagePaths(string directoryPath) {
            var result = new List<string>();
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) {
                return result;
            }
            try {
                result.AddRange(Directory.EnumerateFiles(directoryPath)
                    .Where(p => imageExtensions.Contains(Path.GetExtension(p)))
                    .OrderBy(p => p, new StringLenComparer()));
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
        /// 写出一份 PDF 并返回写入的页数（失败返回 0）。
        /// folders 的每个元素是一个文件夹内已排好序的图片列表，按顺序逐页写入。
        /// </summary>
        private static int WriteDocument(string pathToSave, Layout layout, bool fastFlag, List<List<string>> folders, bool blankPageWhenEmpty) {
            int pages = 0;
            bool ok = WriteToFile(pathToSave, fs => {
                var document = new Document(PageSize.A4, 0, 0, 0, 0);
                var writer = PdfWriter.GetInstance(document, fs);
                writer.SetFullCompression();
                document.Open();

                try {
                    foreach (var images in folders) {
                        WriteFolderPages(document, writer, images, layout, fastFlag);
                    }

                    // 如果零页，添加一页空页
                    if (document.PageNumber == 0 && blankPageWhenEmpty) {
                        document.NewPage();
                        document.Add(Chunk.NEWLINE);
                    }
                    pages = document.PageNumber;
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
                        var pageImage = LoadPageImage(imagePath, fastFlag);
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
                PageImage bm1;
                try {
                    bm1 = LoadPageImage(enumerator.Current, fastFlag);
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
                PageImage bm2;
                try {
                    bm2 = LoadPageImage(enumerator.Current, fastFlag);
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
                    PageImage picAtLeft = layout == Layout.DuplexLeftToRight ? bm1 : bm2;
                    PageImage picAtRight = layout == Layout.DuplexLeftToRight ? bm2 : bm1;
                    AddDuplexPage(document, writer, picAtLeft, picAtRight);
                }
                else {
                    AddPage(document, writer, bm1);
                    AddPage(document, writer, bm2);
                }
            }
        }

        private static readonly ImageCodecInfo jpegCodec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

        private static long GetFastJpegQuality() {
            try {
                int q = CSGlobal.luaConfig != null ? CSGlobal.luaConfig.FastQuality : 0;
                if (q >= 1 && q <= 100) {
                    return q;
                }
            }
            catch {
                // Lua 配置读取异常时安全降级
            }
            return 75L;
        }

        /// <summary>
        /// 将 Image 压缩为指定质量的 JPEG 字节流（支持透明通道白底铺垫，防止透明背景变黑）
        /// </summary>
        static byte[] CompressToJpeg(System.Drawing.Image img, long quality) {
            using var outMs = new MemoryStream();
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);

            // 如果原图带透明通道（如 PNG），绘制到白色背景画布上，避免转 JPEG 后透明区域变黑
            if (System.Drawing.Image.IsAlphaPixelFormat(img.PixelFormat)) {
                using var canvas = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(canvas)) {
                    g.Clear(Color.White);
                    g.DrawImage(img, 0, 0, img.Width, img.Height);
                }
                if (jpegCodec != null) {
                    canvas.Save(outMs, jpegCodec, encoderParams);
                }
                else {
                    canvas.Save(outMs, ImageFormat.Jpeg);
                }
            }
            else {
                if (jpegCodec != null) {
                    img.Save(outMs, jpegCodec, encoderParams);
                }
                else {
                    img.Save(outMs, ImageFormat.Jpeg);
                }
            }

            return outMs.ToArray();
        }

        /// <summary>
        /// 载入一张图片用于排版：WebP 必须经 libwebp 解码，其余格式走原始字节直通/按需压缩。
        /// </summary>
        static PageImage LoadPageImage(string imagePath, bool fastFlag) {
            if (string.Equals(Path.GetExtension(imagePath), ".webp", StringComparison.OrdinalIgnoreCase)) {
                using var bitmap = LoadWebPBitmap(imagePath);
                return new PageImage { Image = GetImageInstance(bitmap, fastFlag) };
            }
            return LoadPdfImage(imagePath, fastFlag);
        }

        /// <summary>
        /// 把解码得到的位图封装为 iTextSharp 图像实例。
        /// --fast 时统一按 FastQuality 压缩为 JPEG（透明区域铺白底），与其它路径保持一致；
        /// 否则 WebP 直接转为 BMP 写出，其余按读入的格式写。
        /// </summary>
        static iTextSharp.text.Image GetImageInstance(Bitmap bitmap, bool fastFlag) {
            if (fastFlag) {
                return iTextSharp.text.Image.GetInstance(CompressToJpeg(bitmap, GetFastJpegQuality()));
            }
            // webp直接转为bmp写，否则报错；其他的按读入的格式写
            if (bitmap.RawFormat.Equals(ImageFormat.MemoryBmp)) {
                return iTextSharp.text.Image.GetInstance(bitmap, ImageFormat.Bmp);
            }
            return iTextSharp.text.Image.GetInstance(bitmap, bitmap.RawFormat);
        }

        /// <summary>
        /// 载入非 WebP 图片并封装为页面图像。
        /// --fast：解码后按 FastQuality 重编码为 JPEG（EXIF 方向此时烘焙进像素，透明区域铺白底）；
        /// 未开启：JPEG 原始字节直通 PDF（无解码，逐字节一致），PNG/GIF/BMP/TIFF 则无损重编码为
        /// Deflate；EXIF 方向留到排版期由变换矩阵应用，因此带方向的照片不会再被重新编码。
        /// </summary>
        static PageImage LoadPdfImage(string imagePath, bool fastFlag) {
            var fileExt = Path.GetExtension(imagePath);
            bool needsExifCheck = imageExtensionsEXIFOrientation.Contains(fileExt);

            // --- 开启 fastFlag：常规格式均压缩为指定质量的 JPEG，最大化减小产物体积 ---
            if (fastFlag) {
                long quality = GetFastJpegQuality();

                // 常规格式（JPG, PNG, GIF, BMP, TIFF 等）：读入后处理 EXIF 旋转，再压缩为目标质量 JPEG
                var rawBytes = File.ReadAllBytes(imagePath);
                using var stream = new MemoryStream(rawBytes);
                using var img = System.Drawing.Image.FromStream(stream);

                if (needsExifCheck) {
                    ushort orientation = ReadOrientation(img);
                    RotateFlipType rotateFlip = GetRotateFlipType(orientation);

                    if (rotateFlip != RotateFlipType.RotateNoneFlipNone) {
                        img.RotateFlip(rotateFlip);
                        img.RemovePropertyItem(OrientationId);
                    }
                }

                var jpegBytes = CompressToJpeg(img, quality);
                return new PageImage { Image = iTextSharp.text.Image.GetInstance(jpegBytes) };
            }

            // --- 未开启 fastFlag：无损直通方案 ---
            // 原生支持格式（JPG, PNG, GIF, BMP, TIFF 等）：直接读取原始字节，避免占用磁盘句柄
            var normalBytes = File.ReadAllBytes(imagePath);

            // 只解析元数据取方向（不解码像素），旋转交给排版期的变换矩阵，
            // 这样带 EXIF 方向的 JPEG 同样保持原始字节直通
            ushort exifOrientation = needsExifCheck ? ReadExifOrientation(normalBytes) : (ushort)1;

            // JPEG 为字节直通（DCTDecode），源文件与 PDF 内嵌流逐字节一致，真正 0 损耗；
            // PNG/GIF/BMP/TIFF 会被解码后用 Deflate 重新压缩（FlateDecode），同样无损（像素不变），
            // 但不是字节直通，需要一次解码 + 一次重编码，因此只有 JPEG 走的是"极速嵌入"路径。
            return new PageImage {
                Image = iTextSharp.text.Image.GetInstance(normalBytes),
                Orientation = exifOrientation
            };
        }

        /// <summary>
        /// 解码 WebP 为位图（libwebp 无法直通 PDF，必须解码），并按 EXIF 方向旋转。
        /// </summary>
        static Bitmap LoadWebPBitmap(string imagePath) {
            // 只读一次盘：解码与 EXIF Orientation 解析共用同一份字节，避免同一文件读两遍
            var rawWebP = File.ReadAllBytes(imagePath);
            using WebP webp = new();
            var bitmapWebp = webp.Decode(rawWebP);

            ushort? orientation = WebPExif.GetOrientation(rawWebP);
            if (orientation.HasValue) {
                RotateFlipType rotateFlip = GetRotateFlipType(orientation.Value);
                if (rotateFlip != RotateFlipType.RotateNoneFlipNone) {
                    bitmapWebp.RotateFlip(rotateFlip);
                }
            }

            return bitmapWebp;
        }
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
            WriteToFile(outFile, stream => {
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

            WriteToFile(outFile, stream => {
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
        static string GetRelativePath(string rootPath, string fullPath) {
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
        private static string NormalizeDir(string path) {
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
        private static List<string> AncestorFolders(string rootPath, string filePath) {
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
            WriteToFile(outFile, stream => {
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
        /// <summary>
        /// 给文件名排序的方法，不使用默认的排序方法，在lua里重写
        /// </summary>
        class StringLenComparer : IComparer<string> {
            int IComparer<string>.Compare(string x, string y) {
                return CSGlobal.luaConfig.FilePathComparer(x, y);
            }
        }
    }
}
