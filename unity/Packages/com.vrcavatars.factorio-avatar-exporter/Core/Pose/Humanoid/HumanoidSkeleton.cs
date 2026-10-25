using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Forward kinematics of a <see cref="PoseResult"/> on a <see cref="HumanoidRig"/>, exactly as the Unity
    /// layer applies it.
    /// </summary>
    public static class HumanoidSkeleton
    {
        /// <summary>
        /// Where every bone of <paramref name="rig"/> goes in <paramref name="pose"/> (model space, tiles). In
        /// <see cref="HumanBones.ParentFirst"/> order, a bone's world delta is its entry in
        /// <see cref="PoseResult.WorldDeltas"/> (normalized) if there is one, else its parent's (it keeps its
        /// rotation relative to the parent); the Hips, without an entry, keep their rest rotation. The Hips go to
        /// <see cref="PoseResult.HipsPosition"/> and every other bone to
        /// <c>posed(parent) + delta(parent).Rotate(rest(bone) - rest(parent))</c>, where the parent is the
        /// nearest bone the rig has (<see cref="HumanoidRig.Parent"/>). Deltas of bones the rig lacks are
        /// ignored.
        /// </summary>
        public static PosedSkeleton Pose(HumanoidRig rig, PoseResult pose)
        {
            if (rig == null)
            {
                throw new ArgumentNullException(nameof(rig));
            }
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }
            var positions = new Dictionary<HumanBone, Vec3>();
            var deltas = new Dictionary<HumanBone, Quat>();
            foreach (HumanBone bone in rig.Bones)
            {
                HumanBone? parent = rig.Parent(bone);
                Quat inherited = Quat.Identity;
                if (parent.HasValue)
                {
                    inherited = deltas[parent.Value];
                    positions.Add(bone, positions[parent.Value] + inherited.Rotate(rig.Rest(bone) - rig.Rest(parent.Value)));
                }
                else
                {
                    positions.Add(bone, pose.HipsPosition);
                }
                deltas.Add(bone, pose.WorldDeltas.TryGetValue(bone, out Quat own) ? own.Normalized : inherited);
            }
            return new PosedSkeleton(rig, positions, deltas);
        }
    }
}
