namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Body proportions of a <see cref="SyntheticRigs"/> humanoid, as factors on an average adult's (all 1).
    /// The finished rig is scaled to the requested height, so these change proportions, not size.
    /// </summary>
    public sealed class SyntheticRigProportions
    {
        /// <summary>Thigh, shin, ankle height and foot length.</summary>
        public double Legs { get; set; } = 1.0;

        /// <summary>Upper arm and forearm.</summary>
        public double Arms { get; set; } = 1.0;

        /// <summary>Hips to neck.</summary>
        public double Torso { get; set; } = 1.0;

        /// <summary>Neck to the top of the head.</summary>
        public double Head { get; set; } = 1.0;

        /// <summary>Distance between the shoulder joints.</summary>
        public double ShoulderWidth { get; set; } = 1.0;

        /// <summary>A chibi figure: a big head on a small body with short limbs.</summary>
        public static SyntheticRigProportions Chibi() => new SyntheticRigProportions
        {
            Legs = 0.5,
            Arms = 0.55,
            Torso = 0.7,
            Head = 2.4,
            ShoulderWidth = 0.8,
        };
    }
}
