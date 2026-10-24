using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MilkJugg24.FactorioAvatarExporter.Core.Tests.Humanoid;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Dragon
{
    public class DragonPoseResultTests
    {
        private const double Eps = 1e-12;

        private static IEnumerable<string> AllJointNames()
        {
            yield return JointNames.Pelvis;
            yield return JointNames.Waist;
            yield return JointNames.Chest;
            yield return JointNames.NeckBase;
            yield return JointNames.NeckTop;
            yield return JointNames.Head;
            yield return JointNames.HeadTop;
            foreach (int side in new[] { 1, -1 })
            {
                yield return JointNames.Shoulder(side);
                yield return JointNames.Elbow(side);
                yield return JointNames.Hand(side);
                yield return JointNames.Hip(side);
                yield return JointNames.Knee(side);
                yield return JointNames.Ankle(side);
                yield return JointNames.Ball(side);
                yield return JointNames.Toe(side);
            }
        }

        [Test]
        public void JointsAreThePosePoints()
        {
            DragonPose p = DragonAnimations.Run(BodyShape.Female, 0.3);
            PoseResult r = p.ToPoseResult();
            Assert.That(r.Joints.Keys, Is.EquivalentTo(AllJointNames()));
            Assert.That(r.Joints[JointNames.Pelvis], Is.EqualTo(p.Pelvis));
            Assert.That(r.Joints[JointNames.Waist], Is.EqualTo(p.Waist));
            Assert.That(r.Joints[JointNames.Chest], Is.EqualTo(p.Chest));
            Assert.That(r.Joints[JointNames.NeckBase], Is.EqualTo(p.NeckBase));
            Assert.That(r.Joints[JointNames.NeckTop], Is.EqualTo(p.NeckTop));
            Assert.That(r.Joints[JointNames.Head], Is.EqualTo(p.Head));
            foreach (int side in DragonModel.Sides)
            {
                Assert.That(r.Joints[JointNames.Shoulder(side)], Is.EqualTo(p.Arms[side].Shoulder));
                Assert.That(r.Joints[JointNames.Elbow(side)], Is.EqualTo(p.Arms[side].Elbow));
                Assert.That(r.Joints[JointNames.Hand(side)], Is.EqualTo(p.Arms[side].Hand));
                Assert.That(r.Joints[JointNames.Hip(side)], Is.EqualTo(p.Legs[side].Hip));
                Assert.That(r.Joints[JointNames.Knee(side)], Is.EqualTo(p.Legs[side].Knee));
                Assert.That(r.Joints[JointNames.Ankle(side)], Is.EqualTo(p.Legs[side].Ankle));
                Assert.That(r.Joints[JointNames.Ball(side)], Is.EqualTo(p.Legs[side].Ball));
                Assert.That(r.Joints[JointNames.Toe(side)], Is.EqualTo(p.Legs[side].Toe));
            }
            Assert.That(r.Joints[JointNames.Hand(1)].X, Is.GreaterThan(0), ".R is the right side (+x)");
        }

        [Test]
        public void HeadTopIsTheTopOfTheScaledSkull()
        {
            DragonPose p = DragonAnimations.Idle(BodyShape.Male, 0.4);
            Vec3 top = p.ToPoseResult().Joints[JointNames.HeadTop];
            Assert.That(top.ApproximatelyEquals(p.Head + p.HeadUp * (0.094 * 1.28), Eps), top.ToString());
            Assert.That(top.Z, Is.GreaterThan(1.45).And.LessThan(1.55), "the dragon is about 1.5 tiles tall");
        }

        [Test]
        public void HipsPelvisNoBoneRotationsAndNotGrounded()
        {
            DragonPose p = DragonAnimations.Hover(BodyShape.Male, 0.2);
            PoseResult r = p.ToPoseResult();
            Assert.That(r.HipsPosition, Is.EqualTo(p.Pelvis));
            Assert.That(r.WorldDeltas, Is.Empty);
            Assert.That(r.GroundToLowestPoint, Is.False);
            Assert.That(r.Prop, Is.Null);
            Assert.That(r.LeftGrip, Is.EqualTo(0));
            Assert.That(r.RightGrip, Is.EqualTo(0));
        }

        [Test]
        public void GunPropPointsUpAwayFromTheMagazine()
        {
            var rng = new Random(31);
            for (int i = 0; i < 50; i++)
            {
                DragonPose p = DragonAnimations.Run(BodyShape.All[i % 2], rng.NextDouble(), true, rng.NextDouble() - 0.5);
                var gun = (DragonGun)p.Prop;
                PoseResult r = p.ToPoseResult();
                Assert.That(r.Prop.Kind, Is.EqualTo(PropKind.Gun));
                Assert.That(r.Prop.Base, Is.EqualTo(gun.Stock));
                Assert.That(r.Prop.Tip, Is.EqualTo(gun.Muzzle));
                Assert.That(r.Prop.Up.Length, Is.EqualTo(1).Within(Eps));
                Assert.That(Vec3.Dot(r.Prop.Up, gun.Aim), Is.EqualTo(0).Within(Eps));
                Assert.That(r.Prop.Up.Z, Is.GreaterThan(0));
                // add_prop's down vector, flipped
                Vec3 down = Vec3.Cross(Vec3.Cross(gun.Aim, Vec3.UnitZ), gun.Aim).Normalized;
                down = Vec3.Dot(down, Vec3.UnitZ) > 0 ? -down : down;
                Assert.That(r.Prop.Up, Is.EqualTo(-down));
                Assert.That(r.LeftGrip, Is.EqualTo(1));
                Assert.That(r.RightGrip, Is.EqualTo(1));
            }
        }

        [Test]
        public void GunUpOfAPitchedAimStaysPerpendicular()
        {
            PropPose prop = new DragonGun(Vec3.Zero, new Vec3(0, 0.5, -0.1), new Vec3(0, 0.98, -0.2).Normalized).ToPropPose();
            Assert.That(Vec3.Dot(prop.Up, new Vec3(0, 0.98, -0.2)), Is.EqualTo(0).Within(Eps));
            Assert.That(prop.Up.Y, Is.GreaterThan(0), "tilts forward with the aim pointing down");
            Assert.That(prop.Up.Z, Is.GreaterThan(0));
        }

        [Test]
        public void PickPropUpIsAlongTheHead()
        {
            for (int frame = 0; frame < DragonAnimations.MineFrames; frame++)
            {
                DragonPose p = DragonAnimations.Mine(BodyShape.Female, frame / 12.0);
                var pick = (DragonPick)p.Prop;
                PoseResult r = p.ToPoseResult();
                Assert.That(r.Prop.Kind, Is.EqualTo(PropKind.Pickaxe));
                Assert.That(r.Prop.Base, Is.EqualTo(pick.Base));
                Assert.That(r.Prop.Tip, Is.EqualTo(pick.Top));
                Assert.That(r.Prop.Up, Is.EqualTo(pick.HeadDirection));
                Assert.That(r.LeftGrip, Is.EqualTo(1));
                Assert.That(r.RightGrip, Is.EqualTo(1));
            }
        }

        [Test]
        public void ConvertingAnIncompletePoseThrows()
        {
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7));
            var e = Assert.Throws<InvalidOperationException>(() => p.ToPoseResult());
            Assert.That(e.Message, Does.Contain("right arm"));
            DragonAnimations.RelaxedArms(p, BodyShape.Male);
            DragonModel.SetLeg(p, BodyShape.Male, 1, new Vec3(0.1, 0.07, 0.035));
            e = Assert.Throws<InvalidOperationException>(() => p.ToPoseResult());
            Assert.That(e.Message, Does.Contain("left leg"));
            DragonAnimations.StandingLegs(p, BodyShape.Male);
            Assert.DoesNotThrow(() => p.ToPoseResult());
        }

        [Test]
        public void PoseSourceServesTheDragon()
        {
            var source = new DragonPoseSource(BodyShape.Female);
            Assert.That(source.Body, Is.SameAs(BodyShape.Female));
            PoseResult r = source.Pose(AnimationSpec.FlyGun, 0.25, 0.5);
            PoseResult expected = DragonPoses.Pose(AnimationSpec.FlyGun, BodyShape.Female, 0.25, 0.5).ToPoseResult();
            Assert.That(r.Joints, Is.EquivalentTo(expected.Joints));
            Assert.That(r.Prop.Tip, Is.EqualTo(expected.Prop.Tip));
            Assert.That(source.Timing.DistancePerFrame, Is.EqualTo(0.238));
            Assert.That(source.Timing.MineImpactFrame, Is.EqualTo(DragonAnimations.MineImpactFrame));
            Assert.That(source.Timing.LeftFootFrames, Is.EqualTo(new[] { 1 }));
            Assert.That(source.Timing.RightFootFrames, Is.EqualTo(new[] { 7 }));
            Assert.Throws<ArgumentException>(() => source.Pose("walk", 0, 0));
            Assert.Throws<ArgumentNullException>(() => new DragonPoseSource(null));
        }

        [Test]
        public void TheCorpseLiesDownAsGenerateRendersIt()
        {
            PoseResult standing = DragonPoses.Pose(AnimationSpec.Corpse, BodyShape.Male, 0, 0).ToPoseResult();
            PoseResult corpse = new DragonPoseSource(BodyShape.Male).Pose(AnimationSpec.Corpse, 0, 0);
            double a = MathUtil.Tau * 0.30;
            foreach (KeyValuePair<string, Vec3> joint in standing.Joints)
            {
                // generate.py: rot(v, Y, -pi/2) + (0.75, 0, 0.2), then turned clockwise to orientation 0.30
                Vec3 v = joint.Value;
                var lying = new Vec3(-v.Z + 0.75, v.Y, v.X + 0.2);
                var expected = new Vec3(lying.X * Math.Cos(a) + lying.Y * Math.Sin(a),
                    -lying.X * Math.Sin(a) + lying.Y * Math.Cos(a), lying.Z);
                TestMath.AssertClose(corpse.Joints[joint.Key], expected, 1e-12, joint.Key);
            }
            double height = corpse.Joints.Values.Max(p => p.Z) - corpse.Joints.Values.Min(p => p.Z);
            Assert.That(height, Is.LessThan(0.6), "lying down");
            Assert.That(corpse.GroundToLowestPoint, Is.False);
        }

        [Test]
        public void EveryAnimationConverts()
        {
            var source = new DragonPoseSource(BodyShape.Male);
            foreach (AnimationInfo info in AnimationSpec.All)
            {
                PoseResult r = source.Pose(info.Name, AnimationSpec.FramePhase(info.Name, 0), 0);
                Assert.That(r.Joints.Count, Is.EqualTo(AllJointNames().Count()), info.Name);
                Assert.That(r.Joints.Values.All(v => !double.IsNaN(v.X + v.Y + v.Z)), info.Name);
            }
        }
    }
}
