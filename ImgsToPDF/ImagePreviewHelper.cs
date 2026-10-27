using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WebPWrapper;

namespace ImgsToPDF {
    /// <summary>
    /// UI 图像预览与缩略图处理服务。
    /// 负责按需解码 WebP/常规图片、纠正 EXIF 旋转、生成高质量缩略图并安全释放 GDI+ 资源。
    /// </summary>
    internal static class ImagePreviewHelper {
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
        /// 按目标最大宽高限制生成高质量缩略图，避免巨幅大图常驻内存造成内存暴涨
        /// </summary>
        public static Bitmap CreateThumbnail(Image source, int maxWidth, int maxHeight) {
            if (source.Width <= maxWidth && source.Height <= maxHeight) {
                return new Bitmap(source);
            }

            float ratio = Math.Min((float)maxWidth / source.Width, (float)maxHeight / source.Height);
            int destWidth = Math.Max(1, (int)(source.Width * ratio));
            int destHeight = Math.Max(1, (int)(source.Height * ratio));

            var thumb = new Bitmap(destWidth, destHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try {
                using (var g = Graphics.FromImage(thumb)) {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, destWidth, destHeight));
                }
                return thumb;
            }
            catch {
                thumb.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 加载单张图片用于缩略图预览生成。常规格式通过 GDI+ 流式加载，WebP 格式通过 libwebp + ImageExif 加载并纠正旋转。
        /// </summary>
        public static Image LoadPreviewImage(string imagepath) {
            // 提取扩展名局部变量，避免多次解析路径字符串
            string ext = Path.GetExtension(imagepath);

            if (string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase)) {
                // 只读一次盘：解码与 EXIF Orientation 解析共用同一份字节，避免同一文件读两遍
                var rawWebP = File.ReadAllBytes(imagepath);
                using WebP webp = new();
                var bitmapWebp = webp.Decode(rawWebP);
                if (bitmapWebp == null) return null;

                try {
                    ushort? orientation = ImgsToPDFCore.ImageExif.GetOrientation(rawWebP);
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

            // 采用只读文件流加载，并在流保持打开时将其完整克隆脱钩为纯内存 Bitmap
            // 避免直接使用 Image.FromFile 导致的底层文件长期被 GDI+ 锁定占用
            RotateFlipType exifRotateFlip = RotateFlipType.RotateNoneFlipNone;
            Bitmap detachedImg;

            using (var stream = new FileStream(imagepath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var rawImg = Image.FromStream(stream, false, false)) {
                if (FolderScanner.ImageExtensionsEXIFOrientation.Contains(ext)) {
                    const int OrientationId = 0x0112;

                    // 在流保持可用期间读取 EXIF 元数据
                    if (Array.IndexOf(rawImg.PropertyIdList, OrientationId) != -1) {
                        var property = rawImg.GetPropertyItem(OrientationId);
                        ushort orientation = BitConverter.ToUInt16(property.Value, 0);
                        exifRotateFlip = GetRotateFlipType(orientation);
                    }
                }

                try {
                    detachedImg = new Bitmap(rawImg);
                }
                catch {
                    // 兼容少数极端颜色格式（如部分特定调色板/CMYK格式），通过新位图绘制保底完成像素脱钩
                    detachedImg = new Bitmap(rawImg.Width, rawImg.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(detachedImg);
                    g.DrawImage(rawImg, 0, 0, rawImg.Width, rawImg.Height);
                }
            }

            try {
                if (exifRotateFlip != RotateFlipType.RotateNoneFlipNone) {
                    detachedImg.RotateFlip(exifRotateFlip);
                }
                return detachedImg;
            }
            catch {
                detachedImg.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 替换 PictureBox 显示的图像并及时释放旧图像的 GDI+ 句柄。
        /// Properties.Resources.* 每次访问都会反序列化生成新的 Bitmap 实例，
        /// 因此与动态生成的缩略图一样，在被替换或窗体销毁时都应及时 Dispose。
        /// </summary>
        public static void ReplaceImage(PictureBox pictureBox, Image newImage) {
            Image oldImage = pictureBox.Image;
            if (ReferenceEquals(oldImage, newImage)) {
                return;
            }
            pictureBox.Image = newImage;
            oldImage?.Dispose();
        }
    }
}
