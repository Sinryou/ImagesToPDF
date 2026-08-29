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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImgsToPDF {
    public partial class ImgsToPDF : Form {
        public ImgsToPDF() {
            string language = Properties.Settings.Default.DefaultLanguage != "" ? Properties.Settings.Default.DefaultLanguage : System.Globalization.CultureInfo.CurrentCulture.Name;
            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(language);

            this.StartPosition = FormStartPosition.CenterScreen; // 窗口居中

            InitializeComponent();
        }

        private void ImgsToPDF_Load(object sender, EventArgs e) {
            if (System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh")) {
                chineseToolStripMenuItem.Checked = true;
                chineseToolStripMenuItem.Enabled = false;
            }
            else {
                englishToolStripMenuItem.Checked = true;
                englishToolStripMenuItem.Enabled = false;
            }
            //FolderImg.SizeMode = PictureBoxSizeMode.Zoom;
            //PicInFolder.SizeMode = PictureBoxSizeMode.Zoom;
            MsgLabel.ForeColor = Color.Blue;
            generateModeBox.Items.AddRange([
                Extra.ApplyResource(typeof(Extra), "strSingle"),
                Extra.ApplyResource(typeof(Extra), "strDuplex"),
                Extra.ApplyResource(typeof(Extra), "strDuplexRightToLeft")
            ]);
            generateModeBox.SelectedIndex = 0;
            Merge.Enabled = false;
        }
        readonly HashSet<string> compressExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };
        /// <summary>
        /// 当前动态创建的预览位图（需要手动释放）。
        /// Properties.Resources.* 返回的是缓存的单例，绝不能 Dispose。
        /// </summary>
        private Bitmap _previewImage;
        private void ImgsToPDF_DragEnter(object sender, DragEventArgs e) {
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
        /// 释放动态创建的预览位图。
        /// 注意：Properties.Resources.* 返回的是缓存的单例，不能在这里 Dispose，
        /// 否则再次赋值时会拿到已释放的实例。
        /// </summary>
        private void DisposePreviewImage() {
            _previewImage?.Dispose();
            _previewImage = null;
        }
        private void ChooseFileAction(string directoryPath) {
            DisposePreviewImage();

            PathLabel.Text = directoryPath;

            // 检查路径是否有效
            if (Directory.Exists(directoryPath)) {
                PicInFolder.Image = Properties.Resources.no_photo;
                FolderImg.Image = Properties.Resources.folder;
                // 1. 使用 HashSet(StringComparer.OrdinalIgnoreCase) 提高查找效率并自动忽略大小写
                HashSet<string> imageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif" };

                HashSet<string> imageExtensionsEXIFOrientation = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".tif", ".tiff" };

                IEnumerable<string> imagepaths = Directory.EnumerateFiles(directoryPath)
                    .Where(p => imageExtensions.Contains(Path.GetExtension(p)));
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

                        _previewImage = new Bitmap(img);
                        PicInFolder.Image = _previewImage;
                        break;
                    }
                    catch (Exception ex) {
                        // 如果文件不是一张合法的图片，则直接跳过
                        System.Diagnostics.Debug.WriteLine($"[ImgsToPDF] Skipped invalid image '{imagepath}': {ex.Message}");
                        continue;
                    }
                }
            }
            else if (compressExtensions.Contains(Path.GetExtension(directoryPath))) {
                PicInFolder.Image = Properties.Resources.compressedFile;
                FolderImg.Image = null;
            }
            else {
                PicInFolder.Image = Properties.Resources.no_photo;
                FolderImg.Image = null;
                MsgLabel.Text = "Invalid directory path";
                return;
            }

            StartButton.Enabled = true;
            MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strClickToStart");
        }
        private void ImgsToPDF_DragDrop(object sender, DragEventArgs e) {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];       //获得路径
            if (files == null || files.Length == 0) {
                return;
            }
            ChooseFileAction(files[0]);   // 只处理第一个拖入项
        }
        private async void StartButton_Click(object sender, EventArgs e) {
            // 在 UI 线程捕获控件状态快照，后台任务只读取这些局部变量，
            // 因此不需要关闭跨线程安全检查
            string directoryPath = PathLabel.Text;
            bool recursive = Recursive.Checked;
            bool fastMode = FastMode.Checked;
            bool merge = Merge.Checked;
            int layoutIndex = generateModeBox.SelectedIndex;

            MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strPDFIsGenerating");
            progressBar.Visible = true;
            progressBar.Maximum = 100;
            progressBar.Value = 50;
            StartButton.Enabled = false;

            try {
                var errors = await ButtonClickActionAsync(directoryPath, recursive, fastMode, merge, layoutIndex);
                progressBar.Value = 100;
                if (errors.Count > 0) {
                    MsgLabel.Text = string.Format(Extra.ApplyResource(typeof(Extra), "strPDFGeneratedWithErrors"), errors.Count);
                    // 回到 UI 线程统一展示错误，不再在后台线程弹 MessageBox
                    MessageBox.Show(
                        string.Join(Environment.NewLine + Environment.NewLine, errors),
                        Extra.ApplyResource(typeof(Extra), "strErrorTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
                else {
                    MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strPDFGenerationSuccess");
                }
            }
            catch (Exception ex) {
                progressBar.Value = 100;
                MsgLabel.Text = string.Format(Extra.ApplyResource(typeof(Extra), "strPDFGenerationFailed"), ex.Message);
                MessageBox.Show(
                    ex.Message,
                    Extra.ApplyResource(typeof(Extra), "strErrorTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally {
                // 无论成功还是出错，都恢复按钮可用状态，避免界面卡死
                StartButton.Enabled = true;
            }
        }
        private static List<string> RecursiveFolder(string path, List<string> dirs) {
            dirs.Add(path);
            var TheFolder = new DirectoryInfo(path);
            foreach (var childFolder in TheFolder.GetDirectories()) {
                RecursiveFolder(childFolder.FullName, dirs);
            }
            return dirs;
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
        /// </summary>
        private async Task<List<string>> ButtonClickActionAsync(string directoryPath, bool recursive, bool fastMode, bool merge, int layoutIndex) {
            var fileName = AppDomain.CurrentDomain.BaseDirectory + @"\Core\ImgsToPDFCore.exe";
            var errorQueue = new ConcurrentQueue<string>();

            if (recursive && Directory.Exists(directoryPath)) {
                // 递归收集子目录可能较慢（大量文件夹），放到后台执行
                var dirs = await Task.Run(() => RecursiveFolder(directoryPath, []));

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
                        var (_, stderr) = await RunProcessAsync(fileName, BuildCoreArgs(dirPath, fastMode, layoutIndex));
                        if (stderr.Length > 0) {
                            errorQueue.Enqueue(stderr);
                        }
                    }
                    finally {
                        semaphore.Release();
                    }
                });
                await Task.WhenAll(tasks);

                if (merge) {
                    var (_, stderr) = await RunProcessAsync(fileName, BuildCoreArgs(directoryPath, fastMode: false, layoutIndex: 0, mergePdfs: true));
                    if (stderr.Length > 0) {
                        errorQueue.Enqueue(stderr);
                    }
                }
            }
            else {
                var (_, stderr) = await RunProcessAsync(fileName, BuildCoreArgs(directoryPath, fastMode, layoutIndex));
                if (stderr.Length > 0) {
                    errorQueue.Enqueue(stderr);
                }
            }
            return errorQueue.ToList();
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
            return args.ToArray();
        }
        /// <summary>
        /// 运行给定的命令，返回得到的标准输出及标准错误
        /// </summary>
        /// <param name="fileName">需要运行的指令</param>
        /// <returns>元组：(stdout:标准输出, stderr:标准错误)</returns>
        private static async Task<(string stdout, string stderr)> RunProcessAsync(string fileName, string[] args) {
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
            p.Start();
            // 同时异步读取 stdout 与 stderr，避免管道缓冲写满时互相阻塞导致死锁
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            var outputs = await Task.WhenAll(stdoutTask, stderrTask);
            return (outputs[0], outputs[1]);
        }
        private void toolStripMenuExit_Click(object sender, EventArgs e) {
            this.Close();
        }
        private void toolStripMenuConfigFile_Click(object sender, EventArgs e) {
            using var _ = Process.Start(AppDomain.CurrentDomain.BaseDirectory + "/Core/config.lua");
        }
        private void toolStripMenuAbout_Click(object sender, EventArgs e) {
            MessageBox.Show(
                "ImagesToPDF v" + Assembly.GetExecutingAssembly().GetName().Version + "\n"
                + ((AssemblyCopyrightAttribute)Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0]).Copyright + " Under MIT License.",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                0,
                "https://github.com/Sinryou/ImagesToPDF"
            );
        }
        private void toolStripMenuOpenFolder_Click(object sender, EventArgs e) {
            FolderBrowserDialog dialog = new() {
                Description = Extra.ApplyResource(typeof(Extra), "strSelectIMGFolder")
            };
            if (dialog.ShowDialog() == DialogResult.Cancel) {
                return;
            }
            string directoryPath = dialog.SelectedPath.Trim();
            ChooseFileAction(directoryPath);
        }
        private void toolStripMenuClearChosen_Click(object sender, EventArgs e) {
            DisposePreviewImage();
            PicInFolder.Image = Properties.Resources.folder;
            FolderImg.Image = null;
            PathLabel.Text = null;
            StartButton.Enabled = false;
            MsgLabel.Text = Extra.ApplyResource(this.GetType(), "MsgLabel.Text");
        }

        private void englishToolStripMenuItem_Click(object sender, EventArgs e) {
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
    }
}
