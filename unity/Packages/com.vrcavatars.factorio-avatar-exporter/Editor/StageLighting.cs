using System;
using System.Globalization;
using FactorioAvatarExporter.Core;
using UnityEngine;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>
    /// How the avatar is lit for the sprites. "Factorio style" is the game's own: a key light from the left and a
    /// little in front, above (Core's <see cref="Conventions.ShadeLight"/>), soft flat ambient light and a neutral
    /// studio for shiny materials to reflect. "Custom" lets the user set every part. The lights stay fixed
    /// relative to the camera, so every direction of every animation is lit alike. The ground shadow is not part
    /// of this: it always falls the game's way (<see cref="Conventions.ShadowLight"/>).
    ///
    /// Directions are compass degrees as seen in the picture: 0 from the far side (the top of the picture),
    /// 90 from the right, 180 from the front (where the camera is), 270 from the left.
    /// </summary>
    [Serializable]
    internal sealed class StageLighting
    {
        /// <summary>Height of the fill light, degrees; it comes from opposite the key light.</summary>
        public const float FillHeight = 20f;

        /// <summary>Where the rim light comes from: the far side, this many degrees up.</summary>
        public const float RimDirection = 0f, RimHeight = 30f;

        public bool Custom;

        /// <summary>Where the key light comes from, compass degrees as seen in the picture.</summary>
        public float KeyDirection = FactorioKeyDirection;

        /// <summary>The key light's height above the horizon, degrees.</summary>
        public float KeyHeight = FactorioKeyHeight;

        public float KeyStrength = 1f;
        public Color KeyColor = Color.white;

        /// <summary>A softer light from opposite the key light, lifting the shadowed side (0 = none).</summary>
        public float FillStrength;

        /// <summary>A light from the far side, drawing bright edges (0 = none).</summary>
        public float RimStrength;

        public float AmbientStrength = 0.45f;
        public Color AmbientColor = Color.white;

        /// <summary>How strongly shiny and metallic materials reflect their surroundings.</summary>
        public float ReflectionStrength = 1f;

        /// <summary>Surroundings for reflections (a skybox or HDRI imported as a cubemap); null for the neutral studio.</summary>
        public Cubemap Environment;

        /// <summary>The key light's compass direction in Factorio style (about 243 degrees: left, a little in front).</summary>
        public static float FactorioKeyDirection
        {
            get
            {
                // Core's world: x east, y north; the camera looks north
                Vec3 l = Conventions.ShadeLight;
                double degrees = Math.Atan2(l.X, l.Y) * 180 / Math.PI;
                return (float)(degrees < 0 ? degrees + 360 : degrees);
            }
        }

        /// <summary>The key light's height in Factorio style (about 49 degrees).</summary>
        public static float FactorioKeyHeight => (float)(Math.Asin(Conventions.ShadeLight.Normalized.Z) * 180 / Math.PI);

        /// <summary>The settings in effect: these, or Factorio style's when <see cref="Custom"/> is off.</summary>
        public StageLighting Effective => Custom ? this : new StageLighting();

        public StageLighting Copy() => (StageLighting)MemberwiseClone();

        /// <summary>
        /// The direction towards a light at compass <paramref name="direction"/> and <paramref name="height"/>
        /// degrees, in Unity's world space.
        /// </summary>
        public static Vector3 TowardsLight(float direction, float height)
        {
            double az = direction * Math.PI / 180, el = height * Math.PI / 180;
            var core = new Vec3(Math.Cos(el) * Math.Sin(az), Math.Cos(el) * Math.Cos(az), Math.Sin(el));
            (double x, double y, double z) unity = UnitySpace.ToUnity(core);
            return new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
        }

        /// <summary>One line for report.txt.</summary>
        public string Describe()
        {
            if (!Custom)
            {
                return "lighting: Factorio style";
            }
            string Number(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            return $"lighting: custom, key light from {Number(KeyDirection)} degrees and {Number(KeyHeight)} up at " +
                $"{Number(KeyStrength)}, fill {Number(FillStrength)}, rim {Number(RimStrength)}, ambient {Number(AmbientStrength)}, " +
                $"reflections {Number(ReflectionStrength)} of {(Environment != null ? Environment.name : "the neutral studio")}";
        }
    }
}
