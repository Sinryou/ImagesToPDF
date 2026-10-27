using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace ImgsToPDF.Shared {
    internal static class ImageExif {
        // TIFF 中 Orientation 标签的 ID
        private const ushort OrientationTag = 0x0112;

        // TIFF 魔数（42）
        private const ushort TiffMagic = 42;

        // TIFF 数据类型：SHORT (16-bit) 与 LONG (32-bit，兼容部分非标设备)
        private const ushort TypeShort = 3;
        private const ushort TypeLong = 4;

        // TIFF 字节序标识（按小端读取的 uint16 值）
        private const ushort TiffLittleEndian = 0x4949; // "II"
        private const ushort TiffBigEndian = 0x4D4D;    // "MM"

        // WebP / RIFF 块标识（按小端读取的 uint32 值）
        private const uint RiffFourCC = 0x46464952; // "RIFF"
        private const uint WebPFourCC = 0x50424557; // "WEBP"
        private const uint ExifFourCC = 0x46495845; // "EXIF"

        /// <summary>
        /// 从图片文件极速提取 EXIF Orientation（支持 JPEG, WebP, TIFF，按需读取头部，不加载整图到内存）。
        /// </summary>
        public static ushort? GetOrientation(string file) {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            return GetImageOrientation(stream);
        }

        /// <summary>
        /// 兼容旧调用：从图片原始字节中提取 EXIF Orientation（自动识别 JPEG, WebP, TIFF）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort? GetOrientation(byte[] data) => GetImageOrientation(data);

        /// <summary>
        /// 从图片原始字节中极速提取 EXIF Orientation，无需通过 GDI+ 实例化 Image。
        /// 自动识别并支持 JPEG, WebP, TIFF 格式，零对象分配。
        /// </summary>
        public static ushort? GetImageOrientation(byte[] data) {
            if (data == null || data.Length < 8) return null;

            // 1. JPEG (0xFF, 0xD8)
            if (data[0] == 0xFF && data[1] == 0xD8) {
                return GetJpegOrientation(data);
            }

            // 2. WebP ("RIFF" ... "WEBP")
            if (ReadUInt32LE(data, 0) == RiffFourCC) {
                return GetWebPOrientation(data);
            }

            // 3. TIFF ("II" 或 "MM")
            ushort byteOrder = ReadUInt16LE(data, 0);
            if (byteOrder is TiffLittleEndian or TiffBigEndian) {
                return ParseExifOrientation(data, 0, data.Length);
            }

            return null;
        }

        /// <summary>
        /// 从流中极速提取 EXIF Orientation（自动识别 JPEG, WebP, TIFF，仅读取必要的元数据块）。
        /// </summary>
        public static ushort? GetImageOrientation(Stream stream) {
            if (stream == null || !stream.CanRead || !stream.CanSeek || stream.Length < 8)
                return null;

            byte[] header = new byte[12];
            int read = ReadExact(stream, header, 0, 12);
            if (read < 8) return null;

            // 1. JPEG (0xFF, 0xD8)
            if (header[0] == 0xFF && header[1] == 0xD8) {
                stream.Position = 2;
                return ReadJpegOrientationFromStream(stream, header);
            }

            // 2. WebP ("RIFF" + size + "WEBP")
            if (read >= 12 && ReadUInt32LE(header, 0) == RiffFourCC && ReadUInt32LE(header, 8) == WebPFourCC) {
                return ReadWebPOrientationFromStream(stream, header);
            }

            // 3. TIFF ("II" 或 "MM")
            ushort byteOrder = ReadUInt16LE(header, 0);
            if (byteOrder is TiffLittleEndian or TiffBigEndian) {
                // TIFF 的 IFD 可能在文件任意偏移处，限制读取前 64KB 覆盖绝大部分头部 IFD0
                stream.Position = 0;
                int tiffReadLen = (int)Math.Min(stream.Length, 65536);
                byte[] buf = new byte[tiffReadLen];
                int actual = ReadExact(stream, buf, 0, tiffReadLen);
                return ParseExifOrientation(buf, 0, actual);
            }

            return null;
        }

        /// <summary>
        /// 从 JPEG 字节数据中扫描 APP1 标记并解析 EXIF Orientation（纯二进制扫描，不解码像素）。
        /// </summary>
        public static ushort? GetJpegOrientation(byte[] data) {
            if (data == null || data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
                return null;

            int idx = 2;
            int len = data.Length;

            while (idx + 4 <= len) {
                if (data[idx] != 0xFF) break;

                // 跳过填充的 0xFF
                while (idx < len && data[idx] == 0xFF) {
                    idx++;
                }
                if (idx >= len) break;

                byte marker = data[idx++];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                    continue; // SOI, TEM 或 RSTn 无长度字段
                if (marker is 0xD9 or 0xDA)
                    break;    // EOI 或 SOS（扫描线压缩数据开始，停止扫描）

                if (idx + 2 > len) break;
                int payloadLen = (data[idx] << 8) | data[idx + 1];
                if (payloadLen < 2) break;

                int markerDataStart = idx + 2;
                int markerDataLen = payloadLen - 2;
                if (markerDataStart + markerDataLen > len) break;

                // 如果是 APP1 (0xE1) 且包含 "Exif\0\0" 前缀，直接跳过 6 字节进入 TIFF 解析
                if (marker == 0xE1 && HasExifPrefix(data, markerDataStart, markerDataLen)) {
                    return ParseTiffOrientationCore(data, markerDataStart + 6, markerDataLen - 6);
                }

                idx = markerDataStart + markerDataLen;
            }

            return null;
        }

        /// <summary>
        /// 从内存中的 WebP 字节解析 Orientation（纯内存扫描 RIFF 块）。
        /// </summary>
        public static ushort? GetWebPOrientation(byte[] data) {
            if (data == null || data.Length < 12) return null;
            if (ReadUInt32LE(data, 0) != RiffFourCC || ReadUInt32LE(data, 8) != WebPFourCC)
                return null;

            int offset = 12;
            int len = data.Length;

            while (offset + 8 <= len) {
                uint chunkId = ReadUInt32LE(data, offset);
                uint chunkSize = ReadUInt32LE(data, offset + 4);

                int chunkDataStart = offset + 8;
                if (chunkDataStart + (long)chunkSize > len) break;

                if (chunkId == ExifFourCC) {
                    return ParseExifOrientation(data, chunkDataStart, (int)chunkSize);
                }

                // RIFF 块按偶数大小对齐
                long next = chunkDataStart + chunkSize + (chunkSize & 1);
                if (next > len) break;
                offset = (int)next;
            }

            return null;
        }

        /// <summary>
        /// 解析 EXIF/TIFF 数据中的 Orientation 标签值（兼容带或不带 "Exif\0\0" 前缀）。
        /// </summary>
        public static ushort? ParseExifOrientation(byte[] data, int offset, int length) {
            // 使用无符号转换防止 offset + length 整数溢出
            if (data == null || offset < 0 || length < 8 || (uint)offset > (uint)data.Length || (uint)length > (uint)(data.Length - offset))
                return null;

            if (HasExifPrefix(data, offset, length)) {
                offset += 6;
                length -= 6;
            }

            return ParseTiffOrientationCore(data, offset, length);
        }

        private static ushort? ParseTiffOrientationCore(byte[] data, int tiffHeaderOffset, int length) {
            if (length < 8) return null;

            int end = tiffHeaderOffset + length;

            ushort byteOrder = ReadUInt16LE(data, tiffHeaderOffset);
            bool littleEndian;
            if (byteOrder == TiffLittleEndian)
                littleEndian = true;
            else if (byteOrder == TiffBigEndian)
                littleEndian = false;
            else
                return null;

            if (ReadUInt16(data, tiffHeaderOffset + 2, littleEndian) != TiffMagic)
                return null;

            uint ifdOffset = ReadUInt32(data, tiffHeaderOffset + 4, littleEndian);
            if (ifdOffset > (uint)(length - 2))
                return null;

            int ifd = tiffHeaderOffset + (int)ifdOffset;
            ushort entryCount = ReadUInt16(data, ifd, littleEndian);
            int entriesStart = ifd + 2;

            for (int i = 0; i < entryCount; i++) {
                int entry = entriesStart + i * 12;
                if (entry + 12 > end)
                    return null;

                ushort tag = ReadUInt16(data, entry, littleEndian);
                if (tag != OrientationTag)
                    continue;

                ushort type = ReadUInt16(data, entry + 2, littleEndian);
                uint count = ReadUInt32(data, entry + 4, littleEndian);
                if (count != 1)
                    return null;

                ushort val;
                if (type == TypeShort) {
                    val = ReadUInt16(data, entry + 8, littleEndian);
                }
                else if (type == TypeLong) {
                    uint raw = ReadUInt32(data, entry + 8, littleEndian);
                    val = raw <= ushort.MaxValue ? (ushort)raw : (ushort)0;
                }
                else {
                    return null;
                }

                return val is >= 1 and <= 8 ? val : (ushort)1;
            }

            return null;
        }

        private static ushort? ReadJpegOrientationFromStream(Stream stream, byte[] scratch) {
            long len = stream.Length;
            while (stream.Position + 4 <= len) {
                int b = stream.ReadByte();
                if (b != 0xFF) break;

                do {
                    b = stream.ReadByte();
                } while (b == 0xFF);

                if (b < 0) break;
                byte marker = (byte)b;

                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                    continue;
                if (marker is 0xD9 or 0xDA)
                    break;

                if (ReadExact(stream, scratch, 0, 2) != 2) break;
                int payloadLen = (scratch[0] << 8) | scratch[1];
                if (payloadLen < 2) break;

                int markerDataLen = payloadLen - 2;
                long nextPos = stream.Position + markerDataLen;
                if (nextPos > len) break;

                if (marker == 0xE1 && markerDataLen >= 14) {
                    if (ReadExact(stream, scratch, 0, 6) == 6 && HasExifPrefix(scratch, 0, 6)) {
                        int tiffLen = markerDataLen - 6;
                        byte[] exifBuf = new byte[tiffLen];
                        if (ReadExact(stream, exifBuf, 0, tiffLen) == tiffLen) {
                            return ParseTiffOrientationCore(exifBuf, 0, tiffLen);
                        }
                    }
                }

                stream.Position = nextPos;
            }
            return null;
        }

        private static ushort? ReadWebPOrientationFromStream(Stream stream, byte[] scratch) {
            long len = stream.Length;
            while (stream.Position + 8 <= len) {
                if (ReadExact(stream, scratch, 0, 8) != 8) break;

                uint chunkId = ReadUInt32LE(scratch, 0);
                uint chunkSize = ReadUInt32LE(scratch, 4);

                long chunkStart = stream.Position;
                long chunkEnd = chunkStart + chunkSize;
                if (chunkEnd > len) break;

                if (chunkId == ExifFourCC && chunkSize >= 8 && chunkSize <= 1024 * 1024) {
                    byte[] exifData = new byte[(int)chunkSize];
                    if (ReadExact(stream, exifData, 0, exifData.Length) == exifData.Length) {
                        return ParseExifOrientation(exifData, 0, exifData.Length);
                    }
                    return null;
                }

                chunkEnd += chunkSize & 1;
                if (chunkEnd > len) break;
                stream.Position = chunkEnd;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasExifPrefix(byte[] data, int offset, int length) {
            return length >= 6 &&
                   data[offset] == (byte)'E' &&
                   data[offset + 1] == (byte)'x' &&
                   data[offset + 2] == (byte)'i' &&
                   data[offset + 3] == (byte)'f' &&
                   data[offset + 4] == 0 &&
                   data[offset + 5] == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ReadUInt16LE(byte[] data, int pos) =>
            (ushort)(data[pos] | (data[pos + 1] << 8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadUInt32LE(byte[] data, int pos) =>
            (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ReadUInt16(byte[] data, int pos, bool littleEndian) =>
            littleEndian
                ? (ushort)(data[pos] | (data[pos + 1] << 8))
                : (ushort)((data[pos] << 8) | data[pos + 1]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadUInt32(byte[] data, int pos, bool littleEndian) =>
            littleEndian
                ? (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24))
                : (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);

        private static int ReadExact(Stream stream, byte[] buffer, int offset, int count) {
            int totalRead = 0;
            while (totalRead < count) {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            return totalRead;
        }
    }
}