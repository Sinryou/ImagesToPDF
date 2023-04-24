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
            this.labelLayout = new System.Windows.Forms.Label();
            this.FastMode = new System.Windows.Forms.CheckBox();
            this.menuStripMain = new System.Windows.Forms.MenuStrip();
            this.toolStripMenuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuOpenFolder = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuClearChosen = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripMenuExit = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuConfigFile = new System.Windows.Forms.ToolStripMenuItem();
            this.languageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.englishToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.chineseToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.Recursive = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.FolderImg)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PicInFolder)).BeginInit();
            this.menuStripMain.SuspendLayout();
            this.SuspendLayout();
            // 
            // progressBar
            // 
            resources.ApplyResources(this.progressBar, "progressBar");
            this.progressBar.Name = "progressBar";
            // 
            // StartButton
            // 
            resources.ApplyResources(this.StartButton, "StartButton");
            this.StartButton.Name = "StartButton";
            this.StartButton.UseVisualStyleBackColor = true;
            this.StartButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // MsgLabel
            // 
            resources.ApplyResources(this.MsgLabel, "MsgLabel");
            this.MsgLabel.Name = "MsgLabel";
            // 
            // PathLabel
            // 
            resources.ApplyResources(this.PathLabel, "PathLabel");
            this.PathLabel.Name = "PathLabel";
            // 
            // FolderImg
            // 
            resources.ApplyResources(this.FolderImg, "FolderImg");
            this.FolderImg.BackColor = System.Drawing.Color.Transparent;
            this.FolderImg.Name = "FolderImg";
            this.FolderImg.TabStop = false;
            // 
            // PicInFolder
            // 
            resources.ApplyResources(this.PicInFolder, "PicInFolder");
            this.PicInFolder.Image = global::ImgsToPDF.Properties.Resources.folder;
            this.PicInFolder.Name = "PicInFolder";
            this.PicInFolder.TabStop = false;
            // 
            // generateModeBox
            // 
            resources.ApplyResources(this.generateModeBox, "generateModeBox");
            this.generateModeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.generateModeBox.FormattingEnabled = true;
            this.generateModeBox.Name = "generateModeBox";
            // 
            // labelLayout
            // 
            resources.ApplyResources(this.labelLayout, "labelLayout");
            this.labelLayout.Name = "labelLayout";
            // 
            // FastMode
            // 
            resources.ApplyResources(this.FastMode, "FastMode");
            this.FastMode.Name = "FastMode";
            this.FastMode.UseVisualStyleBackColor = true;
            // 
            // menuStripMain
            // 
            resources.ApplyResources(this.menuStripMain, "menuStripMain");
            this.menuStripMain.GripMargin = new System.Windows.Forms.Padding(2, 2, 0, 2);
            this.menuStripMain.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.menuStripMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuFile,
            this.toolStripMenuConfigFile,
            this.languageToolStripMenuItem,
            this.toolStripMenuAbout});
            this.menuStripMain.Name = "menuStripMain";
            // 
            // toolStripMenuFile
            // 
            resources.ApplyResources(this.toolStripMenuFile, "toolStripMenuFile");
            this.toolStripMenuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuOpenFolder,
            this.toolStripMenuClearChosen,
            this.toolStripSeparator1,
            this.toolStripMenuExit});
            this.toolStripMenuFile.Name = "toolStripMenuFile";
            // 
            // toolStripMenuOpenFolder
            // 
            resources.ApplyResources(this.toolStripMenuOpenFolder, "toolStripMenuOpenFolder");
            this.toolStripMenuOpenFolder.Name = "toolStripMenuOpenFolder";
            this.toolStripMenuOpenFolder.Click += new System.EventHandler(this.toolStripMenuOpenFolder_Click);
            // 
            // toolStripMenuClearChosen
            // 
            resources.ApplyResources(this.toolStripMenuClearChosen, "toolStripMenuClearChosen");
            this.toolStripMenuClearChosen.Name = "toolStripMenuClearChosen";
            this.toolStripMenuClearChosen.Click += new System.EventHandler(this.toolStripMenuClearChosen_Click);
            // 
            // toolStripSeparator1
            // 
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            // 
            // toolStripMenuExit
            // 
            resources.ApplyResources(this.toolStripMenuExit, "toolStripMenuExit");
            this.toolStripMenuExit.Name = "toolStripMenuExit";
            this.toolStripMenuExit.Click += new System.EventHandler(this.toolStripMenuExit_Click);
            // 
            // toolStripMenuConfigFile
            // 
            resources.ApplyResources(this.toolStripMenuConfigFile, "toolStripMenuConfigFile");
            this.toolStripMenuConfigFile.Name = "toolStripMenuConfigFile";
            this.toolStripMenuConfigFile.Click += new System.EventHandler(this.toolStripMenuConfigFile_Click);
            // 
            // languageToolStripMenuItem
            // 
            resources.ApplyResources(this.languageToolStripMenuItem, "languageToolStripMenuItem");
            this.languageToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.englishToolStripMenuItem,
            this.chineseToolStripMenuItem});
            this.languageToolStripMenuItem.Name = "languageToolStripMenuItem";
            // 
            // englishToolStripMenuItem
            // 
            resources.ApplyResources(this.englishToolStripMenuItem, "englishToolStripMenuItem");
            this.englishToolStripMenuItem.Name = "englishToolStripMenuItem";
            this.englishToolStripMenuItem.Click += new System.EventHandler(this.englishToolStripMenuItem_Click);
            // 
            // chineseToolStripMenuItem
            // 
            resources.ApplyResources(this.chineseToolStripMenuItem, "chineseToolStripMenuItem");
            this.chineseToolStripMenuItem.Name = "chineseToolStripMenuItem";
            this.chineseToolStripMenuItem.Click += new System.EventHandler(this.chineseToolStripMenuItem_Click);
            // 
            // toolStripMenuAbout
            // 
            resources.ApplyResources(this.toolStripMenuAbout, "toolStripMenuAbout");
            this.toolStripMenuAbout.Name = "toolStripMenuAbout";
            this.toolStripMenuAbout.Click += new System.EventHandler(this.toolStripMenuAbout_Click);
            // 
            // Recursive
            // 
            resources.ApplyResources(this.Recursive, "Recursive");
            this.Recursive.Name = "Recursive";
            this.Recursive.UseVisualStyleBackColor = true;
            // 
            // ImgsToPDF
            // 
            resources.ApplyResources(this, "$this");
            this.AllowDrop = true;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Recursive);
            this.Controls.Add(this.FastMode);
            this.Controls.Add(this.generateModeBox);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.StartButton);
            this.Controls.Add(this.MsgLabel);
            this.Controls.Add(this.PathLabel);
            this.Controls.Add(this.FolderImg);
            this.Controls.Add(this.PicInFolder);
            this.Controls.Add(this.labelLayout);
            this.Controls.Add(this.menuStripMain);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MainMenuStrip = this.menuStripMain;
            this.Name = "ImgsToPDF";
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
        private System.Windows.Forms.Label labelLayout;
        private System.Windows.Forms.CheckBox FastMode;
        private System.Windows.Forms.MenuStrip menuStripMain;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuFile;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuOpenFolder;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuExit;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuConfigFile;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuAbout;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuClearChosen;
        private System.Windows.Forms.ToolStripMenuItem languageToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem englishToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem chineseToolStripMenuItem;
        private System.Windows.Forms.CheckBox Recursive;
    }
}

