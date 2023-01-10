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
        public static Bitmap CombineBitmap(Bitmap bm1, Bitmap bm2) {
            //var width = Math.Max(bm1.Width, bm2.Width);
            var width = bm1.Width + bm2.Width + 10;
            //var height = bm1.Height + bm2.Height + 10;
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
            //canavas.DrawImage(bm2, 0, bm1.Height + 10, bm2.Width, bm2.Height);
            canavas.DrawImage(bm2, bm1.Width + 10, 0, bm2.Width, bm2.Height);
            bm1.Dispose();
            bm2.Dispose();
            return bitMap;
        }
        static public void ImagesToPdf(List<Bitmap> imageList, int generateFlag, int fastFlag) {
            using (var ms = new MemoryStream()) {
                var document = new iTextSharp.text.Document(PageSize.A4, 0, 0, 0, 0);
                iTextSharp.text.pdf.PdfWriter.GetInstance(document, ms).SetFullCompression();
                iTextSharp.text.Rectangle pageSize;
                iTextSharp.text.Image image;
                document.Open();
                if (generateFlag == 0) {
                    // 如果generateFlag为0，单页来写
                    foreach (var imagePic in imageList) {
                        pageSize = new iTextSharp.text.Rectangle(0, 0, imagePic.Width, imagePic.Height);
                        document.SetPageSize(pageSize);
                        document.NewPage();
                        // webp直接转为bmp写，否则报错；其他的按读入的格式写
                        if (fastFlag == 1) {
                            image = iTextSharp.text.Image.GetInstance(imagePic, System.Drawing.Imaging.ImageFormat.Jpeg);
                        }
                        else if (imagePic.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.MemoryBmp)) {
                            image = iTextSharp.text.Image.GetInstance(imagePic, System.Drawing.Imaging.ImageFormat.Bmp);
                        }
                        else {
                            image = iTextSharp.text.Image.GetInstance(imagePic, imagePic.RawFormat);
                        }
                        document.PageCount = document.PageNumber + 1;
                        document.Add(image);
                        // 释放位图占用资源
                        imagePic.Dispose();
                    }
                }
                else {
                    for (int i = 0; i < imageList.Count; i++) {
                        if (imageList[i].Height >= imageList[i].Width && imageList[i + 1].Height >= imageList[i + 1].Width) {   // 如果图片长大于宽且下一张也如此，把他们拼起来
                            Bitmap picAtLeft, picAtRight;
                            switch (generateFlag) {
                                // 双页写，左往右
                                case 1:
                                    picAtLeft = imageList[i]; picAtRight = imageList[i + 1];
                                    break;
                                // 双页写，右往左
                                default:
                                    picAtLeft = imageList[i + 1]; picAtRight = imageList[i];
                                    break;
                            }
                            using (var combinedBitmap = CombineBitmap(picAtLeft, picAtRight)) {
                                pageSize = new iTextSharp.text.Rectangle(0, 0, combinedBitmap.Width, combinedBitmap.Height);
                                document.SetPageSize(pageSize);
                                document.NewPage();
                                if (fastFlag == 1) {
                                    image = iTextSharp.text.Image.GetInstance(combinedBitmap, System.Drawing.Imaging.ImageFormat.Jpeg);
                                }
                                else if (combinedBitmap.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.MemoryBmp)) {
                                    image = iTextSharp.text.Image.GetInstance(combinedBitmap, System.Drawing.Imaging.ImageFormat.Bmp);
                                }
                                else {
                                    image = iTextSharp.text.Image.GetInstance(combinedBitmap, combinedBitmap.RawFormat);
                                }
                            }
                            imageList[i].Dispose();
                            imageList[i + 1].Dispose();
                            i++;
                        }
                        else {   // 其他情况，不管他直接单页写
                            pageSize = new iTextSharp.text.Rectangle(0, 0, imageList[i].Width, imageList[i].Height);
                            document.SetPageSize(pageSize);
                            document.NewPage();
                            if (fastFlag == 1) {
                                image = iTextSharp.text.Image.GetInstance(imageList[i], System.Drawing.Imaging.ImageFormat.Jpeg);
                            }
                            else if (imageList[i].RawFormat.Equals(System.Drawing.Imaging.ImageFormat.MemoryBmp)) {
                                image = iTextSharp.text.Image.GetInstance(imageList[i], System.Drawing.Imaging.ImageFormat.Bmp);
                            }
                            else {
                                image = iTextSharp.text.Image.GetInstance(imageList[i], imageList[i].RawFormat);
                            }
                            imageList[i].Dispose();
                        }
                        document.PageCount = document.PageNumber + 1;
                        document.Add(image);
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

            string[] imagepaths = Directory.GetFiles(directoryPath); // 读取文件夹下所有文件路径
            imagepaths = imagepaths.OrderBy(p => p, new StringLenComparer()).ToArray(); // 排序，方法定义在lua内
            List<Bitmap> imageBitmapList = new List<Bitmap>();
            foreach (string imagepath in imagepaths) {
                var fileExt = Path.GetExtension(imagepath).ToLower();
                if (fileExt == ".webp") {
                    // 读取webp文件的方法
                    using (WebP webp = new WebP()) {
                        Bitmap srcImage = webp.Load(imagepath);
                        imageBitmapList.Add(srcImage);
                    }
                }
                else {
                    try {
                        Bitmap srcImage = new Bitmap(imagepath);
                        imageBitmapList.Add(srcImage);
                    }
                    catch (Exception) {
                        // 不是图片文件直接throw
                        //throw;
                    }
                }
            }
            ImagesToPdf(imageBitmapList, generateFlag, fastFlag);
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
