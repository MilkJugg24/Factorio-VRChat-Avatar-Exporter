using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The sRGB transfer functions of IEC 61966-2-1. Values are channel intensities in [0, 1]; inputs
    /// outside that range are clamped first (NaN counts as 0), so results are always in [0, 1].
    /// </summary>
    public static class ColorSpace
    {
        /// <summary>Encodes a linear intensity with the sRGB curve (the encoding PNG files and screens use).</summary>
        public static double LinearToSrgb(double linear)
        {
            double v = Clamp01(linear);
            return v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
        }

        /// <summary>Decodes an sRGB encoded value to a linear intensity.</summary>
        public static double SrgbToLinear(double srgb)
        {
            double v = Clamp01(srgb);
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        /// <summary>Clamps to [0, 1]; NaN becomes 0, so a stray NaN from a shader cannot spread.</summary>
        public static double Clamp01(double v) => v > 0 ? (v < 1 ? v : 1) : 0;
    }
}
