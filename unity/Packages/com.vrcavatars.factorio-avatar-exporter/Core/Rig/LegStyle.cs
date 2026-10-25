namespace FactorioAvatarExporter.Core
{
    /// <summary>How a rig stands: on the whole foot, or high on the toes.</summary>
    public enum LegStyle
    {
        /// <summary>Human-like: the ankle is low and the sole rests on the ground.</summary>
        Plantigrade,

        /// <summary>
        /// Animal-like: the ankle is higher than <see cref="HumanoidRig.DigitigradeAnkleRatio"/> times the leg
        /// length.
        /// </summary>
        Digitigrade,
    }
}
