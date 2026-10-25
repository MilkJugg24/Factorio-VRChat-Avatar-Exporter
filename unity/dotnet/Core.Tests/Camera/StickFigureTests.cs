using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Camera
{
    public class StickFigureTests
    {
        private const double Eps = 1e-12;

        private static int Count(StickFigure figure, StickPart part) => figure.Capsules.Count(c => c.Part == part);

        [Test]
        public void TPoseHasEveryCapsule()
        {
            StickFigure figure = StickFigure.Build(TestPoses.TPose(), 0, false);
            Assert.That(Count(figure, StickPart.Torso), Is.EqualTo(5), "pelvis-waist-chest-neck_base-neck_top-head");
            Assert.That(Count(figure, StickPart.Head), Is.EqualTo(1));
            Assert.That(Count(figure, StickPart.LeftArm), Is.EqualTo(3), "chest-shoulder-elbow-hand");
            Assert.That(Count(figure, StickPart.RightArm), Is.EqualTo(3));
            Assert.That(Count(figure, StickPart.LeftLeg), Is.EqualTo(5), "pelvis-hip-knee-ankle-ball-toe");
            Assert.That(Count(figure, StickPart.RightLeg), Is.EqualTo(5));
            Assert.That(Count(figure, StickPart.Prop), Is.EqualTo(0));
            Assert.That(figure.Capsules.Select(c => c.Part).ToArray(), Is.Ordered, "torso, head, arms, legs, prop");
        }

        [Test]
        public void CapsulesFollowTheJointsOfTheirSide()
        {
            PoseResult pose = TestPoses.TPose();
            StickFigure figure = StickFigure.Build(pose, 0, false);
            Capsule[] rightArm = figure.Capsules.Where(c => c.Part == StickPart.RightArm).ToArray();
            Assert.That(rightArm[0].A, Is.EqualTo(pose.Joints[JointNames.Chest]));
            Assert.That(rightArm[0].B, Is.EqualTo(pose.Joints[JointNames.Shoulder(1)]));
            Assert.That(rightArm[2].B, Is.EqualTo(pose.Joints[JointNames.Hand(1)]));
            Capsule[] leftLeg = figure.Capsules.Where(c => c.Part == StickPart.LeftLeg).ToArray();
            Assert.That(leftLeg[0].A, Is.EqualTo(pose.Joints[JointNames.Pelvis]));
            Assert.That(leftLeg[4].B, Is.EqualTo(pose.Joints[JointNames.Toe(-1)]));
            Capsule head = figure.Capsules.Single(c => c.Part == StickPart.Head);
            Assert.That(head.A, Is.EqualTo(pose.Joints[JointNames.Head]));
            Assert.That(head.B, Is.EqualTo(head.A), "a sphere");
            Assert.That(head.Radius, Is.EqualTo(0.12).Within(Eps), "reaches head_top");
        }

        [Test]
        public void RadiiScaleWithTheFigure()
        {
            PoseResult small = TestPoses.TPose();
            var big = new PoseResult();
            foreach (KeyValuePair<string, Vec3> joint in small.Joints)
            {
                big.Joints[joint.Key] = joint.Value * 2;
            }
            Assert.That(StickFigure.FigureHeight(big), Is.EqualTo(2 * StickFigure.FigureHeight(small)).Within(1e-12));
            IReadOnlyList<Capsule> a = StickFigure.Build(small, 0, false).Capsules;
            IReadOnlyList<Capsule> b = StickFigure.Build(big, 0, false).Capsules;
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Radius, Is.EqualTo(2 * a[i].Radius).Within(1e-12));
            }
        }

        [Test]
        public void FigureHeightUsesBoneLengthsOnly()
        {
            // torso chain 0.56 + head 0.12 + leg hip-knee-ankle
            PoseResult pose = TestPoses.TPose();
            double leg = Vec3.Distance(new Vec3(0.09, 0, 0.78), new Vec3(0.10, 0.02, 0.42))
                + Vec3.Distance(new Vec3(0.10, 0.02, 0.42), new Vec3(0.10, 0, 0.08));
            Assert.That(StickFigure.FigureHeight(pose), Is.EqualTo(0.56 + 0.12 + leg).Within(1e-12));
            Assert.That(StickFigure.FigureHeight(TestPoses.TPose(lift: 3)), Is.EqualTo(StickFigure.FigureHeight(pose)).Within(1e-12));
            Assert.That(StickFigure.FigureHeight(new PoseResult()), Is.EqualTo(Conventions.DefaultCharacterHeight));
        }

        [Test]
        public void DragonFigureIsAboutAsTallAsTheDragon()
        {
            (PoseResult pose, _) = TestPoses.Dragon(AnimationSpec.Idle, 0, 0);
            Assert.That(StickFigure.FigureHeight(pose), Is.InRange(1.2, 1.6));
            StickFigure figure = StickFigure.Build(pose, 0, false);
            Assert.That(figure.Bounds.Max.Z, Is.InRange(1.4, 1.7));
            Assert.That(figure.Capsules.Count, Is.EqualTo(22));
        }

        [Test]
        public void MissingJointsAreSkippedAndTheirNeighboursJoined()
        {
            PoseResult pose = TestPoses.TPose();
            pose.Joints.Remove(JointNames.NeckTop);
            pose.Joints.Remove(JointNames.Elbow(-1));
            pose.Joints.Remove(JointNames.Toe(1));
            StickFigure figure = StickFigure.Build(pose, 0, false);
            Assert.That(Count(figure, StickPart.Torso), Is.EqualTo(4));
            Capsule[] leftArm = figure.Capsules.Where(c => c.Part == StickPart.LeftArm).ToArray();
            Assert.That(leftArm.Length, Is.EqualTo(2));
            Assert.That(leftArm[1].A, Is.EqualTo(pose.Joints[JointNames.Shoulder(-1)]));
            Assert.That(leftArm[1].B, Is.EqualTo(pose.Joints[JointNames.Hand(-1)]), "shoulder joined to hand");
            Assert.That(Count(figure, StickPart.RightLeg), Is.EqualTo(4));
        }

        [Test]
        public void EmptyPoseHasNoCapsules()
        {
            StickFigure figure = StickFigure.Build(new PoseResult(), 0.3, true);
            Assert.That(figure.Capsules, Is.Empty);
            Assert.That(figure.Bounds.IsEmpty, Is.True);
            var lonely = new PoseResult();
            lonely.Joints[JointNames.Knee(1)] = new Vec3(0, 0, 0.4);
            Assert.That(StickFigure.Build(lonely, 0, false).Capsules, Is.Empty, "a chain needs two joints");
        }

        [Test]
        public void JointsArePlacedWithOrientationAndMirroring()
        {
            var rng = new Random(341);
            PoseResult pose = TestPoses.TPose();
            for (int i = 0; i < 20; i++)
            {
                double orientation = rng.NextDouble() * 2 - 0.5;
                bool mirrored = rng.Next(2) == 1;
                StickFigure figure = StickFigure.Build(pose, orientation, mirrored);
                Capsule hand = figure.Capsules.Where(c => c.Part == StickPart.RightArm).Last();
                Vec3 expected = Conventions.ModelPointToWorld(pose.Joints[JointNames.Hand(1)], orientation, mirrored);
                Assert.That(hand.B.ApproximatelyEquals(expected, Eps));
            }
            // facing east, the right hand points south
            Capsule east = StickFigure.Build(pose, 0.25, false).Capsules.Where(c => c.Part == StickPart.RightArm).Last();
            Assert.That(east.B.ApproximatelyEquals(new Vec3(0, -0.62, 1.18), 1e-12), east.B.ToString());
            // mirrored, the right arm (still the RightArm part) is on the west side
            Capsule west = StickFigure.Build(pose, 0, true).Capsules.Where(c => c.Part == StickPart.RightArm).Last();
            Assert.That(west.B.ApproximatelyEquals(new Vec3(-0.62, 0, 1.18), 1e-12), west.B.ToString());
        }

        [Test]
        public void GroundToLowestPointPutsTheLowestSurfaceOnTheGround()
        {
            foreach (double lift in new[] { 0.4, 0.0, -0.25 })
            {
                PoseResult pose = TestPoses.TPose(lift);
                Bounds3 loose = StickFigure.Build(pose, 0.1, false).Bounds;
                pose.GroundToLowestPoint = true;
                StickFigure grounded = StickFigure.Build(pose, 0.1, false);
                Assert.That(grounded.Bounds.Min.Z, Is.EqualTo(0).Within(1e-12), "lift " + lift);
                Assert.That(grounded.Bounds.Max.Z - grounded.Bounds.Min.Z, Is.EqualTo(loose.Max.Z - loose.Min.Z).Within(1e-12));
                Assert.That(grounded.Bounds.Min.X, Is.EqualTo(loose.Min.X).Within(1e-12), "only moved vertically");
            }
        }

        [Test]
        public void GunIsOneCapsuleFromBaseToTip()
        {
            PoseResult pose = TestPoses.TPose();
            pose.Prop = new PropPose(PropKind.Gun, new Vec3(0.1, 0.1, 1.0), new Vec3(0.1, 0.6, 1.0), Vec3.UnitZ);
            StickFigure figure = StickFigure.Build(pose, 0.25, false);
            Capsule gun = figure.Capsules.Single(c => c.Part == StickPart.Prop);
            Assert.That(gun.A.ApproximatelyEquals(Conventions.ModelPointToWorld(pose.Prop.Base, 0.25, false), Eps));
            Assert.That(gun.B.ApproximatelyEquals(Conventions.ModelPointToWorld(pose.Prop.Tip, 0.25, false), Eps));
            Assert.That(gun.Radius, Is.LessThan(figure.Capsules[0].Radius / 2), "thin");
        }

        [Test]
        public void PickaxeHasAHandleAndAHeadAlongUpAtTheTip()
        {
            PoseResult pose = TestPoses.TPose();
            pose.Prop = new PropPose(PropKind.Pickaxe, new Vec3(0, 0.2, 0.9), new Vec3(0, 0.76, 0.9), Vec3.UnitZ);
            StickFigure figure = StickFigure.Build(pose, 0, false);
            Capsule[] pick = figure.Capsules.Where(c => c.Part == StickPart.Prop).ToArray();
            Assert.That(pick.Length, Is.EqualTo(2));
            Assert.That(pick[0].A, Is.EqualTo(pose.Prop.Base));
            Assert.That(pick[0].B, Is.EqualTo(pose.Prop.Tip));
            // head: 0.2 handle lengths along +Up, the point 0.32 along -Up (model.py's 0.11 and 0.18 of 0.56)
            Assert.That(pick[1].A.ApproximatelyEquals(new Vec3(0, 0.76, 0.9 + 0.112), 1e-12), pick[1].A.ToString());
            Assert.That(pick[1].B.ApproximatelyEquals(new Vec3(0, 0.76, 0.9 - 0.1792), 1e-12), pick[1].B.ToString());
        }

        [Test]
        public void DragonPropsAreDrawn()
        {
            (PoseResult gun, _) = TestPoses.Dragon(AnimationSpec.RunGun, 3, 2);
            Assert.That(StickFigure.Build(gun, 0, false).Capsules.Count(c => c.Part == StickPart.Prop), Is.EqualTo(1));
            (PoseResult mine, _) = TestPoses.Dragon(AnimationSpec.Mine, 0, 5);
            Assert.That(StickFigure.Build(mine, 0, false).Capsules.Count(c => c.Part == StickPart.Prop), Is.EqualTo(2));
        }

        [Test]
        public void RejectsBadInput()
        {
            Assert.That(() => StickFigure.Build(null, 0, false), Throws.ArgumentNullException);
            Assert.That(() => StickFigure.FigureHeight(null), Throws.ArgumentNullException);
            Assert.That(() => StickFigure.Build(TestPoses.TPose(), double.NaN, false), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => StickFigure.Build(TestPoses.TPose(), double.PositiveInfinity, false),
                Throws.TypeOf<ArgumentOutOfRangeException>());

            PoseResult nanJoint = TestPoses.TPose();
            nanJoint.Joints[JointNames.Knee(1)] = new Vec3(0, double.NaN, 0);
            Assert.That(() => StickFigure.Build(nanJoint, 0, false), Throws.ArgumentException.With.Message.Contains("knee.R"));

            PoseResult badProp = TestPoses.TPose();
            badProp.Prop = new PropPose(PropKind.Gun, Vec3.Zero, new Vec3(double.PositiveInfinity, 0, 0), Vec3.UnitZ);
            Assert.That(() => StickFigure.Build(badProp, 0, false), Throws.ArgumentException.With.Message.Contains("prop tip"));

            PoseResult flatPick = TestPoses.TPose();
            flatPick.Prop = new PropPose(PropKind.Pickaxe, Vec3.Zero, Vec3.UnitY, Vec3.Zero);
            Assert.That(() => StickFigure.Build(flatPick, 0, false), Throws.ArgumentException.With.Message.Contains("pickaxe"));
        }
    }

    public class StickPaletteTests
    {
        private static readonly StickPart[] Parts = (StickPart[])Enum.GetValues(typeof(StickPart));

        [Test]
        public void EveryPartHasItsOwnColor()
        {
            var colors = Parts.Select(StickPalette.Albedo).ToList();
            Assert.That(colors, Is.Unique);
            foreach ((double r, double g, double b) in colors)
            {
                Assert.That(new[] { r, g, b }, Is.All.InRange(0.0, 1.0));
            }
        }

        [Test]
        public void SidesTorsoHeadAndPropAreRecognisable()
        {
            foreach (StickPart part in new[] { StickPart.LeftArm, StickPart.LeftLeg })
            {
                (double r, _, double b) = StickPalette.Albedo(part);
                Assert.That(b, Is.GreaterThan(r + 0.3), part + " is blue-ish");
            }
            foreach (StickPart part in new[] { StickPart.RightArm, StickPart.RightLeg })
            {
                (double r, _, double b) = StickPalette.Albedo(part);
                Assert.That(r, Is.GreaterThan(b + 0.3), part + " is red-ish");
            }
            (double tr, double tg, double tb) = StickPalette.Albedo(StickPart.Torso);
            Assert.That(tr == tg && tg == tb, "torso is grey");
            (double hr, double hg, double hb) = StickPalette.Albedo(StickPart.Head);
            Assert.That(hr > hg && hg > hb, "head is skin colored");
            (double pr, double pg, double pb) = StickPalette.Albedo(StickPart.Prop);
            Assert.That(pr + pg + pb, Is.LessThan(1.0), "prop is dark");
        }

        [Test]
        public void FacingTheLightShowsTheAlbedo()
        {
            Assert.That(StickPalette.Ambient + StickPalette.Diffuse, Is.EqualTo(1.0));
            foreach (StickPart part in Parts)
            {
                (double r, double g, double b) = StickPalette.Shade(part, Conventions.ShadeLight);
                (double ar, double ag, double ab) = StickPalette.Albedo(part);
                Assert.That(r, Is.EqualTo(ar).Within(1e-9));
                Assert.That(g, Is.EqualTo(ag).Within(1e-9));
                Assert.That(b, Is.EqualTo(ab).Within(1e-9));
            }
        }

        [Test]
        public void SurfacesTurnedAwayGetOnlyAmbientLight()
        {
            (double r, double g, double b) = StickPalette.Shade(StickPart.Head, -Conventions.ShadeLight);
            (double ar, double ag, double ab) = StickPalette.Albedo(StickPart.Head);
            Assert.That(r, Is.EqualTo(ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(ar) * StickPalette.Ambient)).Within(1e-12));
            Assert.That(g, Is.EqualTo(ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(ag) * StickPalette.Ambient)).Within(1e-12));
            Assert.That(b, Is.EqualTo(ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(ab) * StickPalette.Ambient)).Within(1e-12));
            Vec3 sideways = Vec3.Cross(Conventions.ShadeLight, Vec3.UnitX).Normalized;
            Assert.That(StickPalette.Shade(StickPart.Head, sideways).r, Is.EqualTo(r).Within(1e-12), "no light from behind");
        }

        [Test]
        public void BrightnessGrowsTowardsTheLight()
        {
            var rng = new Random(342);
            for (int i = 0; i < 200; i++)
            {
                var a = new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5).Normalized;
                var b = new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5).Normalized;
                if (Vec3.Dot(a, Conventions.ShadeLight) < Vec3.Dot(b, Conventions.ShadeLight))
                {
                    (a, b) = (b, a);
                }
                Assert.That(StickPalette.Shade(StickPart.Torso, a).g, Is.GreaterThanOrEqualTo(StickPalette.Shade(StickPart.Torso, b).g));
            }
        }

        [Test]
        public void UnknownPartsAreRejected()
        {
            Assert.That(() => StickPalette.Albedo((StickPart)99), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => StickPalette.Shade((StickPart)99, Vec3.UnitZ), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
