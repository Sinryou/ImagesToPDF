namespace ImgsToPDF {
    partial class ImgsToPDF {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent() {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ImgsToPDF));
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.StartButton = new System.Windows.Forms.Button();
            this.MsgLabel = new System.Windows.Forms.Label();
            this.PathLabel = new System.Windows.Forms.Label();
            this.FolderImg = new System.Windows.Forms.PictureBox();
            this.PicInFolder = new System.Windows.Forms.PictureBox();
            this.generateModeBox = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.FastMode = new System.Windows.Forms.CheckBox();
            this.menuStripMain = new System.Windows.Forms.MenuStrip();
            this.toolStripMenuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuOpenFolder = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuClearChosen = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripMenuExit = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuConfigFile = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuAbout = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.FolderImg)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PicInFolder)).BeginInit();
            this.menuStripMain.SuspendLayout();
            this.SuspendLayout();
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(545, 505);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(239, 28);
            this.progressBar.TabIndex = 11;
            this.progressBar.Visible = false;
            // 
            // StartButton
            // 
            this.StartButton.Enabled = false;
            this.StartButton.Location = new System.Drawing.Point(355, 503);
            this.StartButton.Name = "StartButton";
            this.StartButton.Size = new System.Drawing.Size(97, 32);
            this.StartButton.TabIndex = 10;
            this.StartButton.Text = "Start";
            this.StartButton.UseVisualStyleBackColor = true;
            this.StartButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // MsgLabel
            // 
            this.MsgLabel.Location = new System.Drawing.Point(155, 420);
            this.MsgLabel.Name = "MsgLabel";
            this.MsgLabel.Size = new System.Drawing.Size(483, 36);
            this.MsgLabel.TabIndex = 9;
            this.MsgLabel.Text = "拖入包含图片的文件夹 Drop your folder here";
            this.MsgLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // PathLabel
            // 
            this.PathLabel.Location = new System.Drawing.Point(10, 385);
            this.PathLabel.Name = "PathLabel";
            this.PathLabel.Size = new System.Drawing.Size(776, 35);
            this.PathLabel.TabIndex = 8;
            this.PathLabel.Text = " ";
            this.PathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // FolderImg
            // 
            this.FolderImg.BackColor = System.Drawing.Color.Transparent;
            this.FolderImg.Location = new System.Drawing.Point(245, 290);
            this.FolderImg.Name = "FolderImg";
            this.FolderImg.Size = new System.Drawing.Size(90, 90);
            this.FolderImg.TabIndex = 7;
            this.FolderImg.TabStop = false;
            // 
            // PicInFolder
            // 
            this.PicInFolder.Image = global::ImgsToPDF.Properties.Resources.folder;
            this.PicInFolder.Location = new System.Drawing.Point(295, 60);
            this.PicInFolder.Name = "PicInFolder";
            this.PicInFolder.Size = new System.Drawing.Size(200, 300);
            this.PicInFolder.TabIndex = 6;
            this.PicInFolder.TabStop = false;
            // 
            // generateModeBox
            // 
            this.generateModeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.generateModeBox.FormattingEnabled = true;
            this.generateModeBox.Location = new System.Drawing.Point(170, 505);
            this.generateModeBox.Name = "generateModeBox";
            this.generateModeBox.Size = new System.Drawing.Size(170, 26);
            this.generateModeBox.TabIndex = 12;
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(-3, 495);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(192, 48);
            this.label1.TabIndex = 13;
            this.label1.Text = "PDF生成模式 Mode：";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // FastMode
            // 
            this.FastMode.AutoSize = true;
            this.FastMode.Location = new System.Drawing.Point(20, 470);
            this.FastMode.Name = "FastMode";
            this.FastMode.Size = new System.Drawing.Size(304, 22);
            this.FastMode.TabIndex = 14;
            this.FastMode.Text = "有损(更快生成速度更小文件)Fast";
            this.FastMode.UseVisualStyleBackColor = true;
            // 
            // menuStripMain
            // 
            this.menuStripMain.GripMargin = new System.Windows.Forms.Padding(2, 2, 0, 2);
            this.menuStripMain.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.menuStripMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuFile,
            this.toolStripMenuConfigFile,
            this.toolStripMenuAbout});
            this.menuStripMain.Location = new System.Drawing.Point(0, 0);
            this.menuStripMain.Name = "menuStripMain";
            this.menuStripMain.Size = new System.Drawing.Size(803, 36);
            this.menuStripMain.TabIndex = 15;
            this.menuStripMain.Text = "menuStripMain";
            // 
            // toolStripMenuFile
            // 
            this.toolStripMenuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuOpenFolder,
            this.toolStripMenuClearChosen,
            this.toolStripSeparator1,
            this.toolStripMenuExit});
            this.toolStripMenuFile.Name = "toolStripMenuFile";
            this.toolStripMenuFile.Size = new System.Drawing.Size(84, 32);
            this.toolStripMenuFile.Text = "文件(&F)";
            // 
            // toolStripMenuOpenFolder
            // 
            this.toolStripMenuOpenFolder.Name = "toolStripMenuOpenFolder";
            this.toolStripMenuOpenFolder.Size = new System.Drawing.Size(227, 34);
            this.toolStripMenuOpenFolder.Text = "打开文件夹(&O)";
            this.toolStripMenuOpenFolder.Click += new System.EventHandler(this.toolStripMenuOpenFolder_Click);
            // 
            // toolStripMenuClearChosen
            // 
            this.toolStripMenuClearChosen.Name = "toolStripMenuClearChosen";
            this.toolStripMenuClearChosen.Size = new System.Drawing.Size(227, 34);
            this.toolStripMenuClearChosen.Text = "清除选择(&S)";
            this.toolStripMenuClearChosen.Click += new System.EventHandler(this.toolStripMenuClearChosen_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(224, 6);
            // 
            // toolStripMenuExit
            // 
            this.toolStripMenuExit.Name = "toolStripMenuExit";
            this.toolStripMenuExit.Size = new System.Drawing.Size(227, 34);
            this.toolStripMenuExit.Text = "退出程序(&E)";
            this.toolStripMenuExit.Click += new System.EventHandler(this.toolStripMenuExit_Click);
            // 
            // toolStripMenuConfigFile
            // 
            this.toolStripMenuConfigFile.Name = "toolStripMenuConfigFile";
            this.toolStripMenuConfigFile.Size = new System.Drawing.Size(122, 32);
            this.toolStripMenuConfigFile.Text = "配置文件(&C)";
            this.toolStripMenuConfigFile.Click += new System.EventHandler(this.toolStripMenuConfigFile_Click);
            // 
            // toolStripMenuAbout
            // 
            this.toolStripMenuAbout.Name = "toolStripMenuAbout";
            this.toolStripMenuAbout.Size = new System.Drawing.Size(87, 32);
            this.toolStripMenuAbout.Text = "关于(&A)";
            this.toolStripMenuAbout.Click += new System.EventHandler(this.toolStripMenuAbout_Click);
            // 
            // ImgsToPDF
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(803, 554);
            this.Controls.Add(this.FastMode);
            this.Controls.Add(this.generateModeBox);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.StartButton);
            this.Controls.Add(this.MsgLabel);
            this.Controls.Add(this.PathLabel);
            this.Controls.Add(this.FolderImg);
            this.Controls.Add(this.PicInFolder);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.menuStripMain);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStripMain;
            this.Name = "ImgsToPDF";
            this.Text = "ImgsToPDF";
            this.Load += new System.EventHandler(this.ImgsToPDF_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.ImgsToPDF_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.ImgsToPDF_DragEnter);
            ((System.ComponentModel.ISupportInitialize)(this.FolderImg)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PicInFolder)).EndInit();
            this.menuStripMain.ResumeLayout(false);
            this.menuStripMain.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Button StartButton;
        private System.Windows.Forms.Label MsgLabel;
        private System.Windows.Forms.Label PathLabel;
        private System.Windows.Forms.PictureBox FolderImg;
        private System.Windows.Forms.PictureBox PicInFolder;
        private System.Windows.Forms.ComboBox generateModeBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox FastMode;
        private System.Windows.Forms.MenuStrip menuStripMain;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuFile;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuOpenFolder;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuExit;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuConfigFile;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuAbout;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuClearChosen;
    }
}

