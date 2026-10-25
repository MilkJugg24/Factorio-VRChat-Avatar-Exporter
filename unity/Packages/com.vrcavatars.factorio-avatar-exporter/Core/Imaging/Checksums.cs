using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>The checksums of PNG and zlib (also those of zip files).</summary>
    public static class Checksums
    {
        private const uint Crc32Polynomial = 0xEDB88320; // reflected IEEE 802.3
        private const uint AdlerModulus = 65521;

        // The most bytes Adler-32 can sum before its 32-bit accumulators may overflow (zlib's NMAX).
        private const int AdlerBlock = 5552;

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        /// <summary>CRC-32 (IEEE, as used by PNG, zlib's crc32 and zip) of a whole array.</summary>
        public static uint Crc32(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            return UpdateCrc32(0, data, 0, data.Length);
        }

        /// <summary>CRC-32 of <paramref name="count"/> bytes of <paramref name="data"/> from <paramref name="offset"/>.</summary>
        public static uint Crc32(byte[] data, int offset, int count) => UpdateCrc32(0, data, offset, count);

        /// <summary>
        /// Continues a CRC-32 with more bytes: <c>UpdateCrc32(Crc32(a), b)</c> is the CRC-32 of a followed
        /// by b. The CRC-32 of nothing is 0.
        /// </summary>
        public static uint UpdateCrc32(uint crc, byte[] data, int offset, int count)
        {
            CheckRange(data, offset, count);
            uint c = ~crc;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                c = Crc32Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            }
            return ~c;
        }

        /// <summary>Adler-32 (RFC 1950, the checksum ending a zlib stream) of a whole array.</summary>
        public static uint Adler32(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            return Adler32(data, 0, data.Length);
        }

        /// <summary>Adler-32 of <paramref name="count"/> bytes of <paramref name="data"/> from <paramref name="offset"/>.</summary>
        public static uint Adler32(byte[] data, int offset, int count)
        {
            CheckRange(data, offset, count);
            uint a = 1;
            uint b = 0;
            int end = offset + count;
            for (int i = offset; i < end;)
            {
                int blockEnd = Math.Min(end, i + AdlerBlock);
                for (; i < blockEnd; i++)
                {
                    a += data[i];
                    b += a;
                }
                a %= AdlerModulus;
                b %= AdlerModulus;
            }
            return (b << 16) | a;
        }

        private static void CheckRange(byte[] data, int offset, int count)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (offset < 0 || count < 0 || offset > data.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(count),
                    $"range [{offset}, {offset}+{count}) is outside an array of {data.Length} bytes");
            }
        }

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? Crc32Polynomial ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }
    }
}
