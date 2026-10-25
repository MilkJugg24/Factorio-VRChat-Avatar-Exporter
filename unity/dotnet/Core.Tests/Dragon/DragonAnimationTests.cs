using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Dragon
{
    public class DragonAnimationTests
    {
        private const double Eps = 1e-12;

        /// <summary>Every (animation, frame, move) the exporter renders, for both bodies.</summary>
        private static IEnumerable<(BodyShape body, string animation, double t, double move)> AllFrames()
        {
            foreach (BodyShape body in BodyShape.All)
            {
                foreach (AnimationInfo info in AnimationSpec.All)
                {
                    IEnumerable<double> moves = Enumerable.Range(0, info.DirectionCount)
                        .Select(row => AnimationSpec.Direction(info.Name, row).Move).Distinct();
                    foreach (double move in moves)
                    {
                        for (int frame = 0; frame < info.FrameCount; frame++)
                        {
                            yield return (body, info.Name, AnimationSpec.FramePhase(info.Name, frame), move);
                        }
                    }
                }
            }
        }

        [Test]
        public void EveryFrameHasBothArmsAndLegsAndTheRightProp()
        {
            foreach ((BodyShape body, string animation, double t, double move) in AllFrames())
            {
                DragonPose p = DragonPoses.Pose(animation, body, t, move);
                Assert.That(p.Arms.Keys, Is.EquivalentTo(new[] { 1, -1 }), animation);
                Assert.That(p.Legs.Keys, Is.EquivalentTo(new[] { 1, -1 }), animation);
                AnimationInfo info = AnimationSpec.Get(animation);
                PropKind? expected = info.Gun ? PropKind.Gun : animation == AnimationSpec.Mine ? PropKind.Pickaxe : (PropKind?)null;
                Assert.That(p.Prop?.Kind, Is.EqualTo(expected), animation);
            }
        }

        [Test]
        public void LimbsKeepTheirLengthsInEveryFrame()
        {
            foreach ((BodyShape body, string animation, double t, double move) in AllFrames())
            {
                DragonPose p = DragonPoses.Pose(animation, body, t, move);
                string where = $"{body}/{animation} t={t} move={move}";
                foreach (int side in DragonModel.Sides)
                {
                    DragonArm arm = p.Arms[side];
                    DragonLeg leg = p.Legs[side];
                    Assert.That(Vec3.Distance(arm.Shoulder, arm.Elbow), Is.EqualTo(0.235).Within(1e-9), where);
                    Assert.That(Vec3.Distance(arm.Elbow, arm.Hand), Is.EqualTo(0.215).Within(1e-9), where);
                    Assert.That(Vec3.Distance(leg.Hip, leg.Knee), Is.EqualTo(0.300).Within(1e-9), where);
                    Assert.That(Vec3.Distance(leg.Knee, leg.Ankle), Is.EqualTo(0.300).Within(1e-9), where);
                }
            }
        }

        [Test]
        public void PoseDispatchesLikeAnimsPy()
        {
            BodyShape b = BodyShape.Female;
            const double t = 0.3, move = 0.7;
            DragonPoseAssert.Same(DragonAnimations.Idle(b, t), DragonPoses.Pose("idle", b, t), 0, "idle");
            DragonPoseAssert.Same(DragonAnimations.Idle(b, t, true), DragonPoses.Pose("idle-gun", b, t), 0, "idle-gun");
            DragonPoseAssert.Same(DragonAnimations.Run(b, t), DragonPoses.Pose("run", b, t), 0, "run");
            DragonPoseAssert.Same(DragonAnimations.Run(b, t, true, move), DragonPoses.Pose("run-gun", b, t, move), 0, "run-gun");
            DragonPoseAssert.Same(DragonAnimations.Mine(b, t), DragonPoses.Pose("mine", b, t), 0, "mine");
            DragonPoseAssert.Same(DragonAnimations.Hover(b, t), DragonPoses.Pose("hover", b, t), 0, "hover");
            DragonPoseAssert.Same(DragonAnimations.Hover(b, t, true), DragonPoses.Pose("hover-gun", b, t), 0, "hover-gun");
            DragonPoseAssert.Same(DragonAnimations.Fly(b, t), DragonPoses.Pose("fly", b, t), 0, "fly");
            DragonPoseAssert.Same(DragonAnimations.Fly(b, t, true, move), DragonPoses.Pose("fly-gun", b, t, move), 0, "fly-gun");
            DragonPoseAssert.Same(DragonAnimations.TakeOff(b, t), DragonPoses.Pose("take-off", b, t), 0, "take-off");
            DragonPoseAssert.Same(DragonAnimations.CorpsePose(b), DragonPoses.Pose("corpse", b, t), 0, "corpse");
        }

        [Test]
        public void UnknownAnimationsThrow()
        {
            var e = Assert.Throws<ArgumentException>(() => DragonPoses.Pose("walk", BodyShape.Male, 0));
            Assert.That(e.ParamName, Is.EqualTo("animation"));
            Assert.That(e.Message, Does.Contain("walk"));
            e = Assert.Throws<ArgumentException>(() => DragonPoses.Pose("Idle", BodyShape.Male, 0, 1.0));
            Assert.That(e.ParamName, Is.EqualTo("animation"));
            Assert.Throws<ArgumentNullException>(() => DragonPoses.Pose(null, BodyShape.Male, 0));
            Assert.Throws<ArgumentNullException>(() => DragonPoses.Pose("idle", null, 0));
        }

        [Test]
        public void OnlyGunMoveAnimationsTakeAMove()
        {
            foreach (AnimationInfo info in AnimationSpec.All)
            {
                if (info.GunMoveRows)
                {
                    Assert.DoesNotThrow(() => DragonPoses.Pose(info.Name, BodyShape.Male, 0.25, -1.5), info.Name);
                }
                else
                {
                    var e = Assert.Throws<ArgumentException>(() => DragonPoses.Pose(info.Name, BodyShape.Male, 0.25, 0.5), info.Name);
                    Assert.That(e.ParamName, Is.EqualTo("move"));
                }
            }
        }

        [Test]
        public void TimeAndMoveMustBeFinite()
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => DragonPoses.Pose("run", BodyShape.Male, bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => DragonPoses.Pose("run-gun", BodyShape.Male, 0, bad));
            }
        }

        [Test]
        public void AnimationsRejectANullBody()
        {
            Assert.Throws<ArgumentNullException>(() => DragonAnimations.Idle(null, 0));
            Assert.Throws<ArgumentNullException>(() => DragonAnimations.Run(null, 0));
            Assert.Throws<ArgumentNullException>(() => DragonAnimations.Mine(null, 0));
            Assert.Throws<ArgumentNullException>(() => DragonAnimations.Hover(null, 0));
            Assert.Throws<ArgumentNullException>(() => DragonAnimations.Fly(null, 0));
            Assert.Throws<ArgumentNullException>(() => DragonAnimations.TakeOff(null, 0));
            Assert.Throws<ArgumentNullException>(() => DragonAnimations.CorpsePose(null));
        }

        [Test]
        public void LoopingAnimationsRepeatEveryCycle()
        {
            var rng = new Random(23);
            foreach (AnimationInfo info in AnimationSpec.All.Where(a => a.Loop && a.Name != AnimationSpec.Corpse))
            {
                for (int i = 0; i < 25; i++)
                {
                    double t = rng.NextDouble();
                    double move = info.GunMoveRows ? (rng.NextDouble() - 0.5) * 3 : 0;
                    BodyShape body = BodyShape.All[i % 2];
                    DragonPoseAssert.Same(DragonPoses.Pose(info.Name, body, t, move),
                        DragonPoses.Pose(info.Name, body, t + 1, move), 1e-9, $"{info.Name} t={t}");
                }
            }
        }

        [Test]
        public void MineHoldsFramesAndRoundsHalvesToEvenLikePython()
        {
            BodyShape b = BodyShape.Male;
            DragonPose Frame(int i) => DragonAnimations.Mine(b, i / 12.0);
            Assert.That(DragonAnimations.MineFrames, Is.EqualTo(12));
            Assert.That(DragonAnimations.MineLean.Count, Is.EqualTo(DragonAnimations.MineFrames));
            DragonPoseAssert.Same(Frame(2), DragonAnimations.Mine(b, 0.125), 0, "t*12 = 1.5 rounds to 2");
            DragonPoseAssert.Same(Frame(4), DragonAnimations.Mine(b, 0.375), 0, "t*12 = 4.5 rounds to 4");
            DragonPoseAssert.Same(Frame(8), DragonAnimations.Mine(b, 0.625), 0, "t*12 = 7.5 rounds to 8");
            DragonPoseAssert.Same(Frame(4), DragonAnimations.Mine(b, 4.4 / 12.0), 0, "held between frames");
            DragonPoseAssert.Same(Frame(11), DragonAnimations.Mine(b, -1 / 12.0), 0, "Python modulo of -1");
            DragonPoseAssert.Same(Frame(0), DragonAnimations.Mine(b, 1.0), 0, "wraps");
            Assert.That(Frame(5).Pelvis, Is.Not.EqualTo(Frame(4).Pelvis));
        }

        [Test]
        public void ThePickIsDownInTheImpactFrame()
        {
            int impact = DragonAnimations.MineImpactFrame - 1;
            Assert.That(DragonAnimations.MineAlpha[impact], Is.LessThan(0));
            Assert.That(DragonAnimations.MineAlpha[impact - 1], Is.GreaterThan(0));
        }

        [Test]
        public void TheBladeReachesTheGroundInFrontOfTheFeetAtTheBlow()
        {
            // the in-game check of 2026-10-22 saw the pick stop at hip height; frames 9 to 11 are the blow
            foreach (BodyShape body in BodyShape.All)
            {
                for (int frame = DragonAnimations.MineImpactFrame - 1; frame <= DragonAnimations.MineImpactFrame + 1; frame++)
                {
                    var pick = (DragonPick)DragonAnimations.Mine(body, frame / 12.0).Prop;
                    Vec3 d = pick.HandleDirection, e = pick.HeadDirection;
                    // the blade's point, as model.py's add_prop draws it
                    Vec3 point = pick.Top - d * 0.055 - e * 0.18;
                    Assert.That(point.Z, Is.InRange(-0.02, 0.08), $"{body.Name} frame {frame + 1}");
                    Assert.That(point.Y, Is.GreaterThan(0.45), $"{body.Name} frame {frame + 1}");
                }
            }
        }

        [Test]
        public void TakeOffEndsOnTheFirstHoverFrame()
        {
            int last = AnimationSpec.Get(AnimationSpec.TakeOff).FrameCount - 1;
            foreach (BodyShape body in BodyShape.All)
            {
                DragonPose end = DragonPoses.Pose(AnimationSpec.TakeOff, body, AnimationSpec.FramePhase(AnimationSpec.TakeOff, last));
                DragonPose hover = DragonPoses.Pose(AnimationSpec.Hover, body, AnimationSpec.FramePhase(AnimationSpec.Hover, 0));
                DragonPoseAssert.Same(hover, end, Eps, body.Name);
            }
        }

        [Test]
        public void TakeOffStartsStanding()
        {
            DragonPose p = DragonAnimations.TakeOff(BodyShape.Male, 0);
            foreach (int side in DragonModel.Sides)
            {
                Assert.That(p.Legs[side].Ball, Is.EqualTo(new Vec3(side * (0.100 + 0.018), 0.07, 0.035)));
            }
            Assert.That(p.Pelvis, Is.EqualTo(new Vec3(0, 0, 0.70)));
        }

        [Test]
        public void GunMoveTurnsTheHipsAndLegsButNotTheTorso()
        {
            var rng = new Random(29);
            for (int i = 0; i < 50; i++)
            {
                double t = rng.NextDouble();
                double move = (rng.NextDouble() - 0.5) * 3;
                foreach (string animation in new[] { AnimationSpec.RunGun, AnimationSpec.FlyGun })
                {
                    DragonPose straight = DragonPoses.Pose(animation, BodyShape.Female, t);
                    DragonPose turned = DragonPoses.Pose(animation, BodyShape.Female, t, move);
                    Assert.That(turned.TorsoForward, Is.EqualTo(straight.TorsoForward), animation);
                    Assert.That(turned.HeadForward, Is.EqualTo(straight.HeadForward), animation);
                    Assert.That(((DragonGun)turned.Prop).Aim, Is.EqualTo(((DragonGun)straight.Prop).Aim), animation);
                    // the hip frame twists by -move about its up vector: clockwise seen from above
                    Assert.That(turned.HipUp.ApproximatelyEquals(straight.HipUp, Eps), animation);
                    double turn = Math.Atan2(Vec3.Dot(Vec3.Cross(straight.HipForward, turned.HipForward), straight.HipUp),
                        Vec3.Dot(straight.HipForward, turned.HipForward));
                    Assert.That(turn, Is.EqualTo(-move).Within(1e-9), animation);
                    Assert.That(Math.Sign(Vec3.Cross(straight.HipForward, turned.HipForward).Z), Is.EqualTo(-Math.Sign(move)), animation);
                }
            }
        }

        [Test]
        public void RunGunTurnsTheFootTargetsAboutTheOrigin()
        {
            DragonPose straight = DragonAnimations.Run(BodyShape.Male, 0.2, true);
            DragonPose turned = DragonAnimations.Run(BodyShape.Male, 0.2, true, 0.8);
            foreach (int side in DragonModel.Sides)
            {
                Vec3 expected = DragonAnimations.Turn(straight.Legs[side].Ball, 0.8);
                Assert.That(turned.Legs[side].Ball.ApproximatelyEquals(expected, Eps));
            }
        }

        [Test]
        public void TurnIsClockwiseAboutTheVertical()
        {
            Assert.That(DragonAnimations.Turn(Vec3.UnitY, Math.PI / 2).ApproximatelyEquals(Vec3.UnitX, Eps));
            var v = new Vec3(0.3, -0.2, 0.5);
            Assert.That(DragonAnimations.Turn(v, 0), Is.EqualTo(v));
            Assert.That(DragonAnimations.Turn(v, 1.1).Z, Is.EqualTo(0.5).Within(Eps));
        }

        [Test]
        public void TheLeftFootLeadsAtTheStartOfTheRunCycle()
        {
            DragonPose p = DragonAnimations.Run(BodyShape.Male, 0);
            Assert.That(p.Legs[-1].Ball.Y, Is.GreaterThan(p.Legs[1].Ball.Y));
            DragonPose half = DragonAnimations.Run(BodyShape.Male, 0.5);
            Assert.That(half.Legs[1].Ball.Y, Is.GreaterThan(half.Legs[-1].Ball.Y));
        }

        [Test]
        public void FlyingPosesAreRaisedByTheFlyHeight()
        {
            DragonPose idle = DragonAnimations.Idle(BodyShape.Male, 0);
            DragonPose hover = DragonAnimations.Hover(BodyShape.Male, 0);
            Assert.That(hover.Pelvis.Z, Is.EqualTo(0.70 + Conventions.FlyHeight).Within(Eps));
            Assert.That(idle.Pelvis.Z, Is.EqualTo(0.70).Within(Eps));
            DragonPose low = DragonAnimations.Hover(BodyShape.Male, 0, elevation: 0.1);
            Assert.That(low.Pelvis.Z, Is.EqualTo(0.80).Within(Eps));
            DragonPose shifted = DragonAnimations.Hover(BodyShape.Male, 0, phaseShift: Math.PI / 2);
            Assert.That(shifted.Pelvis.Z, Is.EqualTo(0.70 + Conventions.FlyHeight + 0.035).Within(Eps));
        }

        [Test]
        public void HelpersMoveTheLimbsAsDocumented()
        {
            BodyShape b = BodyShape.Male;
            DragonPose p = DragonModel.BuildPose(new Vec3(0, 0, 0.7));
            DragonAnimations.StandingLegs(p, b, stance: 0.05, front: 0.04);
            Assert.That(p.Legs[1].Ball, Is.EqualTo(new Vec3(0.100 + 0.018 + 0.05, 0.07 + 0.04, 0.035)));
            Assert.That(p.Legs[-1].Ball, Is.EqualTo(new Vec3(-(0.100 + 0.018 + 0.05), 0.07 - 0.04, 0.035)));

            DragonAnimations.RelaxedArms(p, b, swing: 0.05);
            Assert.That(p.Arms[1].Hand.Y, Is.GreaterThan(p.Arms[-1].Hand.Y), "swing moves the right hand forward");

            DragonAnimations.DanglingLegs(p, b);
            Vec3 hip = DragonModel.HipJoint(p, b, 1);
            Assert.That(p.Legs[1].Ball, Is.EqualTo(hip + new Vec3(0.025, 0.05, -0.52)));
            DragonAnimations.DanglingLegs(p, b, back: 0.2);
            Assert.That(p.Legs[1].Ball.ApproximatelyEquals(hip + new Vec3(0.025, 0.05 - 0.2, -0.52 + 0.05), Eps));
        }

        [Test]
        public void CorpseIgnoresTime()
        {
            DragonPoseAssert.Same(DragonPoses.Pose("corpse", BodyShape.Male, 0), DragonPoses.Pose("corpse", BodyShape.Male, 0.7), 0);
        }
    }
}
