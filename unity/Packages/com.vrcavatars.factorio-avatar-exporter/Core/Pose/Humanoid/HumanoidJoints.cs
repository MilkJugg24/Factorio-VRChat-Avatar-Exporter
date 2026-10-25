using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The named points of a pose (<see cref="JointNames"/>, <see cref="PoseResult.Joints"/>) on a humanoid:
    /// bone positions, plus a few rest points that move rigidly with a bone.
    /// </summary>
    public static class HumanoidJoints
    {
        /// <summary>
        /// Every joint of <see cref="JointNames"/> for a posed skeleton, model space, tiles: pelvis = Hips,
        /// waist = Spine, chest and neck_base = <see cref="PosedSkeleton.TorsoPoint"/> of the Chest and the Neck
        /// (estimated points when the rig lacks them), neck_top = Head, head_top = the point at
        /// <see cref="HumanoidRig.HeadTopHeight"/> straight above the rest Head bone and head = half way between
        /// the two (both moving with the Head), shoulder / elbow / hand = UpperArm / LowerArm / Hand,
        /// hip / knee / ankle = UpperLeg / LowerLeg / Foot, ball = <see cref="HumanoidRig.Ball"/> moving with the
        /// Foot, toe = <see cref="HumanoidRig.ToeTip"/> moving with the Toes (else the Foot).
        /// </summary>
        public static Dictionary<string, Vec3> FromSkeleton(PosedSkeleton skeleton)
        {
            if (skeleton == null)
            {
                throw new ArgumentNullException(nameof(skeleton));
            }
            HumanoidRig rig = skeleton.Rig;
            Vec3 head = rig.Rest(HumanBone.Head);
            var joints = new Dictionary<string, Vec3>(StringComparer.Ordinal)
            {
                [JointNames.Pelvis] = skeleton.Position(HumanBone.Hips),
                [JointNames.Waist] = skeleton.Position(HumanBone.Spine),
                [JointNames.Chest] = skeleton.TorsoPoint(HumanBone.Chest),
                [JointNames.NeckBase] = skeleton.TorsoPoint(HumanBone.Neck),
                [JointNames.NeckTop] = skeleton.Position(HumanBone.Head),
                [JointNames.Head] = skeleton.Attached(HumanBone.Head, head.WithZ((head.Z + rig.HeadTopHeight) / 2)),
                [JointNames.HeadTop] = skeleton.Attached(HumanBone.Head, head.WithZ(rig.HeadTopHeight)),
            };
            foreach (int side in new[] { 1, -1 })
            {
                joints[JointNames.Shoulder(side)] = skeleton.Position(HumanBoneHierarchy.UpperArm(side));
                joints[JointNames.Elbow(side)] = skeleton.Position(HumanBoneHierarchy.LowerArm(side));
                joints[JointNames.Hand(side)] = skeleton.Position(HumanBoneHierarchy.Hand(side));
                joints[JointNames.Hip(side)] = skeleton.Position(HumanBoneHierarchy.UpperLeg(side));
                joints[JointNames.Knee(side)] = skeleton.Position(HumanBoneHierarchy.LowerLeg(side));
                joints[JointNames.Ankle(side)] = skeleton.Position(HumanBoneHierarchy.Foot(side));
                joints[JointNames.Ball(side)] = skeleton.Attached(HumanBoneHierarchy.Foot(side), rig.Ball(side));
                HumanBone toes = HumanBoneHierarchy.Toes(side);
                joints[JointNames.Toe(side)] = skeleton.Attached(rig.Has(toes) ? toes : HumanBoneHierarchy.Foot(side),
                    rig.ToeTip(side));
            }
            return joints;
        }
    }
}
