using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    public class HumanoidRigTests
    {
        private const double Eps = 1e-12;

        [Test]
        public void MeasuresTheGenericRig()
        {
            RigInput input = SyntheticRigs.Generic(1.7);
            HumanoidRig rig = HumanoidRig.Measure(input);
            double k = 1.5 / 1.7;
            Assert.That(rig.Height, Is.EqualTo(1.7).Within(Eps));
            Assert.That(rig.TilesPerMeter, Is.EqualTo(k).Within(Eps));
            Assert.That(rig.HeadTopHeight, Is.EqualTo(Conventions.DefaultCharacterHeight));
            Assert.That(rig.Warnings, Is.Empty);
            Assert.That(rig.LegStyle, Is.EqualTo(LegStyle.Plantigrade));
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                Assert.That(rig.Has(bone), bone.ToString());
                TestMath.AssertClose(rig.Rest(bone), input.Bones[bone] * k, Eps, bone.ToString());
            }
            Assert.That(rig.Bones, Is.EqualTo(HumanBones.ParentFirst));
            Assert.That(rig.HipsHeight, Is.EqualTo(input.Bones[HumanBone.Hips].Z * k).Within(Eps));
            Assert.That(rig.ShoulderWidth, Is.EqualTo(
                Vec3.Distance(input.Bones[HumanBone.LeftUpperArm], input.Bones[HumanBone.RightUpperArm]) * k).Within(Eps));
            Assert.That(rig.HipWidth, Is.EqualTo(
                Vec3.Distance(input.Bones[HumanBone.LeftUpperLeg], input.Bones[HumanBone.RightUpperLeg]) * k).Within(Eps));
            foreach (int side in TestMath.Sides)
            {
                Vec3 Bone(HumanBone b) => input.Bones[b];
                Assert.That(rig.UpperArmLength(side), Is.EqualTo(
                    Vec3.Distance(Bone(HumanBoneHierarchy.UpperArm(side)), Bone(HumanBoneHierarchy.LowerArm(side))) * k).Within(Eps));
                Assert.That(rig.ForearmLength(side), Is.EqualTo(
                    Vec3.Distance(Bone(HumanBoneHierarchy.LowerArm(side)), Bone(HumanBoneHierarchy.Hand(side))) * k).Within(Eps));
                Assert.That(rig.ThighLength(side), Is.EqualTo(
                    Vec3.Distance(Bone(HumanBoneHierarchy.UpperLeg(side)), Bone(HumanBoneHierarchy.LowerLeg(side))) * k).Within(Eps));
                Assert.That(rig.ShinLength(side), Is.EqualTo(
                    Vec3.Distance(Bone(HumanBoneHierarchy.LowerLeg(side)), Bone(HumanBoneHierarchy.Foot(side))) * k).Within(Eps));
                Assert.That(rig.AnkleHeight(side), Is.EqualTo(Bone(HumanBoneHierarchy.Foot(side)).Z * k).Within(Eps));
            }
            Assert.That(rig.LegLength, Is.EqualTo(rig.ThighLength(1) + rig.ShinLength(1)).Within(Eps));
            Assert.That(rig.ArmLength, Is.EqualTo(rig.UpperArmLength(-1) + rig.ForearmLength(-1)).Within(Eps));
        }

        [TestCaseSource(typeof(TestRigs), nameof(TestRigs.Variants))]
        public void RestSpaceStandsOnTheGroundUnderThePelvis(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input, 2.0);
            Vec3 hips = rig.Rest(HumanBone.Hips);
            Assert.That(hips.X, Is.EqualTo(0).Within(Eps));
            Assert.That(hips.Y, Is.EqualTo(0).Within(Eps));
            Assert.That(rig.HeadTopHeight, Is.EqualTo(2.0));
            foreach (int side in TestMath.Sides)
            {
                Assert.That(rig.ToeTip(side).Z, Is.EqualTo(0).Within(Eps), "toe tips on the ground");
                Assert.That(rig.Heel(side).Z, Is.EqualTo(0).Within(Eps), "heels on the ground");
                Assert.That(rig.AnkleHeight(side), Is.GreaterThan(0));
                Assert.That(rig.Rest(HumanBone.Head).Z, Is.LessThan(rig.HeadTopHeight));
            }
        }

        [Test]
        public void RandomRigsAreInvariantUnderTranslationAndScale()
        {
            var rng = new Random(11);
            for (int i = 0; i < 100; i++)
            {
                RigInput input = TestRigs.RandomInput(rng);
                HumanoidRig a = HumanoidRig.Measure(input);
                var offset = new Vec3(TestMath.Uniform(rng, -5, 5), TestMath.Uniform(rng, -5, 5), TestMath.Uniform(rng, -5, 5));
                HumanoidRig b = HumanoidRig.Measure(TestRigs.Translate(input, offset));
                Assert.That(b.TilesPerMeter, Is.EqualTo(a.TilesPerMeter).Within(1e-12 * a.TilesPerMeter));
                foreach (HumanBone bone in a.Bones)
                {
                    TestMath.AssertClose(b.Rest(bone), a.Rest(bone), 1e-9, bone.ToString());
                }
                foreach (int side in TestMath.Sides)
                {
                    TestMath.AssertClose(b.Ball(side), a.Ball(side), 1e-9, "ball");
                    TestMath.AssertClose(b.ToeTip(side), a.ToeTip(side), 1e-9, "toe tip");
                    TestMath.AssertClose(b.Heel(side), a.Heel(side), 1e-9, "heel");
                }
                // twice the target height: everything twice as far from the origin
                HumanoidRig c = HumanoidRig.Measure(input, 2 * Conventions.DefaultCharacterHeight);
                foreach (HumanBone bone in a.Bones)
                {
                    TestMath.AssertClose(c.Rest(bone), a.Rest(bone) * 2, 1e-9, bone.ToString());
                }
                Assert.That(c.LegLength, Is.EqualTo(2 * a.LegLength).Within(1e-9));
            }
        }

        [Test]
        public void ListsEveryMissingRequiredBone()
        {
            RigInput input = SyntheticRigs.Generic(1.6);
            input.Bones.Remove(HumanBone.LeftHand);
            input.Bones.Remove(HumanBone.Spine);
            input.Bones.Remove(HumanBone.RightFoot);
            var e = Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(input));
            StringAssert.Contains("LeftHand", e.Message);
            StringAssert.Contains("Spine", e.Message);
            StringAssert.Contains("RightFoot", e.Message);
            Assert.That(e.ParamName, Is.EqualTo("input"));
        }

        [Test]
        public void EstimatesTheHeightFromTheHeadWhenTheMeshBoundsAreEmpty()
        {
            RigInput input = SyntheticRigs.Generic(1.6);
            double head = input.Bones[HumanBone.Head].Z;
            input.MeshTop = 0;
            input.MeshBottom = 0;
            HumanoidRig rig = HumanoidRig.Measure(input);
            Assert.That(rig.Height, Is.EqualTo(head * HumanoidRig.HeightPerHeadHeight).Within(Eps));
            Assert.That(rig.TilesPerMeter, Is.EqualTo(1.5 / rig.Height).Within(Eps));
            Assert.That(rig.Warnings, Has.Count.EqualTo(1));
            StringAssert.Contains("estimated", rig.Warnings[0]);
            StringAssert.Contains(rig.Height.ToString("0.###", CultureInfo.InvariantCulture), rig.Warnings[0]);
        }

        [Test]
        public void RefusesAnAvatarWithoutAnyHeight()
        {
            RigInput input = SyntheticRigs.Generic(1.6);
            input.MeshTop = 2;
            input.MeshBottom = 2;
            Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(input));
        }

        [Test]
        public void RejectsBadArguments()
        {
            Assert.Throws<ArgumentNullException>(() => HumanoidRig.Measure(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => HumanoidRig.Measure(SyntheticRigs.Generic(1.6), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HumanoidRig.Measure(SyntheticRigs.Generic(1.6), double.NaN));

            RigInput nan = SyntheticRigs.Generic(1.6);
            nan.Bones[HumanBone.Chest] = new Vec3(0, double.NaN, 1);
            StringAssert.Contains("Chest", Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(nan)).Message);
            RigInput infiniteTop = SyntheticRigs.Generic(1.6);
            infiniteTop.MeshTop = double.PositiveInfinity;
            Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(infiniteTop));
            RigInput badHeel = SyntheticRigs.Generic(1.6);
            badHeel.LeftHeel = new Vec3(double.NaN, 0, 0);
            Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(badHeel));
        }

        [Test]
        public void RejectsDegenerateLimbs()
        {
            RigInput input = SyntheticRigs.Generic(1.6);
            input.Bones[HumanBone.RightLowerArm] = input.Bones[HumanBone.RightUpperArm];
            StringAssert.Contains("RightLowerArm", Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(input)).Message);

            // arms pointing forward are not a T-pose
            RigInput forward = SyntheticRigs.Generic(1.6);
            Vec3 shoulder = forward.Bones[HumanBone.LeftUpperArm];
            forward.Bones[HumanBone.LeftLowerArm] = shoulder + new Vec3(0, 0.25, 0);
            forward.Bones[HumanBone.LeftHand] = shoulder + new Vec3(0, 0.5, 0);
            StringAssert.Contains("T-pose", Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(forward)).Message);

            // so is an elbow bent straight forward, even with the hand out to the side
            RigInput bent = SyntheticRigs.Generic(1.6);
            Vec3 rightShoulder = bent.Bones[HumanBone.RightUpperArm];
            bent.Bones[HumanBone.RightLowerArm] = rightShoulder + new Vec3(0, 0.25, 0);
            bent.Bones[HumanBone.RightHand] = rightShoulder + new Vec3(0.25, 0.25, 0);
            StringAssert.Contains("RightUpperArm to RightLowerArm",
                Assert.Throws<ArgumentException>(() => HumanoidRig.Measure(bent)).Message);
        }

        [Test]
        public void TorsoRestIsTheBoneWhenTheRigHasIt()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            foreach (HumanBone bone in HumanBoneHierarchy.TorsoChain)
            {
                Assert.That(rig.TorsoRest(bone), Is.EqualTo(rig.Rest(bone)), bone.ToString());
            }
            Assert.That(rig.NeckBase, Is.EqualTo(rig.Rest(HumanBone.Neck)));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.TorsoRest(HumanBone.LeftLowerArm));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.TorsoRest(HumanBone.RightFoot));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.TorsoRest((HumanBone)42));
        }

        [Test]
        public void TorsoRestEstimatesMissingOptionalBones()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, null, true));
            Vec3 neckBase = Vec3.Lerp(rig.Rest(HumanBone.LeftUpperArm), rig.Rest(HumanBone.RightUpperArm), 0.5);
            TestMath.AssertClose(rig.NeckBase, neckBase, Eps, "neck base between the shoulder joints");
            Vec3 chest = Vec3.Lerp(rig.Rest(HumanBone.Spine), neckBase, 0.5);
            TestMath.AssertClose(rig.TorsoRest(HumanBone.Chest), chest, Eps, "chest");
            TestMath.AssertClose(rig.TorsoRest(HumanBone.UpperChest), Vec3.Lerp(chest, neckBase, 0.5), Eps, "upper chest");
            TestMath.AssertClose(rig.TorsoRest(HumanBone.Neck), neckBase, Eps, "neck");
            foreach (int side in TestMath.Sides)
            {
                Vec3 shoulder = rig.TorsoRest(HumanBoneHierarchy.Shoulder(side));
                TestMath.AssertClose(shoulder,
                    Vec3.Lerp(neckBase, rig.Rest(HumanBoneHierarchy.UpperArm(side)), HumanoidRig.ShoulderBoneFraction), Eps,
                    "shoulder");
                Assert.That(shoulder.X * side, Is.GreaterThan(0), "on its own side");
            }
            // estimates stack up the torso
            Assert.That(rig.TorsoRest(HumanBone.Chest).Z, Is.GreaterThan(rig.Rest(HumanBone.Spine).Z));
            Assert.That(rig.TorsoRest(HumanBone.UpperChest).Z, Is.GreaterThan(rig.TorsoRest(HumanBone.Chest).Z));
            Assert.That(rig.TorsoRest(HumanBone.Neck).Z, Is.GreaterThan(rig.TorsoRest(HumanBone.UpperChest).Z));
        }

        [Test]
        public void ParentIsTheNearestBoneTheRigHas()
        {
            HumanoidRig full = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            HumanoidRig minimal = HumanoidRig.Measure(SyntheticRigs.Generic(1.6, null, true));
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                Assert.That(full.Parent(bone), Is.EqualTo(HumanBoneHierarchy.Parent(bone)), bone.ToString());
            }
            Assert.That(minimal.Parent(HumanBone.Hips), Is.Null);
            Assert.That(minimal.Parent(HumanBone.Head), Is.EqualTo(HumanBone.Spine));
            Assert.That(minimal.Parent(HumanBone.LeftUpperArm), Is.EqualTo(HumanBone.Spine));
            Assert.That(minimal.Parent(HumanBone.RightUpperLeg), Is.EqualTo(HumanBone.Hips));
            Assert.That(minimal.Parent(HumanBone.Neck), Is.EqualTo(HumanBone.Spine), "also for bones the rig lacks");
            Assert.That(minimal.Bones, Has.Count.EqualTo(HumanBones.Required.Count));
            Assert.That(minimal.Has(HumanBone.LeftToes), Is.False);
            Assert.Throws<ArgumentException>(() => minimal.Rest(HumanBone.LeftToes));
            Assert.Throws<ArgumentOutOfRangeException>(() => minimal.Parent((HumanBone)42));
        }

        [Test]
        public void ParentPropertyOverRandomRigs()
        {
            var rng = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                var order = new List<HumanBone>(rig.Bones);
                foreach (HumanBone bone in rig.Bones)
                {
                    HumanBone? expected = HumanBoneHierarchy.Parent(bone);
                    while (expected.HasValue && !rig.Has(expected.Value))
                    {
                        expected = HumanBoneHierarchy.Parent(expected.Value);
                    }
                    Assert.That(rig.Parent(bone), Is.EqualTo(expected));
                    if (expected.HasValue)
                    {
                        Assert.That(order.IndexOf(expected.Value), Is.LessThan(order.IndexOf(bone)), "parents first");
                    }
                }
            }
        }

        [Test]
        public void FootGeometryFromBonesAndSolePoints()
        {
            RigInput input = SyntheticRigs.Generic(1.7);
            HumanoidRig rig = HumanoidRig.Measure(input);
            double k = rig.TilesPerMeter;
            foreach (int side in TestMath.Sides)
            {
                TestMath.AssertClose(rig.Ball(side), input.Bones[HumanBoneHierarchy.Toes(side)] * k, Eps, "ball = Toes");
                Vec3? tip = side > 0 ? input.RightToeTip : input.LeftToeTip;
                Vec3? heel = side > 0 ? input.RightHeel : input.LeftHeel;
                TestMath.AssertClose(rig.ToeTip(side), tip.Value * k, Eps, "measured toe tip");
                TestMath.AssertClose(rig.Heel(side), heel.Value * k, Eps, "measured heel");
                TestMath.AssertClose(rig.FootForward(side), Vec3.UnitY, Eps, "forward");
                Assert.That(rig.ToeTip(side).Y, Is.GreaterThan(rig.Ball(side).Y));
                Assert.That(rig.Ball(side).Y, Is.GreaterThan(rig.Rest(HumanBoneHierarchy.Foot(side)).Y));
                Assert.That(rig.Heel(side).Y, Is.LessThan(rig.Rest(HumanBoneHierarchy.Foot(side)).Y));
            }
        }

        [Test]
        public void EstimatesToeTipAndHeelFromTheToesBone()
        {
            RigInput input = SyntheticRigs.Generic(1.7);
            input.LeftToeTip = null;
            input.LeftHeel = null;
            HumanoidRig rig = HumanoidRig.Measure(input);
            Vec3 ankle = rig.Rest(HumanBone.LeftFoot);
            Vec3 toes = rig.Rest(HumanBone.LeftToes);
            Vec3 tip = (ankle + (toes - ankle) / HumanoidRig.BallFraction).WithZ(0);
            TestMath.AssertClose(rig.ToeTip(-1), tip, Eps, "toe tip");
            TestMath.AssertClose(rig.Ball(-1), toes, Eps, "ball");
            double back = HumanoidRig.HeelFraction * (tip - ankle).Horizontal.Length;
            TestMath.AssertClose(rig.Heel(-1), (ankle - Vec3.UnitY * back).WithZ(0), Eps, "heel");
        }

        [Test]
        public void EstimatesTheWholeFootWithoutToesOrSolePoints()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, null, true));
            foreach (int side in TestMath.Sides)
            {
                Vec3 ankle = rig.Rest(HumanBoneHierarchy.Foot(side));
                double reach = HumanoidRig.ToeReachPerLegLength * (rig.ThighLength(side) + rig.ShinLength(side));
                Vec3 tip = (ankle + Vec3.UnitY * reach).WithZ(0);
                TestMath.AssertClose(rig.ToeTip(side), tip, Eps, "toe tip");
                TestMath.AssertClose(rig.Ball(side), ankle + (tip - ankle) * HumanoidRig.BallFraction, Eps, "ball");
                TestMath.AssertClose(rig.Heel(side), (ankle - Vec3.UnitY * (HumanoidRig.HeelFraction * reach)).WithZ(0), Eps,
                    "heel");
                TestMath.AssertClose(rig.FootForward(side), Vec3.UnitY, Eps, "forward");
            }
        }

        [Test]
        public void MeasuredToeTipWithoutToesBoneGivesTheBallAndDirection()
        {
            RigInput input = SyntheticRigs.Generic(1.7);
            input.Bones.Remove(HumanBone.RightToes);
            Vec3 ankle = input.Bones[HumanBone.RightFoot];
            input.RightToeTip = ankle + new Vec3(0.06, 0.17, -ankle.Z); // splayed outwards
            HumanoidRig rig = HumanoidRig.Measure(input);
            Vec3 restAnkle = rig.Rest(HumanBone.RightFoot);
            Vec3 restTip = rig.ToeTip(1);
            TestMath.AssertClose(rig.Ball(1), restAnkle + (restTip - restAnkle) * HumanoidRig.BallFraction, Eps, "ball");
            TestMath.AssertClose(rig.FootForward(1), new Vec3(0.06, 0.17, 0).Normalized, Eps, "forward");
        }

        [TestCase(0.0, -0.05)]
        [TestCase(0.05, 0.01)]
        public void ToesBehindOrBesideTheAnkleGiveNoFootDirection(double toesOutward, double toesForward)
        {
            RigInput input = SyntheticRigs.Generic(1.7);
            Vec3 ankle = input.Bones[HumanBone.RightFoot];
            input.Bones[HumanBone.RightToes] = ankle + new Vec3(toesOutward, toesForward, -0.04);
            input.RightToeTip = ankle + new Vec3(0.06, 0.17, -ankle.Z);
            HumanoidRig rig = HumanoidRig.Measure(input);
            TestMath.AssertClose(rig.FootForward(1), new Vec3(0.06, 0.17, 0).Normalized, Eps, "the measured toe tip's direction");
            TestMath.AssertClose(rig.Ball(1), rig.Rest(HumanBone.RightToes), Eps, "ball = Toes");
            input.RightToeTip = null;
            TestMath.AssertClose(HumanoidRig.Measure(input).FootForward(1), Vec3.UnitY, Eps, "without one, forward");
        }

        [Test]
        public void DetectsDigitigradeLegs()
        {
            RigInput input = SyntheticRigs.Generic(1.7, null, true);
            foreach (int side in TestMath.Sides)
            {
                // a long foot standing on its toes: the ankle a third of the way up the leg
                Vec3 hip = input.Bones[HumanBoneHierarchy.UpperLeg(side)];
                input.Bones[HumanBoneHierarchy.LowerLeg(side)] = new Vec3(hip.X, 0.1, hip.Z * 0.6);
                input.Bones[HumanBoneHierarchy.Foot(side)] = new Vec3(hip.X, -0.15, hip.Z * 0.33);
            }
            HumanoidRig rig = HumanoidRig.Measure(input);
            Assert.That(rig.LegStyle, Is.EqualTo(LegStyle.Digitigrade));
            Assert.That(HumanoidRig.Measure(SyntheticRigs.Chibi(1.0)).LegStyle, Is.EqualTo(LegStyle.Plantigrade));
        }

        [Test]
        public void BendAxesFollowAnatomy()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            TestMath.AssertClose(rig.ElbowBendAxis(1), Vec3.UnitZ, Eps, "right elbow");
            TestMath.AssertClose(rig.ElbowBendAxis(-1), -Vec3.UnitZ, Eps, "left elbow");
            TestMath.AssertClose(rig.KneeBendAxis(1), -Vec3.UnitX, Eps, "right knee");
            TestMath.AssertClose(rig.KneeBendAxis(-1), -Vec3.UnitX, Eps, "left knee");
            // flexing the right forearm about its axis turns it forward; the shin about its axis backwards
            Assert.That(MathUtil.Rot(Vec3.UnitX, rig.ElbowBendAxis(1), 0.5).Y, Is.GreaterThan(0));
            Assert.That(MathUtil.Rot(-Vec3.UnitX, rig.ElbowBendAxis(-1), 0.5).Y, Is.GreaterThan(0));
            Assert.That(MathUtil.Rot(-Vec3.UnitZ, rig.KneeBendAxis(1), 0.5).Y, Is.LessThan(0));
        }

        [Test]
        public void RandomRigsHaveConsistentMeasurements()
        {
            var rng = new Random(3);
            for (int i = 0; i < 200; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                Assert.That(rig.HipsHeight, Is.GreaterThan(0).And.LessThan(rig.HeadTopHeight));
                Assert.That(rig.LegLength, Is.EqualTo(
                    (rig.ThighLength(1) + rig.ShinLength(1) + rig.ThighLength(-1) + rig.ShinLength(-1)) / 2).Within(Eps));
                Assert.That(rig.ArmLength, Is.EqualTo(
                    (rig.UpperArmLength(1) + rig.ForearmLength(1) + rig.UpperArmLength(-1) + rig.ForearmLength(-1)) / 2).Within(Eps));
                foreach (int side in TestMath.Sides)
                {
                    Assert.That(rig.ElbowBendAxis(side).Length, Is.EqualTo(1).Within(Eps));
                    Assert.That(rig.KneeBendAxis(side).Length, Is.EqualTo(1).Within(Eps));
                    Assert.That(rig.FootForward(side).Length, Is.EqualTo(1).Within(Eps));
                    Assert.That(rig.FootForward(side).Z, Is.EqualTo(0));
                    Assert.That(Vec3.Dot(rig.FootForward(side), Vec3.UnitY), Is.GreaterThan(0.5), "jitter keeps feet forward");
                    Assert.That(rig.ElbowBendAxis(side).Z * side, Is.GreaterThan(0.9));
                    Assert.That(rig.KneeBendAxis(side).X, Is.LessThan(-0.9));
                }
                Assert.That(rig.LegStyle, Is.EqualTo(LegStyle.Plantigrade));
                Assert.That(rig.Warnings, Is.Empty);
            }
        }

        [Test]
        public void SideMustBePlusOrMinusOne()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.UpperArmLength(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Ball(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => HumanBoneHierarchy.Hand(-2));
        }
    }

    public class HumanBoneHierarchyTests
    {
        [Test]
        public void ParentsComeFirstAndMirrorIsAnInvolution()
        {
            var seen = new HashSet<HumanBone>();
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                HumanBone? parent = HumanBoneHierarchy.Parent(bone);
                if (parent.HasValue)
                {
                    Assert.That(seen, Does.Contain(parent.Value), bone + " after its parent");
                }
                else
                {
                    Assert.That(bone, Is.EqualTo(HumanBone.Hips));
                }
                seen.Add(bone);
                HumanBone mirror = HumanBoneHierarchy.Mirror(bone);
                Assert.That(HumanBoneHierarchy.Mirror(mirror), Is.EqualTo(bone));
                Assert.That(mirror.ToString().StartsWith("Left", StringComparison.Ordinal),
                    Is.EqualTo(bone.ToString().StartsWith("Right", StringComparison.Ordinal)), bone.ToString());
                if (parent.HasValue)
                {
                    Assert.That(HumanBoneHierarchy.Parent(mirror), Is.EqualTo(HumanBoneHierarchy.Mirror(parent.Value)));
                }
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => HumanBoneHierarchy.Mirror((HumanBone)42));
            Assert.Throws<ArgumentOutOfRangeException>(() => HumanBoneHierarchy.Parent((HumanBone)42));
        }

        [Test]
        public void SideLookups()
        {
            Assert.That(HumanBoneHierarchy.UpperArm(1), Is.EqualTo(HumanBone.RightUpperArm));
            Assert.That(HumanBoneHierarchy.LowerArm(-1), Is.EqualTo(HumanBone.LeftLowerArm));
            Assert.That(HumanBoneHierarchy.Hand(1), Is.EqualTo(HumanBone.RightHand));
            Assert.That(HumanBoneHierarchy.Shoulder(-1), Is.EqualTo(HumanBone.LeftShoulder));
            Assert.That(HumanBoneHierarchy.UpperLeg(-1), Is.EqualTo(HumanBone.LeftUpperLeg));
            Assert.That(HumanBoneHierarchy.LowerLeg(1), Is.EqualTo(HumanBone.RightLowerLeg));
            Assert.That(HumanBoneHierarchy.Foot(-1), Is.EqualTo(HumanBone.LeftFoot));
            Assert.That(HumanBoneHierarchy.Toes(1), Is.EqualTo(HumanBone.RightToes));
        }
    }
}
