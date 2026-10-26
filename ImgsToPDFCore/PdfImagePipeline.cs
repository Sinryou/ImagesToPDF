using iTextSharp.text;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using WebPWrapper;

namespace ImgsToPDFCore {
    /// <summary>
    /// 图像加载、解码、EXIF 校正与 PDF 格式重编码管道。
    /// 负责将磁盘文件安全转换为适合写入 PDF 的 PageImage 对象。
    /// </summary>
    internal static class PdfImagePipeline {
        /// <summary>EXIF 中 Orientation 标签的 ID。</summary>
        public const int OrientationId = 0x0112;

        public static readonly HashSet<string> ImageExtensions = [with(StringComparer.OrdinalIgnoreCase), ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif", ".webp"];
        public static readonly HashSet<string> ImageExtensionsEXIFOrientation = [with(StringComparer.OrdinalIgnoreCase), ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".tif", ".tiff"];

        private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

        /// <summary>
        /// 待排入 PDF 的一张图片：iTextSharp 图像对象 + 仍需在排版期应用的 EXIF 方向。
        /// 方向留到排版期而不是解码期，是为了让原始字节（尤其是 JPEG）能无损直通 PDF：
        /// 解码期旋转必然要重新编码，会带来画质损失与体积膨胀。
        /// </summary>
        public sealed class PageImage {
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
        public static bool ExifSwapsAxes(ushort orientation) => orientation is 5 or 6 or 7 or 8;

        /// <summary>
        /// EXIF Orientation 到 RotateFlipType 的映射函数
        /// </summary>
        public static RotateFlipType GetRotateFlipType(ushort orientation) => orientation switch {
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
        public static ushort ReadOrientation(System.Drawing.Image image) {
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
        /// 只解析元数据读出 EXIF Orientation（纯文件流/头部扫描，零整图加载，避免 LOH 分配）。
        /// 优先通过纯二进制扫描文件前几 KB，未命中时安全降级回 GDI+。
        /// </summary>
        public static ushort ReadExifOrientation(string imagePath) {
            try {
                ushort? orientation = ImageExif.GetOrientation(imagePath);
                if (orientation.HasValue) {
                    return orientation.Value;
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[ImgsToPDFCore] Pure-byte EXIF parse failed: {ex.Message}");
            }

            // 极少数格式若未能被二进制扫描命中，安全降级回 GDI+ 流式加载
            try {
                using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
                using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                return ReadOrientation(image);
            }
            catch {
                return 1;
            }
        }

        /// <summary>
        /// 只解析元数据读出 EXIF Orientation（纯二进制流扫描，零 GDI+ 实例分配）。
        /// 优先通过纯二进制扫描 JPEG/WebP/TIFF 结构，未命中时安全降级回 GDI+。
        /// </summary>
        public static ushort ReadExifOrientation(byte[] imageBytes) {
            try {
                ushort? orientation = ImageExif.GetImageOrientation(imageBytes);
                if (orientation.HasValue) {
                    return orientation.Value;
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[ImgsToPDFCore] Pure-byte EXIF parse failed: {ex.Message}");
            }

            // 极少数格式若未能被二进制扫描命中，安全降级回 GDI+
            try {
                using var stream = new MemoryStream(imageBytes, writable: false);
                using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                return ReadOrientation(image);
            }
            catch {
                return 1;
            }
        }

        public static long GetFastJpegQuality() {
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
        public static byte[] CompressToJpeg(System.Drawing.Image img, long quality) {
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
                if (JpegCodec != null) {
                    canvas.Save(outMs, JpegCodec, encoderParams);
                }
                else {
                    canvas.Save(outMs, ImageFormat.Jpeg);
                }
            }
            else {
                if (JpegCodec != null) {
                    img.Save(outMs, JpegCodec, encoderParams);
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
        public static PageImage LoadPageImage(string imagePath, bool fastFlag) {
            if (string.Equals(Path.GetExtension(imagePath), ".webp", StringComparison.OrdinalIgnoreCase)) {
                using var bitmap = LoadWebPBitmap(imagePath);
                return new PageImage { Image = GetImageInstance(bitmap, fastFlag) };
            }
            return LoadPdfImage(imagePath, fastFlag);
        }

        /// <summary>
        /// 把解码得到的位图封装为 iTextSharp 图像实例。
        /// --fast 时统一按 FastQuality 压缩为 JPEG（透明区域铺白底），与其它路径保持一致；
        /// 否则带透明通道的位图编码为 PNG（保留 alpha，iText 会据此生成 /SMask），
        /// 不透明的位图仍按 BMP 写出（体积更小，且不会产生多余的 /SMask）。
        /// </summary>
        public static iTextSharp.text.Image GetImageInstance(Bitmap bitmap, bool fastFlag) {
            if (fastFlag) {
                return iTextSharp.text.Image.GetInstance(CompressToJpeg(bitmap, GetFastJpegQuality()));
            }
            // webp 解码得到的是内存位图（RawFormat 为 MemoryBmp），iText 无法直接识别，需要指定格式。
            // 这里必须区分透明与不透明：BMP 不携带 alpha，透明区域会变成纯黑（libwebp 对全透明像素
            // 输出 RGBA(0,0,0,0)）；改用 PNG 则 iText 会生成 /SMask，透明背景得以保留。
            if (bitmap.RawFormat.Equals(ImageFormat.MemoryBmp)) {
                if (System.Drawing.Image.IsAlphaPixelFormat(bitmap.PixelFormat)) {
                    using var ms = new MemoryStream();
                    bitmap.Save(ms, ImageFormat.Png);
                    return iTextSharp.text.Image.GetInstance(ms.ToArray());
                }
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
        public static PageImage LoadPdfImage(string imagePath, bool fastFlag) {
            var fileExt = Path.GetExtension(imagePath);
            bool needsExifCheck = ImageExtensionsEXIFOrientation.Contains(fileExt);

            // --- 开启 fastFlag：常规格式均压缩为指定质量的 JPEG，最大化减小产物体积 ---
            if (fastFlag) {
                long quality = GetFastJpegQuality();

                // 常规格式（JPG, PNG, GIF, BMP, TIFF 等）：流式读入后处理 EXIF 旋转，再压缩为目标质量 JPEG
                // 采用缓冲流逐块读取，避免 File.ReadAllBytes 导致大图进入大对象堆 (LOH)
                using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                using var img = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);

                if (needsExifCheck) {
                    ushort orientation = ImageExif.GetOrientation(imagePath) ?? ReadOrientation(img);
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
            // 只解析头部元数据取方向（不解码像素），旋转交给排版期的变换矩阵；
            // 直接传递路径由 iTextSharp 流式直通嵌入，彻底消除托管内存中的巨幅 byte[] 分配
            ushort exifOrientation = needsExifCheck ? ReadExifOrientation(imagePath) : (ushort)1;

            // JPEG 为字节直通（DCTDecode），源文件与 PDF 内嵌流逐字节一致，真正 0 损耗；
            // PNG/GIF/BMP/TIFF 会被解码后用 Deflate 重新压缩（FlateDecode），同样无损（像素不变），
            // 但不是字节直通，需要一次解码 + 一次重编码，因此只有 JPEG 走的是"极速嵌入"路径。
            return new PageImage {
                Image = iTextSharp.text.Image.GetInstance(imagePath),
                Orientation = exifOrientation
            };
        }

        /// <summary>
        /// 解码 WebP 为位图（libwebp 无法直通 PDF，必须解码），并按 EXIF 方向旋转。
        /// </summary>
        public static Bitmap LoadWebPBitmap(string imagePath) {
            // 只读一次盘：解码与 EXIF Orientation 解析共用同一份字节，避免同一文件读两遍
            var rawWebP = File.ReadAllBytes(imagePath);
            using WebP webp = new();
            var bitmapWebp = webp.Decode(rawWebP);
            // Decode 仅在字节为空（0 字节文件）时返回 null，正常解码失败会直接抛异常。
            // 此时若把 null 传下去，GetImageInstance 会解引用成毫无线索的 NullReferenceException；
            // 在这里抛出可被 WriteFolderPages 捕获、按图片路径上报的真实原因。
            if (bitmapWebp == null) {
                throw new InvalidDataException("The file is empty or not a valid WebP image.");
            }

            try {
                ushort? orientation = ImageExif.GetOrientation(rawWebP);
                if (orientation.HasValue) {
                    RotateFlipType rotateFlip = GetRotateFlipType(orientation.Value);
                    if (rotateFlip != RotateFlipType.RotateNoneFlipNone) {
                        bitmapWebp.RotateFlip(rotateFlip);
                    }
                }
                return bitmapWebp;
            }
            catch {
                bitmapWebp.Dispose();
                throw;
            }
        }
    }
}
