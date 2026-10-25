using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Dragon
{
    public class BodyShapeTests
    {
        [Test]
        public void ByNameFindsBothBodies()
        {
            Assert.That(BodyShape.ByName("male"), Is.SameAs(BodyShape.Male));
            Assert.That(BodyShape.ByName("female"), Is.SameAs(BodyShape.Female));
            Assert.That(BodyShape.All, Is.EqualTo(new[] { BodyShape.Male, BodyShape.Female }));
        }

        [Test]
        public void ByNameRejectsUnknownNames()
        {
            var e = Assert.Throws<ArgumentException>(() => BodyShape.ByName("Male"));
            Assert.That(e.Message, Does.Contain("Male"));
            Assert.Throws<ArgumentException>(() => BodyShape.ByName(null));
        }

        [Test]
        public void PosingMeasurements()
        {
            Assert.That(BodyShape.Male.ShoulderWidth, Is.EqualTo(0.212));
            Assert.That(BodyShape.Male.HipWidth, Is.EqualTo(0.100));
            Assert.That(BodyShape.Female.ShoulderWidth, Is.EqualTo(0.165));
            Assert.That(BodyShape.Female.HipWidth, Is.EqualTo(0.112));
            Assert.That(BodyShape.Female.Bust, Is.True);
            Assert.That(BodyShape.Male.ToString(), Is.EqualTo("male"));
        }

        [Test]
        public void ConstructorValidates()
        {
            var r = new Vec3(0.1, 0.1, 0.1);
            Assert.Throws<ArgumentException>(() => new BodyShape("", 0.2, r, r, r, 0.1, 0.05, 0.05, 0.1, 0.07, false, "male", 0.02));
            Assert.Throws<ArgumentException>(() => new BodyShape("x", 0.2, r, r, r, 0.1, 0.05, 0.05, 0.1, 0.07, false, null, 0.02));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyShape("x", -0.2, r, r, r, 0.1, 0.05, 0.05, 0.1, 0.07, false, "male", 0.02));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyShape("x", 0.2, new Vec3(0.1, 0, 0.1), r, r, 0.1, 0.05, 0.05, 0.1, 0.07, false, "male", 0.02));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyShape("x", 0.2, r, r, r, double.NaN, 0.05, 0.05, 0.1, 0.07, false, "male", 0.02));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyShape("x", 0.2, r, r, r, 0.1, 0.05, 0.05, 0.1, 0.07, false, "male", double.PositiveInfinity));
        }
    }

    public class DragonModelTests
    {
        private const double Eps = 1e-12;

        [Test]
        public void TorsoFrameWithoutAnglesIsTheModelAxes()
        {
            (Vec3 r, Vec3 f, Vec3 u) = DragonModel.TorsoFrame(0);
            Assert.That(r, Is.EqualTo(Vec3.UnitX));
            Assert.That(f, Is.EqualTo(Vec3.UnitY));
            Assert.That(u, Is.EqualTo(Vec3.UnitZ));
        }

        [Test]
        public void PositiveLeanTiltsTheTorsoBackwardsLikeModelPy()
        {
            (Vec3 r, Vec3 f, Vec3 u) = DragonModel.TorsoFrame(0.3);
            Assert.That(u.ApproximatelyEquals(new Vec3(0, -Math.Sin(0.3), Math.Cos(0.3)), Eps), u.ToString());
            Assert.That(f.ApproximatelyEquals(new Vec3(0, Math.Cos(0.3), Math.Sin(0.3)), Eps), f.ToString());
            Assert.That(r, Is.EqualTo(Vec3.UnitX));
        }

        [Test]
        public void TwistTurnsCounterclockwiseAndRollTiltsRight()
        {
            (Vec3 r, Vec3 f, _) = DragonModel.TorsoFrame(0, Math.PI / 2);
            Assert.That(f.ApproximatelyEquals(-Vec3.UnitX, Eps), f.ToString());
            Assert.That(r.ApproximatelyEquals(Vec3.UnitY, Eps), r.ToString());
            (_, _, Vec3 u) = DragonModel.TorsoFrame(0, 0, 0.3);
            Assert.That(u.ApproximatelyEquals(new Vec3(Math.Sin(0.3), 0, Math.Cos(0.3)), Eps), u.ToString());
        }

        [Test]
        public void TinyTwistsAndRollsAreSkipped()
        {
            Assert.That(DragonModel.TorsoFrame(0.2, 1e-10, -1e-10), Is.EqualTo(DragonModel.TorsoFrame(0.2)));
            Assert.That(DragonModel.TorsoFrame(0.2, 2e-9), Is.Not.EqualTo(DragonModel.TorsoFrame(0.2)));
        }

        [Test]
        public void TorsoFrameIsOrthonormalAndRightHanded()
        {
            var rng = new Random(7);
            for (int i = 0; i < 500; i++)
            {
                (Vec3 r, Vec3 f, Vec3 u) = DragonModel.TorsoFrame(Angle(rng), Angle(rng), Angle(rng));
                Assert.That(r.Length, Is.EqualTo(1).Within(Eps));
                Assert.That(f.Length, Is.EqualTo(1).Within(Eps));
                Assert.That(u.Length, Is.EqualTo(1).Within(Eps));
                Assert.That(Vec3.Dot(r, f), Is.EqualTo(0).Within(Eps));
                Assert.That(Vec3.Dot(f, u), Is.EqualTo(0).Within(Eps));
                Assert.That(Vec3.Cross(r, f).ApproximatelyEquals(u, Eps));
            }
        }

        [Test]
        public void BuildPosePlacesTheSpineAlongTheTorsoFrame()
        {
            var rng = new Random(11);
            for (int i = 0; i < 200; i++)
            {
                var pelvis = new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, 0.5 + rng.NextDouble() * 0.5);
                double lean = Angle(rng), twist = Angle(rng), roll = Angle(rng), hipTwist = Angle(rng);
                double breath = rng.NextDouble() * 2 - 1, pitch = Angle(rng), yaw = Angle(rng), headRoll = Angle(rng);
                DragonPose p = DragonModel.BuildPose(pelvis, lean, twist, roll, hipTwist, breath, pitch, yaw, headRoll);
                (Vec3 tr, Vec3 tf, Vec3 tu) = DragonModel.TorsoFrame(lean, twist, roll);
                (Vec3 hr, Vec3 hf, Vec3 hu) = DragonModel.TorsoFrame(lean * 0.35 + pitch, twist * 0.5 + yaw, headRoll);
                (Vec3 pr, Vec3 pf, Vec3 pu) = DragonModel.TorsoFrame(lean * 0.5, hipTwist, roll * 0.5);
                Assert.That((p.TorsoRight, p.TorsoForward, p.TorsoUp), Is.EqualTo((tr, tf, tu)));
                Assert.That((p.HeadRight, p.HeadForward, p.HeadUp), Is.EqualTo((hr, hf, hu)));
                Assert.That((p.HipRight, p.HipForward, p.HipUp), Is.EqualTo((pr, pf, pu)));
                Assert.That(p.Pelvis, Is.EqualTo(pelvis));
                Assert.That(p.Breath, Is.EqualTo(breath));
                Assert.That(p.Waist.ApproximatelyEquals(pelvis + tu * 0.165 + tf * 0.008, Eps));
                Assert.That(p.Chest.ApproximatelyEquals(pelvis + tu * (0.345 + breath * 0.006) + tf * 0.012, Eps));
                Assert.That(Vec3.Distance(p.NeckBase, p.NeckTop), Is.EqualTo(0.13).Within(Eps));
                Assert.That(p.Head.ApproximatelyEquals(p.NeckTop + hu * 0.075 + hf * 0.035, Eps));
                Assert.That(p.Arms, Is.Empty);
                Assert.That(p.Legs, Is.Empty);
                Assert.That(p.Prop, Is.Null);
            }
        }

        [Test]
        public void ShouldersAndHipsAreMirrorImages()
        {
            foreach (BodyShape body in BodyShape.All)
            {
                DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7), lean: 0.2, breath: 0.5);
                Vec3 r = DragonModel.Shoulder(p, body, 1);
                Vec3 l = DragonModel.Shoulder(p, body, -1);
                Assert.That(r.ApproximatelyEquals(new Vec3(-l.X, l.Y, l.Z), Eps));
                Assert.That(Vec3.Distance(r, l), Is.EqualTo(2 * body.ShoulderWidth).Within(Eps));
                Vec3 hr = DragonModel.HipJoint(p, body, 1);
                Vec3 hl = DragonModel.HipJoint(p, body, -1);
                Assert.That(hr.ApproximatelyEquals(new Vec3(-hl.X, hl.Y, hl.Z), Eps));
                Assert.That(Vec3.Distance(hr, hl), Is.EqualTo(2 * body.HipWidth).Within(Eps));
            }
        }

        [Test]
        public void ArmsReachReachableTargetsAndKeepTheirLengths()
        {
            const double l1 = DragonModel.UpperArmLength, l2 = DragonModel.ForearmLength;
            Assert.That((l1, l2), Is.EqualTo((0.235, 0.215)));
            var rng = new Random(3);
            for (int i = 0; i < 500; i++)
            {
                DragonPose p = RandomPose(rng);
                BodyShape body = BodyShape.All[rng.Next(2)];
                int side = rng.Next(2) == 0 ? 1 : -1;
                Vec3 s = DragonModel.Shoulder(p, body, side);
                double reach = Math.Abs(l1 - l2) + 0.002 + rng.NextDouble() * (l1 + l2 - Math.Abs(l1 - l2) - 0.004);
                Vec3 target = s + RandomUnit(rng) * reach;
                Vec3 pole = RandomUnit(rng);
                DragonModel.SetArm(p, body, side, target, pole);
                DragonArm arm = p.Arms[side];
                Assert.That(arm.Shoulder, Is.EqualTo(s));
                Assert.That(arm.Hand.ApproximatelyEquals(target, Eps), $"{arm.Hand} vs {target}");
                Assert.That(Vec3.Distance(arm.Shoulder, arm.Elbow), Is.EqualTo(l1).Within(Eps));
                Assert.That(Vec3.Distance(arm.Elbow, arm.Hand), Is.EqualTo(l2).Within(Eps));
                Assert.That(arm.HandDirection.ApproximatelyEquals((arm.Hand - arm.Elbow).Normalized, Eps));
                // the elbow bends towards the pole
                Vec3 axis = (target - s).Normalized;
                Vec3 bend = arm.Elbow - (s + axis * Vec3.Dot(arm.Elbow - s, axis));
                Vec3 polePerp = pole - axis * Vec3.Dot(pole, axis);
                if (polePerp.Length > 1e-3)
                {
                    Assert.That(Vec3.Dot(bend, polePerp), Is.GreaterThan(0));
                }
            }
        }

        [Test]
        public void ArmsStopShortOfUnreachableTargets()
        {
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7));
            Vec3 s = DragonModel.Shoulder(p, BodyShape.Male, 1);
            DragonModel.SetArm(p, BodyShape.Male, 1, s + new Vec3(0, 2, 0), -Vec3.UnitZ);
            DragonArm arm = p.Arms[1];
            Assert.That(arm.Hand.ApproximatelyEquals(s + new Vec3(0, 0.449, 0), Eps), arm.Hand.ToString());
            Assert.That(Vec3.Distance(arm.Shoulder, arm.Elbow), Is.EqualTo(0.235).Within(Eps));
            Assert.That(Vec3.Distance(arm.Elbow, arm.Hand), Is.EqualTo(0.215).Within(Eps));
        }

        [Test]
        public void SetArmKeepsAGivenHandDirection()
        {
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7));
            var dir = new Vec3(0, 0.6, 0.8);
            DragonModel.SetArm(p, BodyShape.Female, -1, new Vec3(-0.2, 0.2, 0.9), -Vec3.UnitY, dir);
            Assert.That(p.Arms[-1].HandDirection, Is.EqualTo(dir));
        }

        [Test]
        public void LegsReachReachableAnklesAndKeepTheirLengths()
        {
            const double l1 = DragonModel.ThighLength, l2 = DragonModel.ShinLength;
            Assert.That((l1, l2), Is.EqualTo((0.300, 0.300)));
            var rng = new Random(5);
            for (int i = 0; i < 500; i++)
            {
                DragonPose p = RandomPose(rng);
                BodyShape body = BodyShape.All[rng.Next(2)];
                int side = rng.Next(2) == 0 ? 1 : -1;
                double heelLift = rng.NextDouble() * 0.2, heelBack = rng.NextDouble() * 0.15, toePitch = Angle(rng);
                Vec3 fwd = p.HipForward.Horizontal.Normalized;
                Vec3 hip = DragonModel.HipJoint(p, body, side);
                Vec3 ankleTarget = hip + RandomUnit(rng) * (0.01 + rng.NextDouble() * (l1 + l2 - 0.02));
                Vec3 ball = ankleTarget + fwd * heelBack - Vec3.UnitZ * heelLift;
                DragonModel.SetLeg(p, body, side, ball, heelLift, heelBack, toePitch);
                DragonLeg leg = p.Legs[side];
                Assert.That(leg.Hip, Is.EqualTo(hip));
                Assert.That(leg.Ball, Is.EqualTo(ball));
                Assert.That(leg.Ankle.ApproximatelyEquals(ankleTarget, 1e-11), $"{leg.Ankle} vs {ankleTarget}");
                Assert.That(Vec3.Distance(leg.Hip, leg.Knee), Is.EqualTo(l1).Within(Eps));
                Assert.That(Vec3.Distance(leg.Knee, leg.Ankle), Is.EqualTo(l2).Within(Eps));
                Vec3 toeDir = fwd * Math.Cos(toePitch) - Vec3.UnitZ * Math.Sin(toePitch);
                Assert.That(leg.Toe.ApproximatelyEquals(ball + toeDir * 0.085 - Vec3.UnitZ * 0.012, Eps));
            }
        }

        [Test]
        public void KneesBendForwardUnlessAPoleIsGiven()
        {
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.62));
            DragonModel.SetLeg(p, BodyShape.Male, 1, new Vec3(0.1, 0.07, 0.035));
            Vec3 hip = p.Legs[1].Hip, knee = p.Legs[1].Knee, ankle = p.Legs[1].Ankle;
            double lineY = hip.Y + (ankle.Y - hip.Y) * (hip.Z - knee.Z) / (hip.Z - ankle.Z);
            Assert.That(knee.Y, Is.GreaterThan(lineY), "default pole: knee in front of the hip-ankle line");
            DragonModel.SetLeg(p, BodyShape.Male, 1, new Vec3(0.1, 0.07, 0.035), pole: -Vec3.UnitY);
            Assert.That(p.Legs[1].Knee.Y, Is.LessThan(lineY), "backward pole: knee behind it");
        }

        [Test]
        public void HoldGunAimsAlongTheHorizontalTorsoForward()
        {
            var rng = new Random(13);
            for (int i = 0; i < 100; i++)
            {
                DragonPose p = RandomPose(rng);
                DragonModel.HoldGun(p, BodyShape.Male);
                var gun = (DragonGun)p.Prop;
                Assert.That(gun.Kind, Is.EqualTo(PropKind.Gun));
                Vec3 aim = p.TorsoForward.Horizontal.Normalized;
                Assert.That(gun.Aim.ApproximatelyEquals(aim, Eps));
                Assert.That((gun.Muzzle - gun.Stock).ApproximatelyEquals(aim * 0.52, Eps));
                Vec3 side = Vec3.Cross(aim, Vec3.UnitZ);
                Assert.That(gun.Stock.ApproximatelyEquals(p.Chest + side * 0.055 + aim * 0.075 - Vec3.UnitZ * 0.075, Eps));
                Assert.That(p.Arms[1].HandDirection, Is.EqualTo(gun.Aim));
                Assert.That(p.Arms[-1].HandDirection, Is.EqualTo(gun.Aim));
            }
        }

        [Test]
        public void TheLeftHandFallsShortOfItsGunGripLikeModelPy()
        {
            foreach (BodyShape body in BodyShape.All)
            {
                DragonPose p = DragonAnimations.Idle(body, 0.0, gun: true);
                var gun = (DragonGun)p.Prop;
                Assert.That(p.Arms[1].Hand.ApproximatelyEquals(gun.Stock + gun.Aim * 0.13 - Vec3.UnitZ * 0.035, Eps));
                // the front grip is out of reach, so the arm stretches towards it (model.py does the same)
                Vec3 grip = gun.Stock + gun.Aim * 0.31 - Vec3.UnitZ * 0.02;
                Vec3 shoulder = p.Arms[-1].Shoulder;
                Assert.That(Vec3.Distance(shoulder, grip), Is.GreaterThan(0.449));
                Vec3 stretched = shoulder + (grip - shoulder).Normalized * 0.449;
                Assert.That(p.Arms[-1].Hand.ApproximatelyEquals(stretched, Eps), body.Name);
            }
        }

        [Test]
        public void PositiveAimPitchAimsDown()
        {
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7));
            DragonModel.HoldGun(p, BodyShape.Male, aimPitch: 0.3);
            Assert.That(((DragonGun)p.Prop).Aim.ApproximatelyEquals(new Vec3(0, Math.Cos(0.3), -Math.Sin(0.3)), Eps));
        }

        [Test]
        public void HoldPickPutsTheHandleAtAlpha()
        {
            var rng = new Random(17);
            for (int i = 0; i < 100; i++)
            {
                DragonPose p = RandomPose(rng);
                double alpha = (rng.NextDouble() - 0.3) * 3;
                DragonModel.HoldPick(p, BodyShape.Female, alpha);
                var pick = (DragonPick)p.Prop;
                Assert.That(pick.Kind, Is.EqualTo(PropKind.Pickaxe));
                Vec3 fwd = p.TorsoForward.Horizontal.Normalized;
                Vec3 d = pick.HandleDirection, e = pick.HeadDirection;
                Assert.That(d.Length, Is.EqualTo(1).Within(Eps));
                Assert.That(e.Length, Is.EqualTo(1).Within(Eps));
                Assert.That(Vec3.Dot(d, e), Is.EqualTo(0).Within(Eps));
                Assert.That(Vec3.Dot(d, fwd), Is.EqualTo(Math.Cos(alpha)).Within(Eps));
                Assert.That(d.Z, Is.EqualTo(Math.Sin(alpha)).Within(Eps));
                Assert.That(e.Z, Is.EqualTo(Math.Cos(alpha)).Within(Eps));
                Assert.That((pick.Top - pick.Base).ApproximatelyEquals(d * (0.10 + DragonModel.PickLength), Eps));
                Assert.That(p.Arms[1].HandDirection, Is.EqualTo(d));
                Assert.That(p.Arms[-1].HandDirection, Is.EqualTo(d));
            }
        }

        [Test]
        public void SidesMustBePlusOrMinusOne()
        {
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7));
            BodyShape b = BodyShape.Male;
            Assert.Throws<ArgumentOutOfRangeException>(() => DragonModel.Shoulder(p, b, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => DragonModel.HipJoint(p, b, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => DragonModel.SetArm(p, b, -2, Vec3.Zero, Vec3.UnitY));
            Assert.Throws<ArgumentOutOfRangeException>(() => DragonModel.SetLeg(p, b, 0, Vec3.Zero));
            Assert.That(p.Arms, Is.Empty);
            Assert.That(p.Legs, Is.Empty);
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7));
            Assert.Throws<ArgumentNullException>(() => DragonModel.Shoulder(null, BodyShape.Male, 1));
            Assert.Throws<ArgumentNullException>(() => DragonModel.HipJoint(p, null, 1));
            Assert.Throws<ArgumentNullException>(() => DragonModel.HoldGun(null, BodyShape.Male));
            Assert.Throws<ArgumentNullException>(() => DragonModel.HoldPick(p, null, 0.5));
        }

        private static double Angle(Random rng) => (rng.NextDouble() - 0.5) * 1.6;

        private static Vec3 RandomUnit(Random rng)
        {
            while (true)
            {
                var v = new Vec3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
                if (v.Length > 0.1 && v.Length <= 1)
                {
                    return v.Normalized;
                }
            }
        }

        private static DragonPose RandomPose(Random rng) =>
            DragonModel.BuildPose(new Vec3(0, 0, 0.5 + rng.NextDouble() * 0.6), Angle(rng), Angle(rng), Angle(rng),
                Angle(rng), rng.NextDouble() * 2 - 1);
    }
}
