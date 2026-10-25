using System;
using System.IO;
using System.Text;

namespace FactorioAvatarExporter.Core.Tests.Pack
{
    /// <summary>
    /// A minimal PNG encoder for tests (the real one is another module): 8-bit RGBA, no filtering,
    /// zlib "stored" (uncompressed) deflate blocks.
    /// </summary>
    internal static class TestPng
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] Encode(Image8 image)
        {
            var png = new MemoryStream();
            png.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            var header = new MemoryStream();
            WriteBigEndian(header, (uint)image.Width);
            WriteBigEndian(header, (uint)image.Height);
            header.Write(new byte[] { 8, 6, 0, 0, 0 }, 0, 5); // 8 bits, RGBA, deflate, no filter method, no interlace
            WriteChunk(png, "IHDR", header.ToArray());
            WriteChunk(png, "IDAT", Zlib(Scanlines(image)));
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static byte[] Scanlines(Image8 image)
        {
            int rowBytes = image.Width * 4;
            var raw = new byte[(rowBytes + 1) * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                // filter type 0 (None) at the start of each row
                Buffer.BlockCopy(image.Pixels, y * rowBytes, raw, y * (rowBytes + 1) + 1, rowBytes);
            }
            return raw;
        }

        private static byte[] Zlib(byte[] data)
        {
            var z = new MemoryStream();
            z.WriteByte(0x78);
            z.WriteByte(0x01);
            int offset = 0;
            do
            {
                int length = Math.Min(65535, data.Length - offset);
                bool last = offset + length == data.Length;
                z.WriteByte(last ? (byte)1 : (byte)0);
                z.WriteByte((byte)length);
                z.WriteByte((byte)(length >> 8));
                z.WriteByte((byte)~length);
                z.WriteByte((byte)(~length >> 8));
                z.Write(data, offset, length);
                offset += length;
            }
            while (offset < data.Length);
            WriteBigEndian(z, Adler32(data));
            return z.ToArray();
        }

        private static void WriteChunk(Stream png, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            WriteBigEndian(png, (uint)data.Length);
            png.Write(typeBytes, 0, 4);
            png.Write(data, 0, data.Length);
            uint crc = Crc32(Crc32(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
            WriteBigEndian(png, crc);
        }

        private static void WriteBigEndian(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(uint crc, byte[] data)
        {
            foreach (byte b in data)
            {
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}
