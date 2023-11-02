using iTextSharp.text;
using System;
using System.Collections.Generic;
using System.Drawing;
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
        static iTextSharp.text.Image GetImageInstance(Bitmap bitmap, bool fastFlag) {
            iTextSharp.text.Image resultImage;
            if (fastFlag) {
                resultImage = iTextSharp.text.Image.GetInstance(bitmap, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            // webp直接转为bmp写，否则报错；其他的按读入的格式写
            else if (bitmap.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.MemoryBmp)) {
                resultImage = iTextSharp.text.Image.GetInstance(bitmap, System.Drawing.Imaging.ImageFormat.Bmp);
            }
            else {
                resultImage = iTextSharp.text.Image.GetInstance(bitmap, bitmap.RawFormat);
            }
            return resultImage;
        }
        static void AddPage(Document document, Bitmap bitmap, bool fastFlag) {
            iTextSharp.text.Rectangle pageSize;
            //Console.WriteLine(luaConfig.PageSizeToSave.Width);
            if (CSGlobal.luaConfig.PageSizeToSave != null) {
                pageSize = CSGlobal.luaConfig.PageSizeToSave;
            }
            else {
                pageSize = new iTextSharp.text.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            }
            document.SetPageSize(pageSize);
            var image = GetImageInstance(bitmap, fastFlag);
            if (CSGlobal.luaConfig.PageSizeToSave != null) {
                image.ScaleToFit(pageSize.Width, pageSize.Height);
                var wMargins = (pageSize.Width - image.ScaledWidth) / 2;
                var hMargins = (pageSize.Height - image.ScaledHeight) / 2;
                document.SetMargins(wMargins, wMargins, hMargins, hMargins);
            }
            document.NewPage();
            document.PageCount = document.PageNumber + 1;
            document.Add(image);
            bitmap.Dispose();   // 释放位图占用资源
        }
        static Bitmap CombineBitmap(Bitmap bm1, Bitmap bm2, int margin) {
            var width = bm1.Width + bm2.Width + margin;
            var height = Math.Max(bm1.Height, bm2.Height);
            // 初始化画布(最终的拼图画布)并设置宽高
            Bitmap bitMap = new Bitmap(width, height);
            // 初始化画板
            Graphics canavas = Graphics.FromImage(bitMap);
            // 将画布涂为白色(底部颜色可自行设置)
            canavas.FillRectangle(Brushes.White, new System.Drawing.Rectangle(0, 0, width, height));
            //在x=0，y=0处画上图一
            canavas.DrawImage(bm1, 0, 0, bm1.Width, bm1.Height);
            //在x=0，y在图一往下10像素处画上图二
            canavas.DrawImage(bm2, bm1.Width + margin, 0, bm2.Width, bm2.Height);
            bm1.Dispose();
            bm2.Dispose();
            return bitMap;
        }
        static void ImagesToPdf(List<Bitmap> imageList, Layout layout = Layout.Single, bool fastFlag = false) {
            using (var ms = new MemoryStream()) {
                var document = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 0, 0, 0, 0);
                iTextSharp.text.pdf.PdfWriter.GetInstance(document, ms).SetFullCompression();
                document.Open();
                if (layout != Layout.DuplexLeftToRight && layout != Layout.DuplexRightToLeft) {
                    // 如果layout flag为0，单页来写
                    foreach (var imagePic in imageList) {
                        AddPage(document, imagePic, fastFlag);
                    }
                }
                else {
                    for (int i = 0; i < imageList.Count; i++) {
                        if (i + 1 >= imageList.Count || !(imageList[i].Height >= imageList[i].Width && imageList[i + 1].Height >= imageList[i + 1].Width)) {
                            AddPage(document, imageList[i], fastFlag);
                        }
                        else {   // 如果图片长大于宽且下一张也如此，把他们拼起来
                            Bitmap picAtLeft = layout == Layout.DuplexLeftToRight ? imageList[i] : imageList[i + 1];
                            Bitmap picAtRight = layout == Layout.DuplexLeftToRight ? imageList[i + 1] : imageList[i];
                            using (var combinedBitmap = CombineBitmap(picAtLeft, picAtRight, 10)) {
                                AddPage(document, combinedBitmap, fastFlag);
                            }
                            imageList[i]?.Dispose();
                            imageList[i + 1]?.Dispose();
                            i++;
                        }
                    }
                }
                //Console.WriteLine(document.PageNumber);
                // 如果零页，添加一页空页
                if (document.PageNumber == 0) {
                    document.NewPage();
                    document.Add(iTextSharp.text.Chunk.NEWLINE);
                }
                document.Close();
                string pathToSave = CSGlobal.luaConfig.PathToSave(); // 从lua里读设置的保存路径
                File.WriteAllBytes(pathToSave, ms.ToArray());
            }
        }
        /// <summary>
        /// 将指定文件夹下的图片合并为PDF文件
        /// </summary>
        /// <param name="directoryPath">文件夹路径</param>
        /// <param name="layout">合并方式</param>
        /// <param name="fastFlag">是否以图片质量换取生成速度</param>
        public static void ImagesToPDF(string directoryPath, Layout layout = Layout.Single, bool fastFlag = false) {
            if (!Directory.Exists(directoryPath)) { return; }   // 不存在文件夹则直接结束执行
            List<string> imageExtensions = new List<string> { ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif", ".webp" };
            IEnumerable<string> imagepaths = Directory.EnumerateFiles(directoryPath)
                .Where(p => imageExtensions.Any(e => Path.GetExtension(p)?.ToLower() == e))
                .OrderBy(p => p, new StringLenComparer());
            List<Bitmap> imageBitmapList = new List<Bitmap>();
            foreach (var imagepath in imagepaths) {
                var fileExt = Path.GetExtension(imagepath)?.ToLower();
                Bitmap srcImage;
                if (fileExt == ".webp") {
                    // 读取webp文件的方法
                    using (WebP webp = new WebP()) {
                        srcImage = webp.Load(imagepath);
                    }
                }
                else {
                    try {
                        srcImage = Bitmap.FromFile(imagepath) as Bitmap;
                    }
                    catch (Exception) {
                        continue;
                    }
                }
                if (srcImage != null) {
                    imageBitmapList.Add(srcImage);
                }
            };
            ImagesToPdf(imageBitmapList, layout, fastFlag);
            foreach (Bitmap bitmap in imageBitmapList) {
                bitmap?.Dispose();
            }
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
