using System;
using System.IO;
using System.IO.Compression;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// zlib streams (RFC 1950) on top of System.IO.Compression's raw deflate, because .NET Standard 2.1
    /// has no ZLibStream: a 2-byte header, the deflate data, then the big-endian Adler-32 of the
    /// uncompressed bytes.
    /// </summary>
    public static class Zlib
    {
        // CMF: deflate with a 32 KiB window. FLG: "default compression" level and check bits such that
        // (CMF * 256 + FLG) % 31 == 0.
        private const byte Cmf = 0x78;
        private const byte Flg = 0x9C;

        private const int HeaderLength = 2;
        private const int TrailerLength = 4;

        /// <summary>
        /// Compresses <paramref name="data"/> with <see cref="CompressionLevel.Optimal"/>. Equal input gives
        /// equal output (on the same runtime).
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            using (var output = new MemoryStream(data.Length / 2 + 64))
            {
                output.WriteByte(Cmf);
                output.WriteByte(Flg);
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    deflate.Write(data, 0, data.Length);
                }
                if (output.Length == HeaderLength)
                {
                    // Mono's DeflateStream (and so Unity's) writes nothing at all for empty input; an
                    // empty final fixed-Huffman block, as .NET writes, keeps the stream valid
                    output.WriteByte(0x03);
                    output.WriteByte(0x00);
                }
                uint adler = Checksums.Adler32(data);
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                return output.ToArray();
            }
        }

        /// <summary>
        /// Decompresses a zlib stream after checking its header (deflate, window at most 32 KiB, valid
        /// check bits, no preset dictionary), and checks the Adler-32 in its last 4 bytes.
        /// </summary>
        /// <param name="zlibData">The whole stream.</param>
        /// <param name="maxLength">
        /// The most bytes the caller expects; longer output is rejected, so a small malicious stream
        /// cannot expand to gigabytes.
        /// </param>
        /// <exception cref="InvalidDataException">The stream is malformed, corrupt or longer than allowed.</exception>
        public static byte[] Decompress(byte[] zlibData, int maxLength)
        {
            if (zlibData == null)
            {
                throw new ArgumentNullException(nameof(zlibData));
            }
            if (maxLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "must not be negative");
            }
            CheckHeader(zlibData);
            byte[] result = Inflate(zlibData, maxLength);
            int t = zlibData.Length - TrailerLength;
            uint expected = (uint)(zlibData[t] << 24 | zlibData[t + 1] << 16 | zlibData[t + 2] << 8 | zlibData[t + 3]);
            uint actual = Checksums.Adler32(result);
            if (actual != expected)
            {
                throw new InvalidDataException(
                    $"zlib data is corrupt: Adler-32 is 0x{actual:X8}, the stream says 0x{expected:X8}");
            }
            return result;
        }

        private static void CheckHeader(byte[] data)
        {
            if (data.Length < HeaderLength + TrailerLength + 1)
            {
                throw new InvalidDataException($"zlib stream too short ({data.Length} bytes)");
            }
            int cmf = data[0];
            int flg = data[1];
            if ((cmf & 0x0F) != 8)
            {
                throw new InvalidDataException($"zlib compression method {cmf & 0x0F} is not deflate (8)");
            }
            if (cmf >> 4 > 7)
            {
                throw new InvalidDataException($"zlib window size code {cmf >> 4} is larger than 7");
            }
            if ((cmf * 256 + flg) % 31 != 0)
            {
                throw new InvalidDataException("zlib header check bits are wrong");
            }
            if ((flg & 0x20) != 0)
            {
                throw new InvalidDataException("zlib streams with a preset dictionary are not supported");
            }
        }

        private static byte[] Inflate(byte[] data, int maxLength)
        {
            var input = new MemoryStream(data, HeaderLength, data.Length - HeaderLength - TrailerLength, false);
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[65536];
                int read;
                while ((read = ReadBlock(deflate, buffer)) > 0)
                {
                    if (read > maxLength - output.Length)
                    {
                        throw new InvalidDataException($"zlib data expands to more than the expected {maxLength} bytes");
                    }
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static int ReadBlock(Stream deflate, byte[] buffer)
        {
            try
            {
                return deflate.Read(buffer, 0, buffer.Length);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                // runtimes differ in which of the two they throw for bad deflate data
                throw new InvalidDataException("zlib data is corrupt: " + e.Message, e);
            }
        }
    }
}
