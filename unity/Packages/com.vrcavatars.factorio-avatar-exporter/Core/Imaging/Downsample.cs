using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Reduces supersampled renders to their final size.</summary>
    public static class Downsample
    {
        /// <summary>
        /// Averages every <paramref name="factor"/> x <paramref name="factor"/> block of pixels into one,
        /// all four channels alike (tools/dragon_render/render.py's <c>_downsample</c>). Use it on
        /// premultiplied data, where averaging is correct; averaging straight colors would let transparent
        /// pixels darken the edges. Returns a new image, even for a factor of 1.
        /// </summary>
        /// <exception cref="ArgumentException">The image size is not a multiple of the factor.</exception>
        public static ImageF Box(ImageF image, int factor)
        {
            CheckFactor(image, factor);
            int width = image.Width / factor;
            int height = image.Height / factor;
            var result = new ImageF(width, height);
            float[] source = image.Pixels;
            float[] output = result.Pixels;
            double weight = 1.0 / ((double)factor * factor);
            var sum = new double[4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Array.Clear(sum, 0, 4);
                    for (int sy = y * factor; sy < (y + 1) * factor; sy++)
                    {
                        int i = image.Index(x * factor, sy);
                        int end = i + factor * 4;
                        for (; i < end; i += 4)
                        {
                            sum[0] += source[i];
                            sum[1] += source[i + 1];
                            sum[2] += source[i + 2];
                            sum[3] += source[i + 3];
                        }
                    }
                    int o = result.Index(x, y);
                    for (int c = 0; c < 4; c++)
                    {
                        output[o + c] = (float)(sum[c] * weight);
                    }
                }
            }
            return result;
        }

        /// <summary>Checks that <paramref name="image"/> can be reduced by <paramref name="factor"/>.</summary>
        internal static void CheckFactor(ImageF image, int factor)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }
            if (factor <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(factor), factor, "the factor must be positive");
            }
            if (image.Width % factor != 0 || image.Height % factor != 0)
            {
                throw new ArgumentException(
                    $"a {image.Width}x{image.Height} image cannot be reduced by {factor}: " +
                    "both sides must be multiples of the factor", nameof(factor));
            }
        }
    }
}
