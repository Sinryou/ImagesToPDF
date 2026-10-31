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
            MsgLabel.ForeColor = Color.Blue;
            _layoutChoices = [
                (Strings.Single, Shared.Layout.Single),
                (Strings.Duplex, Shared.Layout.DuplexLeftToRight),
                (Strings.DuplexRightToLeft, Shared.Layout.DuplexRightToLeft)
            ];
            generateModeBox.Items.AddRange([.. _layoutChoices.Select(choice => choice.Display)]);
            generateModeBox.SelectedIndex = 0;

            // 启动时按初始状态（无任何选择）推导全部控件可用性
            UpdateControlStates();
        }

        /// <summary>
        /// 当前目标选择状态（模式 + 载荷，不可变、整体替换）。
        /// 唯一转移入口是 <see cref="SetSelection"/>；所有模式判定只读
        /// <see cref="SelectionState"/> 的谓词，不再从控件文本反推状态。
        /// 定义见本文件末尾的 SelectionMode / SelectionState。
        /// </summary>
        private SelectionState _selection = SelectionState.Empty;

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
        /// 版式下拉框选项：显示文本与 ImgsToPDF.Shared.Layout 值显式配对。
        /// 选中项直接取 Value 作为 -l 参数传给 Core，
        /// 不再把 SelectedIndex 当枚举数值使用（顺序与数值解耦，仅存在这一处映射）。
        /// 注意：Control.Layout 是继承的事件，此处须用全限定名 ImgsToPDF.Shared.Layout 消除遮蔽。
        /// 在 MainForm_Load 中初始化，确保读取 Strings.* 时 UI 文化已就绪。
        /// </summary>
        private (string Display, Shared.Layout Value)[] _layoutChoices;

        /// <summary>
        /// 避免并发 Core 进程同时弹出多个密码输入窗口，将密码输入串行化
        /// </summary>
        private readonly SemaphoreSlim _passwordPromptLock = new(1, 1);

        /// <summary>
        /// UpdateControlStates 的重入保护：方法内部设置 Checked 会触发
        /// Recursive_CheckedChanged 递归调用自身，用标志拦下重复推导
        /// </summary>
        private bool _isSyncingControlStates;

        /// <summary>
        /// 全局设置界面忙碌/就绪状态，锁定用户输入以防止竞态条件。
        /// 控件的可用状态由 <see cref="UpdateControlStates"/> 统一推导，此处不直接触碰控件。
        /// </summary>
        private void SetUiState(bool isProcessing) {
            _isProcessing = isProcessing;
            UpdateControlStates();
        }

        /// <summary>
        /// 全局唯一的控件状态同步点：从「选择模式 + 当前路径 + 忙碌状态」推导全部控件的
        /// Enabled，并维护两条勾选不变量：
        /// 1. 递归子目录与合并子目录仅对目录模式有意义，其它模式下强制取消勾选并禁用；
        /// 2. 合并子目录依赖递归子目录，后者取消勾选时前者随之取消。
        /// 任何改变上述状态的代码（选择目标、开始/结束处理、清除、用户勾选）完成后都必须调用
        /// 本方法；禁止在调用方手工零散设置控件 Enabled/Checked，避免状态同步逻辑漂移。
        /// </summary>
        private void UpdateControlStates() {
            if (_isSyncingControlStates) {
                return;
            }
            _isSyncingControlStates = true;
            try {
                bool isProcessing = _isProcessing;
                SelectionState selection = _selection;
                // 递归与合并子目录仅对路径模式下的真实文件夹生效；压缩包与两种合并模式一律禁用
                // （压缩包由 Core 侧固定按「整体解压 + 递归生成单个 PDF」处理，config.lua 的既有行为）。
                // IsRealDirectory/IsArchiveFile 每次实时重判文件系统，目录被删/网络盘断开后勾选随之收敛
                bool supportsRecursion = selection.Mode == SelectionMode.None || (selection.Mode == SelectionMode.Path && !selection.IsArchiveFile);

                Recursive.Enabled = !isProcessing && supportsRecursion;
                if (!supportsRecursion) {
                    Recursive.Checked = false;
                }

                Merge.Enabled = !isProcessing && supportsRecursion && Recursive.Checked;
                if (!supportsRecursion || !Recursive.Checked) {
                    Merge.Checked = false;
                }

                // 版式与快速压缩对 PDF 合并无意义（合并只搬移页面，不重排版、不重编码）
                generateModeBox.Enabled = !isProcessing && !selection.IsPdfMerge;
                FastMode.Enabled = !isProcessing && !selection.IsPdfMerge;

                // 无效路径时 IsStartable 为 false，开始按钮自然禁用，
                // 不会把无效路径传给 Core
                StartButton.Enabled = !isProcessing && selection.IsStartable;

                toolStripMenuOpenFolder.Enabled = !isProcessing;
                toolStripMenuItemOpenArchive.Enabled = !isProcessing;
                chooseImagesToolStripMenuItem.Enabled = !isProcessing;
                choosePDFsToolStripMenuItem.Enabled = !isProcessing;
                toolStripMenuClearChosen.Enabled = !isProcessing;
                languageToolStripMenuItem.Enabled = !isProcessing;
                toolStripMenuConfigFile.Enabled = !isProcessing;

                PicInFolder.Cursor = isProcessing ? Cursors.Default : NativeMethods.SystemHandCursor;
            }
            finally {
                _isSyncingControlStates = false;
            }
        }

        /// <summary>
        /// 目标选择的唯一转移入口：整体替换 <see cref="_selection"/> 并渲染全部纯派生视图
        /// （路径标签文本/悬停提示/对齐、消息标签文案），最后经状态同步点刷新控件可用性。
        /// 昂贵的派生（首图扫描、缩略图解码、预览图与角标替换）仍由各动作方法自行完成，不在此处执行。
        /// </summary>
        private void SetSelection(SelectionState selection) {
            _selection = selection ?? SelectionState.Empty;

            (string text, string toolTip) = _selection.Describe();
            PathLabel.Text = text;
            _pathToolTip.SetToolTip(PathLabel, toolTip);

            // 若文本像素宽度超出控件，切换为靠左对齐，确保盘符及前置路径可见并通过 AutoEllipsis
            // 在末尾追加省略号；较短时居中对齐，与上方预览控件保持对称（清除后无文本同样居中）
            if (string.IsNullOrEmpty(text)) {
                PathLabel.TextAlign = ContentAlignment.MiddleCenter;
            }
            else {
                int textWidth = TextRenderer.MeasureText(text, PathLabel.Font).Width;
                PathLabel.TextAlign = textWidth > PathLabel.Width ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter;
            }

            // 消息标签由状态推导：无选择恢复默认文案；路径模式区分可开始/无效路径；合并模式一律可开始
            MsgLabel.Text = _selection.Mode switch {
                SelectionMode.None => Strings.MsgLabelDefault,
                SelectionMode.Path => _selection.IsStartable ? Strings.ClickToStart : Strings.InvalidPath,
                _ => Strings.ClickToStart
            };

            UpdateControlStates();
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
                    ImageContracts.ArchiveExtensions.Contains(Path.GetExtension(f)) ||
                    ImageContracts.ImageExtensions.Contains(Path.GetExtension(f)) ||
                    string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)
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

        /// <summary>
        /// 选中一个目录或压缩包路径（PicInFolder_Click、打开文件夹/压缩包菜单与
        /// 兜底拖放的共用实现）。路径是否有效由 SelectionState 读取时判定，
        /// 此处只按分类设置预览图，标签/提示/消息文案交给 SetSelection 统一渲染。
        /// </summary>
        private void ChooseFileAction(string directoryPath) {
            var selection = SelectionState.FromPath(directoryPath);

            if (selection.IsRealDirectory) {
                Bitmap preview = null;
                IEnumerable<string> imagepaths = Directory.EnumerateFiles(directoryPath)
                    .Where(p => ImageContracts.ImageExtensions.Contains(Path.GetExtension(p)));
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
            else if (selection.IsArchiveFile) {
                ImagePreviewHelper.ReplaceImage(PicInFolder, Properties.Resources.compressedFile);
                SetFolderOverlay(null);
            }
            else {
                ImagePreviewHelper.ReplaceImage(PicInFolder, Properties.Resources.no_photo);
                SetFolderOverlay(null);
            }

            SetSelection(selection);
        }

        /// <summary>
        /// 选中一组离散图片文件，进入图片合并为一个 PDF 的模式。
        /// </summary>
        private void ChooseImagesAction(List<string> imageFiles) {
            if (imageFiles == null || imageFiles.Count == 0) return;

            // 自然排序（与 Windows 资源管理器文件排序规则一致）
            imageFiles.Sort(FolderScanner.NaturalPathComparer.Instance);

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

            // 标签/提示/消息文案由 SelectionState.Describe 派生并经 SetSelection 统一渲染；
            // 递归/合并子目录的禁用与取消勾选由 UpdateControlStates 按模式统一处理
            SetSelection(SelectionState.FromImageFiles(imageFiles));
        }

        /// <summary>
        /// 选中一组离散 PDF 文件，进入多个 PDF 合并为一个 PDF 的模式。
        /// </summary>
        private void ChoosePdfsAction(List<string> pdfFiles) {
            if (pdfFiles == null || pdfFiles.Count == 0) return;

            // 自然排序（与 Windows 资源管理器文件排序规则一致）
            pdfFiles.Sort(FolderScanner.NaturalPathComparer.Instance);

            ImagePreviewHelper.ReplaceImage(PicInFolder, Properties.Resources.pdf);
            SetFolderOverlay(null); // PDF 合并模式不需要文件夹角标

            // 标签/提示/消息文案由 SelectionState.Describe 派生并经 SetSelection 统一渲染；
            // 版式/快速压缩的禁用与递归/合并的取消勾选由 UpdateControlStates 按模式统一处理
            SetSelection(SelectionState.FromPdfFiles(pdfFiles));
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
                .Where(f => File.Exists(f) && ImageContracts.ImageExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            // 提取所有容器（文件夹或压缩包）
            var containerFiles = files
                .Where(f => Directory.Exists(f) || ImageContracts.ArchiveExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            // 提取所有有效的 PDF 文件
            var pdfFiles = files
                .Where(f => File.Exists(f) && string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 纯 PDF 拖入：进入 PDF 合并模式
            if (pdfFiles.Count > 0 && imageFiles.Count == 0 && containerFiles.Count == 0) {
                ChoosePdfsAction(pdfFiles);
            }
            // 纯图片拖入：进入图片合并模式
            else if (imageFiles.Count > 0 && containerFiles.Count == 0) {
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

            // 在 UI 线程捕获选择状态快照，后台任务只读取这些局部变量
            SelectionState selection = _selection;
            if (selection.Mode == SelectionMode.None) {
                return;
            }

            string manifestPath = null;
            string targetPathForCore;
            string displayTargetPath;

            if (selection.IsImageMerge || selection.IsPdfMerge) {
                // 合并模式：写临时清单交给 Core，输出名与既有命名保持一致
                // （图片合并 = 首个文件所在目录名.pdf；PDF 合并 = 首个文件所在目录名_Merged.pdf）
                string firstFile = selection.Files[0];
                string outDir = Path.GetDirectoryName(firstFile);
                string baseName = Path.GetFileName(outDir);
                if (string.IsNullOrEmpty(baseName)) {
                    baseName = Path.GetFileNameWithoutExtension(firstFile);
                }
                displayTargetPath = Path.Combine(
                    outDir,
                    selection.IsPdfMerge ? $"{baseName}_Merged.pdf" : $"{baseName}.pdf");

                manifestPath = Path.Combine(Path.GetTempPath(), $"imgstopdf_{Guid.NewGuid():N}.txt");
                var lines = new List<string> {
                    IpcContracts.ManifestOutputPrefix + displayTargetPath
                };
                lines.AddRange(selection.Files);
                File.WriteAllLines(manifestPath, lines, new UTF8Encoding(false));
                targetPathForCore = manifestPath;
            }
            else {
                // 目录/压缩包模式：路径原样传给 Core
                targetPathForCore = selection.TargetPath;
                displayTargetPath = selection.TargetPath;
            }

            bool recursive = selection.Mode == SelectionMode.Path && Recursive.Checked;
            bool fastMode = !selection.IsPdfMerge && FastMode.Checked;
            bool merge = selection.Mode == SelectionMode.Path && Merge.Checked;
            Shared.Layout layout = selection.IsPdfMerge ? Shared.Layout.Single : _layoutChoices[generateModeBox.SelectedIndex].Value;

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
                    layout,
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
                Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core", "config.lua"));
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

            // 预览复位为初始资源图；标签/提示/消息文案与勾选、可用性
            // 全部由 SetSelection(Empty) 的统一渲染与状态推导完成（清除 = 完整复位）
            ImagePreviewHelper.ReplaceImage(PicInFolder, Properties.Resources.folder);
            SetFolderOverlay(null);
            progressBar.Visible = false;
            progressBar.Value = 0;

            SetSelection(SelectionState.Empty);
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

        /// <summary>
        /// 用户勾选/取消递归子目录：合并子目录的可用与勾选状态由状态同步点统一推导。
        /// </summary>
        private void Recursive_CheckedChanged(object sender, EventArgs e) {
            UpdateControlStates();
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

        /// <summary>
        /// 弹出多选文件对话框并返回用户选中的文件列表；用户取消时返回 null。
        /// 筛选器用的扩展名由调用方传入（图片侧来自 <see cref="ImageContracts"/>），
        /// 避免对话框允许选择的类型与 Core 实际支持的类型各自维护、逐渐漂移。
        /// </summary>
        private static List<string> PickFiles(string title, string filterLabel, params string[] extensions) {
            // 排序只是为了筛选器里的通配符顺序稳定，与文件本身的排序无关
            string pattern = string.Join(";", extensions
                .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
                .Select(ext => "*" + ext));

            using OpenFileDialog dialog = new() {
                Title = title,
                Filter = filterLabel + "|" + pattern,
                // 只有一组筛选器，固定选中它
                FilterIndex = 1,
                // 离散图片 / PDF 合并模式的前提就是一次选中多个文件
                Multiselect = true
            };

            if (dialog.ShowDialog() != DialogResult.OK) {
                return null;
            }
            return [.. dialog.FileNames];
        }

        /// <summary>
        /// 选择多个离散图片文件，合并输出为一个 PDF（进入图片合并模式）。
        /// 输出位置与拖入多张图片时一致：首张图片所在目录下的「目录名.pdf」。
        /// </summary>
        private void ChooseImagesToolStripMenuItem_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            List<string> files = PickFiles(
                Strings.SelectImages,
                Strings.ImageFile,
                [.. ImageContracts.ImageExtensions]);
            if (files == null) return;

            ChooseImagesAction(files);
        }

        /// <summary>
        /// 选择多个 PDF 文件，按文件名顺序合并为一个 PDF（进入 PDF 合并模式）。
        /// 输出位置与拖入多个 PDF 时一致：首个 PDF 所在目录下的「目录名_Merged.pdf」。
        /// </summary>
        private void ChoosePDFsToolStripMenuItem_Click(object sender, EventArgs e) {
            if (_isProcessing) return;

            List<string> files = PickFiles(Strings.SelectPdfs, Strings.PdfFile, ".pdf");
            if (files == null) return;

            ChoosePdfsAction(files);
        }

        /// <summary>
        /// 目标选择的互斥形态：把原先由「图片列表 + PDF 列表 + 路径标签文本」三处字段
        /// 共同隐式表达的状态收敛为单一可判别来源。
        /// 目录/压缩包/失效路径不作为枚举值：它们是「同一路径读取时对文件系统的实时分类」，
        /// 可能随外部变化（目录被删、网络盘断开），读取时重判才不会出现第二真相源
        /// （开始按钮与控件状态推导本就每次重判，压缩包复选框修复即依赖此语义）。
        /// </summary>
        private enum SelectionMode {
            /// <summary>无选择（启动初始态、清除后）。</summary>
            None,
            /// <summary>路径模式：TargetPath 指向目录/压缩包/其它，有效性读取时判定。</summary>
            Path,
            /// <summary>离散图片合并模式：Files 为已排序的非空列表。</summary>
            ImageMerge,
            /// <summary>PDF 合并模式：Files 为已排序的非空列表。</summary>
            PdfMerge,
        }

        /// <summary>
        /// 目标选择的完整状态：模式 + 载荷。不可变，经 <see cref="SetSelection"/> 整体替换；
        /// 路径子形态（目录/压缩包/失效）的判定集中在此，且每次读取都实时重判文件系统。
        /// </summary>
        private sealed class SelectionState {
            /// <summary>无选择状态（启动初始态、清除后）。</summary>
            public static readonly SelectionState Empty = new(SelectionMode.None, null, null);

            private SelectionState(SelectionMode mode, string targetPath, IReadOnlyList<string> files) {
                Mode = mode;
                TargetPath = targetPath;
                Files = files;
            }

            /// <summary>当前形态。</summary>
            public SelectionMode Mode { get; }

            /// <summary>路径模式下的目标路径；合并模式下为 null（标签文本是展示用派生值，不再是状态）。</summary>
            public string TargetPath { get; }

            /// <summary>合并模式下的文件列表（已排序、非空）；其它模式为 null。</summary>
            public IReadOnlyList<string> Files { get; }

            public static SelectionState FromPath(string path) {
                return new(SelectionMode.Path, path, null);
            }

            /// <summary>离散图片合并状态。文件列表由调用方排序并保证非空，构造后不再修改（不可变语义）。</summary>
            public static SelectionState FromImageFiles(List<string> files) {
                return new(SelectionMode.ImageMerge, null, files);
            }

            /// <summary>PDF 合并状态。文件列表由调用方排序并保证非空，构造后不再修改（不可变语义）。</summary>
            public static SelectionState FromPdfFiles(List<string> files) {
                return new(SelectionMode.PdfMerge, null, files);
            }

            public bool IsImageMerge => Mode == SelectionMode.ImageMerge;

            public bool IsPdfMerge => Mode == SelectionMode.PdfMerge;

            /// <summary>
            /// 路径模式且目标是真实存在的文件夹。每次读取都实时重判（目录可能已被删除、
            /// 网络盘可能已断开）。先判目录再判扩展名，避免恰以 .zip 等结尾命名的真实目录被误判为压缩包。
            /// </summary>
            public bool IsRealDirectory => Mode == SelectionMode.Path && Directory.Exists(TargetPath);

            /// <summary>路径模式且目标是受支持的压缩包文件（非目录、扩展名匹配）。</summary>
            public bool IsArchiveFile => Mode == SelectionMode.Path && !IsRealDirectory &&
                ImageContracts.ArchiveExtensions.Contains(Path.GetExtension(TargetPath));

            /// <summary>
            /// 当前选择是否可作为生成目标直接开始：
            /// 两种合并模式选中即有效；路径模式要求目标真实存在（目录或压缩包）。
            /// </summary>
            public bool IsStartable => IsImageMerge || IsPdfMerge || IsRealDirectory || IsArchiveFile;

            /// <summary>
            /// 计算路径标签的显示文本与悬停提示（纯派生视图，不写任何控件）。
            /// 路径模式原样显示路径；合并模式单文件显示完整路径，多文件显示「[N 项] 目录名」。
            /// </summary>
            public (string Text, string ToolTip) Describe() {
                return Mode switch {
                    SelectionMode.Path => (TargetPath, TargetPath),
                    SelectionMode.ImageMerge => DescribeFileList(Strings.SelectedImagesCount, "[{0} Images] {1}", "image"),
                    SelectionMode.PdfMerge => DescribeFileList(Strings.SelectedPdfsCount, "[{0} PDFs] {1}", "pdf"),
                    _ => (null, null),
                };
            }

            /// <summary>合并模式的标签与悬停提示文案。</summary>
            private (string Text, string ToolTip) DescribeFileList(string countPattern, string fallbackPattern, string kind) {
                string firstFile = Files[0];
                string dir = Path.GetDirectoryName(firstFile);
                string dirName = Path.GetFileName(dir);

                if (Files.Count == 1) {
                    return (firstFile, firstFile);
                }

                string pattern = string.IsNullOrEmpty(countPattern) ? fallbackPattern : countPattern;
                string displayDir = string.IsNullOrEmpty(dirName) ? firstFile : dirName;
                string toolTip = string.Format(
                    "Selected {0} {1}(s)\nDirectory: {2}\nFirst file: {3}",
                    Files.Count,
                    kind,
                    dir,
                    Path.GetFileName(firstFile)
                );
                return (string.Format(pattern, Files.Count, displayDir), toolTip);
            }
        }
    }
}
