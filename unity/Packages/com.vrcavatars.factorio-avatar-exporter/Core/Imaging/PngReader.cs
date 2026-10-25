using System;
using System.IO;
using System.Text;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Reads PNG files.</summary>
    public static class PngReader
    {
        private const int ChunkOverhead = 12; // length, type, CRC

        /// <summary>
        /// Decodes a PNG file into a straight-alpha RGBA image. Supports 8 bits per sample in color types 6
        /// (RGBA), 2 (RGB), 4 (gray + alpha) and 0 (gray), all five filters and image data split over any
        /// number of IDAT chunks: what Unity, Pillow and image editors write for sprites. Gray becomes
        /// equal r, g and b; images without alpha are opaque, except pixels matching a tRNS color key.
        /// Every chunk's CRC, the zlib header and the Adler-32 are checked. Other ancillary chunks (gamma,
        /// color profiles, text) are ignored and samples are returned as stored.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The data is not a well-formed PNG: bad signature, truncated, out-of-order or corrupt chunks,
        /// a CRC or checksum mismatch, bad filter types or too little image data.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// A PNG this reader does not handle: palette images, bit depths other than 8, interlacing, or an
        /// unknown critical chunk.
        /// </exception>
        public static Image8 Decode(byte[] png)
        {
            if (png == null)
            {
                throw new ArgumentNullException(nameof(png));
            }
            CheckSignature(png);
            using (var imageData = new MemoryStream())
            {
                Header header = null;
                byte[] colorKey = null;
                bool idatSeen = false;
                bool idatEnded = false;
                int offset = PngFormat.Signature.Length;
                while (true)
                {
                    Chunk chunk = ReadChunk(png, offset);
                    offset = chunk.End;
                    if (header == null)
                    {
                        if (chunk.Type != PngFormat.Ihdr)
                        {
                            throw new InvalidDataException($"the first chunk is '{chunk.Type}', not IHDR");
                        }
                        header = Header.Parse(png, chunk);
                        continue;
                    }
                    if (chunk.Type == PngFormat.Idat)
                    {
                        if (idatEnded)
                        {
                            throw new InvalidDataException("IDAT chunks are not consecutive");
                        }
                        imageData.Write(png, chunk.DataOffset, chunk.Length);
                        idatSeen = true;
                        continue;
                    }
                    idatEnded = idatSeen;
                    switch (chunk.Type)
                    {
                        case PngFormat.Iend:
                            if (!idatSeen)
                            {
                                throw new InvalidDataException("the PNG has no IDAT chunk");
                            }
                            return DecodePixels(header, imageData.ToArray(), colorKey);
                        case PngFormat.Ihdr:
                            throw new InvalidDataException("the PNG has more than one IHDR chunk");
                        case PngFormat.Trns:
                            colorKey = header.ReadColorKey(png, chunk);
                            break;
                        case PngFormat.Plte:
                            break; // a suggested palette for true color images: not needed
                        default:
                            if (IsCritical(chunk.Type))
                            {
                                throw new NotSupportedException($"unknown critical PNG chunk '{chunk.Type}'");
                            }
                            break;
                    }
                }
            }
        }

        private static void CheckSignature(byte[] png)
        {
            if (png.Length < PngFormat.Signature.Length)
            {
                throw new InvalidDataException("not a PNG file: too short for the signature");
            }
            for (int i = 0; i < PngFormat.Signature.Length; i++)
            {
                if (png[i] != PngFormat.Signature[i])
                {
                    throw new InvalidDataException("not a PNG file: bad signature");
                }
            }
        }

        // Chunks whose type starts with an uppercase letter must be understood to read the image.
        private static bool IsCritical(string type) => type[0] >= 'A' && type[0] <= 'Z';

        private static Chunk ReadChunk(byte[] png, int offset)
        {
            if (png.Length - offset < ChunkOverhead)
            {
                throw new InvalidDataException($"the PNG is truncated at byte {offset} (no IEND chunk)");
            }
            uint length = PngFormat.ReadUInt32(png, offset);
            for (int i = offset + 4; i < offset + 8; i++)
            {
                byte c = png[i];
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z'))
                {
                    throw new InvalidDataException($"bad chunk type at byte {offset}");
                }
            }
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (length > png.Length - offset - ChunkOverhead)
            {
                throw new InvalidDataException($"chunk '{type}' at byte {offset} is truncated");
            }
            var chunk = new Chunk(type, offset + 8, (int)length);
            uint stored = PngFormat.ReadUInt32(png, chunk.DataOffset + chunk.Length);
            uint actual = Checksums.Crc32(png, offset + 4, 4 + chunk.Length);
            if (stored != actual)
            {
                throw new InvalidDataException(
                    $"CRC mismatch in chunk '{type}' at byte {offset}: the file is corrupt");
            }
            return chunk;
        }

        private static Image8 DecodePixels(Header header, byte[] compressed, byte[] colorKey)
        {
            int channels = header.Channels;
            int stride = header.Width * channels;
            int expected = (stride + 1) * header.Height;
            byte[] scanlines = Zlib.Decompress(compressed, expected);
            if (scanlines.Length != expected)
            {
                throw new InvalidDataException(
                    $"the image data is truncated: {scanlines.Length} of {expected} bytes");
            }
            var image = new Image8(header.Width, header.Height);
            var prior = new byte[stride];
            var row = new byte[stride];
            for (int y = 0; y < header.Height; y++)
            {
                int o = y * (stride + 1);
                int filter = scanlines[o];
                if (filter >= PngFilters.Count)
                {
                    throw new InvalidDataException($"unknown filter type {filter} in row {y}");
                }
                Buffer.BlockCopy(scanlines, o + 1, row, 0, stride);
                PngFilters.Undo(filter, row, prior, channels);
                ToRgba(header.ColorType, row, colorKey, image.Pixels, y * header.Width * 4);
                (prior, row) = (row, prior);
            }
            return image;
        }

        private static void ToRgba(int colorType, byte[] row, byte[] colorKey, byte[] pixels, int offset)
        {
            int o = offset;
            switch (colorType)
            {
                case PngFormat.ColorRgba:
                    Buffer.BlockCopy(row, 0, pixels, offset, row.Length);
                    break;
                case PngFormat.ColorRgb:
                    for (int i = 0; i < row.Length; i += 3, o += 4)
                    {
                        pixels[o] = row[i];
                        pixels[o + 1] = row[i + 1];
                        pixels[o + 2] = row[i + 2];
                        bool keyed = colorKey != null && row[i] == colorKey[0] && row[i + 1] == colorKey[1]
                            && row[i + 2] == colorKey[2];
                        pixels[o + 3] = keyed ? (byte)0 : (byte)255;
                    }
                    break;
                case PngFormat.ColorGrayAlpha:
                    for (int i = 0; i < row.Length; i += 2, o += 4)
                    {
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = row[i];
                        pixels[o + 3] = row[i + 1];
                    }
                    break;
                default: // gray
                    for (int i = 0; i < row.Length; i++, o += 4)
                    {
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = row[i];
                        pixels[o + 3] = colorKey != null && row[i] == colorKey[0] ? (byte)0 : (byte)255;
                    }
                    break;
            }
        }

        private readonly struct Chunk
        {
            public Chunk(string type, int dataOffset, int length)
            {
                Type = type;
                DataOffset = dataOffset;
                Length = length;
            }

            public string Type { get; }
            public int DataOffset { get; }
            public int Length { get; }

            /// <summary>Offset of the next chunk.</summary>
            public int End => DataOffset + Length + 4;
        }

        private sealed class Header
        {
            private Header(int width, int height, int colorType, int channels)
            {
                Width = width;
                Height = height;
                ColorType = colorType;
                Channels = channels;
            }

            public int Width { get; }
            public int Height { get; }
            public int ColorType { get; }

            /// <summary>Samples (= bytes, at 8 bits) per pixel.</summary>
            public int Channels { get; }

            public static Header Parse(byte[] png, Chunk chunk)
            {
                if (chunk.Length != PngFormat.IhdrLength)
                {
                    throw new InvalidDataException($"IHDR is {chunk.Length} bytes long instead of 13");
                }
                int d = chunk.DataOffset;
                uint width = PngFormat.ReadUInt32(png, d);
                uint height = PngFormat.ReadUInt32(png, d + 4);
                int bitDepth = png[d + 8];
                int colorType = png[d + 9];
                if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
                {
                    throw new InvalidDataException($"invalid image size {width}x{height}");
                }
                if (png[d + 10] != 0 || png[d + 11] != 0)
                {
                    throw new InvalidDataException("unknown PNG compression or filter method");
                }
                int interlace = png[d + 12];
                if (interlace > 1)
                {
                    throw new InvalidDataException($"unknown PNG interlace method {interlace}");
                }
                int channels = ChannelCount(colorType);
                if (bitDepth != 8)
                {
                    throw new NotSupportedException(
                        $"PNGs with {bitDepth} bits per sample are not supported, only 8");
                }
                if (interlace == 1)
                {
                    throw new NotSupportedException("interlaced (Adam7) PNGs are not supported");
                }
                // the pixels as RGBA and the scanlines (with a filter byte each) must fit in arrays
                if ((long)width * height * 4 > int.MaxValue || ((long)width * channels + 1) * height > int.MaxValue)
                {
                    throw new NotSupportedException($"a {width}x{height} image is too large");
                }
                return new Header((int)width, (int)height, colorType, channels);
            }

            private static int ChannelCount(int colorType)
            {
                switch (colorType)
                {
                    case PngFormat.ColorGray:
                        return 1;
                    case PngFormat.ColorRgb:
                        return 3;
                    case PngFormat.ColorGrayAlpha:
                        return 2;
                    case PngFormat.ColorRgba:
                        return 4;
                    case PngFormat.ColorPalette:
                        throw new NotSupportedException(
                            "palette PNGs (color type 3) are not supported; save the image as RGBA");
                    default:
                        throw new InvalidDataException($"invalid PNG color type {colorType}");
                }
            }

            /// <summary>
            /// The tRNS color key as 8-bit samples (one gray or r, g, b), or null when no pixel can match
            /// it (a 16-bit key above 255) or the image has an alpha channel (tRNS is not allowed there and
            /// is ignored, like libpng does).
            /// </summary>
            public byte[] ReadColorKey(byte[] png, Chunk chunk)
            {
                int samples = ColorType == PngFormat.ColorGray ? 1 : ColorType == PngFormat.ColorRgb ? 3 : 0;
                if (samples == 0)
                {
                    return null;
                }
                if (chunk.Length != samples * 2)
                {
                    throw new InvalidDataException($"tRNS is {chunk.Length} bytes long instead of {samples * 2}");
                }
                var key = new byte[samples];
                for (int i = 0; i < samples; i++)
                {
                    int value = png[chunk.DataOffset + 2 * i] << 8 | png[chunk.DataOffset + 2 * i + 1];
                    if (value > 255)
                    {
                        return null;
                    }
                    key[i] = (byte)value;
                }
                return key;
            }
        }
    }
}
