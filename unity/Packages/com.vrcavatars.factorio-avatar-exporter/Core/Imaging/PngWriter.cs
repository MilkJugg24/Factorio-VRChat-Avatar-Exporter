using System;
using System.IO;
using System.Text;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Writes PNG files.</summary>
    public static class PngWriter
    {
        private const int BytesPerPixel = 4;

        /// <summary>
        /// Encodes an image as an 8-bit RGBA, non-interlaced PNG made of the signature, IHDR, one IDAT and
        /// IEND, and nothing else: no color space chunk, so Factorio and viewers take the pixels as sRGB.
        /// Each row gets the filter whose output has the smallest sum of absolute values (bytes read as
        /// signed), the heuristic the PNG specification recommends; ties go to the lower filter type. Equal
        /// images give equal bytes.
        /// </summary>
        public static byte[] Encode(Image8 image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }
            byte[] compressed = Zlib.Compress(FilterRows(image));
            using (var png = new MemoryStream(compressed.Length + 64))
            {
                png.Write(PngFormat.Signature, 0, PngFormat.Signature.Length);
                WriteChunk(png, PngFormat.Ihdr, Header(image));
                WriteChunk(png, PngFormat.Idat, compressed);
                WriteChunk(png, PngFormat.Iend, Array.Empty<byte>());
                return png.ToArray();
            }
        }

        private static byte[] Header(Image8 image)
        {
            var header = new byte[PngFormat.IhdrLength];
            PngFormat.WriteUInt32(header, 0, (uint)image.Width);
            PngFormat.WriteUInt32(header, 4, (uint)image.Height);
            header[8] = 8; // bits per sample
            header[9] = PngFormat.ColorRgba;
            // compression, filter method and interlace are all 0: deflate, adaptive filtering, none
            return header;
        }

        /// <summary>The scanlines as the IDAT stream holds them: per row, the filter type, then the filtered bytes.</summary>
        private static byte[] FilterRows(Image8 image)
        {
            int stride = image.Width * BytesPerPixel;
            var output = new byte[(long)(stride + 1) * image.Height];
            var prior = new byte[stride];
            var row = new byte[stride];
            var candidates = new byte[PngFilters.Count][];
            for (int f = 0; f < PngFilters.Count; f++)
            {
                candidates[f] = new byte[stride];
            }
            for (int y = 0; y < image.Height; y++)
            {
                Buffer.BlockCopy(image.Pixels, y * stride, row, 0, stride);
                int best = 0;
                long bestScore = long.MaxValue;
                for (int f = 0; f < PngFilters.Count; f++)
                {
                    PngFilters.Apply(f, row, prior, BytesPerPixel, candidates[f]);
                    long score = PngFilters.Score(candidates[f]);
                    if (score < bestScore)
                    {
                        best = f;
                        bestScore = score;
                    }
                }
                int o = y * (stride + 1);
                output[o] = (byte)best;
                Buffer.BlockCopy(candidates[best], 0, output, o + 1, stride);
                (prior, row) = (row, prior);
            }
            return output;
        }

        private static void WriteChunk(Stream png, string type, byte[] data)
        {
            var head = new byte[8];
            PngFormat.WriteUInt32(head, 0, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
            uint crc = Checksums.UpdateCrc32(Checksums.Crc32(head, 4, 4), data, 0, data.Length);
            var crcBytes = new byte[4];
            PngFormat.WriteUInt32(crcBytes, 0, crc);
            png.Write(head, 0, head.Length);
            png.Write(data, 0, data.Length);
            png.Write(crcBytes, 0, crcBytes.Length);
        }
    }
}
