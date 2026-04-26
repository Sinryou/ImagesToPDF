using iTextSharp.text;
using iTextSharp.text.pdf;
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
            }
            ImagesToPdf(imageBitmapList, layout, fastFlag);
            foreach (Bitmap bitmap in imageBitmapList) {
                bitmap?.Dispose();
            }
        }
        /// <summary>
        /// 合并PDF文件
        /// </summary>
        /// <param name="inFiles">待合并文件列表</param>
        /// <param name="outFile">合并生成的文件名称</param>
        public static void PdfMerge(List<String> inFiles, String outFile) {
            // 1. 实例化比较器
            var comparer = new StringLenComparer();
            // 2. 调用 Sort 方法并传入比较器
            inFiles.Sort(comparer);
            using (var stream = new FileStream(outFile, FileMode.Create)) {
                using (var doc = new Document()) {
                    using (var pdf = new PdfCopy(doc, stream)) {
                        doc.Open();
                        inFiles.ForEach(file => {
                            if (File.Exists(file)) {
                                var reader = new PdfReader(file);
                                for (int i = 0; i < reader.NumberOfPages; i++) {
                                    var page = pdf.GetImportedPage(reader, i + 1);
                                    pdf.AddPage(page);
                                }
                                pdf.FreeReader(reader);
                                reader.Close();
                            }
                        });
                    }
                }
            }
        }
        public static void PdfMergeWithHierarchicalOutlines(List<string> inFiles, string outFile) {
            // 1. 排序逻辑
            var comparer = new StringLenComparer();
            inFiles.Sort(comparer);

            // 用于缓存已经创建过的文件夹书签，避免重复创建
            var folderOutlineCache = new Dictionary<string, PdfOutline>();

            using (var stream = new FileStream(outFile, FileMode.Create)) {
                using (var doc = new Document()) {
                    using (var pdf = new PdfCopy(doc, stream)) {
                        doc.Open();

                        int currentPage = 1;
                        PdfOutline root = pdf.RootOutline;

                        foreach (var file in inFiles) {
                            if (!File.Exists(file)) continue;

                            using (var reader = new PdfReader(file)) {
                                int pageCount = reader.NumberOfPages;

                                // --- 核心逻辑：处理层级书签 ---

                                // 获取父文件夹名称 (例如 "第1话")
                                string folderName = Path.GetFileName(Path.GetDirectoryName(file));
                                // 获取文件名 (例如 "第1话.pdf")
                                string fileName = Path.GetFileNameWithoutExtension(file);

                                // 定义跳转动作：跳转到当前文件的第一页
                                PdfAction action = PdfAction.GotoLocalPage(currentPage,
                                                   new PdfDestination(PdfDestination.FITH), pdf);

                                PdfOutline parentNode = root;

                                // 如果文件夹名有效且不是根目录，则创建/获取一级书签
                                if (!string.IsNullOrEmpty(folderName) && folderName != "abc") {
                                    if (!folderOutlineCache.ContainsKey(folderName)) {
                                        // 创建一级目录节点
                                        var folderNode = new PdfOutline(root, action, folderName);
                                        folderOutlineCache[folderName] = folderNode;
                                    }
                                    parentNode = folderOutlineCache[folderName];
                                }

                                // 在父节点下创建具体文件的二级书签
                                // 如果文件名和文件夹名完全一样，可以考虑跳过这一级，直接用文件夹书签指向它
                                if (fileName != folderName) {
                                    new PdfOutline(parentNode, action, fileName);
                                }

                                // --- 书签逻辑结束 ---

                                // 复制页面
                                for (int i = 1; i <= pageCount; i++) {
                                    pdf.AddPage(pdf.GetImportedPage(reader, i));
                                }

                                currentPage += pageCount;
                                pdf.FreeReader(reader);
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// 手动实现相对路径获取（兼容 .NET Framework）
        /// </summary>
        static string GetRelativePath(string rootPath, string fullPath) {
            // 确保路径以目录分隔符结尾，避免 abc 与 abcd 混淆
            if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())) {
                rootPath += Path.DirectorySeparatorChar;
            }

            Uri rootUri = new Uri(rootPath);
            Uri fullUri = new Uri(fullPath);

            // 计算相对路径
            Uri relativeUri = rootUri.MakeRelativeUri(fullUri);
            // 将 Uri 格式转回系统路径格式（处理斜杠方向和空格转义 %20）
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
        }
        public static void PdfMergeWithDeepOutlines(List<string> inFiles, string outFile, string rootPath) {
            inFiles.Sort(new StringLenComparer());
            var outlineCache = new Dictionary<string, PdfOutline>();

            using (var stream = new FileStream(outFile, FileMode.Create)) {
                using (var doc = new Document()) {
                    using (var pdf = new PdfCopy(doc, stream)) {
                        doc.Open();

                        int currentPage = 1;
                        PdfOutline rootOutline = pdf.RootOutline;

                        foreach (var file in inFiles) {
                            if (!File.Exists(file)) continue;

                            using (var reader = new PdfReader(file)) {
                                int pageCount = reader.NumberOfPages;

                                // 1. 使用兼容方法获取相对路径
                                string relativePath = GetRelativePath(rootPath, file);
                                // 2. 切分目录层级
                                string[] pathParts = relativePath.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                                PdfOutline parent = rootOutline;
                                string currentPathAccumulator = rootPath;

                                // 3. 迭代文件夹层级 (不含最后一个文件名)
                                for (int i = 0; i < pathParts.Length - 1; i++) {
                                    string folderName = pathParts[i];
                                    currentPathAccumulator = Path.Combine(currentPathAccumulator, folderName);

                                    if (!outlineCache.ContainsKey(currentPathAccumulator)) {
                                        PdfAction folderAction = PdfAction.GotoLocalPage(currentPage,
                                                                 new PdfDestination(PdfDestination.FITH), pdf);
                                        // 创建并缓存文件夹书签
                                        outlineCache[currentPathAccumulator] = new PdfOutline(parent, folderAction, folderName);
                                    }
                                    parent = outlineCache[currentPathAccumulator];
                                }

                                // 4. 创建文件书签
                                string fileName = Path.GetFileNameWithoutExtension(file);
                                PdfAction fileAction = PdfAction.GotoLocalPage(currentPage,
                                                       new PdfDestination(PdfDestination.FITH), pdf);

                                // 挂载到最后一级文件夹下
                                new PdfOutline(parent, fileAction, fileName);

                                // 5. 复制页面
                                for (int i = 1; i <= pageCount; i++) {
                                    pdf.AddPage(pdf.GetImportedPage(reader, i));
                                }

                                currentPage += pageCount;
                                pdf.FreeReader(reader);
                            }
                        }
                    }
                }
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
