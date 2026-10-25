using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    public class LimbChainTests
    {
        private const double Eps = 1e-9;

        // a right arm in a T-pose: shoulder at the origin, along +x, the elbow bending about +z (forearm forward)
        private static LimbChain RightArm() =>
            new LimbChain(Vec3.Zero, new Vec3(0.3, 0, 0), new Vec3(0.55, 0, 0), Vec3.UnitZ);

        /// <summary>
        /// A random limb, slightly bent at rest (by at least <paramref name="minBend"/> radians about its bend
        /// axis; negative = hyperextended), with a bend axis perpendicular to both segments, give or take
        /// <paramref name="axisJitter"/> along the upper segment.
        /// </summary>
        private static LimbChain RandomChain(Random rng, out Vec3 restRoot, double minBend = -0.3, double axisJitter = 0.05)
        {
            restRoot = TestMath.RandomUnit(rng) * TestMath.Uniform(rng, 0, 2);
            Vec3 upper = TestMath.RandomUnit(rng);
            Vec3 axis = Vec3.Cross(upper, TestMath.RandomUnit(rng)).Normalized;
            Vec3 lower = MathUtil.Rot(upper, axis, TestMath.Uniform(rng, minBend, 0.3));
            Vec3 joint = restRoot + upper * TestMath.Uniform(rng, 0.1, 1);
            Vec3 end = joint + lower * TestMath.Uniform(rng, 0.1, 1);
            return new LimbChain(restRoot, joint, end, axis + upper * TestMath.Uniform(rng, -axisJitter, axisJitter));
        }

        [Test]
        public void SolveReachesTargetsInReachAndKeepsTheSegments()
        {
            var rng = new Random(41);
            for (int i = 0; i < 500; i++)
            {
                LimbChain chain = RandomChain(rng, out _);
                double l1 = chain.UpperLength, l2 = chain.LowerLength;
                double margin = 0.01 * (l1 + l2);
                Vec3 root = TestMath.RandomUnit(rng);
                Vec3 target = root + TestMath.RandomUnit(rng) * TestMath.Uniform(rng, Math.Abs(l1 - l2) + margin, l1 + l2 - margin);
                Vec3 pole = TestMath.RandomUnit(rng);
                LimbPose limb = chain.Solve(root, target, pole);

                TestMath.AssertClose(limb.End, target, Eps, "end");
                Assert.That(Vec3.Distance(root, limb.Joint), Is.EqualTo(l1).Within(Eps), "upper length");
                Assert.That(Vec3.Distance(limb.Joint, limb.End), Is.EqualTo(l2).Within(Eps), "lower length");
                // the bones' rotations carry the rest segments onto the posed ones
                TestMath.AssertClose(root + limb.Upper.Rotate(chain.RestUpper), limb.Joint, Eps, "upper bone");
                TestMath.AssertClose(limb.Joint + limb.Lower.Rotate(chain.RestLower), limb.End, Eps, "lower bone");
                TestMath.AssertUnit(limb.Upper, "upper");
                TestMath.AssertUnit(limb.Lower, "lower");
            }
        }

        [Test]
        public void JointsFlexAboutTheAnatomicalAxisOnly()
        {
            var rng = new Random(42);
            for (int i = 0; i < 500; i++)
            {
                LimbChain chain = RandomChain(rng, out _);
                Vec3 root = TestMath.RandomUnit(rng);
                Vec3 target = root + TestMath.RandomUnit(rng) * (chain.UpperLength + chain.LowerLength) * TestMath.Uniform(rng, 0, 1.5);
                LimbPose limb = chain.Solve(root, target, TestMath.RandomUnit(rng));

                Vec3 upper = limb.Joint - root, lower = limb.End - limb.Joint;
                Vec3 axis = Vec3.Cross(upper, lower).Normalized;
                TestMath.AssertClose(limb.BendAxis, axis, Eps, "bend axis of the actual joint");
                // both bones turn the rest bend axis into the posed one: the joint is a hinge that flexes
                // positively about its anatomical axis, never backwards or sideways
                TestMath.AssertClose(limb.Upper.Rotate(TestMath.Perpendicular(chain.RestBendAxis, chain.RestUpper)), axis, Eps,
                    "upper bone's hinge axis");
                TestMath.AssertClose(limb.Lower.Rotate(TestMath.Perpendicular(chain.RestBendAxis, chain.RestLower)), axis, Eps,
                    "lower bone's hinge axis");
            }
        }

        [Test]
        public void TheJointBendsTowardsThePole()
        {
            var rng = new Random(43);
            for (int i = 0; i < 500; i++)
            {
                LimbChain chain = RandomChain(rng, out _);
                Vec3 root = TestMath.RandomUnit(rng);
                Vec3 direction = TestMath.RandomUnit(rng);
                Vec3 target = root + direction * (chain.UpperLength + chain.LowerLength) * TestMath.Uniform(rng, 0.3, 0.95);
                Vec3 pole = TestMath.RandomUnit(rng);
                Vec3 polePerpendicular = pole - direction * Vec3.Dot(pole, direction);
                if (polePerpendicular.Length < 0.05)
                {
                    continue;
                }
                LimbPose limb = chain.Solve(root, target, pole);
                Vec3 offset = limb.Joint - root - direction * Vec3.Dot(limb.Joint - root, direction);
                Assert.That(Vec3.Dot(offset.Normalized, polePerpendicular.Normalized), Is.EqualTo(1).Within(Eps),
                    "the joint sticks out exactly towards the pole");
            }
        }

        [Test]
        public void ThePoseOfTheRestGeometryIsTheIdentity()
        {
            var rng = new Random(44);
            for (int i = 0; i < 200; i++)
            {
                // (posing a hyperextended rest limb would flex it the anatomical way instead, and a bend axis
                // not perpendicular to the lower segment would turn that bone about itself)
                LimbChain chain = RandomChain(rng, out Vec3 root, 0.01, 0);
                Vec3 joint = root + chain.RestUpper;
                LimbPose limb = chain.Pose(root, joint, joint + chain.RestLower, TestMath.RandomUnit(rng));
                TestMath.AssertSameRotation(limb.Upper, Quat.Identity, Eps, "upper");
                TestMath.AssertSameRotation(limb.Lower, Quat.Identity, Eps, "lower");
            }
            // a straight rest limb needs the pole to say which way it would bend: the anatomical way gives the identity
            LimbPose straight = RightArm().Pose(Vec3.Zero, new Vec3(0.3, 0, 0), new Vec3(0.55, 0, 0), -Vec3.UnitY);
            TestMath.AssertSameRotation(straight.Upper, Quat.Identity, Eps, "straight upper");
            TestMath.AssertSameRotation(straight.Lower, Quat.Identity, Eps, "straight lower");
            TestMath.AssertClose(straight.BendAxis, Vec3.UnitZ, Eps, "straight axis");
        }

        [Test]
        public void ElbowsPointedForwardTwistTheArmInstead()
        {
            // the hand in front of the shoulder with the elbow forced forward: the forearm still flexes about
            // the elbow's own axis, so the upper arm has to roll over
            LimbChain arm = RightArm();
            LimbPose limb = arm.Solve(Vec3.Zero, new Vec3(0.2, 0.2, 0), Vec3.UnitY);
            Assert.That(limb.Joint.Y, Is.GreaterThan(0.2), "elbow forward");
            Vec3 localForearm = limb.Upper.Inverse.Rotate(limb.End - limb.Joint);
            Assert.That(localForearm.Z, Is.EqualTo(0).Within(Eps), "in the elbow's flexing plane");
            Assert.That(localForearm.Y, Is.GreaterThan(0), "flexed forward, not hyperextended");
            TestMath.AssertClose(limb.Upper.Rotate(Vec3.UnitZ), limb.BendAxis, Eps, "rolled axis");
            Assert.That(limb.BendAxis.Z, Is.LessThan(-0.99), "upside down");
        }

        [Test]
        public void PolesAlongTheLimbBendItTheRestWay()
        {
            LimbChain arm = RightArm();
            var down = new Vec3(0, 0, -0.5);
            foreach (Vec3 pole in new[] { -Vec3.UnitZ, Vec3.UnitZ, Vec3.Zero })
            {
                LimbPose limb = arm.Solve(Vec3.Zero, down, pole);
                // hanging down, the T-pose's bend (forearm forward, elbow back) carried along by the swing
                Assert.That(limb.Joint.Y, Is.LessThan(-0.01), "elbow back for pole " + pole);
                Assert.That(limb.Joint.X, Is.EqualTo(0).Within(Eps));
                TestMath.AssertClose(limb.End, down, Eps, "end");
                TestMath.AssertClose(limb.BendAxis, Vec3.UnitX, Eps, "axis swung down with the arm");
            }
        }

        [Test]
        public void TargetsOutOfReachAreClampedDeterministically()
        {
            LimbChain arm = RightArm();
            var far = new Vec3(2, 1, -1);
            LimbPose a = arm.Solve(Vec3.Zero, far, -Vec3.UnitY);
            LimbPose b = arm.Solve(Vec3.Zero, far, -Vec3.UnitY);
            Assert.That(a.End, Is.EqualTo(b.End));
            Assert.That(a.Joint, Is.EqualTo(b.Joint));
            Assert.That(a.Upper, Is.EqualTo(b.Upper));
            Assert.That(a.Lower, Is.EqualTo(b.Lower));
            TestMath.AssertClose(a.End, far.Normalized * (0.55 - 1e-3), Eps, "at full reach towards the target");
            Assert.That(Vec3.Distance(a.Joint, a.End), Is.EqualTo(0.25).Within(Eps));

            LimbPose near = arm.Solve(Vec3.Zero, new Vec3(0.01, 0, 0), -Vec3.UnitY);
            TestMath.AssertClose(near.End, new Vec3(0.05 + 1e-3, 0, 0), Eps, "folded as far as it goes");

            LimbPose atRoot = arm.Solve(Vec3.Zero, Vec3.Zero, -Vec3.UnitY);
            TestMath.AssertClose(atRoot.End, new Vec3(0.05 + 1e-3, 0, 0), Eps, "a target at the root lies along the rest arm");
            Assert.That(atRoot.Joint.Y, Is.LessThan(0));
        }

        [Test]
        public void StraightLimbsTakeTheirTwistFromThePole()
        {
            LimbChain arm = RightArm();
            LimbPose limb = arm.Pose(Vec3.Zero, new Vec3(0, 0, -0.3), new Vec3(0, 0, -0.55), Vec3.UnitX);
            // straight down, would bend with the elbow out to the side (+x): the bend axis is +y
            TestMath.AssertClose(limb.BendAxis, Vec3.Cross(Vec3.UnitX, -Vec3.UnitZ), Eps, "axis");
            TestMath.AssertClose(limb.Upper.Rotate(arm.RestUpper), new Vec3(0, 0, -0.3), Eps, "upper");
            TestMath.AssertClose(limb.Upper.Rotate(Vec3.UnitZ), limb.BendAxis, Eps, "twist");
        }

        [Test]
        public void ArmsAndLegsOfARig()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            foreach (int side in TestMath.Sides)
            {
                LimbChain arm = LimbChain.Arm(rig, side);
                Assert.That(arm.UpperLength, Is.EqualTo(rig.UpperArmLength(side)).Within(1e-12));
                Assert.That(arm.LowerLength, Is.EqualTo(rig.ForearmLength(side)).Within(1e-12));
                Assert.That(arm.RestBendAxis, Is.EqualTo(rig.ElbowBendAxis(side)));
                LimbChain leg = LimbChain.Leg(rig, side);
                Assert.That(leg.UpperLength, Is.EqualTo(rig.ThighLength(side)).Within(1e-12));
                Assert.That(leg.LowerLength, Is.EqualTo(rig.ShinLength(side)).Within(1e-12));
                Assert.That(leg.RestBendAxis, Is.EqualTo(rig.KneeBendAxis(side)));
            }
            Assert.Throws<ArgumentNullException>(() => LimbChain.Arm(null, 1));
            Assert.Throws<ArgumentNullException>(() => LimbChain.Leg(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => LimbChain.Leg(rig, 0));
        }

        [Test]
        public void RejectsBadArguments()
        {
            Assert.Throws<ArgumentException>(() => new LimbChain(Vec3.Zero, Vec3.Zero, Vec3.UnitX, Vec3.UnitZ));
            Assert.Throws<ArgumentException>(() => new LimbChain(Vec3.Zero, Vec3.UnitX, Vec3.Zero, Vec3.UnitZ));
            var e = Assert.Throws<ArgumentException>(() =>
                new LimbChain(Vec3.Zero, Vec3.UnitX, Vec3.UnitX * 2, new Vec3(1, 0, 0.05)));
            Assert.That(e.ParamName, Is.EqualTo("restBendAxis"));
            Assert.Throws<ArgumentException>(() => new LimbChain(new Vec3(double.NaN, 0, 0), Vec3.UnitX, Vec3.UnitX * 2, Vec3.UnitZ));
            LimbChain arm = RightArm();
            Assert.Throws<ArgumentException>(() => arm.Solve(Vec3.Zero, new Vec3(double.PositiveInfinity, 0, 0), Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => arm.Solve(Vec3.Zero, Vec3.UnitX, new Vec3(0, double.NaN, 0)));
            Assert.Throws<ArgumentException>(() => arm.Pose(Vec3.Zero, Vec3.Zero, Vec3.UnitX, Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => arm.Pose(Vec3.Zero, Vec3.UnitX, Vec3.UnitX, Vec3.UnitY));
        }
    }
}
