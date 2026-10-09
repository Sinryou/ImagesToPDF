using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImgsToPDF {
    /// <summary>
    /// 支持穿透同级兄弟控件背景的增强型 PictureBox。
    /// 解决 WinForms 中两控件重叠且 BackColor=Transparent 时，
    /// 上层控件无法透出下层兄弟控件画面（只能透出 Form 底色形成灰色缺口）的缺陷。
    /// </summary>
    public class TransparentOverlayPictureBox : PictureBox {
        private Control _underlyingControl;
        private bool _isCapturingUnderlying;
        private Bitmap _cachedUnderlyingBmp;
        private Image _lastUnderlyingImage;
        private bool _isCacheDirty = true;

        /// <summary>
        /// 位于本控件下方的同级兄弟控件（例如大图预览 PicInFolder）。
        /// 当两控件矩形相交时，相交区域将透出底层控件的实际画面。
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Control UnderlyingControl {
            get => _underlyingControl;
            set {
                if (_underlyingControl != value) {
                    if (_underlyingControl != null) {
                        _underlyingControl.Paint -= OnUnderlyingControlPaint;
                        _underlyingControl.LocationChanged -= OnUnderlyingControlLayoutChanged;
                        _underlyingControl.SizeChanged -= OnUnderlyingControlLayoutChanged;
                        _underlyingControl.VisibleChanged -= OnUnderlyingControlLayoutChanged;
                    }
                    _underlyingControl = value;
                    if (_underlyingControl != null) {
                        _underlyingControl.Paint += OnUnderlyingControlPaint;
                        _underlyingControl.LocationChanged += OnUnderlyingControlLayoutChanged;
                        _underlyingControl.SizeChanged += OnUnderlyingControlLayoutChanged;
                        _underlyingControl.VisibleChanged += OnUnderlyingControlLayoutChanged;
                    }
                    InvalidateCache();
                }
            }
        }

        public TransparentOverlayPictureBox() {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            UpdateStyles();
            BackColor = Color.Transparent;
        }

        public void InvalidateCache() {
            _isCacheDirty = true;
            Invalidate();
        }

        private void OnUnderlyingControlPaint(object sender, PaintEventArgs e) {
            // 防重入：在 DrawToBitmap 截取期间底层控件可能触发 Paint，此时不重复 Invalidate
            if (_isCapturingUnderlying) {
                return;
            }

            // 针对底层 PictureBox 优化：底层 Image 引用未变时无需重绘，避免级联 Invalidate
            if (_underlyingControl is PictureBox pb) {
                if (!ReferenceEquals(pb.Image, _lastUnderlyingImage)) {
                    InvalidateCache();
                }
            }
            else {
                InvalidateCache();
            }
        }

        private void OnUnderlyingControlLayoutChanged(object sender, EventArgs e) {
            InvalidateCache();
        }

        protected override void OnPaint(PaintEventArgs pe) {
            var g = pe.Graphics;

            // 如果当前无图片，直接返回（保持透明/不可见状态）
            if (Image == null) {
                return;
            }

            // 1. 先用父容器背景色填充非重叠区域
            if (Parent != null) {
                using var bgBrush = new SolidBrush(Parent.BackColor);
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            // 2. 若与底层兄弟控件相交，将底层控件在交集区域的画面截取并绘制到底部
            if (_underlyingControl != null && _underlyingControl.Visible && _underlyingControl.Width > 0 && _underlyingControl.Height > 0) {
                Rectangle intersect = Rectangle.Intersect(Bounds, _underlyingControl.Bounds);
                if (!intersect.IsEmpty) {
                    UpdateUnderlyingCacheIfNeeded();

                    if (_cachedUnderlyingBmp != null) {
                        // 计算交集在底层控件中的源矩形与当前控件中的目标矩形
                        Rectangle srcRect = new(
                            intersect.X - _underlyingControl.Left,
                            intersect.Y - _underlyingControl.Top,
                            intersect.Width,
                            intersect.Height
                        );

                        Rectangle destRect = new(
                            intersect.X - this.Left,
                            intersect.Y - this.Top,
                            intersect.Width,
                            intersect.Height
                        );

                        g.DrawImage(_cachedUnderlyingBmp, destRect, srcRect, GraphicsUnit.Pixel);
                    }
                }
            }

            // 3. 高质量平滑绘制本控件的前景 Image
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            DrawImageBySizeMode(g, Image, ClientRectangle);
        }

        private void UpdateUnderlyingCacheIfNeeded() {
            if (!_isCacheDirty && _cachedUnderlyingBmp != null) {
                return;
            }

            try {
                _isCapturingUnderlying = true;
                int w = _underlyingControl.Width;
                int h = _underlyingControl.Height;
                if (w <= 0 || h <= 0) {
                    return;
                }

                // 尺寸改变或尚未分配时才重新创建 Bitmap，重用现有实例减少 GC 分配
                if (_cachedUnderlyingBmp == null || _cachedUnderlyingBmp.Width != w || _cachedUnderlyingBmp.Height != h) {
                    _cachedUnderlyingBmp?.Dispose();
                    _cachedUnderlyingBmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                }

                _underlyingControl.DrawToBitmap(_cachedUnderlyingBmp, new Rectangle(0, 0, w, h));

                if (_underlyingControl is PictureBox pb) {
                    _lastUnderlyingImage = pb.Image;
                }
                _isCacheDirty = false;
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[TransparentOverlayPictureBox] Capture underlying control failed: {ex.Message}");
            }
            finally {
                _isCapturingUnderlying = false;
            }
        }

        private void DrawImageBySizeMode(Graphics g, Image img, Rectangle destRect) {
            if (img.Width <= 0 || img.Height <= 0 || destRect.Width <= 0 || destRect.Height <= 0) {
                return;
            }

            switch (SizeMode) {
                case PictureBoxSizeMode.Zoom: {
                    float ratio = Math.Min((float)destRect.Width / img.Width, (float)destRect.Height / img.Height);
                    int w = Math.Max(1, (int)(img.Width * ratio));
                    int h = Math.Max(1, (int)(img.Height * ratio));
                    int x = destRect.X + (destRect.Width - w) / 2;
                    int y = destRect.Y + (destRect.Height - h) / 2;
                    g.DrawImage(img, new Rectangle(x, y, w, h));
                    break;
                }
                case PictureBoxSizeMode.StretchImage: {
                    g.DrawImage(img, destRect);
                    break;
                }
                case PictureBoxSizeMode.CenterImage: {
                    int x = destRect.X + (destRect.Width - img.Width) / 2;
                    int y = destRect.Y + (destRect.Height - img.Height) / 2;
                    g.DrawImage(img, new Rectangle(x, y, img.Width, img.Height));
                    break;
                }
                default: {
                    g.DrawImage(img, destRect.Location);
                    break;
                }
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                UnderlyingControl = null; // 解绑事件监听
                _cachedUnderlyingBmp?.Dispose();
                _cachedUnderlyingBmp = null;
                _lastUnderlyingImage = null;
            }
            base.Dispose(disposing);
        }
    }
}
