using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    public class SyntheticRigsTests
    {
        private const double Eps = 1e-12;

        [TestCaseSource(typeof(TestRigs), nameof(TestRigs.Variants))]
        public void StandsInATPose(RigInput input)
        {
            Assert.That(input.MeshBottom, Is.EqualTo(0));
            foreach (HumanBone bone in HumanBones.Required)
            {
                Assert.That(input.Has(bone), bone.ToString());
            }
            Vec3 Bone(HumanBone b) => input.Bones[b];
            foreach (int side in TestMath.Sides)
            {
                Vec3 shoulder = Bone(HumanBoneHierarchy.UpperArm(side));
                Vec3 elbow = Bone(HumanBoneHierarchy.LowerArm(side));
                Vec3 hand = Bone(HumanBoneHierarchy.Hand(side));
                // arms straight out along +/-x
                TestMath.AssertClose((elbow - shoulder).Normalized, Vec3.UnitX * side, Eps, "upper arm");
                TestMath.AssertClose((hand - elbow).Normalized, Vec3.UnitX * side, Eps, "forearm");
                // legs straight down
                Vec3 hip = Bone(HumanBoneHierarchy.UpperLeg(side));
                Vec3 knee = Bone(HumanBoneHierarchy.LowerLeg(side));
                Vec3 ankle = Bone(HumanBoneHierarchy.Foot(side));
                TestMath.AssertClose((knee - hip).Normalized, -Vec3.UnitZ, Eps, "thigh");
                TestMath.AssertClose((ankle - knee).Normalized, -Vec3.UnitZ, Eps, "shin");
                Assert.That(hip.X * side, Is.GreaterThan(0));
                Assert.That(ankle.Z, Is.GreaterThan(0));
                Assert.That(hip.Z, Is.LessThan(Bone(HumanBone.Hips).Z));
                if (input.Has(HumanBoneHierarchy.Toes(side)))
                {
                    Vec3 toes = Bone(HumanBoneHierarchy.Toes(side));
                    Assert.That(toes.Y, Is.GreaterThan(ankle.Y), "toes ahead of the ankle");
                    Assert.That(toes.Z, Is.LessThan(ankle.Z));
                }
            }
            Assert.That(Bone(HumanBone.Spine).Z, Is.GreaterThan(Bone(HumanBone.Hips).Z));
            Assert.That(Bone(HumanBone.Head).Z, Is.GreaterThan(Bone(HumanBone.LeftUpperArm).Z));
            Assert.That(input.MeshTop, Is.GreaterThan(Bone(HumanBone.Head).Z));
        }

        [TestCaseSource(typeof(TestRigs), nameof(TestRigs.Variants))]
        public void LeftIsTheMirrorImageOfRight(RigInput input)
        {
            foreach (HumanBone bone in input.Bones.Keys)
            {
                Assert.That(input.Bones[HumanBoneHierarchy.Mirror(bone)], Is.EqualTo(TestMath.Mirror(input.Bones[bone])),
                    bone.ToString());
            }
            Assert.That(input.LeftToeTip.HasValue, Is.EqualTo(input.RightToeTip.HasValue));
            if (input.LeftToeTip.HasValue)
            {
                Assert.That(input.LeftToeTip, Is.EqualTo(TestMath.Mirror(input.RightToeTip.Value)));
                Assert.That(input.LeftHeel, Is.EqualTo(TestMath.Mirror(input.RightHeel.Value)));
            }
        }

        [Test]
        public void FullAndMinimalBoneSets()
        {
            RigInput full = SyntheticRigs.Generic(1.7);
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                Assert.That(full.Has(bone), bone.ToString());
            }
            Assert.That(full.Bones.Count, Is.EqualTo(HumanBones.ParentFirst.Count));
            Assert.That(full.LeftToeTip.HasValue && full.RightToeTip.HasValue && full.LeftHeel.HasValue && full.RightHeel.HasValue);
            Assert.That(full.RightToeTip.Value.Y, Is.GreaterThan(full.Bones[HumanBone.RightToes].Y));
            Assert.That(full.RightHeel.Value.Y, Is.LessThan(full.Bones[HumanBone.RightFoot].Y));

            RigInput minimal = SyntheticRigs.Chibi(1.0, true);
            Assert.That(minimal.Bones.Count, Is.EqualTo(HumanBones.Required.Count));
            foreach (HumanBone bone in HumanBones.Optional)
            {
                Assert.That(minimal.Has(bone), Is.False, bone.ToString());
            }
            Assert.That(minimal.LeftToeTip.HasValue || minimal.RightToeTip.HasValue || minimal.LeftHeel.HasValue
                || minimal.RightHeel.HasValue, Is.False);
        }

        [Test]
        public void AnyProportionsGiveTheRequestedHeight()
        {
            var rng = new Random(2);
            for (int i = 0; i < 200; i++)
            {
                double height = TestMath.Uniform(rng, 0.3, 3);
                var p = new SyntheticRigProportions
                {
                    Legs = TestMath.Uniform(rng, 0.3, 3),
                    Arms = TestMath.Uniform(rng, 0.3, 3),
                    Torso = TestMath.Uniform(rng, 0.3, 3),
                    Head = TestMath.Uniform(rng, 0.3, 3),
                    ShoulderWidth = TestMath.Uniform(rng, 0.3, 3),
                };
                RigInput input = SyntheticRigs.Generic(height, p, rng.Next(2) == 0);
                Assert.That(input.MeshTop, Is.EqualTo(height));
                HumanoidRig rig = HumanoidRig.Measure(input);
                Assert.That(rig.Height, Is.EqualTo(height).Within(1e-12));
                Assert.That(rig.Warnings, Is.Empty);
            }
        }

        [Test]
        public void ProportionsChangeTheBody()
        {
            HumanoidRig average = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            HumanoidRig longLegs = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, new SyntheticRigProportions { Legs = 1.3 }));
            HumanoidRig longArms = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, new SyntheticRigProportions { Arms = 1.3 }));
            HumanoidRig bigHead = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, new SyntheticRigProportions { Head = 1.5 }));
            HumanoidRig longTorso = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, new SyntheticRigProportions { Torso = 1.3 }));
            HumanoidRig broad = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, new SyntheticRigProportions { ShoulderWidth = 1.4 }));
            Assert.That(longLegs.LegLength, Is.GreaterThan(average.LegLength));
            Assert.That(longArms.ArmLength / longArms.LegLength, Is.EqualTo(1.3 * average.ArmLength / average.LegLength).Within(1e-9));
            Assert.That(HeadFraction(bigHead), Is.GreaterThan(HeadFraction(average)));
            Assert.That(TorsoLength(longTorso) / longTorso.LegLength, Is.EqualTo(1.3 * TorsoLength(average) / average.LegLength)
                .Within(1e-9));
            Assert.That(broad.ShoulderWidth / broad.HipWidth, Is.EqualTo(1.4 * average.ShoulderWidth / average.HipWidth).Within(1e-9));
        }

        [Test]
        public void ChibiHasABigHeadAndShortLimbs()
        {
            HumanoidRig average = HumanoidRig.Measure(SyntheticRigs.Generic(1.0));
            HumanoidRig chibi = HumanoidRig.Measure(SyntheticRigs.Chibi(1.0));
            Assert.That(HeadFraction(chibi), Is.GreaterThan(0.25));
            Assert.That(HeadFraction(average), Is.LessThan(0.15));
            Assert.That(chibi.LegLength, Is.LessThan(0.75 * average.LegLength));
            Assert.That(chibi.ArmLength, Is.LessThan(0.75 * average.ArmLength));
            Assert.That(chibi.LegStyle, Is.EqualTo(LegStyle.Plantigrade));
        }

        [Test]
        public void RejectsBadArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticRigs.Generic(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticRigs.Chibi(double.NaN));
            var e = Assert.Throws<ArgumentOutOfRangeException>(() =>
                SyntheticRigs.Generic(1.7, new SyntheticRigProportions { Head = -1 }));
            StringAssert.Contains("Head", e.Message);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SyntheticRigs.Generic(1.7, new SyntheticRigProportions { Legs = double.PositiveInfinity }));
        }

        // share of the height above the Head bone
        private static double HeadFraction(HumanoidRig rig) =>
            (rig.HeadTopHeight - rig.Rest(HumanBone.Head).Z) / rig.HeadTopHeight;

        private static double TorsoLength(HumanoidRig rig) => rig.Rest(HumanBone.Neck).Z - rig.HipsHeight;
    }
}
