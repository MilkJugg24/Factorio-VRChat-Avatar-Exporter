using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The structure of Unity's humanoid skeleton: each bone's parent, left/right mirroring and the bones
    /// of each side. Sides are as in <see cref="JointNames"/>: +1 = right (positive x), -1 = left.
    /// </summary>
    public static class HumanBoneHierarchy
    {
        /// <summary>
        /// The torso chain: the bones whose posed positions depend only on the pelvis and the rotations of the
        /// hips, torso and neck, not on the limbs (see <see cref="HumanoidPoseBuilder.TorsoPoint"/>).
        /// </summary>
        public static readonly IReadOnlyList<HumanBone> TorsoChain = new[]
        {
            HumanBone.Hips, HumanBone.Spine, HumanBone.Chest, HumanBone.UpperChest, HumanBone.Neck, HumanBone.Head,
            HumanBone.LeftShoulder, HumanBone.RightShoulder, HumanBone.LeftUpperArm, HumanBone.RightUpperArm,
            HumanBone.LeftUpperLeg, HumanBone.RightUpperLeg,
        };

        /// <summary>
        /// The bone's parent in Unity's complete humanoid hierarchy (every optional bone present), or null
        /// for <see cref="HumanBone.Hips"/>. <see cref="HumanoidRig.Parent"/> skips the bones an avatar lacks.
        /// </summary>
        public static HumanBone? Parent(HumanBone bone)
        {
            switch (bone)
            {
                case HumanBone.Hips:
                    return null;
                case HumanBone.Spine:
                case HumanBone.LeftUpperLeg:
                case HumanBone.RightUpperLeg:
                    return HumanBone.Hips;
                case HumanBone.Chest:
                    return HumanBone.Spine;
                case HumanBone.UpperChest:
                    return HumanBone.Chest;
                case HumanBone.Neck:
                case HumanBone.LeftShoulder:
                case HumanBone.RightShoulder:
                    return HumanBone.UpperChest;
                case HumanBone.Head:
                    return HumanBone.Neck;
                case HumanBone.LeftUpperArm:
                    return HumanBone.LeftShoulder;
                case HumanBone.RightUpperArm:
                    return HumanBone.RightShoulder;
                case HumanBone.LeftLowerArm:
                    return HumanBone.LeftUpperArm;
                case HumanBone.RightLowerArm:
                    return HumanBone.RightUpperArm;
                case HumanBone.LeftHand:
                    return HumanBone.LeftLowerArm;
                case HumanBone.RightHand:
                    return HumanBone.RightLowerArm;
                case HumanBone.LeftLowerLeg:
                    return HumanBone.LeftUpperLeg;
                case HumanBone.RightLowerLeg:
                    return HumanBone.RightUpperLeg;
                case HumanBone.LeftFoot:
                    return HumanBone.LeftLowerLeg;
                case HumanBone.RightFoot:
                    return HumanBone.RightLowerLeg;
                case HumanBone.LeftToes:
                    return HumanBone.LeftFoot;
                case HumanBone.RightToes:
                    return HumanBone.RightFoot;
                default:
                    throw new ArgumentOutOfRangeException(nameof(bone), bone, "not a humanoid bone Core poses");
            }
        }

        /// <summary>The same bone on the other side (left and right swapped); centre bones map to themselves.</summary>
        public static HumanBone Mirror(HumanBone bone)
        {
            switch (bone)
            {
                case HumanBone.LeftUpperLeg: return HumanBone.RightUpperLeg;
                case HumanBone.RightUpperLeg: return HumanBone.LeftUpperLeg;
                case HumanBone.LeftLowerLeg: return HumanBone.RightLowerLeg;
                case HumanBone.RightLowerLeg: return HumanBone.LeftLowerLeg;
                case HumanBone.LeftFoot: return HumanBone.RightFoot;
                case HumanBone.RightFoot: return HumanBone.LeftFoot;
                case HumanBone.LeftToes: return HumanBone.RightToes;
                case HumanBone.RightToes: return HumanBone.LeftToes;
                case HumanBone.LeftShoulder: return HumanBone.RightShoulder;
                case HumanBone.RightShoulder: return HumanBone.LeftShoulder;
                case HumanBone.LeftUpperArm: return HumanBone.RightUpperArm;
                case HumanBone.RightUpperArm: return HumanBone.LeftUpperArm;
                case HumanBone.LeftLowerArm: return HumanBone.RightLowerArm;
                case HumanBone.RightLowerArm: return HumanBone.LeftLowerArm;
                case HumanBone.LeftHand: return HumanBone.RightHand;
                case HumanBone.RightHand: return HumanBone.LeftHand;
                case HumanBone.Hips:
                case HumanBone.Spine:
                case HumanBone.Chest:
                case HumanBone.UpperChest:
                case HumanBone.Neck:
                case HumanBone.Head:
                    return bone;
                default:
                    throw new ArgumentOutOfRangeException(nameof(bone), bone, "not a humanoid bone Core poses");
            }
        }

        public static HumanBone Shoulder(int side) => Pick(side, HumanBone.RightShoulder, HumanBone.LeftShoulder);
        public static HumanBone UpperArm(int side) => Pick(side, HumanBone.RightUpperArm, HumanBone.LeftUpperArm);
        public static HumanBone LowerArm(int side) => Pick(side, HumanBone.RightLowerArm, HumanBone.LeftLowerArm);
        public static HumanBone Hand(int side) => Pick(side, HumanBone.RightHand, HumanBone.LeftHand);
        public static HumanBone UpperLeg(int side) => Pick(side, HumanBone.RightUpperLeg, HumanBone.LeftUpperLeg);
        public static HumanBone LowerLeg(int side) => Pick(side, HumanBone.RightLowerLeg, HumanBone.LeftLowerLeg);
        public static HumanBone Foot(int side) => Pick(side, HumanBone.RightFoot, HumanBone.LeftFoot);
        public static HumanBone Toes(int side) => Pick(side, HumanBone.RightToes, HumanBone.LeftToes);

        private static HumanBone Pick(int side, HumanBone right, HumanBone left)
        {
            HumanoidArgs.CheckSide(side);
            return side > 0 ? right : left;
        }
    }
}
