using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImgsToPDF {
    /// <summary>
    /// 支持穿透同级兄弟控件背景的轻量级透明 PictureBox。
    /// 自动合成父容器及下方相交同级控件的画面，支持在设计器中自由拖动与跨边界叠放。
    /// </summary>
    public class TransparentOverlayPictureBox : PictureBox {
        public TransparentOverlayPictureBox() {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnLocationChanged(EventArgs e) {
            base.OnLocationChanged(e);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent) {
            base.OnPaintBackground(pevent);
            if (Parent == null) return;

            // 按 Z-Order 从底向上遍历位于当前控件下方的同级控件，将相交区域直接绘制到背景上
            int myIndex = Parent.Controls.GetChildIndex(this);
            for (int i = Parent.Controls.Count - 1; i > myIndex; i--) {
                Control c = Parent.Controls[i];
                if (!c.Visible || !Bounds.IntersectsWith(c.Bounds)) continue;

                GraphicsState state = pevent.Graphics.Save();
                try {
                    pevent.Graphics.TranslateTransform(c.Left - Left, c.Top - Top);
                    using var pe = new PaintEventArgs(pevent.Graphics, c.ClientRectangle);
                    InvokePaintBackground(c, pe);
                    InvokePaint(c, pe);
                }
                finally {
                    pevent.Graphics.Restore(state);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs pe) {
            pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            pe.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            pe.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            base.OnPaint(pe);
        }
    }
}
