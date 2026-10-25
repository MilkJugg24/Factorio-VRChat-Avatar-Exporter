using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Colors and lighting of the stick figure renderer: grey torso, skin colored head, blue left limbs,
    /// red right limbs (arms lighter than legs) and a dark prop, lit by Lambert shading with
    /// <see cref="Conventions.ShadeLight"/> plus ambient light. Shading happens in linear light; inputs and
    /// results are sRGB encoded, as <see cref="RenderResult"/> with <c>Linear = false</c> holds them.
    /// </summary>
    public static class StickPalette
    {
        /// <summary>Light reaching every surface, as a fraction of full brightness.</summary>
        public const double Ambient = 0.3;

        /// <summary>Light added to a surface facing <see cref="Conventions.ShadeLight"/> head on.</summary>
        public const double Diffuse = 0.7;

        /// <summary>The surface color of <paramref name="part"/>, sRGB encoded, each channel in [0, 1].</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="part"/> is not a <see cref="StickPart"/>.</exception>
        public static (double r, double g, double b) Albedo(StickPart part)
        {
            switch (part)
            {
                case StickPart.Torso:
                    return (0.62, 0.62, 0.62);
                case StickPart.Head:
                    return (0.93, 0.76, 0.62);
                case StickPart.LeftArm:
                    return (0.35, 0.55, 0.95);
                case StickPart.LeftLeg:
                    return (0.20, 0.33, 0.78);
                case StickPart.RightArm:
                    return (0.95, 0.42, 0.35);
                case StickPart.RightLeg:
                    return (0.78, 0.22, 0.20);
                case StickPart.Prop:
                    return (0.22, 0.22, 0.25);
                default:
                    throw new ArgumentOutOfRangeException(nameof(part), part, "not a stick figure part");
            }
        }

        /// <summary>
        /// The sRGB color of <paramref name="part"/> where the surface has the unit outward normal
        /// <paramref name="normal"/>: the linear albedo times
        /// <c><see cref="Ambient"/> + <see cref="Diffuse"/> * max(0, dot(normal, ShadeLight))</c>, encoded
        /// again. A surface facing the light head on shows <see cref="Albedo"/> itself.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="part"/> is not a <see cref="StickPart"/>.</exception>
        public static (double r, double g, double b) Shade(StickPart part, Vec3 normal)
        {
            (double r, double g, double b) = Albedo(part);
            double light = Ambient + Diffuse * Math.Max(0.0, Vec3.Dot(normal, Conventions.ShadeLight));
            return (Lit(r, light), Lit(g, light), Lit(b, light));
        }

        private static double Lit(double srgb, double light) =>
            ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(srgb) * light);
    }
}
