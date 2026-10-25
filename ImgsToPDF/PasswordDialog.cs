using System;
using System.IO;
using System.Windows.Forms;

namespace ImgsToPDF {
    internal partial class PasswordDialog : Form {
        public string Password => textBoxPassword.Text;

        public PasswordDialog() {
            InitializeComponent();
        }

        public PasswordDialog(string archivePath) : this() {
            if (!string.IsNullOrEmpty(archivePath)) {
                string fileName = Path.GetFileName(archivePath);
                try {
                    labelPrompt.Text = string.Format(labelPrompt.Text, fileName);
                }
                catch {
                    labelPrompt.Text = labelPrompt.Text + " " + fileName;
                }
            }
        }

        private void CheckBoxShowPassword_CheckedChanged(object sender, EventArgs e) {
            textBoxPassword.UseSystemPasswordChar = !checkBoxShowPassword.Checked;
        }
    }
}
