using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Copying, cropping, compositing and resizing images.</summary>
    public static class ImageOps
    {
        private const int PrecisionBits = 7; // Pillow's AlphaComposite.c

        /// <summary>
        /// Returns a copy with the rows in reverse order. Unity's ReadPixels gives row 0 at the bottom;
        /// Core's images have row 0 at the top.
        /// </summary>
        public static ImageF FlipRows(ImageF image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }
            var result = new ImageF(image.Width, image.Height);
            int stride = image.Width * 4;
            for (int y = 0; y < image.Height; y++)
            {
                Array.Copy(image.Pixels, (image.Height - 1 - y) * stride, result.Pixels, y * stride, stride);
            }
            return result;
        }

        /// <summary>Returns a copy of the <paramref name="width"/> x <paramref name="height"/> rectangle at (x, y).</summary>
        /// <exception cref="ArgumentOutOfRangeException">The rectangle is empty or not inside the image.</exception>
        public static Image8 Crop(Image8 image, int x, int y, int width, int height)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }
            if (width <= 0 || height <= 0 || x < 0 || y < 0 || x > image.Width - width || y > image.Height - height)
            {
                throw new ArgumentOutOfRangeException(nameof(width),
                    $"rectangle {width}x{height} at ({x}, {y}) is not inside the {image.Width}x{image.Height} image");
            }
            var result = new Image8(width, height);
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(image.Pixels, image.Index(x, y + row), result.Pixels, row * width * 4, width * 4);
            }
            return result;
        }

        /// <summary>
        /// Copies <paramref name="src"/> into <paramref name="dst"/> with its top left corner at (x, y),
        /// replacing the pixels there. The parts falling outside <paramref name="dst"/> are skipped, so
        /// the offset may be negative or beyond the edges.
        /// </summary>
        public static void Blit(Image8 dst, Image8 src, int x, int y)
        {
            CheckPair(dst, src);
            if (!Overlap(dst.Width, src.Width, x, out int dx, out int sx, out int w)
                || !Overlap(dst.Height, src.Height, y, out int dy, out int sy, out int h))
            {
                return;
            }
            for (int row = 0; row < h; row++)
            {
                Buffer.BlockCopy(src.Pixels, src.Index(sx, sy + row), dst.Pixels, dst.Index(dx, dy + row), w * 4);
            }
        }

        /// <summary>
        /// Draws <paramref name="src"/> over <paramref name="dst"/> with its top left corner at (x, y), both
        /// straight alpha, clipped like <see cref="Blit"/>. Uses the integer arithmetic of Pillow's
        /// <c>Image.alpha_composite</c>, so the result matches it exactly: where the source is
        /// transparent the destination is unchanged, otherwise
        /// <c>outAlpha = srcAlpha + dstAlpha * (1 - srcAlpha)</c> and the color is the mix of the two
        /// weighted by <c>srcAlpha</c> and <c>dstAlpha * (1 - srcAlpha)</c>.
        /// </summary>
        public static void CompositeOver(Image8 dst, Image8 src, int x, int y)
        {
            CheckPair(dst, src);
            if (!Overlap(dst.Width, src.Width, x, out int dx, out int sx, out int w)
                || !Overlap(dst.Height, src.Height, y, out int dy, out int sy, out int h))
            {
                return;
            }
            byte[] d = dst.Pixels;
            byte[] s = src.Pixels;
            for (int row = 0; row < h; row++)
            {
                int di = dst.Index(dx, dy + row);
                int si = src.Index(sx, sy + row);
                for (int i = 0; i < w; i++, di += 4, si += 4)
                {
                    uint srcAlpha = s[si + 3];
                    if (srcAlpha == 0)
                    {
                        continue;
                    }
                    uint blend = d[di + 3] * (255 - srcAlpha);
                    uint outAlpha255 = srcAlpha * 255 + blend;
                    uint coef1 = srcAlpha * 255 * 255 * (1u << PrecisionBits) / outAlpha255;
                    uint coef2 = 255 * (1u << PrecisionBits) - coef1;
                    for (int c = 0; c < 3; c++)
                    {
                        uint mixed = s[si + c] * coef1 + d[di + c] * coef2;
                        d[di + c] = (byte)(DivideBy255(mixed + (0x80u << PrecisionBits)) >> PrecisionBits);
                    }
                    d[di + 3] = (byte)DivideBy255(outAlpha255 + 0x80);
                }
            }
        }

        /// <summary>
        /// Resizes to <paramref name="width"/> x <paramref name="height"/> by area averaging: each output
        /// pixel is the average of the source area it covers, weighted by overlap, so it works for both
        /// shrinking (icons, thumbnails) and enlarging (which keeps pixels sharp, blending only where an
        /// output pixel straddles two source pixels). Averaging happens on premultiplied colors (in sRGB
        /// values, like Pillow's resize), so transparent pixels do not bleed their color into the edges;
        /// a result whose alpha quantizes to 0 is (0, 0, 0, 0).
        /// </summary>
        public static Image8 Resize(Image8 image, int width, int height)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), $"size {width}x{height} must be positive");
            }
            double[] premultiplied = Premultiply(image);
            double[] columns = ResizeAxis(premultiplied, image.Height, image.Width, width, true);
            double[] resized = ResizeAxis(columns, width, image.Height, height, false);
            var result = new Image8(width, height);
            byte[] output = result.Pixels;
            for (int i = 0; i < output.Length; i += 4)
            {
                double alpha = resized[i + 3];
                byte alpha8 = ImageConvert.Quantize(alpha);
                if (alpha8 == 0)
                {
                    continue;
                }
                for (int c = 0; c < 3; c++)
                {
                    output[i + c] = ImageConvert.Quantize(resized[i + c] / alpha);
                }
                output[i + 3] = alpha8;
            }
            return result;
        }

        private static void CheckPair(Image8 dst, Image8 src)
        {
            if (dst == null)
            {
                throw new ArgumentNullException(nameof(dst));
            }
            if (src == null)
            {
                throw new ArgumentNullException(nameof(src));
            }
            if (ReferenceEquals(dst, src))
            {
                throw new ArgumentException("source and destination must be different images", nameof(src));
            }
        }

        /// <summary>
        /// The overlap, along one axis, of a destination of <paramref name="dstSize"/> pixels and a source
        /// of <paramref name="srcSize"/> pixels placed at <paramref name="offset"/>. False when empty.
        /// </summary>
        private static bool Overlap(int dstSize, int srcSize, int offset, out int dstStart, out int srcStart, out int length)
        {
            long start = Math.Max(0L, offset);
            long end = Math.Min(dstSize, (long)offset + srcSize);
            dstStart = (int)start;
            srcStart = (int)(start - offset);
            length = (int)Math.Max(0L, end - start);
            return length > 0;
        }

        // Pillow's SHIFTFORDIV255: a fast approximation of a / 255, kept to match Pillow bit for bit.
        private static uint DivideBy255(uint a) => ((a >> 8) + a) >> 8;

        private static double[] Premultiply(Image8 image)
        {
            byte[] pixels = image.Pixels;
            var result = new double[pixels.Length];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                double alpha = pixels[i + 3] / 255.0;
                result[i] = pixels[i] / 255.0 * alpha;
                result[i + 1] = pixels[i + 1] / 255.0 * alpha;
                result[i + 2] = pixels[i + 2] / 255.0 * alpha;
                result[i + 3] = alpha;
            }
            return result;
        }

        /// <summary>
        /// Area-resamples interleaved RGBA doubles along one axis. <paramref name="lines"/> is the count
        /// of rows (horizontal pass) or columns (vertical pass); the other axis goes from
        /// <paramref name="from"/> to <paramref name="to"/> pixels.
        /// </summary>
        private static double[] ResizeAxis(double[] source, int lines, int from, int to, bool horizontal)
        {
            var result = new double[(long)lines * to * 4];
            for (int o = 0; o < to; o++)
            {
                // output pixel o covers [o * from / to, (o + 1) * from / to) in source pixels
                double start = (double)((long)o * from) / to;
                double end = (double)((long)(o + 1) * from) / to;
                int first = (int)Math.Floor(start);
                int last = Math.Min(from - 1, (int)Math.Ceiling(end) - 1);
                double scale = 1.0 / (end - start);
                for (int i = first; i <= last; i++)
                {
                    double weight = (Math.Min(i + 1, end) - Math.Max(i, start)) * scale;
                    for (int line = 0; line < lines; line++)
                    {
                        int s = horizontal ? (line * from + i) * 4 : (i * lines + line) * 4;
                        int r = horizontal ? (line * to + o) * 4 : (o * lines + line) * 4;
                        result[r] += source[s] * weight;
                        result[r + 1] += source[s + 1] * weight;
                        result[r + 2] += source[s + 2] * weight;
                        result[r + 3] += source[s + 3] * weight;
                    }
                }
            }
            return result;
        }
    }
}
