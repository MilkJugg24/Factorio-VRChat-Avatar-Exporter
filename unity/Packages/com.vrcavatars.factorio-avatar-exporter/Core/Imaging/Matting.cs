using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Difference matting: recovers the color and coverage of a frame from two renders of it, one over
    /// opaque black and one over opaque white (<see cref="RenderResult.OnBlack"/> and
    /// <see cref="RenderResult.OnWhite"/>).
    ///
    /// Whatever the avatar's shaders compute, alpha blending puts
    /// <c>rendered = color * alpha + background * (1 - alpha)</c> on screen. Over black that is
    /// <c>B = color * alpha</c>, the premultiplied color; over white it is <c>W = B + (1 - alpha)</c>. So
    /// <c>alpha = 1 - (W - B)</c>, and no shader has to write a meaningful alpha channel (many avatar
    /// shaders don't).
    ///
    /// Light a shader only adds over the background (alpha 0 but some color, like additive glow or
    /// particles) cannot be shown with straight alpha. It is dropped only when both renders keep values
    /// above 1 (a half or float render target, HDR, no tonemapping or other post-processing): then
    /// <c>W - B = 1</c> and alpha is 0. A clamped (8-bit) target saturates white plus glow at 1, so the
    /// glow becomes color with alpha about the mean of the glow, its hue shifted where a channel is clamped
    /// to alpha: a colored, half transparent fog. Partly transparent pixels whose render on white would go
    /// above 1 get too much alpha the same way. <see cref="RenderResult"/> says what renderers should do.
    /// </summary>
    public static class Matting
    {
        /// <summary>
        /// Returns the premultiplied LINEAR image of the frame: alpha = clamp(1 - mean over r, g, b of
        /// (white - black), 0, 1), color = clamp(black, 0, alpha). Averaging the three channels reduces
        /// noise; clamping keeps the result a valid premultiplied image when noise or HDR values break the
        /// blend equation slightly. The input images' alpha channels are ignored and the inputs are not
        /// modified.
        /// </summary>
        /// <param name="onBlack">The frame rendered over opaque black.</param>
        /// <param name="onWhite">The same frame rendered over opaque white, same size.</param>
        /// <param name="linear">
        /// True when the renders hold linear values. False when they are sRGB encoded: they are decoded
        /// first, because the GPU blended in linear space (Unity's linear color space) and the equation
        /// above only holds there.
        /// </param>
        public static ImageF FromBlackAndWhite(ImageF onBlack, ImageF onWhite, bool linear)
        {
            if (onBlack == null)
            {
                throw new ArgumentNullException(nameof(onBlack));
            }
            if (onWhite == null)
            {
                throw new ArgumentNullException(nameof(onWhite));
            }
            if (onBlack.Width != onWhite.Width || onBlack.Height != onWhite.Height)
            {
                throw new ArgumentException(
                    $"the renders differ in size: {onBlack.Width}x{onBlack.Height} on black, " +
                    $"{onWhite.Width}x{onWhite.Height} on white", nameof(onWhite));
            }
            var result = new ImageF(onBlack.Width, onBlack.Height);
            float[] black = onBlack.Pixels;
            float[] white = onWhite.Pixels;
            float[] output = result.Pixels;
            for (int i = 0; i < output.Length; i += 4)
            {
                double r = Decode(black[i], linear);
                double g = Decode(black[i + 1], linear);
                double b = Decode(black[i + 2], linear);
                double difference = (Decode(white[i], linear) - r) + (Decode(white[i + 1], linear) - g)
                    + (Decode(white[i + 2], linear) - b);
                double alpha = ColorSpace.Clamp01(1.0 - difference / 3.0);
                output[i] = (float)ClampTo(r, alpha);
                output[i + 1] = (float)ClampTo(g, alpha);
                output[i + 2] = (float)ClampTo(b, alpha);
                output[i + 3] = (float)alpha;
            }
            return result;
        }

        private static double Decode(float value, bool linear) => linear ? value : ColorSpace.SrgbToLinear(value);

        // clamp(v, 0, max) with NaN as 0
        private static double ClampTo(double v, double max) => v > 0 ? (v < max ? v : max) : 0;
    }
}
