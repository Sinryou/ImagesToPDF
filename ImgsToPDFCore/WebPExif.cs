using System.IO;

namespace ImgsToPDFCore {
    internal static class WebPExif {
        // TIFF 中 Orientation 标签的 ID
        private const ushort OrientationTag = 0x0112;

        // TIFF 魔数（小端存储时为 42）
        private const ushort TiffMagic = 42;

        // TIFF 中 SHORT 类型的 ID
        private const ushort TypeShort = 3;

        // WebP 文件头 "RIFF"（按小端读取的 uint32 值）
        private const uint RiffFourCC = 0x46464952; // "RIFF"
        // WebP 标识 "WEBP"（按小端读取的 uint32 值）
        private const uint WebPFourCC = 0x50424557; // "WEBP"
        // EXIF 块标识 "EXIF"（按小端读取的 uint32 值）
        private const uint ExifFourCC = 0x46495845; // "EXIF"

        // EXIF 数据可能带有的固定前缀 "Exif\0\0"
        private static readonly byte[] ExifPrefix = { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };

        /// <summary>
        /// 从文件读取 WebP 的 Orientation 信息（EXIF 方向）。
        /// </summary>
        public static ushort? GetOrientation(string file) {
            using var stream = File.OpenRead(file);
            return ReadOrientation(stream);
        }

        /// <summary>
        /// 从内存中的 WebP 字节解析 Orientation（与解码共用同一次读盘，避免二次 IO）。
        /// </summary>
        public static ushort? GetOrientation(byte[] data) {
            using var stream = new MemoryStream(data, writable: false);
            return ReadOrientation(stream);
        }

        /// <summary>
        /// 从流中解析 WebP 的 Orientation。
        /// 注意：流的位置会移动到文件末尾或解析完成处。
        /// </summary>
        private static ushort? ReadOrientation(Stream stream) {
            // leaveOpen: true 避免释放 BinaryReader 时关闭外部传入的流
            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

            // 读取并验证 RIFF 头
            uint riffMagic = reader.ReadUInt32();
            if (riffMagic != RiffFourCC)
                throw new InvalidDataException("Not a RIFF file.");

            // 文件大小（此处忽略，后续通过流长度进行边界检查）
            reader.ReadUInt32();

            // 验证 WEBP 标识
            uint webpMagic = reader.ReadUInt32();
            if (webpMagic != WebPFourCC)
                throw new InvalidDataException("Not a WebP file.");

            // 循环扫描 RIFF 块
            while (stream.Position + 8 <= stream.Length) {
                // 读取块 ID（4 字节）
                uint chunkId = reader.ReadUInt32();
                // 读取块大小（4 字节，小端）
                uint chunkSize = reader.ReadUInt32();

                long chunkStart = stream.Position;
                long chunkEnd = chunkStart + (long)chunkSize;

                // 如果是 EXIF 块
                if (chunkId == ExifFourCC) {
                    // 块数据必须完整落在文件内，否则按损坏处理
                    if (chunkEnd > stream.Length)
                        throw new InvalidDataException("Corrupted WebP: EXIF chunk exceeds file size.");

                    // 读取 EXIF 数据
                    byte[] exifData = reader.ReadBytes((int)chunkSize);

                    // 解析并返回 Orientation
                    return ParseExifOrientation(exifData);
                }

                // RIFF 块按偶数字节对齐（若大小为奇数，则需跳过 1 字节填充）
                if ((chunkSize & 1) != 0)
                    chunkEnd++;

                // 如果块结束位置超出文件末尾，说明文件已损坏，停止扫描
                if (chunkEnd > stream.Length)
                    break;

                // 移动流位置到下一个块的开头
                stream.Position = chunkEnd;
            }

            // 未找到 EXIF 块
            return null;
        }

        /// <summary>
        /// 解析 EXIF 数据中的 Orientation 标签值。
        /// 兼容带或不带 "Exif\0\0" 前缀的 EXIF 数据。
        /// </summary>
        private static ushort? ParseExifOrientation(byte[] data) {
            int offset = 0;

            // 跳过可能存在的 "Exif\0\0" 前缀（6 字节）
            if (data.Length >= ExifPrefix.Length &&
                data[0] == ExifPrefix[0] && data[1] == ExifPrefix[1] &&
                data[2] == ExifPrefix[2] && data[3] == ExifPrefix[3] &&
                data[4] == ExifPrefix[4] && data[5] == ExifPrefix[5]) {
                offset = ExifPrefix.Length;
            }

            // 剩余数据至少需要 8 字节（字节序标记 + TIFF 魔数 + IFD 偏移）
            if (data.Length - offset < 8)
                return null;

            // 判断字节序
            bool littleEndian;
            if (data[offset] == 'I' && data[offset + 1] == 'I')
                littleEndian = true;
            else if (data[offset] == 'M' && data[offset + 1] == 'M')
                littleEndian = false;
            else
                return null;

            // 根据字节序提供读取方法
            ushort ReadUInt16(int pos) {
                if (littleEndian)
                    return (ushort)(data[pos] | (data[pos + 1] << 8));
                return (ushort)((data[pos] << 8) | data[pos + 1]);
            }

            uint ReadUInt32(int pos) {
                if (littleEndian)
                    return (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
                return (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
            }

            // 验证 TIFF 魔数
            if (ReadUInt16(offset + 2) != TiffMagic)
                return null;

            // 读取 IFD 偏移
            uint ifdOffset = ReadUInt32(offset + 4);

            // 检查偏移是否合法（至少能容纳 2 字节的条目数量）
            if (ifdOffset > data.Length - offset - 2)
                return null;

            int ifd = checked((int)(offset + ifdOffset));

            // 读取 IFD 条目数量
            ushort entryCount = ReadUInt16(ifd);

            // 条目起始位置
            int entriesStart = ifd + 2;

            // 遍历所有 IFD 条目
            for (int i = 0; i < entryCount; i++) {
                int entry = entriesStart + i * 12; // 每个 TIFF 条目固定 12 字节

                // 防止越界
                if (entry + 12 > data.Length)
                    return null;

                // 读取标签
                ushort tag = ReadUInt16(entry);

                // 只关心 Orientation 标签
                if (tag != OrientationTag)
                    continue;

                // 读取类型和数量
                ushort type = ReadUInt16(entry + 2);
                uint count = ReadUInt32(entry + 4);

                // Orientation 必须是 SHORT 类型且数量为 1
                if (type != TypeShort || count != 1)
                    return null;

                // 读取并返回 Orientation 值（存储在 4 字节值字段的低 2 字节）
                return ReadUInt16(entry + 8);
            }

            return null;
        }
    }
}
