using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Turns float renders into the 8-bit straight-alpha sRGB images packs store.</summary>
    public static class ImageConvert
    {
        /// <summary>
        /// Quantizes a channel value in [0, 1] to 8 bits the way tools/dragon_render/sheets.py's
        /// <c>to_u8</c> does: <c>floor(clamp(v, 0, 1) * 255 + 0.5)</c>, computed in double precision
        /// (NaN gives 0).
        /// </summary>
        public static byte Quantize(double value) => (byte)(ColorSpace.Clamp01(value) * 255.0 + 0.5);

        /// <summary>
        /// Converts a premultiplied linear image (as <see cref="Matting.FromBlackAndWhite"/> returns, after
        /// <see cref="Downsample.Box"/>) to straight-alpha sRGB: alpha is clamped to [0, 1] and quantized;
        /// a pixel whose alpha quantizes to 0 becomes (0, 0, 0, 0); otherwise its color is divided by alpha
        /// (which is then at least 0.5 / 255, well above 1e-6), clamped, sRGB encoded and quantized.
        /// </summary>
        public static Image8 ToImage8(ImageF premultipliedLinear)
        {
            if (premultipliedLinear == null)
            {
                throw new ArgumentNullException(nameof(premultipliedLinear));
            }
            var result = new Image8(premultipliedLinear.Width, premultipliedLinear.Height);
            float[] source = premultipliedLinear.Pixels;
            byte[] output = result.Pixels;
            for (int i = 0; i < source.Length; i += 4)
            {
                double alpha = ColorSpace.Clamp01(source[i + 3]);
                byte alpha8 = Quantize(alpha);
                if (alpha8 == 0)
                {
                    continue;
                }
                for (int c = 0; c < 3; c++)
                {
                    output[i + c] = Quantize(ColorSpace.LinearToSrgb(source[i + c] / alpha));
                }
                output[i + 3] = alpha8;
            }
            return result;
        }

        /// <summary>
        /// Converts a shadow pass (<see cref="RenderPass.Shadow"/>: a black silhouette on white, rendered at
        /// <paramref name="supersampling"/> times the final size) to a shadow layer: black, with alpha =
        /// the coverage of each final pixel. The coverage of a rendered pixel is clamp(1 - mean(r, g, b), 0,
        /// 1) with the channels in linear light (the input alpha is ignored), so an antialiased edge pixel
        /// half covered by the silhouette counts as half; the final alpha is the average over each block,
        /// quantized like <see cref="Quantize"/>.
        /// </summary>
        /// <param name="linear">True when the values are linear, false when they are sRGB encoded
        /// (<see cref="RenderResult.Linear"/>).</param>
        /// <exception cref="ArgumentException">The image size is not a multiple of the supersampling.</exception>
        public static Image8 ShadowFromSilhouette(ImageF silhouette, int supersampling, bool linear)
        {
            Downsample.CheckFactor(silhouette, supersampling);
            int width = silhouette.Width / supersampling;
            int height = silhouette.Height / supersampling;
            var result = new Image8(width, height);
            float[] source = silhouette.Pixels;
            double weight = 1.0 / ((double)supersampling * supersampling);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double coverage = 0;
                    for (int sy = y * supersampling; sy < (y + 1) * supersampling; sy++)
                    {
                        int i = silhouette.Index(x * supersampling, sy);
                        int end = i + supersampling * 4;
                        for (; i < end; i += 4)
                        {
                            double sum = linear
                                ? (double)source[i] + source[i + 1] + source[i + 2]
                                : ColorSpace.SrgbToLinear(source[i]) + ColorSpace.SrgbToLinear(source[i + 1])
                                    + ColorSpace.SrgbToLinear(source[i + 2]);
                            coverage += ColorSpace.Clamp01(1.0 - sum / 3.0);
                        }
                    }
                    result[x, y, 3] = Quantize(coverage * weight);
                }
            }
            return result;
        }
    }
}
