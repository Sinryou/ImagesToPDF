using System;
using System.IO;
using System.Text;

namespace ImgsToPDFCore {
    internal static class WebPExif {
        private const ushort OrientationTag = 0x0112;

        public static ushort? GetOrientation(string file) {
            using var stream = File.OpenRead(file);
            using var reader = new BinaryReader(stream);

            // RIFF
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
                throw new InvalidDataException("Not a RIFF file.");

            // File size
            reader.ReadUInt32();

            // WEBP
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WEBP")
                throw new InvalidDataException("Not a WebP file.");

            while (stream.Position + 8 <= stream.Length) {
                string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint chunkSize = reader.ReadUInt32();

                long chunkEnd = stream.Position + chunkSize;

                if (chunkId == "EXIF") {
                    byte[] exif = reader.ReadBytes(checked((int)chunkSize));

                    return ParseExifOrientation(exif);
                }

                stream.Position = chunkEnd;

                // RIFF chunk 按偶数字节对齐
                if ((chunkSize & 1) != 0)
                    stream.Position++;
            }

            return null;
        }

        private static ushort? ParseExifOrientation(byte[] data) {
            if (data.Length < 8)
                return null;

            bool littleEndian;

            if (data[0] == 'I' && data[1] == 'I')
                littleEndian = true;
            else if (data[0] == 'M' && data[1] == 'M')
                littleEndian = false;
            else
                return null;

            ushort ReadUInt16(int offset) {
                if (littleEndian)
                    return (ushort)(data[offset] | (data[offset + 1] << 8));

                return (ushort)((data[offset] << 8) | data[offset + 1]);
            }

            uint ReadUInt32(int offset) {
                if (littleEndian) {
                    return (uint)(
                        data[offset] |
                        (data[offset + 1] << 8) |
                        (data[offset + 2] << 16) |
                        (data[offset + 3] << 24));
                }

                return (uint)(
                    (data[offset] << 24) |
                    (data[offset + 1] << 16) |
                    (data[offset + 2] << 8) |
                    data[offset + 3]);
            }

            // TIFF magic number = 42
            if (ReadUInt16(2) != 42)
                return null;

            uint ifdOffset = ReadUInt32(4);

            if (ifdOffset > data.Length - 2)
                return null;

            int ifd = checked((int)ifdOffset);

            ushort entryCount = ReadUInt16(ifd);

            int entriesStart = ifd + 2;

            for (int i = 0; i < entryCount; i++) {
                int entry = entriesStart + i * 12;

                if (entry + 12 > data.Length)
                    return null;

                ushort tag = ReadUInt16(entry);

                if (tag != OrientationTag)
                    continue;

                ushort type = ReadUInt16(entry + 2);
                uint count = ReadUInt32(entry + 4);

                // Orientation:
                // TIFF type = SHORT (3)
                // count = 1
                if (type != 3 || count != 1)
                    return null;

                return ReadUInt16(entry + 8);
            }

            return null;
        }
    }
}
