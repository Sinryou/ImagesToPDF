using ImgsToPDF.Shared;
using ImgsToPDF.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
                ImagePreviewHelper.ReplaceImage(PicInFolder, null);
                SetFolderOverlay(null);
                _pathToolTip?.Dispose();
                _passwordPromptLock?.Dispose();
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
            PicInFolder.Cursor = NativeMethods.SystemHandCursor;
            MsgLabel.ForeColor = Color.Blue;
            generateModeBox.Items.AddRange([
                Strings.Single,
                Strings.Duplex,
                Strings.DuplexRightToLeft
            ]);
            generateModeBox.SelectedIndex = 0;
            Merge.Enabled = false;
        }

        /// <summary>
        /// 当前选中的离散图片文件列表（处于图片合并模式时非空）。
        /// 若为 null 则表示当前处于目录/压缩包模式。
        /// </summary>
        private List<string> _chosenImageFiles;

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
        /// 避免并发 Core 进程同时弹出多个密码输入窗口，将密码输入串行化
        /// </summary>
        private readonly SemaphoreSlim _passwordPromptLock = new(1, 1);

        /// <summary>
        /// 全局设置界面忙碌/就绪状态，锁定用户输入以防止竞态条件
        /// </summary>
        private void SetUiState(bool isProcessing) {
            _isProcessing = isProcessing;

            StartButton.Enabled = !isProcessing && !string.IsNullOrEmpty(PathLabel.Text);
            generateModeBox.Enabled = !isProcessing;
            FastMode.Enabled = !isProcessing;
            bool isImageMergeMode = _chosenImageFiles != null && _chosenImageFiles.Count > 0;
            Recursive.Enabled = !isProcessing && !isImageMergeMode;
            Merge.Enabled = !isProcessing && !isImageMergeMode && Recursive.Checked;

            toolStripMenuOpenFolder.Enabled = !isProcessing;
            toolStripMenuItemOpenArchive.Enabled = !isProcessing;
            toolStripMenuClearChosen.Enabled = !isProcessing;
            languageToolStripMenuItem.Enabled = !isProcessing;
            toolStripMenuConfigFile.Enabled = !isProcessing;

            var clickableCursor = isProcessing ? Cursors.Default : NativeMethods.SystemHandCursor;
            PicInFolder.Cursor = clickableCursor;
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
                bool hasSupportedItem = files.Any(f =>
                    Directory.Exists(f) ||
                    FolderScanner.CompressExtensions.Contains(Path.GetExtension(f)) ||
                    FolderScanner.ImageExtensions.Contains(Path.GetExtension(f))
                );
                e.Effect = hasSupportedItem ? DragDropEffects.All : DragDropEffects.None;
            }
            catch (Exception) {
                // 某些程序拖入的是虚拟文件，取路径可能失败，直接拒绝
                e.Effect = DragDropEffects.None;
            }
        }

        /// <summary>
        /// 切换文件夹角标的显示与隐藏，避免无角标时透明控件覆盖底层导致穿透失效或拦截点击。
        /// </summary>
        private void SetFolderOverlay(Image image) {
            ImagePreviewHelper.ReplaceImage(FolderImg, image);
            FolderImg.Visible = image != null;
        }

        private void ChooseFileAction(string directoryPath) {
            _chosenImageFiles = null;
            Recursive.Enabled = !_isProcessing;
            Merge.Enabled = !_isProcessing && Recursive.Checked;

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
                    .Where(p => FolderScanner.ImageExtensions.Contains(Path.GetExtension(p)));
                foreach (var imagepath in imagepaths) {
                    try {
                        using var img = ImagePreviewHelper.LoadPreviewImage(imagepath);
                        if (img == null) {
                            continue;
                        }

                        // 按预览控件两倍尺寸（适配高DPI且保证清晰度）生成缩略图，避免大图常驻内存
                        int maxThumbW = Math.Max(PicInFolder.Width * 2, 400);
                        int maxThumbH = Math.Max(PicInFolder.Height * 2, 600);
                        preview = ImagePreviewHelper.CreateThumbnail(img, maxThumbW, maxThumbH);
                        break;
                    }
                    catch (Exception ex) {
                        // 如果文件不是一张合法的图片，则直接跳过
                        Debug.WriteLine($"[ImgsToPDF] Skipped invalid image '{imagepath}': {ex.Message}");
                        continue;
                    }
                }
                ImagePreviewHelper.ReplaceImage(PicInFolder, preview ?? Properties.Resources.no_photo);
                SetFolderOverlay(Properties.Resources.folder);
            }
            else if (FolderScanner.CompressExtensions.Contains(Path.GetExtension(directoryPath))) {
                ImagePreviewHelper.ReplaceImage(PicInFolder, Properties.Resources.compressedFile);
                SetFolderOverlay(null);
            }
            else {
                ImagePreviewHelper.ReplaceImage(PicInFolder, Properties.Resources.no_photo);
                SetFolderOverlay(null);
                MsgLabel.Text = Strings.InvalidPath;
                // 按钮的可用状态可能还是上一次有效选择留下的，否则点击后会把无效路径传给 Core
                StartButton.Enabled = false;
                return;
            }

            StartButton.Enabled = !_isProcessing;
            MsgLabel.Text = Strings.ClickToStart;
        }

        /// <summary>
        /// 选中一组离散图片文件，进入图片合并为一个 PDF 的模式。
        /// </summary>
        private void ChooseImagesAction(List<string> imageFiles) {
            if (imageFiles == null || imageFiles.Count == 0) return;

            // 自然排序（与 Windows 资源管理器文件排序规则一致）
            imageFiles.Sort(FolderScanner.NaturalPathComparer.Instance);
            _chosenImageFiles = imageFiles;

            // 获取第一张可读图片生成高清缩略图
            Bitmap preview = null;
            foreach (var imagepath in imageFiles) {
                try {
                    using var img = ImagePreviewHelper.LoadPreviewImage(imagepath);
                    if (img == null) continue;

                    int maxThumbW = Math.Max(PicInFolder.Width * 2, 400);
                    int maxThumbH = Math.Max(PicInFolder.Height * 2, 600);
                    preview = ImagePreviewHelper.CreateThumbnail(img, maxThumbW, maxThumbH);
                    break;
                }
                catch (Exception ex) {
                    Debug.WriteLine($"[ImgsToPDF] Skipped invalid image '{imagepath}': {ex.Message}");
                }
            }

            ImagePreviewHelper.ReplaceImage(PicInFolder, preview ?? Properties.Resources.no_photo);
            SetFolderOverlay(null); // 图片合并模式不需要文件夹角标

            string firstFile = imageFiles[0];
            string dir = Path.GetDirectoryName(firstFile);
            string dirName = Path.GetFileName(dir);

            if (imageFiles.Count == 1) {
                PathLabel.Text = firstFile;
                _pathToolTip.SetToolTip(PathLabel, firstFile);
            }
            else {
                string countPattern = Strings.SelectedImagesCount;
                if (string.IsNullOrEmpty(countPattern)) {
                    countPattern = "[{0} Images] {1}";
                }
                string displayDir = string.IsNullOrEmpty(dirName) ? firstFile : dirName;
                PathLabel.Text = string.Format(countPattern, imageFiles.Count, displayDir);
                _pathToolTip.SetToolTip(PathLabel, string.Format(
                    "Selected {0} image(s)\nDirectory: {1}\nFirst file: {2}",
                    imageFiles.Count,
                    dir,
                    Path.GetFileName(firstFile)
                ));
            }

            int textWidth = TextRenderer.MeasureText(PathLabel.Text, PathLabel.Font).Width;
            PathLabel.TextAlign = textWidth > PathLabel.Width ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter;

            // 图片合并模式下，递归子目录与合并子目录不适用，禁用并取消勾选
            Recursive.Checked = false;
            Recursive.Enabled = false;
            Merge.Checked = false;
            Merge.Enabled = false;

            StartButton.Enabled = !_isProcessing;
            MsgLabel.Text = Strings.ClickToStart;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e) {
            if (_isProcessing) {
                return;
            }

            // 获得路径
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) {
                return;
            }

            // 提取所有有效的图片文件
            var imageFiles = files
                .Where(f => File.Exists(f) && FolderScanner.ImageExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            // 提取所有容器（文件夹或压缩包）
            var containerFiles = files
                .Where(f => Directory.Exists(f) || FolderScanner.CompressExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            // 纯图片拖入：进入图片合并模式
            if (imageFiles.Count > 0 && containerFiles.Count == 0) {
                ChooseImagesAction(imageFiles);
            }
            else {
                // 现阶段其他情况保持原有行为：处理第一个项
                ChooseFileAction(files[0]);
            }
        }

        private async void StartButton_Click(object sender, EventArgs e) {
            if (_isProcessing) {
                return;
            }

            // 在 UI 线程捕获控件状态快照，后台任务只读取这些局部变量
            string directoryPath = PathLabel.Text;
            if (string.IsNullOrEmpty(directoryPath)) {
                return;
            }

            bool isImageMerge = _chosenImageFiles != null && _chosenImageFiles.Count > 0;
            string manifestPath = null;
            string targetPathForCore = directoryPath;
            string displayTargetPath = directoryPath;

            if (isImageMerge) {
                string firstImg = _chosenImageFiles[0];
                string outDir = Path.GetDirectoryName(firstImg);
                string pdfName = Path.GetFileName(outDir);
                if (string.IsNullOrEmpty(pdfName)) {
                    pdfName = Path.GetFileNameWithoutExtension(firstImg);
                }
                displayTargetPath = Path.Combine(outDir, $"{pdfName}.pdf");

                manifestPath = Path.Combine(Path.GetTempPath(), $"imgstopdf_{Guid.NewGuid():N}.txt");
                var lines = new List<string> {
                    IpcContracts.ManifestOutputPrefix + displayTargetPath
                };
                lines.AddRange(_chosenImageFiles);
                File.WriteAllLines(manifestPath, lines, new UTF8Encoding(false));
                targetPathForCore = manifestPath;
            }

            bool recursive = !isImageMerge && Recursive.Checked;
            bool fastMode = FastMode.Checked;
            bool merge = !isImageMerge && Merge.Checked;
            int layoutIndex = generateModeBox.SelectedIndex;

            MsgLabel.Text = Strings.PDFIsGenerating;
            progressBar.Visible = true;
            progressBar.Maximum = 100;
            progressBar.Value = 50;

            SetUiState(true);

            try {
                var (failures, warnings) = await CoreProcessRunner.ExecuteBatchAsync(
                    targetPathForCore,
                    recursive,
                    fastMode,
                    merge,
                    layoutIndex,
                    displayTargetPath,
                    PromptPasswordOnUiAsync,
                    () => _isClosing || IsDisposed);

                if (_isClosing || IsDisposed) return;

                progressBar.Value = 100;
                if (failures.Count > 0) {
                    MsgLabel.Text = string.Format(Strings.PDFGeneratedWithErrors, failures.Count);
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine + Environment.NewLine, failures),
                        Strings.ErrorTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
                else if (warnings.Count > 0) {
                    MsgLabel.Text = string.Format(Strings.PDFGeneratedWithSkipped, warnings.Count);
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine + Environment.NewLine, warnings),
                        Strings.WarningTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else {
                    MsgLabel.Text = Strings.PDFGenerationSuccess;
                }
            }
            catch (Exception ex) {
                if (_isClosing || IsDisposed) return;

                progressBar.Value = 100;
                MsgLabel.Text = string.Format(Strings.PDFGenerationFailed, ex.Message);
                MessageBox.Show(
                    this,
                    ex.Message,
                    Strings.ErrorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally {
                if (manifestPath != null) {
                    try {
                        if (File.Exists(manifestPath)) {
                            File.Delete(manifestPath);
                        }
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"[ImgsToPDF] Failed to delete temp manifest '{manifestPath}': {ex.Message}");
                    }
                }

                if (!_isClosing && !IsDisposed) {
                    // 无论成功还是出错，都恢复界面交互可用状态，避免界面卡死
                    SetUiState(false);
                }
            }
        }

        /// <summary>
        /// 在 UI 线程弹出密码输入对话框并等待用户输入或取消。
        /// </summary>
        /// <param name="archivePath">需要解压的压缩包路径</param>
        /// <returns>用户输入的密码；若用户取消或窗体关闭则返回 null</returns>
        private async Task<string> PromptPasswordOnUiAsync(string archivePath) {
            if (_isClosing || this.IsDisposed) {
                return null;
            }

            await _passwordPromptLock.WaitAsync();
            try {
                if (_isClosing || this.IsDisposed) {
                    return null;
                }

                var tcs = new TaskCompletionSource<string>();
                if (this.InvokeRequired) {
                    try {
                        this.BeginInvoke(new Action(() => {
                            try {
                                if (_isClosing || this.IsDisposed) {
                                    tcs.TrySetResult(null);
                                    return;
                                }
                                using var dialog = new PasswordDialog(archivePath);
                                if (dialog.ShowDialog(this) == DialogResult.OK) {
                                    tcs.TrySetResult(dialog.Password);
                                }
                                else {
                                    tcs.TrySetResult(null);
                                }
                            }
                            catch (Exception ex) {
                                tcs.TrySetException(ex);
                            }
                        }));
                        return await tcs.Task;
                    }
                    catch (InvalidOperationException) {
                        return null; // 句柄尚未创建或窗体正在关闭
                    }
                }
                else {
                    using var dialog = new PasswordDialog(archivePath);
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Password : null;
                }
            }
            finally {
                _passwordPromptLock.Release();
            }
        }

        private void ToolStripMenuExit_Click(object sender, EventArgs e) {
            this.Close();
        }

        private void ToolStripMenuConfigFile_Click(object sender, EventArgs e) {
            try {
                // 不用 using：Process.Start 返回后记事本仍在运行，立即 Dispose 语义不正确
                Process.Start(AppDomain.CurrentDomain.BaseDirectory + "/Core/config.lua");
            }
            catch (Exception ex) {
                MessageBox.Show(
                    ex.Message,
                    Strings.ErrorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void ToolStripMenuAbout_Click(object sender, EventArgs e) {
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

        /// <summary>
        /// 弹出文件夹选择对话框，把选中的路径交给 ChooseFileAction 处理。
        /// PicInFolder_Click 与 ToolStripMenuOpenFolder_Click 的共用实现。
        /// </summary>
        private void PickFolderAndChoose() {
            if (_isProcessing) return;

            using FolderBrowserDialog dialog = new() {
                Description = Strings.SelectIMGFolder
            };
            if (dialog.ShowDialog() == DialogResult.Cancel) {
                return;
            }
            ChooseFileAction(dialog.SelectedPath.Trim());
        }

        private void ToolStripMenuOpenFolder_Click(object sender, EventArgs e) {
            PickFolderAndChoose();
        }

        private void ToolStripMenuClearChosen_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            _chosenImageFiles = null;
            Recursive.Enabled = !_isProcessing;
            Recursive.Checked = false;
            Merge.Enabled = false;
            Merge.Checked = false;

            ImagePreviewHelper.ReplaceImage(PicInFolder, Properties.Resources.folder);
            SetFolderOverlay(null);
            PathLabel.Text = null;
            PathLabel.TextAlign = ContentAlignment.MiddleCenter;
            _pathToolTip.SetToolTip(PathLabel, null);
            progressBar.Visible = false;
            progressBar.Value = 0;
            StartButton.Enabled = false;
            MsgLabel.Text = Strings.MsgLabelDefault;
        }

        /// <summary>
        /// 保存语言设置并立即重启生效。提示文案按"目标语言"显示而不是取当前 resx：
        /// 此刻 CurrentUICulture 还是旧语言，弹出的确认框必须让用户看懂即将切换成什么。
        /// </summary>
        private void SwitchLanguageAndRestart(string language, string noticeText, string noticeTitle) {
            if (_isProcessing) return;

            Properties.Settings.Default.DefaultLanguage = language;
            Properties.Settings.Default.Save();
            MessageBox.Show(
                noticeText,
                noticeTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            this.Close();
            Application.Restart();
        }

        private void EnglishToolStripMenuItem_Click(object sender, EventArgs e) {
            SwitchLanguageAndRestart(
                "en-US",
                "Application will restart immediately to take effect your language setting.",
                "Notice"
            );
        }

        private void ChineseToolStripMenuItem_Click(object sender, EventArgs e) {
            SwitchLanguageAndRestart(
                "zh-CN",
                "程序将立即重启以生效你的语言设置。",
                "注意"
            );
        }

        private void Recursive_CheckedChanged(object sender, EventArgs e) {
            if (Recursive.Checked) {
                Merge.Enabled = true;
            }
            else {
                Merge.Enabled = false;
                Merge.Checked = false;
            }
        }

        private void ToolStripMenuItemOpenArchive_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            using OpenFileDialog openFileDialog = new();
            openFileDialog.Title = Strings.SelectArchive;
            openFileDialog.Filter = Strings.ArchiveFile + "|*.zip;*.rar;*.7z";
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
                string msg = Strings.WarnGenerating;
                string title = Strings.WarningTitle;
                if (MessageBox.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) {
                    e.Cancel = true;
                    return;
                }
            }
            _isClosing = true;
            base.OnFormClosing(e);
        }

        private void PicInFolder_Click(object sender, EventArgs e) {
            PickFolderAndChoose();
        }
    }
}
