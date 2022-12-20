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
            ((System.ComponentModel.ISupportInitialize)(this.FolderImg)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PicInFolder)).BeginInit();
            this.SuspendLayout();
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(548, 492);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(239, 28);
            this.progressBar.TabIndex = 11;
            this.progressBar.Visible = false;
            // 
            // StartButton
            // 
            this.StartButton.Enabled = false;
            this.StartButton.Location = new System.Drawing.Point(357, 490);
            this.StartButton.Name = "StartButton";
            this.StartButton.Size = new System.Drawing.Size(97, 32);
            this.StartButton.TabIndex = 10;
            this.StartButton.Text = "Start";
            this.StartButton.UseVisualStyleBackColor = true;
            this.StartButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // MsgLabel
            // 
            this.MsgLabel.Location = new System.Drawing.Point(156, 406);
            this.MsgLabel.Name = "MsgLabel";
            this.MsgLabel.Size = new System.Drawing.Size(483, 36);
            this.MsgLabel.TabIndex = 9;
            this.MsgLabel.Text = "拖入包含图片的文件夹 drop your folder here";
            this.MsgLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // PathLabel
            // 
            this.PathLabel.Location = new System.Drawing.Point(11, 371);
            this.PathLabel.Name = "PathLabel";
            this.PathLabel.Size = new System.Drawing.Size(776, 35);
            this.PathLabel.TabIndex = 8;
            this.PathLabel.Text = " ";
            this.PathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // FolderImg
            // 
            this.FolderImg.BackColor = System.Drawing.Color.Transparent;
            this.FolderImg.Location = new System.Drawing.Point(244, 251);
            this.FolderImg.Name = "FolderImg";
            this.FolderImg.Size = new System.Drawing.Size(90, 90);
            this.FolderImg.TabIndex = 7;
            this.FolderImg.TabStop = false;
            // 
            // PicInFolder
            // 
            this.PicInFolder.Image = global::ImgsToPDF.Properties.Resources.folder;
            this.PicInFolder.Location = new System.Drawing.Point(296, 23);
            this.PicInFolder.Name = "PicInFolder";
            this.PicInFolder.Size = new System.Drawing.Size(200, 300);
            this.PicInFolder.TabIndex = 6;
            this.PicInFolder.TabStop = false;
            // 
            // generateModeBox
            // 
            this.generateModeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.generateModeBox.FormattingEnabled = true;
            this.generateModeBox.Location = new System.Drawing.Point(192, 494);
            this.generateModeBox.Name = "generateModeBox";
            this.generateModeBox.Size = new System.Drawing.Size(134, 26);
            this.generateModeBox.TabIndex = 12;
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(7, 482);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(192, 48);
            this.label1.TabIndex = 13;
            this.label1.Text = "PDF生成模式 Mode：";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // FastMode
            // 
            this.FastMode.AutoSize = true;
            this.FastMode.Location = new System.Drawing.Point(21, 457);
            this.FastMode.Name = "FastMode";
            this.FastMode.Size = new System.Drawing.Size(304, 22);
            this.FastMode.TabIndex = 14;
            this.FastMode.Text = "有损(更快生成速度更小文件)Fast";
            this.FastMode.UseVisualStyleBackColor = true;
            // 
            // ImgsToPDF
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(803, 539);
            this.Controls.Add(this.FastMode);
            this.Controls.Add(this.generateModeBox);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.StartButton);
            this.Controls.Add(this.MsgLabel);
            this.Controls.Add(this.PathLabel);
            this.Controls.Add(this.FolderImg);
            this.Controls.Add(this.PicInFolder);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "ImgsToPDF";
            this.Text = "ImgsToPDF";
            this.Load += new System.EventHandler(this.ImgsToPDF_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.ImgsToPDF_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.ImgsToPDF_DragEnter);
            ((System.ComponentModel.ISupportInitialize)(this.FolderImg)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PicInFolder)).EndInit();
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
    }
}

