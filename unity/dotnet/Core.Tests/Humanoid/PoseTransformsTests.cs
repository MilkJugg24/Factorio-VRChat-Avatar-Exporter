using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    public class PoseTransformsTests
    {
        private const double Eps = 1e-9;

        private static PoseResult RandomPose(HumanoidRig rig, Random rng)
        {
            PoseResult pose = PoseTargets.RandomReachable(rig, rng, rng.Next(2) == 0).Build(rig);
            pose.Prop = new PropPose(PropKind.Pickaxe, TestMath.RandomUnit(rng), TestMath.RandomUnit(rng), TestMath.RandomUnit(rng));
            pose.GroundToLowestPoint = rng.Next(2) == 0;
            return pose;
        }

        [Test]
        public void RigidMovesTheWholePoseAsOneBody()
        {
            var rng = new Random(71);
            for (int i = 0; i < 200; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                PoseResult pose = RandomPose(rig, rng);
                Quat r = TestMath.RandomRotation(rng, Math.PI);
                Vec3 pivot = TestMath.RandomUnit(rng) * 2, translation = TestMath.RandomUnit(rng) * 2;
                Vec3 Move(Vec3 p) => pivot + r.Rotate(p - pivot) + translation;

                PoseResult moved = PoseTransforms.Rigid(pose, r, pivot, translation);

                TestMath.AssertClose(moved.HipsPosition, Move(pose.HipsPosition), Eps, "hips");
                PosedSkeleton before = HumanoidSkeleton.Pose(rig, pose), after = HumanoidSkeleton.Pose(rig, moved);
                foreach (HumanBone bone in rig.Bones)
                {
                    TestMath.AssertClose(after.Position(bone), Move(before.Position(bone)), Eps, bone.ToString());
                    TestMath.AssertSameRotation(after.Delta(bone), r * before.Delta(bone), Eps, bone.ToString());
                }
                foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
                {
                    TestMath.AssertClose(moved.Joints[joint.Key], Move(joint.Value), Eps, joint.Key);
                }
                TestPoses.AssertJointsAreFk(rig, moved, Eps);
                foreach (KeyValuePair<HumanBone, Quat> delta in moved.WorldDeltas)
                {
                    TestMath.AssertUnit(delta.Value, delta.Key.ToString());
                }
                Assert.That(moved.Prop.Kind, Is.EqualTo(PropKind.Pickaxe));
                TestMath.AssertClose(moved.Prop.Base, Move(pose.Prop.Base), Eps, "prop base");
                TestMath.AssertClose(moved.Prop.Tip, Move(pose.Prop.Tip), Eps, "prop tip");
                TestMath.AssertClose(moved.Prop.Up, r.Rotate(pose.Prop.Up), Eps, "prop up");
                Assert.That(moved.LeftGrip, Is.EqualTo(pose.LeftGrip));
                Assert.That(moved.RightGrip, Is.EqualTo(pose.RightGrip));
                Assert.That(moved.GroundToLowestPoint, Is.EqualTo(pose.GroundToLowestPoint));
            }
        }

        [Test]
        public void PosesWithoutAHipsDeltaTurnToo()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            var pose = new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) };
            Quat arm = Quat.FromAxisAngle(Vec3.UnitY, 1.2);
            pose.WorldDeltas[HumanBone.RightUpperArm] = arm;
            Quat r = Quat.FromAxisAngle(Vec3.UnitZ, 0.8);
            PoseResult moved = PoseTransforms.Rigid(pose, r, Vec3.Zero, Vec3.Zero);
            Assert.That(moved.WorldDeltas[HumanBone.Hips], Is.EqualTo(r));
            TestMath.AssertSameRotation(moved.WorldDeltas[HumanBone.RightUpperArm], r * arm, 1e-15, "arm");
            Assert.That(pose.WorldDeltas.ContainsKey(HumanBone.Hips), Is.False, "the input is not changed");
            PosedSkeleton s = HumanoidSkeleton.Pose(rig, moved);
            TestMath.AssertClose(s.Position(HumanBone.LeftHand), r.Rotate(rig.Rest(HumanBone.LeftHand)), Eps, "unposed bones turn");

            // a pose from a source without a skeleton (the dragon) has joints only
            PoseResult dragon = new DragonPoseSource(BodyShape.Female).Pose(AnimationSpec.Idle, 0.25, 0);
            PoseResult turned = PoseTransforms.Rigid(dragon, r, Vec3.Zero, Vec3.UnitX);
            Assert.That(turned.WorldDeltas.Keys, Is.EquivalentTo(new[] { HumanBone.Hips }));
            TestMath.AssertClose(turned.Joints[JointNames.Head], r.Rotate(dragon.Joints[JointNames.Head]) + Vec3.UnitX, Eps, "head");
        }

        [Test]
        public void LaysAStandingPoseDownOnItsBack()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            PoseResult standing = new HumanoidPoseBuilder(rig).Build();
            // fall backwards about the heels' line: up becomes back (-y)
            Quat fall = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
            PoseResult corpse = PoseTransforms.Rigid(standing, fall, Vec3.Zero, Vec3.Zero);
            Assert.That(corpse.Joints[JointNames.HeadTop].Y, Is.EqualTo(-rig.HeadTopHeight).Within(Eps));
            Assert.That(corpse.Joints[JointNames.HeadTop].Z, Is.EqualTo(rig.Rest(HumanBone.Head).Y).Within(Eps),
                "the back of the head touches the ground");
            Assert.That(corpse.Joints[JointNames.Pelvis].Z, Is.EqualTo(0).Within(Eps), "the spine lies on the ground");
            Assert.That(corpse.Joints[JointNames.Toe(1)].Z, Is.GreaterThan(0), "toes point up");
            TestPoses.AssertJointsAreFk(rig, corpse, Eps);
        }

        [Test]
        public void IdentityLeavesThePoseUnchangedAndScaledRotationsAreNormalized()
        {
            var rng = new Random(72);
            HumanoidRig rig = TestRigs.Random(rng);
            PoseResult pose = RandomPose(rig, rng);
            PoseResult same = PoseTransforms.Rigid(pose, Quat.Identity, TestMath.RandomUnit(rng), Vec3.Zero);
            foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
            {
                TestMath.AssertClose(same.Joints[joint.Key], joint.Value, 1e-12, joint.Key);
            }
            foreach (KeyValuePair<HumanBone, Quat> delta in pose.WorldDeltas)
            {
                TestMath.AssertSameRotation(same.WorldDeltas[delta.Key], delta.Value, 1e-12, delta.Key.ToString());
            }
            Quat r = Quat.FromAxisAngle(new Vec3(1, 2, 3), 0.5);
            PoseResult a = PoseTransforms.Rigid(pose, r, Vec3.Zero, Vec3.Zero);
            PoseResult b = PoseTransforms.Rigid(pose, new Quat(r.W * 5, r.X * 5, r.Y * 5, r.Z * 5), Vec3.Zero, Vec3.Zero);
            TestMath.AssertClose(b.HipsPosition, a.HipsPosition, 1e-12, "scaled rotation");
            TestMath.AssertUnit(b.WorldDeltas[HumanBone.Hips], "hips");
        }

        [Test]
        public void RejectsBadArguments()
        {
            var pose = new PoseResult();
            Assert.Throws<ArgumentNullException>(() => PoseTransforms.Rigid(null, Quat.Identity, Vec3.Zero, Vec3.Zero));
            Assert.Throws<ArgumentException>(() => PoseTransforms.Rigid(pose, new Quat(0, 0, 0, 0), Vec3.Zero, Vec3.Zero));
            Assert.Throws<ArgumentException>(() => PoseTransforms.Rigid(pose, Quat.Identity, new Vec3(double.NaN, 0, 0), Vec3.Zero));
            Assert.Throws<ArgumentException>(() =>
                PoseTransforms.Rigid(pose, Quat.Identity, Vec3.Zero, new Vec3(0, double.PositiveInfinity, 0)));
        }
    }
}
