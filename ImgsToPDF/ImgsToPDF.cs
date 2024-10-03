using ImgsToPDF.Lang;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImgsToPDF
{
    public partial class ImgsToPDF : Form
    {
        public ImgsToPDF() {
            string language = Properties.Settings.Default.DefaultLanguage != "" ? Properties.Settings.Default.DefaultLanguage : System.Globalization.CultureInfo.CurrentCulture.Name;
            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(language);

            this.StartPosition = FormStartPosition.CenterScreen; // 窗口居中
            CheckForIllegalCrossThreadCalls = false; // UI不需要限制线程间操作

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
            generateModeBox.Items.AddRange(new string[] {
                Extra.ApplyResource(typeof(Extra), "strSingle"),
                Extra.ApplyResource(typeof(Extra), "strDuplex"),
                Extra.ApplyResource(typeof(Extra), "strDuplexRightToLeft")
            });
            generateModeBox.SelectedIndex = 0;
        }
        readonly string[] compressExtensions = { ".zip", ".rar", ".7z" };
        private void ImgsToPDF_DragEnter(object sender, DragEventArgs e) {
            string filePath = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                (Directory.Exists(filePath) || compressExtensions.Contains(Path.GetExtension(filePath)?.ToLower()))
                ) {
                e.Effect = DragDropEffects.All;
            }
            else {
                e.Effect = DragDropEffects.None;
            }
        }
        private void ChooseFileAction(string directoryPath) {
            // 及时释放Bitmap对象
            PicInFolder.Image?.Dispose();

            PathLabel.Text = directoryPath;

            // 检查路径是否有效
            if (Directory.Exists(directoryPath)) {
                PicInFolder.Image = Properties.Resources.no_photo;
                FolderImg.Image = Properties.Resources.folder;
                List<string> imageExtensions = new List<string> { ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif" };
                IEnumerable<string> imagepaths = Directory.EnumerateFiles(directoryPath)
                    .Where(p => imageExtensions.Any(e => Path.GetExtension(p)?.ToLower() == e));
                foreach (var imagepath in imagepaths) {
                    try {
                        using (var srcImage = new Bitmap(imagepath)) {
                            PicInFolder.Image = Bitmap.FromFile(imagepath) as Bitmap;
                            break;
                        }
                    }
                    catch (Exception) {
                        // 如果文件不是一张合法的图片，则直接跳过
                        continue;
                    }
                }
            }
            else if (compressExtensions.Contains(Path.GetExtension(directoryPath)?.ToLower())) {
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
            string directoryPath = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();       //获得路径
            ChooseFileAction(directoryPath);
        }
        private async void StartButton_Click(object sender, EventArgs e) {
            //Thread ButtonClickThread = new Thread(ButtonClickAction);
            //ButtonClickThread.Start();
            MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strPDFIsGenerating");
            progressBar.Visible = true;
            progressBar.Maximum = 100;
            progressBar.Value = 50;
            StartButton.Enabled = false;
            await Task.Run(() => ButtonClickAction());  // 这里的“await”语句会在后台线程运行LoadData方法
            // LoadData方法完成后，回到主线程更新UI
            progressBar.Value = 100;
            StartButton.Enabled = true;
            MsgLabel.Text = Extra.ApplyResource(typeof(Extra), "strPDFGenerationSuccess");
        }
        static List<string> RecursiveFolder(string path, List<string> dirs) {
            dirs.Add(path);
            var TheFolder = new DirectoryInfo(path);
            foreach (var childFolder in TheFolder.GetDirectories()) {
                RecursiveFolder(childFolder.FullName, dirs);
            }
            return dirs;
        }
        private void ButtonClickAction() {
            var fileName = AppDomain.CurrentDomain.BaseDirectory + @"\Core\ImgsToPDFCore.exe";

            if (Recursive.Checked && Directory.Exists(PathLabel.Text)) {
                //foreach (var dirPath in RecursiveFolder(PathLabel.Text, new List<string> { }))
                //{
                //    string[] args = FastMode.Checked ? new string[] {
                //        "-d", dirPath,
                //        "-l", generateModeBox.SelectedIndex.ToString()
                //    } : new string[] {
                //        "-d", dirPath,
                //        "-l", generateModeBox.SelectedIndex.ToString(), "--fast"
                //    };
                //    var (_, stderr) = RunProcess(fileName, args);
                //    if (stderr.Length > 0) {
                //        MessageBox.Show(stderr);
                //    }
                //}
                RecursiveFolder(PathLabel.Text, new List<string> { }).AsParallel().WithDegreeOfParallelism(4).ForAll(dirPath => {
                    string[] args = FastMode.Checked ? new string[] {
                        "-d", dirPath,
                        "-l", generateModeBox.SelectedIndex.ToString(), "--fast"
                    } : new string[] {
                        "-d", dirPath,
                        "-l", generateModeBox.SelectedIndex.ToString()
                    };
                    var (_, stderr) = RunProcess(fileName, args);
                    if (stderr.Length > 0) {
                        MessageBox.Show(stderr);
                    }
                });
            }
            else {
                string[] args = FastMode.Checked ? new string[] {
                    "-d", PathLabel.Text,
                    "-l", generateModeBox.SelectedIndex.ToString(), "--fast"
                } : new string[] {
                    "-d", PathLabel.Text,
                    "-l", generateModeBox.SelectedIndex.ToString()
                };
                var (_, stderr) = RunProcess(fileName, args);
                if (stderr.Length > 0) {
                    MessageBox.Show(stderr);
                }
            }

        }
        /// <summary>
        /// 运行给定的命令，返回得到的标准输出及标准错误
        /// </summary>
        /// <param name="command">需要运行的指令</param>
        /// <returns>元组：(stdout:标准输出, stderr:标准错误)</returns>
        private static (string stdout, string stderr) RunProcess(string fileName, string[] args) {
            for (int i = 0; i < args.Length; i++) {
                if (args[i].EndsWith(@"\")) {
                    //处理最后若为“\\”，会被转义成“\”，然后变成转义符。
                    args[i] += @"\";
                }
                args[i] = string.Format("\"{0}\"", args[i]);
            }
            // 例Process
            Process p = new Process();
            p.StartInfo.FileName = fileName;
            p.StartInfo.Arguments = string.Join(" ", args);
            p.StartInfo.UseShellExecute = false;        // Shell的使用
            p.StartInfo.RedirectStandardInput = true;   // 重定向输入
            p.StartInfo.RedirectStandardOutput = true;  // 重定向输出
            p.StartInfo.RedirectStandardError = true;   // 重定向输出错误
            p.StartInfo.CreateNoWindow = true;          // 设置置不显示示窗口
            p.Start();
            return (p.StandardOutput.ReadToEnd(), p.StandardError.ReadToEnd()); // 输出出流取得命令行结果
        }
        private void toolStripMenuExit_Click(object sender, EventArgs e) {
            this.Close();
        }
        private void toolStripMenuConfigFile_Click(object sender, EventArgs e) {
            Process.Start(AppDomain.CurrentDomain.BaseDirectory + "/Core/Config.lua");
        }
        private void toolStripMenuAbout_Click(object sender, EventArgs e) {
            MessageBox.Show(
                "ImagesToPDF v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + "\nCopyright (c) 2022-2024 Sinryou. At MIT License.",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                0,
                "https://github.com/Sinryou/ImagesToPDF"
            );
        }
        private void toolStripMenuOpenFolder_Click(object sender, EventArgs e) {
            FolderBrowserDialog dialog = new FolderBrowserDialog {
                Description = Extra.ApplyResource(typeof(Extra), "strSelectIMGFolder")
            };
            if (dialog.ShowDialog() == DialogResult.Cancel) {
                return;
            }
            string directoryPath = dialog.SelectedPath.Trim();
            ChooseFileAction(directoryPath);
        }
        private void toolStripMenuClearChosen_Click(object sender, EventArgs e) {
            PicInFolder.Image?.Dispose();
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
    }
}
