using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// <see cref="RigInput"/>s of plausible humanoids, for tests and for exporting without Unity. They stand in
    /// a T-pose facing +y on the ground (MeshBottom = 0, MeshTop = the height): arms straight out along +/-x,
    /// legs straight down, feet pointing forward with the toes ahead of the ankle. Left and right are exact
    /// mirror images. Positions are in meters, like everything in a <see cref="RigInput"/>.
    /// </summary>
    public static class SyntheticRigs
    {
        // Layout of an average adult in meters (about 1.715 m tall), before proportions and scaling.
        private const double AnkleHeight = 0.075;
        private const double Shin = 0.40;
        private const double Thigh = 0.41;
        private const double HipHalfWidth = 0.09;
        private const double HipsAboveLegs = 0.06;
        private const double SpineAboveHips = 0.10;
        private const double ChestAboveSpine = 0.13;
        private const double UpperChestAboveChest = 0.12;
        private const double NeckAboveUpperChest = 0.13;
        private const double ShouldersAboveUpperChest = 0.09;
        private const double ClavicleHalfWidth = 0.035;
        private const double ShoulderHalfWidth = 0.17;
        private const double UpperArm = 0.28;
        private const double Forearm = 0.25;
        private const double HeadAboveNeck = 0.10;
        private const double HeadTopAboveHead = 0.19;
        private const double ToesAhead = 0.13;
        private const double ToesHeight = 0.025;
        private const double ToeTipAhead = 0.19;
        private const double HeelBehind = 0.055;

        /// <summary>
        /// An average humanoid <paramref name="heightMeters"/> tall, with optional
        /// <paramref name="proportions"/> (null = average). <paramref name="minimal"/> leaves out every optional
        /// bone (<see cref="HumanBones.Optional"/>) and the measured sole points.
        /// </summary>
        public static RigInput Generic(double heightMeters, SyntheticRigProportions proportions = null, bool minimal = false)
        {
            if (!double.IsFinite(heightMeters) || heightMeters <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(heightMeters), heightMeters, "the height must be positive");
            }
            SyntheticRigProportions p = proportions ?? new SyntheticRigProportions();
            CheckFactor(p.Legs, nameof(p.Legs));
            CheckFactor(p.Arms, nameof(p.Arms));
            CheckFactor(p.Torso, nameof(p.Torso));
            CheckFactor(p.Head, nameof(p.Head));
            CheckFactor(p.ShoulderWidth, nameof(p.ShoulderWidth));

            double ankle = AnkleHeight * p.Legs;
            double knee = ankle + Shin * p.Legs;
            double legs = knee + Thigh * p.Legs;
            double hips = legs + HipsAboveLegs * p.Torso;
            double spine = hips + SpineAboveHips * p.Torso;
            double chest = spine + ChestAboveSpine * p.Torso;
            double upperChest = chest + UpperChestAboveChest * p.Torso;
            double neck = upperChest + NeckAboveUpperChest * p.Torso;
            double shoulders = upperChest + ShouldersAboveUpperChest * p.Torso;
            double head = neck + HeadAboveNeck * p.Head;
            double top = head + HeadTopAboveHead * p.Head;
            double s = heightMeters / top;

            var rig = new RigInput { MeshTop = heightMeters, MeshBottom = 0 };
            void Add(HumanBone bone, double x, double y, double z) => rig.Bones.Add(bone, new Vec3(x * s, y * s, z * s));

            // a slightly curved spine and the head forward of the neck, as on real avatars
            Add(HumanBone.Hips, 0, 0, hips);
            Add(HumanBone.Spine, 0, -0.012 * p.Torso, spine);
            Add(HumanBone.Head, 0, 0.015 * p.Head, head);
            if (!minimal)
            {
                Add(HumanBone.Chest, 0, -0.008 * p.Torso, chest);
                Add(HumanBone.UpperChest, 0, -0.004 * p.Torso, upperChest);
                Add(HumanBone.Neck, 0, -0.002 * p.Torso, neck);
            }
            foreach (int side in new[] { 1, -1 })
            {
                double shoulderX = side * ShoulderHalfWidth * p.ShoulderWidth;
                double armY = -0.01 * p.Torso;
                Add(HumanBoneHierarchy.UpperArm(side), shoulderX, armY, shoulders);
                Add(HumanBoneHierarchy.LowerArm(side), shoulderX + side * UpperArm * p.Arms, armY, shoulders);
                Add(HumanBoneHierarchy.Hand(side), shoulderX + side * (UpperArm + Forearm) * p.Arms, armY, shoulders);
                double hipX = side * HipHalfWidth;
                Add(HumanBoneHierarchy.UpperLeg(side), hipX, 0, legs);
                Add(HumanBoneHierarchy.LowerLeg(side), hipX, 0, knee);
                Add(HumanBoneHierarchy.Foot(side), hipX, 0, ankle);
                if (minimal)
                {
                    continue;
                }
                Add(HumanBoneHierarchy.Shoulder(side), side * ClavicleHalfWidth * p.ShoulderWidth, armY, shoulders);
                Add(HumanBoneHierarchy.Toes(side), hipX, ToesAhead * p.Legs, ToesHeight * p.Legs);
                var toeTip = new Vec3(hipX * s, ToeTipAhead * p.Legs * s, 0);
                var heel = new Vec3(hipX * s, -HeelBehind * p.Legs * s, 0);
                if (side > 0)
                {
                    rig.RightToeTip = toeTip;
                    rig.RightHeel = heel;
                }
                else
                {
                    rig.LeftToeTip = toeTip;
                    rig.LeftHeel = heel;
                }
            }
            return rig;
        }

        /// <summary>
        /// A chibi humanoid (<see cref="SyntheticRigProportions.Chibi"/>: big head, short limbs)
        /// <paramref name="heightMeters"/> tall; <paramref name="minimal"/> as in <see cref="Generic"/>.
        /// </summary>
        public static RigInput Chibi(double heightMeters, bool minimal = false) =>
            Generic(heightMeters, SyntheticRigProportions.Chibi(), minimal);

        private static void CheckFactor(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException("proportions", value,
                    "proportion " + name + " must be a positive factor");
            }
        }
    }
}
