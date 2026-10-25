using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Proportions of one of the dragon's body shapes: a port of <c>BodyShape</c> in
    /// tools/dragon_render/model.py, with its <see cref="Male"/> and <see cref="Female"/> constants.
    /// Lengths are in tiles. Posing only uses <see cref="ShoulderWidth"/> and <see cref="HipWidth"/>;
    /// the other fields shape the Python renderer's geometry and are kept so both stay comparable.
    /// Radii triples are (x = right, y = forward, z = up) semi-axes of the ellipsoid drawn along the
    /// torso (or hip) frame.
    /// </summary>
    public sealed class BodyShape
    {
        public BodyShape(string name, double shoulderWidth, Vec3 chestRadii, Vec3 waistRadii, Vec3 hipRadii,
            double hipWidth, double armRadius, double forearmRadius, double thighRadius, double neckRadius,
            bool bust, string hornStyle, double eyeRadius)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("a body shape needs a name", nameof(name));
            }
            if (string.IsNullOrEmpty(hornStyle))
            {
                throw new ArgumentException("a body shape needs a horn style", nameof(hornStyle));
            }
            RequirePositive(shoulderWidth, nameof(shoulderWidth));
            RequirePositive(chestRadii, nameof(chestRadii));
            RequirePositive(waistRadii, nameof(waistRadii));
            RequirePositive(hipRadii, nameof(hipRadii));
            RequirePositive(hipWidth, nameof(hipWidth));
            RequirePositive(armRadius, nameof(armRadius));
            RequirePositive(forearmRadius, nameof(forearmRadius));
            RequirePositive(thighRadius, nameof(thighRadius));
            RequirePositive(neckRadius, nameof(neckRadius));
            RequirePositive(eyeRadius, nameof(eyeRadius));
            Name = name;
            ShoulderWidth = shoulderWidth;
            ChestRadii = chestRadii;
            WaistRadii = waistRadii;
            HipRadii = hipRadii;
            HipWidth = hipWidth;
            ArmRadius = armRadius;
            ForearmRadius = forearmRadius;
            ThighRadius = thighRadius;
            NeckRadius = neckRadius;
            Bust = bust;
            HornStyle = hornStyle;
            EyeRadius = eyeRadius;
        }

        /// <summary>model.py <c>MALE</c>.</summary>
        public static readonly BodyShape Male = new BodyShape(
            "male", 0.212, new Vec3(0.198, 0.148, 0.160), new Vec3(0.148, 0.128, 0.11), new Vec3(0.165, 0.130, 0.12),
            0.100, 0.064, 0.056, 0.100, 0.078, false, "male", 0.024);

        /// <summary>model.py <c>FEMALE</c>.</summary>
        public static readonly BodyShape Female = new BodyShape(
            "female", 0.165, new Vec3(0.152, 0.128, 0.148), new Vec3(0.100, 0.096, 0.105), new Vec3(0.198, 0.148, 0.128),
            0.112, 0.052, 0.046, 0.102, 0.064, true, "female", 0.027);

        /// <summary>Both body shapes, in the order of model.py's <c>BODIES</c>.</summary>
        public static readonly IReadOnlyList<BodyShape> All = new[] { Male, Female };

        /// <summary>The body shape called <paramref name="name"/> ("male" or "female"), like model.py's <c>BODIES[name]</c>.</summary>
        public static BodyShape ByName(string name)
        {
            foreach (BodyShape body in All)
            {
                if (string.Equals(body.Name, name, StringComparison.Ordinal))
                {
                    return body;
                }
            }
            throw new ArgumentException("unknown body shape \"" + name + "\" (expected \"male\" or \"female\")", nameof(name));
        }

        /// <summary><c>name</c>.</summary>
        public string Name { get; }

        /// <summary><c>shoulder_w</c>: sideways distance of each shoulder joint from the chest centre.</summary>
        public double ShoulderWidth { get; }

        /// <summary><c>chest_r</c>: semi-axes of the chest ellipsoid.</summary>
        public Vec3 ChestRadii { get; }

        /// <summary><c>waist_r</c>: semi-axes of the waist ellipsoid.</summary>
        public Vec3 WaistRadii { get; }

        /// <summary><c>hip_r</c>: semi-axes of the pelvis ellipsoid (not the hip frame's right vector).</summary>
        public Vec3 HipRadii { get; }

        /// <summary><c>hip_w</c>: sideways distance of each hip joint from the pelvis.</summary>
        public double HipWidth { get; }

        /// <summary><c>arm_r</c>: radius of the upper arm.</summary>
        public double ArmRadius { get; }

        /// <summary><c>forearm_r</c>: radius of the forearm.</summary>
        public double ForearmRadius { get; }

        /// <summary><c>thigh_r</c>: radius of the thigh.</summary>
        public double ThighRadius { get; }

        /// <summary><c>neck_r</c>: radius of the neck.</summary>
        public double NeckRadius { get; }

        /// <summary><c>bust</c>: the torso gets a bust.</summary>
        public bool Bust { get; }

        /// <summary><c>horn_style</c>: "male" or "female" horns.</summary>
        public string HornStyle { get; }

        /// <summary><c>eye_r</c>: radius of the eyes before <see cref="DragonModel.HeadScale"/>.</summary>
        public double EyeRadius { get; }

        public override string ToString() => Name;

        private static void RequirePositive(double value, string name)
        {
            if (!(value > 0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "must be a positive finite length");
            }
        }

        private static void RequirePositive(Vec3 radii, string name)
        {
            RequirePositive(radii.X, name);
            RequirePositive(radii.Y, name);
            RequirePositive(radii.Z, name);
        }
    }
}
