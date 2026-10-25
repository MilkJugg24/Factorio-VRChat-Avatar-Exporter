using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    /// <summary>
    /// Builds and takes apart PNG files byte by byte, independently of PngWriter and PngReader: its own
    /// bitwise CRC-32, filters written from the specification and .NET's ZLibStream.
    /// </summary>
    internal static class TestPng
    {
        public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static byte[] Ihdr(int width, int height, int bitDepth, int colorType, int interlace = 0)
        {
            var data = new byte[13];
            WriteUInt32(data, 0, (uint)width);
            WriteUInt32(data, 4, (uint)height);
            data[8] = (byte)bitDepth;
            data[9] = (byte)colorType;
            data[12] = (byte)interlace;
            return Chunk("IHDR", data);
        }

        /// <summary>A chunk with a correct CRC.</summary>
        public static byte[] Chunk(string type, byte[] data)
        {
            var chunk = new byte[12 + data.Length];
            WriteUInt32(chunk, 0, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, chunk, 4);
            Array.Copy(data, 0, chunk, 8, data.Length);
            WriteUInt32(chunk, 8 + data.Length, BitwiseCrc32(chunk.Skip(4).Take(4 + data.Length).ToArray()));
            return chunk;
        }

        public static byte[] Iend() => Chunk("IEND", Array.Empty<byte>());

        public static byte[] File(params byte[][] chunks)
        {
            var file = new List<byte>(Signature);
            foreach (byte[] c in chunks)
            {
                file.AddRange(c);
            }
            return file.ToArray();
        }

        /// <summary>A complete PNG from unfiltered samples, with a filter per row and the zlib stream split over IDATs.</summary>
        public static byte[] Build(int width, int height, int colorType, byte[] samples, Func<int, int> filterForRow,
            int idatParts = 1)
        {
            int channels = Channels(colorType);
            byte[] zlib = ZlibCompress(Scanlines(samples, width * channels, height, channels, filterForRow));
            var chunks = new List<byte[]> { Ihdr(width, height, 8, colorType) };
            chunks.AddRange(SplitIdat(zlib, idatParts));
            chunks.Add(Iend());
            return File(chunks.ToArray());
        }

        public static int Channels(int colorType) => colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType)),
        };

        /// <summary>IDAT chunks holding consecutive parts of the stream (some may be empty).</summary>
        public static IEnumerable<byte[]> SplitIdat(byte[] zlib, int parts)
        {
            for (int p = 0; p < parts; p++)
            {
                int start = zlib.Length * p / parts;
                int end = zlib.Length * (p + 1) / parts;
                yield return Chunk("IDAT", zlib[start..end]);
            }
        }

        public static byte[] ZlibCompress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var z = new ZLibStream(output, CompressionLevel.Fastest, true))
            {
                z.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        public static byte[] ZlibDecompress(byte[] data)
        {
            using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>Filters unfiltered rows of <paramref name="stride"/> bytes: per row a filter byte and the filtered bytes.</summary>
        public static byte[] Scanlines(byte[] samples, int stride, int height, int bpp, Func<int, int> filterForRow)
        {
            var output = new byte[(stride + 1) * height];
            for (int y = 0; y < height; y++)
            {
                int filter = filterForRow(y);
                output[y * (stride + 1)] = (byte)filter;
                for (int i = 0; i < stride; i++)
                {
                    output[y * (stride + 1) + 1 + i] = (byte)(samples[y * stride + i] - Predict(filter, samples, stride, bpp, y, i));
                }
            }
            return output;
        }

        /// <summary>The predictor of filter <paramref name="filter"/> for byte i of row y (PNG specification, section 9).</summary>
        public static int Predict(int filter, byte[] samples, int stride, int bpp, int y, int i)
        {
            int a = i >= bpp ? samples[y * stride + i - bpp] : 0;
            int b = y > 0 ? samples[(y - 1) * stride + i] : 0;
            int c = i >= bpp && y > 0 ? samples[(y - 1) * stride + i - bpp] : 0;
            switch (filter)
            {
                case 0: return 0;
                case 1: return a;
                case 2: return b;
                case 3: return (a + b) / 2;
                case 4:
                    int p = a + b - c;
                    int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                default: throw new ArgumentOutOfRangeException(nameof(filter));
            }
        }

        /// <summary>The chunks of a PNG file, checking the signature and every CRC.</summary>
        public static List<(string Type, byte[] Data)> ParseChunks(byte[] png)
        {
            if (!png.Take(8).SequenceEqual(Signature))
            {
                throw new InvalidDataException("bad signature");
            }
            var chunks = new List<(string, byte[])>();
            int offset = 8;
            while (offset < png.Length)
            {
                int length = (int)ReadUInt32(png, offset);
                string type = Encoding.ASCII.GetString(png, offset + 4, 4);
                byte[] typeAndData = png[(offset + 4)..(offset + 8 + length)];
                if (ReadUInt32(png, offset + 8 + length) != BitwiseCrc32(typeAndData))
                {
                    throw new InvalidDataException("bad CRC in " + type);
                }
                chunks.Add((type, typeAndData[4..]));
                offset += 12 + length;
            }
            return chunks;
        }

        /// <summary>CRC-32 computed bit by bit, without a table.</summary>
        public static uint BitwiseCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(int)(crc & 1));
                }
            }
            return ~crc;
        }

        public static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);

        public static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
