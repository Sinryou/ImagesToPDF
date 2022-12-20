using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ImgsToPDF {
    public partial class ImgsToPDF : Form {
        public ImgsToPDF() {
            InitializeComponent();
        }
        private void ImgsToPDF_Load(object sender, EventArgs e) {
            FolderImg.SizeMode = PictureBoxSizeMode.Zoom;
            PicInFolder.SizeMode = PictureBoxSizeMode.Zoom;
            generateModeBox.Items.AddRange(new string[] { "Single单页", "Dual双页", "DualR2L逆排双页" });
            generateModeBox.SelectedIndex = 0;
        }
        private void ImgsToPDF_DragEnter(object sender, DragEventArgs e) {
            string filePath = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && Directory.Exists(filePath))
            {
                e.Effect = DragDropEffects.All;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }

        }
        private void ImgsToPDF_DragDrop(object sender, DragEventArgs e) {
            PicInFolder.Image = Properties.Resources.no_photo;
            FolderImg.Image = Properties.Resources.folder;
            string directoryPath = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();       //获得路径
            PathLabel.Text = directoryPath;
            foreach (var imagepath in Directory.GetFiles(directoryPath))
            {
                try
                {
                    using (var srcImage = new Bitmap(imagepath))
                    {
                        PicInFolder.Image = Image.FromFile(imagepath);
                        break;
                    }
                }
                catch (Exception)
                {
                    //throw;
                }
            }
            StartButton.Enabled = true;
        }

        private void StartButton_Click(object sender, EventArgs e) {
            Thread ButtonClickThread = new Thread(ButtonClickAction);
            ButtonClickThread.Start();
            MsgLabel.ForeColor = Color.Blue;
            MsgLabel.Text = "PDF is Generating......";
            progressBar.Visible = true;
            progressBar.Maximum = 100;
            progressBar.Value = 50;
            StartButton.Enabled = false;
        }
        private void ButtonClickAction() {
            string fileName = Environment.CurrentDirectory + @"\Core\ImgsToPDFCore.exe";
            string args;
            if (FastMode.Checked)
            {
                args = @"""" + PathLabel.Text + @""" " + generateModeBox.SelectedIndex.ToString()+" 1";
            }
            else
            {
                args = @"""" + PathLabel.Text + @""" " + generateModeBox.SelectedIndex.ToString() + " 0";
            }
            var (stdout, stderr) = RunProcess(fileName, args);
            if (stderr.Length > 0)
            {
                MessageBox.Show(stderr);
            }
            Thread.Sleep(100);
            progressBar.Value = 100;
            StartButton.Enabled = true;
            MsgLabel.Text = "PDF file has been output to your folder!";
        }
        /// <summary>
        /// 运行给定的命令，返回得到的标准输出及标准错误
        /// </summary>
        /// <param name="command">需要运行的指令</param>
        /// <returns>元组：(stdout:标准输出, stderr:标准错误)</returns>
        public static (string stdout, string stderr) RunProcess(string fileName,string args) {
            #region 打印给定command
            //string[] splitedStrings = command.Split(new char[] { ' ' }, 2);
            //foreach (string s in splitedStrings) {
            //    Console.WriteLine("splitedStrings.Item: " + s);
            //}
            #endregion
            // 例Process
            Process p = new Process();
            p.StartInfo.FileName = fileName;
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;        // Shell的使用
            p.StartInfo.RedirectStandardInput = true;   // 重定向输入
            p.StartInfo.RedirectStandardOutput = true;  // 重定向输出
            p.StartInfo.RedirectStandardError = true;   // 重定向输出错误
            p.StartInfo.CreateNoWindow = true;          // 设置置不显示示窗口
            p.Start();
            return (p.StandardOutput.ReadToEnd(), p.StandardError.ReadToEnd()); // 输出出流取得命令行结果
        }
    }
}
