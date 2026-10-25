namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The parts of a stick figure. Each has its own color (<see cref="StickPalette"/>), and the limbs of
    /// the two sides differ, so orientation and mirroring show in renders.
    /// </summary>
    public enum StickPart
    {
        /// <summary>The chain pelvis - waist - chest - neck - head.</summary>
        Torso,

        /// <summary>The head sphere.</summary>
        Head,

        /// <summary>Chest - left shoulder - elbow - hand (joints ending in ".L").</summary>
        LeftArm,

        /// <summary>Chest - right shoulder - elbow - hand (joints ending in ".R").</summary>
        RightArm,

        /// <summary>Pelvis - left hip - knee - ankle - ball - toe.</summary>
        LeftLeg,

        /// <summary>Pelvis - right hip - knee - ankle - ball - toe.</summary>
        RightLeg,

        /// <summary>The held gun or pickaxe.</summary>
        Prop,
    }
}
