using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The humanoid bones Core poses. Names and values are those of UnityEngine.HumanBodyBones, so the
    /// Unity layer can cast between the two.
    /// </summary>
    public enum HumanBone
    {
        Hips = 0,
        LeftUpperLeg = 1,
        RightUpperLeg = 2,
        LeftLowerLeg = 3,
        RightLowerLeg = 4,
        LeftFoot = 5,
        RightFoot = 6,
        Spine = 7,
        Chest = 8,
        Neck = 9,
        Head = 10,
        LeftShoulder = 11,
        RightShoulder = 12,
        LeftUpperArm = 13,
        RightUpperArm = 14,
        LeftLowerArm = 15,
        RightLowerArm = 16,
        LeftHand = 17,
        RightHand = 18,
        LeftToes = 19,
        RightToes = 20,
        UpperChest = 54,
    }

    public static class HumanBones
    {
        /// <summary>Bones every humanoid avatar has (Unity's required humanoid bones, minus none).</summary>
        public static readonly IReadOnlyList<HumanBone> Required = new[]
        {
            HumanBone.Hips, HumanBone.Spine, HumanBone.Head,
            HumanBone.LeftUpperArm, HumanBone.LeftLowerArm, HumanBone.LeftHand,
            HumanBone.RightUpperArm, HumanBone.RightLowerArm, HumanBone.RightHand,
            HumanBone.LeftUpperLeg, HumanBone.LeftLowerLeg, HumanBone.LeftFoot,
            HumanBone.RightUpperLeg, HumanBone.RightLowerLeg, HumanBone.RightFoot,
        };

        /// <summary>Bones an avatar may lack (Unity humanoid optional bones that Core uses).</summary>
        public static readonly IReadOnlyList<HumanBone> Optional = new[]
        {
            HumanBone.Chest, HumanBone.UpperChest, HumanBone.Neck,
            HumanBone.LeftShoulder, HumanBone.RightShoulder, HumanBone.LeftToes, HumanBone.RightToes,
        };

        /// <summary>
        /// Every bone in parent-before-child order (the order to apply rotations in).
        /// </summary>
        public static readonly IReadOnlyList<HumanBone> ParentFirst = new[]
        {
            HumanBone.Hips, HumanBone.Spine, HumanBone.Chest, HumanBone.UpperChest, HumanBone.Neck, HumanBone.Head,
            HumanBone.LeftShoulder, HumanBone.LeftUpperArm, HumanBone.LeftLowerArm, HumanBone.LeftHand,
            HumanBone.RightShoulder, HumanBone.RightUpperArm, HumanBone.RightLowerArm, HumanBone.RightHand,
            HumanBone.LeftUpperLeg, HumanBone.LeftLowerLeg, HumanBone.LeftFoot, HumanBone.LeftToes,
            HumanBone.RightUpperLeg, HumanBone.RightLowerLeg, HumanBone.RightFoot, HumanBone.RightToes,
        };
    }

    /// <summary>
    /// What the Unity layer measures on the avatar, in its rest T-pose (Unity's humanoid T-pose from the
    /// avatar's humanDescription), facing forward.
    ///
    /// Positions are in model axes (x = the avatar's right, y = its forward, z = up; convert from Unity
    /// with <see cref="UnitySpace.ToCore"/>) and in METERS, relative to the avatar's root on the ground.
    /// </summary>
    public sealed class RigInput
    {
        /// <summary>World position of each humanoid bone the avatar has (at least <see cref="HumanBones.Required"/>).</summary>
        public Dictionary<HumanBone, Vec3> Bones { get; } = new Dictionary<HumanBone, Vec3>();

        /// <summary>Highest point of the visible meshes, meters (top of the head or hair, ears, horns).</summary>
        public double MeshTop { get; set; }

        /// <summary>Lowest point of the visible meshes, meters (the soles; usually close to 0).</summary>
        public double MeshBottom { get; set; }

        /// <summary>
        /// Optional sole points from the foot meshes: the front-most and back-most points of each foot
        /// near the ground. When missing they are estimated from the foot and toe bones.
        /// </summary>
        public Vec3? LeftToeTip { get; set; }
        public Vec3? RightToeTip { get; set; }
        public Vec3? LeftHeel { get; set; }
        public Vec3? RightHeel { get; set; }

        public bool Has(HumanBone bone) => Bones.ContainsKey(bone);
    }
}
