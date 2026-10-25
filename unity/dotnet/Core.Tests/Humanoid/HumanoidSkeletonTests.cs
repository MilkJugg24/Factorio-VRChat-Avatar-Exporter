using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    public class HumanoidSkeletonTests
    {
        private const double Eps = 1e-12;

        [TestCaseSource(typeof(TestRigs), nameof(TestRigs.Variants))]
        public void IdentityPoseIsTheRestPose(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var pose = new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) };
            PosedSkeleton empty = HumanoidSkeleton.Pose(rig, pose);
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                pose.WorldDeltas[bone] = Quat.Identity;
            }
            PosedSkeleton listed = HumanoidSkeleton.Pose(rig, pose);
            Assert.That(empty.Positions.Count, Is.EqualTo(rig.Bones.Count));
            foreach (HumanBone bone in rig.Bones)
            {
                TestMath.AssertClose(empty.Position(bone), rig.Rest(bone), Eps, bone.ToString());
                TestMath.AssertClose(listed.Position(bone), rig.Rest(bone), Eps, bone.ToString());
                Assert.That(empty.Delta(bone), Is.EqualTo(Quat.Identity));
            }
        }

        [Test]
        public void IdentityPoseIsTheRestPoseOnRandomRigs()
        {
            var rng = new Random(8);
            for (int i = 0; i < 200; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                var offset = new Vec3(TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, -1, 1));
                PosedSkeleton s = HumanoidSkeleton.Pose(rig, new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) + offset });
                foreach (HumanBone bone in rig.Bones)
                {
                    TestMath.AssertClose(s.Position(bone), rig.Rest(bone) + offset, 1e-12, bone.ToString());
                }
            }
        }

        [Test]
        public void TurningTheHipsTurnsEverythingAboutThem()
        {
            var rng = new Random(4);
            for (int i = 0; i < 100; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                Quat q = TestMath.RandomRotation(rng, Math.PI);
                var target = new Vec3(TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, 0, 1));
                var pose = new PoseResult { HipsPosition = target };
                pose.WorldDeltas[HumanBone.Hips] = q;
                PosedSkeleton s = HumanoidSkeleton.Pose(rig, pose);
                foreach (HumanBone bone in rig.Bones)
                {
                    Vec3 expected = target + q.Rotate(rig.Rest(bone) - rig.Rest(HumanBone.Hips));
                    TestMath.AssertClose(s.Position(bone), expected, 1e-12, bone.ToString());
                    TestMath.AssertSameRotation(s.Delta(bone), q, 1e-12, bone.ToString());
                }
            }
        }

        [Test]
        public void UnlistedBonesInheritTheirParentsDelta()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            Quat bend = Quat.FromAxisAngle(Vec3.UnitX, 0.6);
            var pose = new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) };
            pose.WorldDeltas[HumanBone.Chest] = bend;
            PosedSkeleton s = HumanoidSkeleton.Pose(rig, pose);
            Vec3 chest = rig.Rest(HumanBone.Chest);
            foreach (HumanBone bone in rig.Bones)
            {
                bool above = bone != HumanBone.Hips && bone != HumanBone.Spine && !bone.ToString().Contains("Leg")
                    && !bone.ToString().Contains("Foot") && !bone.ToString().Contains("Toes");
                Assert.That(s.Delta(bone), Is.EqualTo(above ? bend : Quat.Identity), bone.ToString());
                Vec3 expected = above ? chest + bend.Rotate(rig.Rest(bone) - chest) : rig.Rest(bone);
                TestMath.AssertClose(s.Position(bone), expected, Eps, bone.ToString());
            }
            // a listed child overrides the inherited delta, and its own children follow it
            Quat arm = Quat.FromAxisAngle(Vec3.UnitY, -1.2);
            pose.WorldDeltas[HumanBone.RightUpperArm] = arm;
            s = HumanoidSkeleton.Pose(rig, pose);
            Vec3 shoulder = s.Position(HumanBone.RightUpperArm);
            Assert.That(s.Delta(HumanBone.RightHand), Is.EqualTo(arm));
            TestMath.AssertClose(s.Position(HumanBone.RightHand),
                shoulder + arm.Rotate(rig.Rest(HumanBone.RightHand) - rig.Rest(HumanBone.RightUpperArm)), Eps, "hand");
        }

        [Test]
        public void MissingBonesAreSkippedAndTheirDeltasIgnored()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, null, true));
            var pose = new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) };
            pose.WorldDeltas[HumanBone.Chest] = Quat.FromAxisAngle(Vec3.UnitX, 0.6);
            pose.WorldDeltas[HumanBone.LeftToes] = Quat.FromAxisAngle(Vec3.UnitX, 0.6);
            PosedSkeleton s = HumanoidSkeleton.Pose(rig, pose);
            Assert.That(s.Positions.ContainsKey(HumanBone.Chest), Is.False);
            foreach (HumanBone bone in rig.Bones)
            {
                TestMath.AssertClose(s.Position(bone), rig.Rest(bone), Eps, bone.ToString());
            }
            Assert.Throws<ArgumentException>(() => s.Position(HumanBone.Chest));
            Assert.Throws<ArgumentException>(() => s.Delta(HumanBone.LeftToes));
        }

        [Test]
        public void DeltasAreNormalized()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            Quat q = Quat.FromAxisAngle(new Vec3(1, 2, 3), 0.8);
            var unit = new PoseResult();
            unit.WorldDeltas[HumanBone.Spine] = q;
            var scaled = new PoseResult();
            scaled.WorldDeltas[HumanBone.Spine] = new Quat(q.W * 3, q.X * 3, q.Y * 3, q.Z * 3);
            PosedSkeleton a = HumanoidSkeleton.Pose(rig, unit);
            PosedSkeleton b = HumanoidSkeleton.Pose(rig, scaled);
            foreach (HumanBone bone in rig.Bones)
            {
                TestMath.AssertClose(b.Position(bone), a.Position(bone), Eps, bone.ToString());
            }
        }

        [Test]
        public void AttachedPointsMoveRigidlyWithTheirBone()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            var pose = new PoseResult { HipsPosition = new Vec3(0.2, 0.1, 0.5) };
            Quat q = Quat.FromAxisAngle(new Vec3(0.3, -1, 0.2), 0.9);
            pose.WorldDeltas[HumanBone.Head] = q;
            PosedSkeleton s = HumanoidSkeleton.Pose(rig, pose);
            Vec3 point = rig.Rest(HumanBone.Head) + new Vec3(0.01, 0.05, 0.1);
            TestMath.AssertClose(s.Attached(HumanBone.Head, point), s.Position(HumanBone.Head) + q.Rotate(new Vec3(0.01, 0.05, 0.1)),
                Eps, "attached");
            TestMath.AssertClose(s.Attached(HumanBone.Head, rig.Rest(HumanBone.Head)), s.Position(HumanBone.Head), Eps, "origin");
        }

        [Test]
        public void RejectsNulls()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            Assert.Throws<ArgumentNullException>(() => HumanoidSkeleton.Pose(null, new PoseResult()));
            Assert.Throws<ArgumentNullException>(() => HumanoidSkeleton.Pose(rig, null));
        }

        /// <summary>
        /// A Unity-style transform hierarchy (<see cref="TransformHierarchy"/>) with arbitrary rest rotations and
        /// non-humanoid transforms between the bones must agree with forward kinematics.
        /// </summary>
        [Test]
        public void MatchesAUnityStyleTransformHierarchy()
        {
            var rng = new Random(21);
            for (int i = 0; i < 150; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                var sim = new TransformHierarchy(rig, rng);
                var pose = new PoseResult
                {
                    HipsPosition = new Vec3(TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, 0, 1)),
                };
                foreach (HumanBone bone in HumanBones.ParentFirst)
                {
                    if (rng.Next(2) == 0)
                    {
                        pose.WorldDeltas[bone] = TestMath.RandomRotation(rng, Math.PI);
                    }
                }
                sim.Apply(pose);
                PosedSkeleton s = HumanoidSkeleton.Pose(rig, pose);
                foreach (HumanBone bone in rig.Bones)
                {
                    TestMath.AssertClose(s.Position(bone), sim.Position(bone), 1e-9, bone.ToString());
                    TestMath.AssertSameRotation(s.Delta(bone), sim.Delta(bone), 1e-9, bone.ToString());
                }
                foreach ((HumanBone bone, Vec3 rest, Vec3 posed) in sim.Leaves())
                {
                    TestMath.AssertClose(s.Attached(bone, rest), posed, 1e-9, "leaf of " + bone);
                }
            }
        }

        [Test]
        public void TorsoPointsAreBonePositionsOrCarriedEstimates()
        {
            var rng = new Random(31);
            for (int i = 0; i < 100; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                var pose = new PoseResult { HipsPosition = TestMath.RandomUnit(rng) };
                foreach (HumanBone bone in HumanBones.ParentFirst)
                {
                    pose.WorldDeltas[bone] = TestMath.RandomRotation(rng, Math.PI);
                }
                PosedSkeleton s = HumanoidSkeleton.Pose(rig, pose);
                foreach (HumanBone bone in HumanBoneHierarchy.TorsoChain)
                {
                    Vec3 expected = rig.Has(bone)
                        ? s.Position(bone)
                        : s.Position(rig.Parent(bone).Value)
                            + s.Delta(rig.Parent(bone).Value).Rotate(rig.TorsoRest(bone) - rig.Rest(rig.Parent(bone).Value));
                    TestMath.AssertClose(s.TorsoPoint(bone), expected, 1e-12, bone.ToString());
                }
            }
        }

        [Test]
        public void TorsoPointRejectsLimbBones()
        {
            PosedSkeleton s = HumanoidSkeleton.Pose(HumanoidRig.Measure(SyntheticRigs.Generic(1.7)), new PoseResult());
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TorsoPoint(HumanBone.LeftHand));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TorsoPoint(HumanBone.RightToes));
        }

        [TestCaseSource(typeof(TestRigs), nameof(TestRigs.Variants))]
        public void RestJointsSitOnTheRestSkeleton(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var pose = new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) };
            Dictionary<string, Vec3> joints = HumanoidJoints.FromSkeleton(HumanoidSkeleton.Pose(rig, pose));
            Assert.That(joints.Keys, Is.EquivalentTo(TestPoses.AllJointNames()));
            Assert.That(joints[JointNames.Pelvis], Is.EqualTo(rig.Rest(HumanBone.Hips)));
            TestMath.AssertClose(joints[JointNames.Waist], rig.Rest(HumanBone.Spine), Eps, "waist");
            TestMath.AssertClose(joints[JointNames.Chest], rig.TorsoRest(HumanBone.Chest), Eps, "chest");
            TestMath.AssertClose(joints[JointNames.NeckBase], rig.NeckBase, Eps, "neck base");
            TestMath.AssertClose(joints[JointNames.NeckTop], rig.Rest(HumanBone.Head), Eps, "neck top");
            Vec3 head = rig.Rest(HumanBone.Head);
            TestMath.AssertClose(joints[JointNames.HeadTop], new Vec3(head.X, head.Y, rig.HeadTopHeight), Eps, "head top");
            TestMath.AssertClose(joints[JointNames.Head], new Vec3(head.X, head.Y, (head.Z + rig.HeadTopHeight) / 2), Eps, "head");
            foreach (int side in TestMath.Sides)
            {
                (string joint, HumanBone bone)[] bones =
                {
                    (JointNames.Shoulder(side), HumanBoneHierarchy.UpperArm(side)),
                    (JointNames.Elbow(side), HumanBoneHierarchy.LowerArm(side)),
                    (JointNames.Hand(side), HumanBoneHierarchy.Hand(side)),
                    (JointNames.Hip(side), HumanBoneHierarchy.UpperLeg(side)),
                    (JointNames.Knee(side), HumanBoneHierarchy.LowerLeg(side)),
                    (JointNames.Ankle(side), HumanBoneHierarchy.Foot(side)),
                };
                foreach ((string joint, HumanBone bone) in bones)
                {
                    TestMath.AssertClose(joints[joint], rig.Rest(bone), Eps, joint);
                }
                TestMath.AssertClose(joints[JointNames.Ball(side)], rig.Ball(side), Eps, "ball");
                TestMath.AssertClose(joints[JointNames.Toe(side)], rig.ToeTip(side), Eps, "toe");
                Assert.That(joints[JointNames.Toe(side)].Z, Is.EqualTo(0).Within(Eps), "toes on the ground");
            }
            Assert.Throws<ArgumentNullException>(() => HumanoidJoints.FromSkeleton(null));
        }

        [Test]
        public void JointsMoveWithTheirBones()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            var pose = new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) };
            Quat nod = Quat.FromAxisAngle(Vec3.UnitX, 0.7);
            Quat toes = Quat.FromAxisAngle(Vec3.UnitX, -0.5);
            pose.WorldDeltas[HumanBone.Head] = nod;
            pose.WorldDeltas[HumanBone.RightToes] = toes;
            Dictionary<string, Vec3> joints = HumanoidJoints.FromSkeleton(HumanoidSkeleton.Pose(rig, pose));
            Vec3 head = rig.Rest(HumanBone.Head);
            TestMath.AssertClose(joints[JointNames.HeadTop], head + nod.Rotate(new Vec3(0, 0, rig.HeadTopHeight - head.Z)), Eps,
                "head top nods");
            Vec3 ball = rig.Ball(1);
            TestMath.AssertClose(joints[JointNames.Toe(1)], ball + toes.Rotate(rig.ToeTip(1) - ball), Eps, "toe tip bends");
            TestMath.AssertClose(joints[JointNames.Toe(-1)], rig.ToeTip(-1), Eps, "other toe tip");
        }
    }
}
