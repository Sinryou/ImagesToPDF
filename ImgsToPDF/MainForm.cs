using ImgsToPDF.Lang;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImgsToPDF {
    public partial class MainForm : Form {
        public MainForm() {
            // 设置中的语言值可能为空或无效（settings 文件被手改/损坏），
            // 构造 CultureInfo 失败时回退到系统当前语言，避免应用无法启动
            string language = Properties.Settings.Default.DefaultLanguage;
            try {
                if (!string.IsNullOrEmpty(language)) {
                    System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(language);
                }
                else {
                    System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.CurrentCulture;
                }
            }
            catch (System.Globalization.CultureNotFoundException) {
                System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.CurrentCulture;
            }

            this.StartPosition = FormStartPosition.CenterScreen; // 窗口居中

            InitializeComponent();

            this.Disposed += (s, e) => {
                ReplaceImage(PicInFolder, null);
                SetFolderOverlay(null);
                _pathToolTip?.Dispose();
            };
        }

        private void MainForm_Load(object sender, EventArgs e) {
            if (System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh")) {
                chineseToolStripMenuItem.Checked = true;
                chineseToolStripMenuItem.Enabled = false;
            }
            else {
                englishToolStripMenuItem.Checked = true;
                englishToolStripMenuItem.Enabled = false;
            }
            FolderImg.BringToFront();
            MsgLabel.ForeColor = Color.Blue;
            generateModeBox.Items.AddRange([
                Extra.ApplyResource(typeof(Extra), "strSingle"),
                Extra.ApplyResource(typeof(Extra), "strDuplex"),
                Extra.ApplyResource(typeof(Extra), "strDuplexRightToLeft")
            ]);
            generateModeBox.SelectedIndex = 0;
            Merge.Enabled = false;
        }

        // 使用 HashSet(StringComparer.OrdinalIgnoreCase) 提高查找效率并自动忽略大小写
        // GDI+ 原生支持解码的图片格式（用于界面缩略图预览）
        private static readonly HashSet<string> gdiPreviewExtensions = [with(StringComparer.OrdinalIgnoreCase), ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif"];
        // 核心转换引擎（Core）支持的所有图片格式（含 libwebp 解码的 .webp，用于目录递归扫描判断）
        private static readonly HashSet<string> coreSupportedExtensions = [with(StringComparer.OrdinalIgnoreCase), ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif", ".webp"];
        private static readonly HashSet<string> imageExtensionsEXIFOrientation = [with(StringComparer.OrdinalIgnoreCase), ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".tif", ".tiff"];
        private static readonly HashSet<string> compressExtensions = [with(StringComparer.OrdinalIgnoreCase), ".zip", ".rar", ".7z"];

        /// <summary>
        /// 当前是否正在后台生成 PDF，用于全局锁定界面交互并防止竞态操作
        /// </summary>
        private bool _isProcessing;

        /// <summary>
        /// 窗体是否已确认关闭，用于阻止关闭瞬间排队中的任务继续拉起新子进程
        /// </summary>
        private volatile bool _isClosing;

        /// <summary>
        /// 用于在悬停时显示完整路径的 ToolTip，防止长路径被截断后用户无法查看
        /// </summary>
        private readonly ToolTip _pathToolTip = new();

        /// <summary>
        /// 全局设置界面忙碌/就绪状态，锁定用户输入以防止竞态条件
        /// </summary>
        private void SetUiState(bool isProcessing) {
            _isProcessing = isProcessing;

            StartButton.Enabled = !isProcessing && !string.IsNullOrEmpty(PathLabel.Text);
            generateModeBox.Enabled = !isProcessing;
            FastMode.Enabled = !isProcessing;
            Recursive.Enabled = !isProcessing;
            Merge.Enabled = !isProcessing && Recursive.Checked;

            toolStripMenuOpenFolder.Enabled = !isProcessing;
            toolStripMenuItemOpenArchive.Enabled = !isProcessing;
            toolStripMenuClearChosen.Enabled = !isProcessing;
            languageToolStripMenuItem.Enabled = !isProcessing;
            toolStripMenuConfigFile.Enabled = !isProcessing;
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e) {
            if (_isProcessing) {
                e.Effect = DragDropEffects.None;
                return;
            }

            // 先判断数据类型，避免 GetData 返回 null 时抛 NullReferenceException
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
                e.Effect = DragDropEffects.None;
                return;
            }
            try {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0) {
                    e.Effect = DragDropEffects.None;
                    return;
                }
                string filePath = files[0];
                if (Directory.Exists(filePath) || compressExtensions.Contains(Path.GetExtension(filePath))) {
                    e.Effect = DragDropEffects.All;
                }
                else {
                    e.Effect = DragDropEffects.None;
                }
            }
            catch (Exception) {
                // 某些程序拖入的是虚拟文件，取路径可能失败，直接拒绝
                e.Effect = DragDropEffects.None;
            }
        }
        /// <summary>
        /// 替换 PictureBox 显示的图像并及时释放旧图像的 GDI+ 句柄。
        /// Properties.Resources.* 每次访问都会反序列化生成新的 Bitmap 实例，
        /// 因此与动态生成的缩略图一样，在被替换或窗体销毁时都应及时 Dispose。
        /// </summary>
        private static void ReplaceImage(PictureBox pictureBox, Image newImage) {
            Image oldImage = pictureBox.Image;
            if (ReferenceEquals(oldImage, newImage)) {
                return;
            }
            pictureBox.Image = newImage;
            oldImage?.Dispose();
        }

        /// <summary>
        /// 切换文件夹角标的显示与隐藏，避免无角标时透明控件覆盖底层导致穿透失效或拦截点击。
        /// </summary>
        private void SetFolderOverlay(Image image) {
            ReplaceImage(FolderImg, image);
            FolderImg.Visible = image != null;
        }

        /// <summary>
        /// 按目标最大宽高限制生成高质量缩略图，避免巨幅大图常驻内存造成内存暴涨
        /// </summary>
        private static Bitmap CreateThumbnail(Image source, int maxWidth, int maxHeight) {
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

        private void ChooseFileAction(string directoryPath) {
            PathLabel.Text = directoryPath;
            _pathToolTip.SetToolTip(PathLabel, directoryPath);

            // 若路径像素宽度超出控件，切换为靠左对齐，确保盘符及前置路径可见并通过 AutoEllipsis 在末尾追加省略号；
            // 路径较短时居中对齐，与上方预览控件保持对称
            int textWidth = TextRenderer.MeasureText(directoryPath, PathLabel.Font).Width;
            PathLabel.TextAlign = textWidth > PathLabel.Width ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter;

            // 检查路径是否有效
            if (Directory.Exists(directoryPath)) {
                Bitmap preview = null;
                IEnumerable<string> imagepaths = Directory.EnumerateFiles(directoryPath)
                    .Where(p => gdiPreviewExtensions.Contains(Path.GetExtension(p)));
                foreach (var imagepath in imagepaths) {
                    try {
                        // 2. 从 Stream 加载可避免文件被 GDI+ 长期占用/锁定
                        using var stream = new FileStream(imagepath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var img = Image.FromStream(stream);

                        if (imageExtensionsEXIFOrientation.Contains(Path.GetExtension(imagepath))) {
                            const int OrientationId = 0x0112;

                            // 3. Array.IndexOf 比 Contains(PropertyIdList) 性能略高，减少不必要的数组遍历
                            if (Array.IndexOf(img.PropertyIdList, OrientationId) != -1) {
                                var property = img.GetPropertyItem(OrientationId);
                                ushort orientation = BitConverter.ToUInt16(property.Value, 0);

                                RotateFlipType rotateFlip = orientation switch {
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

                                if (rotateFlip != RotateFlipType.RotateNoneFlipNone) {
                                    img.RotateFlip(rotateFlip);

                                    // 4. 旋转后需要重置/移除 Orientation 标记，防止后续重复旋转或绘制异常
                                    img.RemovePropertyItem(OrientationId);
                                }
                            }
                        }

                        // 按预览控件两倍尺寸（适配高DPI且保证清晰度）生成缩略图，避免几十上百MB大图常驻内存
                        int maxThumbW = Math.Max(PicInFolder.Width * 2, 400);
                        int maxThumbH = Math.Max(PicInFolder.Height * 2, 600);
                        preview = CreateThumbnail(img, maxThumbW, maxThumbH);
                        break;
                    }
                    catch (Exception ex) {
                        // 如果文件不是一张合法的图片，则直接跳过
                        System.Diagnostics.Debug.WriteLine($"[ImgsToPDF] Skipped invalid image '{imagepath}': {ex.Message}");
                        continue;
                    }
                }
                ReplaceImage(PicInFolder, preview ?? Properties.Resources.no_photo);
                SetFolderOverlay(Properties.Resources.folder);
            }
            else if (compressExtensions.Contains(Path.GetExtension(directoryPath))) {
                ReplaceImage(PicInFolder, Properties.Resources.compressedFile);
                SetFolderOverlay(null);
            }
            else {
                ReplaceImage(PicInFolder, Properties.Resources.no_photo);
                SetFolderOverlay(null);
                MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strInvalidPath");
                return;
            }

            StartButton.Enabled = !_isProcessing;
            MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strClickToStart");
        }
        private void MainForm_DragDrop(object sender, DragEventArgs e) {
            if (_isProcessing) {
                return;
            }

            //获得路径
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) {
                return;
            }
            ChooseFileAction(files[0]);   // 只处理第一个拖入项
        }
        private async void StartButton_Click(object sender, EventArgs e) {
            if (_isProcessing) {
                return;
            }

            // 在 UI 线程捕获控件状态快照，后台任务只读取这些局部变量，
            // 因此不需要关闭跨线程安全检查
            string directoryPath = PathLabel.Text;
            if (string.IsNullOrEmpty(directoryPath)) {
                return;
            }

            bool recursive = Recursive.Checked;
            bool fastMode = FastMode.Checked;
            bool merge = Merge.Checked;
            int layoutIndex = generateModeBox.SelectedIndex;

            MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strPDFIsGenerating");
            progressBar.Visible = true;
            progressBar.Maximum = 100;
            progressBar.Value = 50;

            SetUiState(true);

            try {
                var (failures, warnings) = await ButtonClickActionAsync(directoryPath, recursive, fastMode, merge, layoutIndex);
                if (_isClosing || IsDisposed) return;

                progressBar.Value = 100;
                if (failures.Count > 0) {
                    MsgLabel.Text = string.Format(Extra.ApplyResource(typeof(Extra), "strPDFGeneratedWithErrors"), failures.Count);
                    // 回到 UI 线程统一展示错误，不再在后台线程弹 MessageBox
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine + Environment.NewLine, failures),
                        Extra.ApplyResource(typeof(Extra), "strErrorTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
                else if (warnings.Count > 0) {
                    // 退出码为 0 说明 PDF 已经生成，这里只是部分文件被跳过，不能再报成生成失败
                    MsgLabel.Text = string.Format(Extra.ApplyResource(typeof(Extra), "strPDFGeneratedWithSkipped"), warnings.Count);
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine + Environment.NewLine, warnings),
                        Extra.ApplyResource(typeof(Extra), "strWarningTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else {
                    MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strPDFGenerationSuccess");
                }
            }
            catch (Exception ex) {
                if (_isClosing || IsDisposed) return;

                progressBar.Value = 100;
                MsgLabel.Text = string.Format(Extra.ApplyResource(typeof(Extra), "strPDFGenerationFailed"), ex.Message);
                MessageBox.Show(
                    this,
                    ex.Message,
                    Extra.ApplyResource(typeof(Extra), "strErrorTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally {
                if (!_isClosing && !IsDisposed) {
                    // 无论成功还是出错，都恢复界面交互可用状态，避免界面卡死
                    SetUiState(false);
                }
            }
        }
        /// <summary>
        /// 检查指定文件夹中是否包含至少一张受支持的图片文件。
        /// 使用 EnumerateFiles 惰性枚举，遇到第一张有效图片即返回 true，避免全量遍历产生的性能损耗。
        /// </summary>
        private static bool ContainsSupportedImage(string directoryPath) {
            try {
                return Directory.EnumerateFiles(directoryPath)
                    .Any(f => coreSupportedExtensions.Contains(Path.GetExtension(f)));
            }
            catch (Exception ex) {
                Debug.WriteLine($"[ImgsToPDF] Failed to enumerate files in '{directoryPath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 递归遍历目录树，收集所有包含受支持图片的文件夹路径。
        /// 具备以下防御与优化能力：
        /// 1. 采用栈式迭代（DFS）代替方法递归，彻底规避极端目录层级下的栈溢出风险（StackOverflowException）；
        /// 2. 检查并跳过重解析点（ReparsePoint，如 NTFS Junction、符号链接），同时维护规范化路径的已访问集合，杜绝死循环；
        /// 3. 全面捕获 UnauthorizedAccessException、SecurityException、PathTooLongException 及 IOException，
        ///    遇到无权限或损坏的系统/受限文件夹时跳过并记录调试日志，避免整次扫描任务直接崩溃；
        /// 4. 仅收集实际包含图片的文件夹，过滤空文件夹及纯文本/纯子文件夹目录，避免拉起无意义的 Core 进程或生成单页空白 PDF。
        /// </summary>
        private static List<string> RecursiveFolder(string rootPath, CancellationToken cancellationToken = default) {
            var imageDirs = new List<string>();
            if (string.IsNullOrWhiteSpace(rootPath)) {
                return imageDirs;
            }

            DirectoryInfo rootDirInfo;
            try {
                rootDirInfo = new DirectoryInfo(Path.GetFullPath(rootPath));
                if (!rootDirInfo.Exists) {
                    return imageDirs;
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"[ImgsToPDF] Invalid root path '{rootPath}': {ex.Message}");
                return imageDirs;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<DirectoryInfo>();
            stack.Push(rootDirInfo);

            while (stack.Count > 0) {
                cancellationToken.ThrowIfCancellationRequested();

                DirectoryInfo currentDir = stack.Pop();

                // 统一去除末尾路径分隔符，防止 "C:\Dir\" 与 "C:\Dir" 在 HashSet 中被视为不同路径
                string normalizedPath = currentDir.FullName.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

                if (!visited.Add(normalizedPath)) {
                    continue;
                }

                // 1. 若当前目录包含受支持的图片，则加入待处理列表
                if (ContainsSupportedImage(currentDir.FullName)) {
                    imageDirs.Add(currentDir.FullName);
                }

                // 2. 惰性枚举子目录并压栈
                try {
                    foreach (DirectoryInfo childFolder in currentDir.EnumerateDirectories()) {
                        try {
                            // 跳过重解析点（符号链接、Junction 联接点等），防止死循环
                            if ((childFolder.Attributes & FileAttributes.ReparsePoint) != 0) {
                                Debug.WriteLine($"[ImgsToPDF] Skipped reparse point directory: '{childFolder.FullName}'");
                                continue;
                            }
                            stack.Push(childFolder);
                        }
                        catch (Exception ex) {
                            Debug.WriteLine($"[ImgsToPDF] Failed to inspect child directory '{childFolder.FullName}': {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException || ex is IOException) {
                    Debug.WriteLine($"[ImgsToPDF] Skipped inaccessible subdirectories of '{currentDir.FullName}': {ex.Message}");
                }
                catch (Exception ex) {
                    Debug.WriteLine($"[ImgsToPDF] Unexpected error getting subdirectories of '{currentDir.FullName}': {ex.Message}");
                }
            }

            // 排序输出（注：若希望子目录按倒序压栈以实现正序遍历，也可在压栈时 Reverse，但最终 Sort 最稳妥）
            imageDirs.Sort(StringComparer.OrdinalIgnoreCase);
            return imageDirs;
        }
        /// <summary>
        /// 同时运行的 Core 进程数硬上限：每个进程都是完整 .NET 运行时，
        /// 高核数机器上不设上限会一次拉起几十个进程。
        /// </summary>
        private const int MaxCoreProcessConcurrency = 8;
        /// <summary>
        /// 单个 Core 进程的峰值内存预算（含 .NET 运行时、XLua/libwebp、
        /// 最大单张图片解码及编码缓冲）。按大图最坏情况估算，
        /// 实际峰值通常低于此值。
        /// </summary>
        private const long PerCoreProcessMemoryBudget = 1L << 30; // 1 GB

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        /// <summary>
        /// 按当前可用物理内存估算允许同时运行的 Core 进程数，
        /// 图片很大时避免同时解码过多大图导致内存耗尽。
        /// </summary>
        private static int GetConcurrencyLimitByMemory() {
            var status = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(status)) {
                return int.MaxValue; // 查询失败时不额外限制
            }
            return (int)Math.Max(1, (long)status.ullAvailPhys / PerCoreProcessMemoryBudget);
        }
        /// <summary>
        /// 在后台生成 PDF；错误通过返回值收集，统一回到 UI 线程展示。
        /// 判定标准：Core 进程退出码非 0 才算失败；退出码为 0 但 stderr 有内容
        /// （例如个别图片无法解码被跳过）只算警告，不再把成功当成失败上报。
        /// </summary>
        private async Task<(List<string> failures, List<string> warnings)> ButtonClickActionAsync(string directoryPath, bool recursive, bool fastMode, bool merge, int layoutIndex) {
            var fileName = AppDomain.CurrentDomain.BaseDirectory + @"\Core\ImgsToPDFCore.exe";
            var failureQueue = new ConcurrentQueue<string>();
            var warningQueue = new ConcurrentQueue<string>();

            if (recursive && Directory.Exists(directoryPath)) {
                // 递归收集子目录可能较慢（大量文件夹），放到后台执行
                var dirs = await Task.Run(() => RecursiveFolder(directoryPath));
                if (dirs.Count == 0) {
                    failureQueue.Enqueue(directoryPath + Environment.NewLine + Extra.ApplyResource(typeof(Extra), "strNoImagesFound"));
                    return ([.. failureQueue], [.. warningQueue]);
                }

                // 并发 Core 进程数由任务量、CPU 线程数与可用内存综合决定：
                // - 任务很少时按任务数并发，避免白白拉起多余的完整 .NET 进程；
                // - 任务很多时按 CPU 线程数并发，避免进程间争抢 CPU；
                // - 同时按可用内存估算上限，图片很大时避免同时解码过多大图；
                // - MaxCoreProcessConcurrency 兜底，防止高核数机器一次拉起过多进程。
                int maxConcurrency = Math.Max(1, Math.Min(
                    Math.Min(Environment.ProcessorCount, dirs.Count),
                    Math.Min(GetConcurrencyLimitByMemory(), MaxCoreProcessConcurrency)));
                using var semaphore = new SemaphoreSlim(maxConcurrency);
                var tasks = dirs.Select(async dirPath => {
                    await semaphore.WaitAsync();
                    try {
                        if (_isClosing) return;
                        var (_, stderr, exitCode) = await RunProcessAsync(fileName, BuildCoreArgs(dirPath, fastMode, layoutIndex));
                        CollectProcessResult(dirPath, stderr, exitCode, failureQueue, warningQueue);
                    }
                    finally {
                        semaphore.Release();
                    }
                });
                await Task.WhenAll(tasks);

                if (merge && !_isClosing) {
                    var (_, stderr, exitCode) = await RunProcessAsync(fileName, BuildCoreArgs(directoryPath, fastMode: false, layoutIndex: 0, mergePdfs: true));
                    CollectProcessResult(directoryPath, stderr, exitCode, failureQueue, warningQueue);
                }
            }
            else if (!_isClosing) {
                var (_, stderr, exitCode) = await RunProcessAsync(fileName, BuildCoreArgs(directoryPath, fastMode, layoutIndex));
                CollectProcessResult(directoryPath, stderr, exitCode, failureQueue, warningQueue);
            }
            return ([.. failureQueue], [.. warningQueue]);
        }
        /// <summary>
        /// 归类一次 Core 进程的执行结果。
        /// 失败（退出码非 0）：stderr 为空时补上退出码，避免弹出内容为空的对话框；
        /// 警告（退出码为 0 但 stderr 有内容）：例如个别图片解码失败被跳过，PDF 本身已生成。
        /// 两种情况都会带上对应的目录/压缩包路径，并发处理多个目录时才能定位是谁出的问题。
        /// </summary>
        private static void CollectProcessResult(string targetPath, string stderr, int exitCode, ConcurrentQueue<string> failureQueue, ConcurrentQueue<string> warningQueue) {
            string detail = stderr == null ? string.Empty : stderr.Trim();
            if (exitCode != 0) {
                if (detail.Length == 0) {
                    detail = string.Format(Extra.ApplyResource(typeof(Extra), "strNoErrorOutput"), exitCode);
                }
                failureQueue.Enqueue(targetPath + Environment.NewLine + detail);
            }
            else if (detail.Length > 0) {
                warningQueue.Enqueue(targetPath + Environment.NewLine + detail);
            }
        }
        /// <summary>
        /// 构造传给 Core 进程的命令行参数
        /// </summary>
        private static string[] BuildCoreArgs(string dirPath, bool fastMode, int layoutIndex, bool mergePdfs = false) {
            if (mergePdfs) {
                return ["-d", dirPath, "--merge-pdfs"];
            }
            var args = new List<string> { "-d", dirPath, "-l", layoutIndex.ToString() };
            if (fastMode) {
                args.Add("--fast");
            }
            return [.. args];
        }
        /// <summary>
        /// 运行给定的命令，返回得到的标准输出、标准错误及退出码
        /// </summary>
        /// <param name="fileName">需要运行的指令</param>
        /// <param name="args">命令行参数列表</param>
        /// <returns>元组：(stdout: 标准输出, stderr: 标准错误, exitCode: 进程退出码)</returns>
        private static async Task<(string stdout, string stderr, int exitCode)> RunProcessAsync(string fileName, string[] args) {
            for (int i = 0; i < args.Length; i++) {
                if (args[i].EndsWith(@"\")) {
                    //处理最后若为“\\”，会被转义成“\”，然后变成转义符。
                    args[i] += @"\";
                }
                args[i] = string.Format("\"{0}\"", args[i]);
            }
            // 例Process
            using Process p = new();
            p.StartInfo.FileName = fileName;
            p.StartInfo.Arguments = string.Join(" ", args);
            p.StartInfo.UseShellExecute = false;        // Shell的使用
            p.StartInfo.RedirectStandardInput = true;   // 重定向输入
            p.StartInfo.RedirectStandardOutput = true;  // 重定向输出
            p.StartInfo.RedirectStandardError = true;   // 重定向输出错误
            p.StartInfo.CreateNoWindow = true;          // 设置不显示窗口
            // 必须与 Core 内 Console.OutputEncoding 的设置保持一致：
            // 不指定时 .NET Framework 用 Encoding.Default（ANSI 码页）解码，
            // 而 Core 输出的是 UTF-8，含中文的路径会在错误提示里变成乱码。
            var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            p.StartInfo.StandardOutputEncoding = utf8NoBom;
            p.StartInfo.StandardErrorEncoding = utf8NoBom;

            // 显式告知 Core「我是 GUI 拉起的」，它才会开启父进程看门狗：
            // 该看门狗把 stdin 当作父进程存活管道，一旦读到 EOF 就取消任务并在宽限期后自毁，
            // 避免 GUI 退出后残留孤儿进程。Core 不能靠 Console.IsInputRedirected 自行判断——
            // 那会把「管道/重定向调用命令行」也误判成父进程已退出，导致 Core 完全无法被脚本调用。
            p.StartInfo.EnvironmentVariables["IMGTSPDF_WATCH_PARENT"] = "1";

            p.Start();

            // 同时异步读取 stdout 与 stderr，避免管道缓冲写满时互相阻塞导致死锁
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            var outputs = await Task.WhenAll(stdoutTask, stderrTask);

            // 适配 .NET Framework：使用 Task.Run 包装同步的 WaitForExit
            await Task.Run(() => p.WaitForExit());

            return (outputs[0], outputs[1], p.ExitCode);
        }
        private void toolStripMenuExit_Click(object sender, EventArgs e) {
            this.Close();
        }
        private void toolStripMenuConfigFile_Click(object sender, EventArgs e) {
            try {
                // 不用 using：Process.Start 返回后记事本仍在运行，立即 Dispose 语义不正确；
                // 且文件不存在或未关联打开程序时会抛 Win32Exception，需要提示而不是崩溃
                Process.Start(AppDomain.CurrentDomain.BaseDirectory + "/Core/config.lua");
            }
            catch (Exception ex) {
                MessageBox.Show(
                    ex.Message,
                    Extra.ApplyResource(typeof(Extra), "strErrorTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
        private void toolStripMenuAbout_Click(object sender, EventArgs e) {
            var assembly = Assembly.GetExecutingAssembly();
            string aboutText = "ImagesToPDF v" + assembly.GetName().Version;
            // 用 GetCustomAttribute 并按判空处理，避免特性缺失时抛 IndexOutOfRangeException
            var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
            if (copyright != null && !string.IsNullOrEmpty(copyright.Copyright)) {
                aboutText += "\n" + copyright.Copyright + " Under MIT License.";
            }
            MessageBox.Show(
                aboutText,
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                0,
                "https://github.com/Sinryou/ImagesToPDF"
            );
        }
        private void toolStripMenuOpenFolder_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            using FolderBrowserDialog dialog = new() {
                Description = Extra.ApplyResource(typeof(Extra), "strSelectIMGFolder")
            };
            if (dialog.ShowDialog() == DialogResult.Cancel) {
                return;
            }
            string directoryPath = dialog.SelectedPath.Trim();
            ChooseFileAction(directoryPath);
        }
        private void toolStripMenuClearChosen_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            ReplaceImage(PicInFolder, Properties.Resources.folder);
            SetFolderOverlay(null);
            PathLabel.Text = null;
            PathLabel.TextAlign = ContentAlignment.MiddleCenter;
            _pathToolTip.SetToolTip(PathLabel, null);
            progressBar.Visible = false;
            progressBar.Value = 0;
            StartButton.Enabled = false;
            MsgLabel.Text = Extra.ApplyResource(this.GetType(), "MsgLabel.Text");
        }

        private void englishToolStripMenuItem_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            Properties.Settings.Default.DefaultLanguage = "en-US";
            Properties.Settings.Default.Save();
            MessageBox.Show(
                "Application will restart immediately to take effect your language setting.",
                "Notice",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            this.Close();
            Application.Restart();
        }

        private void chineseToolStripMenuItem_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            Properties.Settings.Default.DefaultLanguage = "zh-CN";
            Properties.Settings.Default.Save();
            MessageBox.Show(
                "程序将立即重启以生效你的语言设置。",
                "注意",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            this.Close();
            Application.Restart();
        }

        private void Recursive_CheckedChanged(object sender, EventArgs e) {
            if (Recursive.Checked) {
                Merge.Enabled = true;
            }
            else {
                Merge.Enabled = false; Merge.Checked = false;
            }
        }

        private void toolStripMenuItemOpenArchive_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            using OpenFileDialog openFileDialog = new();
            // 设置对话框标题
            openFileDialog.Title = Extra.ApplyResource(typeof(Extra), "strSelectArchive");

            openFileDialog.Filter = Extra.ApplyResource(typeof(Extra), "strArchiveFile") + "|*.zip;*.rar;*.7z";

            // 默认选中第一个筛选器（压缩文件）
            openFileDialog.FilterIndex = 1;

            // 不允许选择多个文件
            openFileDialog.Multiselect = false;

            if (openFileDialog.ShowDialog() == DialogResult.OK) {
                string selectedFile = openFileDialog.FileName;
                ChooseFileAction(selectedFile);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (_isProcessing) {
                string msg = Extra.ApplyResource(typeof(Extra), "strWarnGenerating");
                string title = Extra.ApplyResource(typeof(Extra), "strWarningTitle");
                if (MessageBox.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) {
                    e.Cancel = true;
                    return;
                }
            }
            _isClosing = true;
            base.OnFormClosing(e);
        }

        private void PicInFolder_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            using FolderBrowserDialog dialog = new() {
                Description = Extra.ApplyResource(typeof(Extra), "strSelectIMGFolder")
            };
            if (dialog.ShowDialog() == DialogResult.Cancel) {
                return;
            }
            string directoryPath = dialog.SelectedPath.Trim();
            ChooseFileAction(directoryPath);
        }
    }
}
