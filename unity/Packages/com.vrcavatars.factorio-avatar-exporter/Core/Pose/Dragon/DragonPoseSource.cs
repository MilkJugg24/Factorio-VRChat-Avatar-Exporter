using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The dragon as an <see cref="IPoseSource"/>, for previews and tests of the export pipeline: poses come
    /// from <see cref="DragonPoses"/> through <see cref="DragonPose.ToPoseResult"/> (joints and prop only,
    /// no bone rotations). Its timing is the dragon's, which are <see cref="PackTiming"/>'s defaults.
    /// The corpse is laid down as generate.py renders it (<see cref="LieDown"/>).
    /// </summary>
    public sealed class DragonPoseSource : IPoseSource
    {
        /// <summary>The orientation generate.py renders the corpse at (render_corpse).</summary>
        public const double CorpseOrientation = 0.30;

        public DragonPoseSource(BodyShape body)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public BodyShape Body { get; }

        public PackTiming Timing { get; } = new PackTiming { MineImpactFrame = DragonAnimations.MineImpactFrame };

        public PoseResult Pose(string animation, double t, double move)
        {
            PoseResult pose = DragonPoses.Pose(animation, Body, t, move).ToPoseResult();
            return animation == AnimationSpec.Corpse ? LieDown(pose) : pose;
        }

        /// <summary>
        /// generate.py's corpse_transform (a quarter turn about model y, so up becomes left, then
        /// (0.75, 0, 0.2) tiles) followed by its render orientation <see cref="CorpseOrientation"/>, folded
        /// into the pose because the exporter draws the one corpse direction facing north.
        /// </summary>
        public static PoseResult LieDown(PoseResult standing)
        {
            Quat turn = Conventions.ModelToWorld(CorpseOrientation);
            Quat rotation = turn * Quat.FromAxisAngle(Vec3.UnitY, -Math.PI / 2);
            return PoseTransforms.Rigid(standing, rotation, Vec3.Zero, turn.Rotate(new Vec3(0.75, 0, 0.2)));
        }
    }
}
