using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using iTextSharp.text;
using WebPWrapper;
using XLua;

namespace ImgsToPDFCore {
    [CSharpCallLua]
    public interface IConfig {
        string PathToSave { get; set; }
        int FilePathComparer(string a, string b);
        void PreProcess();
        void PostProcess();
    }
    internal struct CSGlobal {
        public static readonly LuaEnv luaEnv = new LuaEnv();
        public static string srcDirPath = "";
    }
    internal class Program {
        static public LuaEnv luaEnv = CSGlobal.luaEnv;
        static public IConfig luaConfig;

        private static iTextSharp.text.Image GetImageInstance(Bitmap bitmap, int fastFlag) {
            iTextSharp.text.Image resultImage;
            if (fastFlag == 1) {
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
        private static void AddPage(Document document, Bitmap bitmap, int fastFlag) {
            var pageSize = new iTextSharp.text.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            document.SetPageSize(pageSize);
            document.NewPage();
            var image = GetImageInstance(bitmap, fastFlag);
            document.PageCount = document.PageNumber + 1;
            document.Add(image);
            bitmap.Dispose();   // 释放位图占用资源
        }
        private static Bitmap CombineBitmap(Bitmap bm1, Bitmap bm2, int margin) {
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
        static public void ImagesToPdf(List<Bitmap> imageList, int generateFlag, int fastFlag) {
            using (var ms = new MemoryStream()) {
                var document = new iTextSharp.text.Document(PageSize.A4, 0, 0, 0, 0);
                iTextSharp.text.pdf.PdfWriter.GetInstance(document, ms).SetFullCompression();
                document.Open();
                if (generateFlag == 0) {
                    // 如果generateFlag为0，单页来写
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
                            Bitmap picAtLeft = generateFlag == 1 ? imageList[i] : imageList[i + 1];
                            Bitmap picAtRight = generateFlag == 1 ? imageList[i + 1] : imageList[i];
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
                string pathToSave = luaConfig.PathToSave; // 从lua里读设置的保存路径
                File.WriteAllBytes(pathToSave, ms.ToArray());
            }
        }
        static void Main(string[] args) {
            //for (int i = 0; i < args.Length; i++)
            //{
            //    Console.WriteLine(i + args[i]);
            //}
            string directoryPath = args[0]; // 包含图像的文件夹路径
            int generateFlag = Convert.ToInt32(args[1]); // 0 1 2
            int fastFlag = Convert.ToInt32(args[2]); // 0 1
            CSGlobal.srcDirPath = directoryPath; // 给lua调用的
            luaEnv.DoString(@"config = require 'config'"); // 获取lua内的方法
            luaConfig = luaEnv.Global.Get<IConfig>("config");
            luaConfig.PreProcess();

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
            ImagesToPdf(imageBitmapList, generateFlag, fastFlag);
            foreach (Bitmap bitmap in imageBitmapList) {
                bitmap?.Dispose();
            }
            luaConfig.PostProcess(); // lua方法，定义完成后的动作
            luaEnv.Dispose();
        }
        /// <summary>
        /// 给文件名排序的方法，不使用默认的排序方法，在lua里重写
        /// </summary>
        class StringLenComparer : IComparer<string> {
            int IComparer<string>.Compare(string x, string y) {
                return luaConfig.FilePathComparer(x, y);
            }
        }
    }
}
