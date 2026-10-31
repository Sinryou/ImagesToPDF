using System;
using System.Collections.Generic;

namespace ImgsToPDF.Shared {
    /// <summary>
    /// GUI 与 Core 双方共同认可的文件格式契约。
    /// </summary>
    internal static class ImageContracts {
        /// <summary>支持的所有图片格式扩展名（含经由 libwebp 解码的 .webp）。</summary>
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif", ".webp" };

        /// <summary>可能含有 EXIF Orientation 信息的图片格式扩展名。</summary>
        public static readonly HashSet<string> ImageExtensionsEXIFOrientation = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".tif", ".tiff" };

        /// <summary>
        /// 支持的压缩包格式扩展名。GUI 据此决定可拖入/打开哪些文件；
        /// Core 侧 config.lua 中的 compressSuffix 列表应与此保持一致。
        /// </summary>
        public static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };
    }
}
