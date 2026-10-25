using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The poses of every pack animation for a humanoid avatar. Each animation keeps the phase structure
    /// of the dragon's (tools/dragon_render/anims.py, ported in Core/Pose/Dragon/), so the pack timing
    /// stays the dragon's: feet land on run frames 1 and 7, the pick hits on mine frame 9. What differs:
    /// <list type="bullet">
    /// <item>sizes come from the avatar's measured rig: strides from its leg length, hand positions from
    /// its arm length, props from its height;</item>
    /// <item>feet are planted flat on their measured ball points and roll over the ball (the dragon walks
    /// on its toes);</item>
    /// <item>leaning forward is forward (the dragon's lean tilts it backwards, see DragonModel.TorsoFrame);</item>
    /// <item>both hands reach the gun;</item>
    /// <item>the corpse lies on its side facing the camera; the renderer puts its lowest point on the ground.</item>
    /// </list>
    /// </summary>
    public sealed class HumanoidAnimations : IPoseSource
    {
        /// <summary>Leg length of the dragon, which the gait sizes of anims.py were made for, tiles.</summary>
        private const double DragonLegLength = 0.6;

        /// <summary>Arm length (upper arm + forearm) of the dragon, tiles.</summary>
        private const double DragonArmLength = 0.45;

        /// <summary>
        /// Half the fore-aft travel of a foot over a run cycle at dragon size, tiles (anims.run uses 0.25;
        /// a little shorter reads better on plantigrade legs).
        /// </summary>
        private const double RunStride = 0.18;

        /// <summary>
        /// Tiles the vanilla engineer covers in one run cycle (two steps): 22 frames x distance_per_frame 0.13 /
        /// animation_speed 0.6.
        /// </summary>
        private const double EngineerRunCycle = 22 * 0.13 / 0.6;

        private readonly HumanoidRig rig;

        /// <summary>Gait sizes relative to the dragon's, from leg length.</summary>
        private readonly double legScale;

        /// <summary>Hand offsets relative to the dragon's, from arm length.</summary>
        private readonly double armScale;

        /// <summary>Prop sizes relative to the dragon's, from overall height.</summary>
        private readonly double sizeScale;

        /// <summary>Pelvis height when standing with the knees slightly bent.</summary>
        private readonly double standingPelvis;

        public HumanoidAnimations(HumanoidRig rig)
        {
            this.rig = rig ?? throw new ArgumentNullException(nameof(rig));
            legScale = rig.LegLength / DragonLegLength;
            armScale = rig.ArmLength / DragonArmLength;
            sizeScale = rig.HeadTopHeight / Conventions.DefaultCharacterHeight;
            standingPelvis = rig.HipsHeight - 0.015 * rig.LegLength;
            AnimationInfo run = AnimationSpec.Get(AnimationSpec.Run);
            Timing = new PackTiming
            {
                // the game moves distance_per_frame / animation_speed tiles per running frame: one cycle
                // covers the vanilla engineer's ground, longer for longer legs, like the dragon's 0.238
                DistancePerFrame = EngineerRunCycle * legScale * run.DefaultSpeed / run.FrameCount,
                SprintDistancePerFrame = EngineerRunCycle * legScale * run.DefaultSpeed / run.FrameCount * AnimationSpec.SprintCycleScale,
            };
        }

        public PackTiming Timing { get; }

        public PoseResult Pose(string animation, double t, double move)
        {
            if (double.IsNaN(t) || double.IsInfinity(t) || double.IsNaN(move) || double.IsInfinity(move))
            {
                throw new ArgumentException("t and move must be finite numbers");
            }
            switch (animation)
            {
                case AnimationSpec.Idle: return Idle(t, false);
                case AnimationSpec.IdleGun: return Idle(t, true);
                case AnimationSpec.Run: return Run(t, false, move);
                case AnimationSpec.RunGun: return Run(t, true, move);
                case AnimationSpec.Sprint: return Sprint(t);
                case AnimationSpec.Mine: return Mine(t);
                case AnimationSpec.Corpse: return Corpse();
                case AnimationSpec.Hover: return Hover(t, false, Conventions.FlyHeight, 0);
                case AnimationSpec.HoverGun: return Hover(t, true, Conventions.FlyHeight, 0);
                case AnimationSpec.Fly: return Fly(t, false, move);
                case AnimationSpec.FlyGun: return Fly(t, true, move);
                case AnimationSpec.TakeOff: return TakeOff(t);
                case AnimationSpec.HoverFlap: return Hover(t, false, Conventions.FlyHeight, 0, true);
                case AnimationSpec.FlyFlap: return Fly(t, false, move, true);
                case AnimationSpec.TakeOffFlap: return TakeOff(t, true);
                default: throw new ArgumentException("unknown animation " + animation, nameof(animation));
            }
        }

        // -- frames and helpers ---------------------------------------------------------------------

        /// <summary>
        /// A body frame: pitched forward by <paramref name="pitchForward"/> radians, turned
        /// counterclockwise (seen from above) by <paramref name="yaw"/>, rolled by <paramref name="roll"/>
        /// (the parameters of model.py's torso_frame, with forward pitch actually forward).
        /// </summary>
        public static Quat Frame(double pitchForward, double yaw = 0, double roll = 0) =>
            Quat.FromAxisAngle(Vec3.UnitX, -pitchForward) * Quat.FromAxisAngle(Vec3.UnitZ, yaw) *
            Quat.FromAxisAngle(Vec3.UnitY, roll);

        /// <summary>Turns a model space vector clockwise (seen from above) by <paramref name="move"/> radians (anims._turn).</summary>
        private static Vec3 Turn(Vec3 v, double move) =>
            move == 0 ? v : Quat.FromAxisAngle(Vec3.UnitZ, -move).Rotate(v);

        private static readonly int[] Sides = { 1, -1 };

        /// <summary>
        /// The body from the hips up. The spine bends gradually from the hips' turn to the chest's
        /// (<see cref="HumanoidPoseBuilder.SetTorso"/>), so hips and chest turned or tilted differently
        /// (<paramref name="hipTwist"/> against <paramref name="twist"/>, <paramref name="hipRoll"/> against
        /// <paramref name="chestRoll"/>, both radians, roll positive dropping the right side) give a body that
        /// counters itself, as people do.
        /// </summary>
        private HumanoidPoseBuilder Torso(Vec3 pelvis, double lean, double twist = 0, double hipTwist = 0,
            double headPitch = 0, double headYaw = 0, double hipRoll = 0, double chestRoll = 0, double headRoll = 0)
        {
            var b = new HumanoidPoseBuilder(rig);
            b.SetPelvis(pelvis, Frame(lean * 0.5, hipTwist, hipRoll));
            b.SetTorso(Frame(lean, twist, chestRoll));
            // the head stays closer to level than the torso and looks where the body goes
            b.SetHead(Frame(lean * 0.35 + headPitch, twist * 0.5 + headYaw, headRoll));
            return b;
        }

        /// <summary>A smooth bump from 0 up to 1 and back over [<paramref name="start"/>, <paramref name="end"/>] of the loop.</summary>
        private static double Bump(double t, double start, double end)
        {
            if (t <= start || t >= end)
            {
                return 0;
            }
            double u = (t - start) / (end - start);
            return Math.Sin(Math.PI * u) * Math.Sin(Math.PI * u);
        }

        private static Quat ChestFrame(double lean, double twist = 0) => Frame(lean, twist);

        private Vec3 Shoulder(HumanoidPoseBuilder b, int side) => b.TorsoPoint(HumanBoneHierarchy.UpperArm(side));

        private Vec3 HipJoint(HumanoidPoseBuilder b, int side) => b.TorsoPoint(HumanBoneHierarchy.UpperLeg(side));

        /// <summary>Arms hanging at the sides (anims._relaxed_arms), slightly curled hands.</summary>
        private void RelaxedArms(HumanoidPoseBuilder b, Quat chest, double swing = 0, double spread = 0)
        {
            foreach (int side in Sides)
            {
                Vec3 s = Shoulder(b, side);
                Vec3 hand = s + chest.Right * (side * (0.055 * armScale + spread * sizeScale))
                            + chest.Forward * ((0.04 + swing * side) * armScale)
                            - chest.Up * (rig.ArmLength * 0.9);
                b.SetArm(side, hand, -chest.Forward + chest.Right * (side * 0.35), null, 0.25);
            }
        }

        /// <summary>Both feet flat on the ground on their rest ball points (anims._standing_legs).</summary>
        private void StandingFeet(HumanoidPoseBuilder b, double stance = 0, double front = 0)
        {
            foreach (int side in Sides)
            {
                Vec3 rest = rig.Ball(side);
                var ball = new Vec3(rest.X + side * stance * legScale, rest.Y + front * side * legScale, rest.Z);
                Vec3 forward = rig.FootForward(side);
                b.SetFoot(side, ball, forward, 0, forward + Vec3.UnitX * (side * 0.15));
            }
        }

        /// <summary>
        /// The ball target that puts the ankle exactly at <paramref name="ankle"/> for a foot facing
        /// <paramref name="forward"/> with <paramref name="heelLift"/>: the inverse of how
        /// <see cref="HumanoidPoseBuilder.SetFoot"/> places the ankle.
        /// </summary>
        private Vec3 BallForAnkle(int side, Vec3 ankle, Vec3 forward, double heelLift)
        {
            Vec3 restForward = rig.FootForward(side);
            Vec3 f = forward.Horizontal.Normalized;
            Quat yaw = Quat.FromAxisAngle(Vec3.UnitZ, Math.Atan2(Vec3.Cross(restForward, f).Z, Vec3.Dot(restForward, f)));
            Quat foot = yaw * Quat.FromAxisAngle(Vec3.Cross(restForward, Vec3.UnitZ), -heelLift);
            return ankle + foot.Rotate(rig.Ball(side) - rig.Rest(HumanBoneHierarchy.Foot(side)));
        }

        /// <summary>
        /// Legs hanging from the hips while flying (anims._dangling_legs): nearly straight, tilted back by
        /// <paramref name="backAngle"/> radians from straight down in the moving direction, toes pointed (a
        /// digitigrade foot already points down, so it only tilts back with the leg).
        /// <paramref name="blend"/> moves them there from standing (take-off).
        /// </summary>
        private void DanglingLegs(HumanoidPoseBuilder b, double backAngle, double move = 0, double blend = 1)
        {
            foreach (int side in Sides)
            {
                Vec3 hip = HipJoint(b, side);
                Vec3 forward = Turn(rig.FootForward(side), move);
                Vec3 down = Turn(new Vec3(side * 0.05, -Math.Sin(backAngle), -Math.Cos(backAngle)), move).Normalized;
                Vec3 ankle = hip + down * ((rig.ThighLength(side) + rig.ShinLength(side)) * 0.95);
                double pointed = (rig.LegStyle == LegStyle.Digitigrade ? 0 : 1.0) + backAngle;
                Vec3 ball = Vec3.Lerp(rig.Ball(side), BallForAnkle(side, ankle, forward, pointed), blend);
                double heelLift = pointed * blend;
                b.SetFoot(side, ball, forward, heelLift, forward, heelLift);
            }
        }

        /// <summary>Distance from the wrist to the middle of a closed fist, as a fraction of the arm length.</summary>
        private const double FistPerArmLength = 0.14;

        /// <summary>
        /// How far from the wrist, along the fingers, a closed hand holds a handle (the middle of the fist), tiles:
        /// <see cref="FistPerArmLength"/> of the arm length.
        /// </summary>
        public double GripOffset => FistPerArmLength * rig.ArmLength;

        /// <summary>
        /// Closes the hand of <paramref name="side"/> round a handle running through <paramref name="point"/>
        /// along <paramref name="axis"/> (unit, towards the pick's head or the muzzle), the elbow bent towards
        /// <paramref name="pole"/>. The fingers point across the handle away from the elbow, the wrist
        /// <see cref="GripOffset"/> short of the handle, and the thumb side of the fist faces back along the
        /// handle: the handle goes on from the hands away from the body, and turning the thumbs towards its far
        /// end would roll the wrists further than forearms turn.
        /// </summary>
        private void HoldHandle(HumanoidPoseBuilder b, int side, Vec3 point, Vec3 axis, Vec3 pole)
        {
            Vec3 fingers = GripFingers(axis, pole);
            b.SetArm(side, point - fingers * GripOffset, pole, fingers, 1, -axis);
        }

        /// <summary>
        /// The direction across a handle along <paramref name="axis"/> (unit) away from <paramref name="pole"/>: where a
        /// hand reaching for the handle with its elbow bent towards the pole points its fingers.
        /// </summary>
        private static Vec3 GripFingers(Vec3 axis, Vec3 pole) => -(pole - axis * Vec3.Dot(pole, axis)).Normalized;

        /// <summary>
        /// The gun held at the chest with both hands, aiming horizontally ahead of the torso
        /// (model.hold_gun, with the support hand kept within the arm's reach). The trigger hand points along
        /// the gun as on a pistol grip; the support hand closes round the gun (<see cref="HoldHandle"/>).
        /// </summary>
        private void HoldGun(HumanoidPoseBuilder b, Quat chest)
        {
            Vec3 aim = chest.Forward.Horizontal.Normalized;
            Vec3 right = Vec3.Cross(aim, Vec3.UnitZ).Normalized;
            Vec3 rightShoulder = Shoulder(b, 1);
            Vec3 leftShoulder = Shoulder(b, -1);
            // the stock against the right side of the chest, a little below the shoulder
            Vec3 stock = rightShoulder - right * (0.04 * sizeScale) + aim * (0.05 * sizeScale) - Vec3.UnitZ * (0.07 * sizeScale);
            Vec3 trigger = stock + aim * (0.13 * sizeScale) - Vec3.UnitZ * (0.035 * sizeScale);
            // the support hand holds the gun as far forward as its arm reaches (short-armed avatars hold it
            // nearer the trigger)
            Vec3 supportPole = -chest.Forward * 0.2 - chest.Right * 0.7 - Vec3.UnitZ * 0.8;
            Vec3 toWrist = -GripFingers(aim, supportPole) * GripOffset;
            double reach = 0.95 * (rig.UpperArmLength(-1) + rig.ForearmLength(-1));
            Vec3 support = stock + aim * (0.31 * sizeScale) - Vec3.UnitZ * (0.02 * sizeScale);
            for (double along = 0.31; along > 0.13 && Vec3.Distance(support + toWrist, leftShoulder) > reach; along -= 0.01)
            {
                support = stock + aim * (along * sizeScale) - Vec3.UnitZ * (0.02 * sizeScale);
            }
            // still out of reach (short arms, wide shoulders): the whole gun moves towards the left shoulder,
            // sideways and then towards the middle of the shoulders, as long as that brings the support hand
            // nearer and the trigger stays within the right arm's reach (or gets no further out of it)
            double triggerReach = 0.95 * (rig.UpperArmLength(1) + rig.ForearmLength(1));
            void Shift(Vec3 direction)
            {
                Vec3 step = direction.Normalized * (0.002 * sizeScale);
                while (Vec3.Distance(support + toWrist, leftShoulder) > reach
                       && Vec3.Distance(support + toWrist + step, leftShoulder) < Vec3.Distance(support + toWrist, leftShoulder)
                       && Vec3.Distance(trigger + step, rightShoulder)
                       <= Math.Max(triggerReach, Vec3.Distance(trigger, rightShoulder)))
                {
                    stock += step;
                    trigger += step;
                    support += step;
                }
            }
            Shift(-right);
            Shift((rightShoulder + leftShoulder) * 0.5 - support);
            b.SetArm(1, trigger, -chest.Forward * 0.4 + chest.Right * 0.6 - Vec3.UnitZ * 0.8, aim, 1);
            HoldHandle(b, -1, support, aim, supportPole);
            Vec3 up = Vec3.Cross(right, aim).Normalized;
            b.SetProp(new PropPose(PropKind.Gun, stock, stock + aim * (0.52 * sizeScale), up));
        }


        /// <summary>Length of the pick's handle from the hand to the head, and behind the hand, tiles at the default height.</summary>
        private const double PickAhead = 0.55, PickBehind = 0.05;

        /// <summary>
        /// The handle angle (radians from forward-horizontal, up positive) at which the pick's point, swung
        /// down in front with the hands <paramref name="reach"/> arm lengths out, touches the ground; the lowest
        /// it reaches when the ground is too far down.
        /// </summary>
        private double GroundAlpha(Vec3 pivot, double reach)
        {
            // the point: pivot + handle(alpha) * along - head(alpha) * point, so its height is
            // pivot.Z + along * sin(alpha) - point * cos(alpha) = pivot.Z + r * sin(alpha - phi)
            (double r, double phi) = PickSwing(reach);
            double height = pivot.Z;
            return phi + (height >= r ? -Math.PI / 2 : Math.Asin(-height / r));
        }

        /// <summary>
        /// How far the pick's point is from the pivot with the hands <paramref name="reach"/> arm lengths out, and
        /// the angle between the handle and the line to the point.
        /// </summary>
        private (double radius, double angle) PickSwing(double reach)
        {
            double along = reach * rig.ArmLength + PickAhead * sizeScale;
            double point = StickFigure.PickHeadPoint * (PickAhead + PickBehind) * sizeScale;
            return (Math.Sqrt(along * along + point * point), Math.Atan2(point, along));
        }

        /// <summary>
        /// The pickaxe in the right hand, as the engineer swings it: the arm at <paramref name="beta"/> radians from
        /// forward-horizontal (up positive), <paramref name="reach"/> arm lengths out from the right shoulder, in
        /// the upright plane through it facing forward, the handle at <paramref name="alpha"/> radians in the same
        /// plane, its head pointing away from the swing. The elbow points out, down and back while the arm is low
        /// and out, up and forward while the pick is raised.
        /// </summary>
        private void HoldPick(HumanoidPoseBuilder b, Quat chest, double beta, double alpha, double reach)
        {
            Vec3 forward = chest.Forward.Horizontal.Normalized;
            Vec3 side = Vec3.Cross(forward, Vec3.UnitZ).Normalized;
            Vec3 handle = forward * Math.Cos(alpha) + Vec3.UnitZ * Math.Sin(alpha);
            Vec3 head = -forward * Math.Sin(alpha) + Vec3.UnitZ * Math.Cos(alpha);
            Vec3 arm = forward * Math.Cos(beta) + Vec3.UnitZ * Math.Sin(beta);
            Vec3 grip = Shoulder(b, 1) + arm * (reach * rig.ArmLength);
            double raised = MathUtil.Smoothstep(0.3, 1.5, beta);
            Vec3 bend = Vec3.Lerp(-forward * 0.3 - Vec3.UnitZ * 0.5, forward * 0.3 + Vec3.UnitZ * 0.4, raised);
            HoldHandle(b, 1, grip, handle, side * 0.8 + bend);
            b.SetProp(new PropPose(PropKind.Pickaxe, grip - handle * (PickBehind * sizeScale), grip + handle * (PickAhead * sizeScale), head));
        }

        /// <summary>
        /// The free left arm, balancing the swing: reaching forward at chest height while the pick is raised
        /// (<paramref name="strike"/> 0), and out to the side and back as the body bends into the blow (1), well
        /// away from the handle, so it never looks like a second hand on it.
        /// </summary>
        private void BalancingArm(HumanoidPoseBuilder b, Quat chest, double strike)
        {
            Vec3 forward = chest.Forward.Horizontal.Normalized;
            Vec3 side = Vec3.Cross(forward, Vec3.UnitZ).Normalized;
            Vec3 s = Shoulder(b, -1);
            Vec3 raised = forward * 0.72 + Vec3.UnitZ * 0.12 - side * 0.12;
            Vec3 down = -side * 0.6 - chest.Up * 0.5 - chest.Forward * 0.15;
            Vec3 hand = s + Vec3.Lerp(raised, down, strike) * rig.ArmLength;
            b.SetArm(-1, hand, -side * 0.6 - Vec3.UnitZ * 0.6 - forward * 0.2, null, 0.5);
        }

        // -- animations -----------------------------------------------------------------------------

        /// <summary>
        /// When each wing stretches in the idle loop (16 s): the left from about 2 s to 3.5 s, the right 8 s later.
        /// </summary>
        private const double LeftStretchStart = 0.125, StretchLength = 0.09375;

        /// <summary>Where a stretching wing points, radians above straight out sideways.</summary>
        private const double WingStretch = 0.35;

        /// <summary>
        /// Standing (16 s loop, 48 frames): breathing, the weight shifting slowly from foot to foot with the hips
        /// dropping on the unweighted side and the chest countering, a slight slouch, the head looking around,
        /// the tail swaying, and a stretch of one wing and, 8 s later, the other, with the body leaning away and
        /// the head turning to look.
        /// </summary>
        private PoseResult Idle(double t, bool gun)
        {
            double ph = MathUtil.Tau * t;
            double breath = Math.Sin(4 * ph);
            double sway = Math.Sin(ph);
            double left = Bump(t, LeftStretchStart, LeftStretchStart + StretchLength);
            double right = Bump(t, LeftStretchStart + 0.5, LeftStretchStart + 0.5 + StretchLength);
            double lean = 0.06 + 0.008 * breath - 0.05 * (left + right);
            var pelvis = new Vec3(-0.012 * legScale * sway, -0.005 * legScale,
                standingPelvis + 0.004 * legScale * breath - 0.006 * legScale * Math.Abs(sway));
            double hipRoll = 0.035 * sway;
            HumanoidPoseBuilder b = Torso(pelvis, lean, twist: 0.06 * (left - right),
                headPitch: 0.03 * Math.Sin(3 * ph + 0.5) - 0.02 * breath,
                headYaw: 0.12 * Math.Sin(2 * ph + 1.0) + 0.45 * (left - right),
                hipRoll: hipRoll, chestRoll: -0.6 * hipRoll + 0.06 * (right - left), headRoll: 0.2 * hipRoll);
            Quat chest = ChestFrame(lean, 0.06 * (left - right));
            if (gun)
            {
                HoldGun(b, chest);
            }
            else
            {
                RelaxedArms(b, chest, 0.015 * Math.Sin(4 * ph + 0.8));
            }
            StandingFeet(b, 0.01);
            PoseResult pose = b.Build();
            pose.LeftWingSpread = left;
            pose.RightWingSpread = right;
            pose.WingElevation = WingStretch;
            pose.TailSwing = 0.06;
            pose.TailPhase = 2 * ph;
            pose.TailLift = 0.05;
            return pose;
        }

        /// <summary>
        /// Running, a light jog: short quick steps, the body leaning into it, and every footfall drives it. Feet
        /// land on frames 1 and 7; the hips drop as the leg takes the weight and rise into the push-off and the
        /// moment both feet are off the ground, tilt down on the swinging leg's side and turn forward with it,
        /// and sit over the foot on the ground. The chest counters
        /// all three, the arms swing against the legs with bent elbows, rising and crossing in front, the head
        /// stays level and looks ahead, and the tail swings against the hips. <paramref name="move"/> turns the
        /// hips and legs clockwise from the facing direction (running with a gun aimed elsewhere); with a gun the
        /// chest twists less, so the gun stays on target.
        /// </summary>
        private PoseResult Run(double t, bool gun, double move)
        {
            double ph = MathUtil.Tau * t;
            // lowest just after each landing, highest in the air between steps
            double bounce = -Math.Cos(2 * ph - 0.4);
            double lean = (gun ? 0.12 : 0.35) + 0.02 * bounce;
            double hipTwist = (gun ? 0.08 : 0.11) * Math.Sin(ph);
            double twist = (gun ? 0.04 : 0.09) * Math.Sin(ph);
            double hipRoll = 0.05 * Math.Sin(ph);
            // the weight over the foot on the ground: the left from frame 1, the right from frame 7
            Vec3 shift = Turn(new Vec3(-0.02 * legScale * Math.Sin(ph), 0, 0), move);
            var pelvis = new Vec3(shift.X, shift.Y, rig.HipsHeight - 0.055 * rig.LegLength + 0.012 * legScale * bounce);
            HumanoidPoseBuilder b = Torso(pelvis, lean, twist, -hipTwist - move,
                headPitch: (gun ? -0.06 : -0.12) - 0.03 * bounce, headYaw: -twist * 0.5,
                hipRoll: hipRoll, chestRoll: -0.55 * hipRoll, headRoll: -0.1 * hipRoll);
            foreach ((int side, double phase) in new[] { (-1, t), (1, t + 0.5) })
            {
                double phi = MathUtil.PositiveModulo(phase, 1.0);
                double y = 0.02 * legScale + RunStride * legScale * Math.Cos(MathUtil.Tau * phi);
                // in the air the heel kicks up behind first, then the knee drives forward
                double s = phi > 0.5 ? (phi - 0.5) / 0.5 : 0.0;
                double swing = s > 0 ? Math.Sin(Math.PI * Math.Pow(s, 0.75)) : 0.0;
                double z = 0.10 * legScale * swing;
                double heelLift = phi <= 0.5 ? 0.7 * MathUtil.Smoothstep(0.3, 0.5, phi) : 0.7 * (1 - MathUtil.Smoothstep(0.5, 0.9, phi)) + 0.3 * swing;
                Vec3 rest = rig.Ball(side);
                Vec3 ball = Turn(new Vec3(rest.X - side * 0.01 * legScale, rest.Y + y, rest.Z + z), move);
                Vec3 forward = Turn(rig.FootForward(side), move);
                b.SetFoot(side, ball, forward, heelLift, forward + Vec3.UnitZ * 0.2, phi > 0.5 ? heelLift : 0);
            }
            Quat chest = ChestFrame(lean, twist);
            if (gun)
            {
                HoldGun(b, chest);
            }
            else
            {
                foreach ((int side, double phase) in new[] { (-1, t), (1, t + 0.5) })
                {
                    double a = Math.Cos(MathUtil.Tau * (phase + 0.5));
                    double ahead = Math.Max(a, 0);
                    Vec3 s = Shoulder(b, side);
                    Vec3 hand = s + chest.Right * (side * (0.08 - 0.05 * ahead) * armScale)
                                + chest.Forward * ((0.15 + 0.10 * a) * armScale)
                                - chest.Up * ((0.24 - 0.07 * a) * armScale);
                    // elbows bent about square: swung back, the upper arm points back and the hand hangs under the
                    // shoulder; swung forward, the hand comes up to the chest
                    b.SetArm(side, hand, -chest.Forward * 0.6 + chest.Right * (side * 0.35) - chest.Up, null, 0.6);
                }
            }
            PoseResult pose = b.Build();
            pose.TailSwing = 0.10;
            pose.TailPhase = ph + Math.PI;
            pose.TailLift = 0.15;
            return pose;
        }

        /// <summary>
        /// Half the fore-aft travel of a foot on the ground in a sprint, at dragon size, tiles. A sprint cycle covers
        /// <see cref="AnimationSpec.SprintCycleScale"/> times a run cycle's ground, and each foot is on the ground for
        /// <see cref="SprintStance"/> of it instead of half, so the foot moves back with the ground as in the run.
        /// </summary>
        private const double SprintStride = RunStride * AnimationSpec.SprintCycleScale * SprintStance / 0.5;

        /// <summary>The part of a sprint cycle each foot is on the ground; between the steps both are in the air.</summary>
        private const double SprintStance = 0.3;

        /// <summary>
        /// Sprinting, for running fast (exoskeletons): long bounding strides with both feet off the ground between
        /// them, the body leaning well into it, the arms pumping hard with the elbows bent, the tail streaming out
        /// behind. Feet land on frames 1 and 7, as in the run. The wings stay as they are.
        /// </summary>
        private PoseResult Sprint(double t)
        {
            double ph = MathUtil.Tau * t;
            // lowest as a foot takes the weight, highest in the air between steps
            double bounce = -Math.Cos(2 * ph - 0.6);
            double lean = 0.5 + 0.03 * bounce;
            double hipTwist = 0.14 * Math.Sin(ph);
            double twist = 0.10 * Math.Sin(ph);
            double hipRoll = 0.04 * Math.Sin(ph);
            Vec3 shift = new Vec3(-0.015 * legScale * Math.Sin(ph), 0, 0);
            var pelvis = new Vec3(shift.X, shift.Y, rig.HipsHeight - 0.09 * rig.LegLength + 0.02 * legScale * bounce);
            HumanoidPoseBuilder b = Torso(pelvis, lean, twist, -hipTwist,
                headPitch: -0.3 - 0.03 * bounce, headYaw: -twist * 0.5,
                hipRoll: hipRoll, chestRoll: -0.55 * hipRoll, headRoll: -0.1 * hipRoll);
            foreach ((int side, double phase) in new[] { (-1, t), (1, t + 0.5) })
            {
                double phi = MathUtil.PositiveModulo(phase, 1.0);
                double y, z, heelLift, toe;
                if (phi < SprintStance)
                {
                    // on the ground: lands ahead, the body passes over it, pushes off behind on the ball
                    double u = phi / SprintStance;
                    y = SprintStride * legScale * Math.Cos(Math.PI * u);
                    z = 0;
                    heelLift = 0.8 * MathUtil.Smoothstep(0.4, 1.0, u);
                    toe = 0;
                }
                else
                {
                    // in the air: the heel kicks up high behind, then the knee drives forward and the foot reaches out
                    double s = (phi - SprintStance) / (1 - SprintStance);
                    y = -SprintStride * legScale * Math.Cos(Math.PI * s);
                    z = 0.2 * legScale * Math.Sin(Math.PI * Math.Pow(s, 0.7));
                    heelLift = 0.8 * (1 - MathUtil.Smoothstep(0.1, 0.9, s)) + 0.2 * Math.Sin(Math.PI * s);
                    toe = heelLift;
                }
                Vec3 rest = rig.Ball(side);
                var ball = new Vec3(rest.X - side * 0.01 * legScale, rest.Y + 0.02 * legScale + y, rest.Z + z);
                Vec3 forward = rig.FootForward(side);
                b.SetFoot(side, ball, forward, heelLift, forward + Vec3.UnitZ * 0.2, toe);
            }
            Quat chest = ChestFrame(lean, twist);
            foreach ((int side, double phase) in new[] { (-1, t), (1, t + 0.5) })
            {
                double a = Math.Cos(MathUtil.Tau * (phase + 0.5));
                double ahead = Math.Max(a, 0);
                Vec3 s = Shoulder(b, side);
                Vec3 hand = s + chest.Right * (side * (0.08 - 0.05 * ahead) * armScale)
                            + chest.Forward * ((0.12 + 0.20 * a) * armScale)
                            - chest.Up * ((0.22 - 0.12 * a) * armScale);
                b.SetArm(side, hand, -chest.Forward * 0.6 + chest.Right * (side * 0.35) - chest.Up, null, 0.6);
            }
            PoseResult pose = b.Build();
            pose.TailSwing = 0.08;
            pose.TailPhase = ph + Math.PI;
            pose.TailLift = 0.35;
            return pose;
        }

        /// <summary>
        /// The engineer's swing, frame by frame (26 frames, the pick hitting on frame 19, as the game's own): the
        /// arm angle and handle angle, degrees from forward-horizontal (up positive), how far the arm reaches out
        /// (arm lengths) and how far the body leans forward. Frames 1 to 14 hold the pick raised over the shoulder,
        /// rising slowly; 15 to 18 bring it down, faster and faster; 19 to 21 are the blow, bent deep, the point on
        /// the ground in front (<see cref="GroundAlpha"/>); 22 to 26 pull it out and up again.
        /// </summary>
        private static readonly int[] MineArm = { 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 105, 105, 104, 100, 75, 25, -25, 0, 0, 0, -35, -5, 40, 75, 90 };
        private static readonly int[] MineHandle = { 140, 142, 144, 146, 148, 150, 151, 152, 153, 154, 155, 155, 155, 154, 145, 105, 40, -15, 0, 0, 0, -25, 15, 70, 115, 135 };
        private static readonly double[] MineReach = { 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55, 0.55,
            0.6, 0.75, 0.85, 0.9, 0.9, 0.9, 0.9, 0.85, 0.75, 0.65, 0.6, 0.55 };
        private static readonly double[] MineLean = { 0.10, 0.09, 0.08, 0.07, 0.06, 0.05, 0.04, 0.03, 0.02, 0.01, 0.0, -0.01, -0.02, -0.02,
            0.0, 0.2, 0.45, 0.75, 0.95, 0.97, 0.93, 0.8, 0.6, 0.4, 0.25, 0.15 };
        private const int FirstDigFrame = 18, LastDigFrame = 20;

        /// <summary>The engineer's mining: one-handed, the pick swung down from over the shoulder, striking on frame 19.</summary>
        private PoseResult Mine(double t)
        {
            int i = (int)(Math.Round(t * MineHandle.Length, MidpointRounding.ToEven) % MineHandle.Length);
            double lean = MineLean[i];
            double reach = MineReach[i];
            double raise = MathUtil.Smoothstep(40, 150, MineHandle[i]);
            // knees a little bent, so the feet stay planted in the wide stance; crouch and shift the weight
            // forward into the blow
            double crouch = MathUtil.Smoothstep(0.3, 0.6, lean);
            var pelvis = new Vec3(0, -0.02 * legScale + 0.06 * legScale * crouch,
                rig.HipsHeight - 0.10 * rig.LegLength - 0.16 * rig.LegLength * crouch);
            double headPitch = 0.15 - 0.25 * (lean - 0.1);
            HumanoidPoseBuilder b = Torso(pelvis, lean, headPitch: headPitch);
            Quat chest = ChestFrame(lean);
            bool dig = i >= FirstDigFrame && i <= LastDigFrame;
            if (dig)
            {
                // a body too tall for its arm and pick crouches lower, so the point reaches the ground ahead
                double tooHigh = Shoulder(b, 1).Z - 0.85 * PickSwing(reach).radius;
                if (tooHigh > 0)
                {
                    pelvis -= Vec3.UnitZ * Math.Min(tooHigh, 0.25 * rig.LegLength);
                    b = Torso(pelvis, lean, headPitch: headPitch);
                }
            }
            // the point never goes into the ground; in the blow it rests on it, the arm in line with the handle
            double ground = GroundAlpha(Shoulder(b, 1), reach);
            double alpha = dig ? ground : Math.Max(MathUtil.Radians(MineHandle[i]), ground);
            double beta = dig ? ground : MathUtil.Radians(MineArm[i]);
            HoldPick(b, chest, beta, alpha, reach);
            BalancingArm(b, chest, crouch);
            foreach (int side in Sides)
            {
                Vec3 rest = rig.Ball(side);
                var ball = new Vec3(rest.X + side * 0.04 * legScale, rest.Y + (side < 0 ? 0.18 : -0.08) * legScale, rest.Z);
                Vec3 forward = rig.FootForward(side);
                b.SetFoot(side, ball, forward, 0, forward + Vec3.UnitX * (side * 0.2));
            }
            PoseResult pose = b.Build();
            // the tail swings up with the wind-up and down with the blow, for balance
            pose.TailLift = 0.25 * raise - 0.1 * crouch;
            return pose;
        }

        /// <summary>How far back the legs hang while hovering, radians.</summary>
        private const double HoverLegAngle = 0.08;

        /// <summary>
        /// anims.hover: hanging in the air, legs dangling, gently bobbing; <paramref name="flap"/>: the arms beat
        /// as wings (<see cref="AnimationSpec.HoverFlap"/>).
        /// </summary>
        private PoseResult Hover(double t, bool gun, double elevation, double phaseShift, bool flap = false)
        {
            double ph = MathUtil.Tau * t + phaseShift;
            const double lean = 0.10;
            var pelvis = new Vec3(0, 0, standingPelvis + elevation + 0.035 * legScale * Math.Sin(ph));
            HumanoidPoseBuilder b = Torso(pelvis, lean, headPitch: -0.05);
            Quat chest = ChestFrame(lean);
            if (gun)
            {
                HoldGun(b, chest);
            }
            else if (flap)
            {
                SpreadArms(b, chest, HoverSweep(ph), 0.15 + 0.1 * Math.Cos(ph), Math.Cos(ph));
            }
            else
            {
                RelaxedArms(b, chest, 0, 0.03);
            }
            DanglingLegs(b, HoverLegAngle);
            PoseResult pose = b.Build();
            // slow, gentle wingbeats holding the body up; the tail hangs and drifts
            pose.LeftWingSpread = pose.RightWingSpread = 1;
            pose.WingElevation = HoverWings(ph);
            pose.TailSwing = 0.05;
            pose.TailPhase = ph;
            pose.TailLift = -0.1;
            return pose;
        }

        /// <summary>
        /// The arms as wings while hovering, radians forward of straight out sideways: they reach out and forward,
        /// then sweep back, slowly.
        /// </summary>
        private static double HoverSweep(double ph) => 0.1 + 0.45 * Math.Sin(ph);

        /// <summary>The arms as wings while flying: full strokes, forward and back.</summary>
        private static double FlySweep(double ph) => 0.65 * Math.Sin(ph);

        /// <summary>
        /// Both arms out to the sides as wings, <paramref name="sweep"/> radians forward of straight out and
        /// <paramref name="lift"/> above it: a wing stroke reaches out and forward, then sweeps back.
        /// <paramref name="stroke"/> (-1 to 1, sweeping forward positive) bends the wrists back against the motion.
        /// </summary>
        private void SpreadArms(HumanoidPoseBuilder b, Quat chest, double sweep, double lift, double stroke)
        {
            double reach = rig.ArmLength * 0.95;
            foreach (int side in Sides)
            {
                Vec3 s = Shoulder(b, side);
                Vec3 outward = chest.Right * (side * Math.Cos(sweep) * Math.Cos(lift)) + chest.Forward * (Math.Sin(sweep) * Math.Cos(lift))
                    + chest.Up * Math.Sin(lift);
                Vec3 hand = s + outward * reach;
                Vec3 handDirection = (outward - chest.Forward * (0.4 * stroke)).Normalized;
                b.SetArm(side, hand, -chest.Forward + chest.Up * 0.3, handDirection, 0.1);
            }
        }

        /// <summary>The wings while hovering at phase <paramref name="ph"/>: slow, gentle beats.</summary>
        private static double HoverWings(double ph) => 0.1 + 0.35 * Math.Sin(ph);

        /// <summary>
        /// anims.fly: leaning into the flight, legs trailing in the flight direction; <paramref name="flap"/>: the
        /// arms beat as wings (<see cref="AnimationSpec.FlyFlap"/>).
        /// </summary>
        private PoseResult Fly(double t, bool gun, double move, bool flap = false)
        {
            double ph = MathUtil.Tau * t;
            const double lean = 0.55;
            var pelvis = new Vec3(0, 0, standingPelvis + Conventions.FlyHeight + 0.03 * legScale * Math.Sin(ph));
            HumanoidPoseBuilder b = Torso(pelvis, lean, hipTwist: -move, headPitch: -0.45);
            Quat chest = ChestFrame(lean);
            if (gun)
            {
                HoldGun(b, chest);
            }
            else if (flap)
            {
                SpreadArms(b, chest, FlySweep(ph), 0.1 + 0.1 * Math.Cos(ph), Math.Cos(ph));
            }
            else
            {
                RelaxedArms(b, chest, 0, 0.02);
            }
            DanglingLegs(b, lean + 0.1, move);
            PoseResult pose = b.Build();
            // full wingbeats, one per loop; the tail streams out behind, rippling
            pose.LeftWingSpread = pose.RightWingSpread = 1;
            pose.WingElevation = 0.15 + 0.6 * Math.Sin(ph);
            pose.TailSwing = 0.06;
            pose.TailPhase = 2 * ph;
            pose.TailLift = 0.25;
            return pose;
        }

        /// <summary>
        /// anims.take_off: crouch, spring up, and end exactly in the first hover frame. Empty-handed: Factorio has
        /// one take-off (and landing, the same played backwards) for armed and unarmed alike, and a gun appearing
        /// for it read as the idle-with-gun pose flashing up.
        /// </summary>
        /// <remarks><paramref name="flap"/>: the arms open out as wings while crouching and beat hard once to lift
        /// off, ending in the first <see cref="AnimationSpec.HoverFlap"/> frame (<see cref="AnimationSpec.TakeOffFlap"/>).</remarks>
        private PoseResult TakeOff(double t, bool flap = false)
        {
            double crouch = MathUtil.Smoothstep(0.0, 0.28, t) * (1 - MathUtil.Smoothstep(0.28, 0.55, t));
            double lift = MathUtil.Smoothstep(0.3, 1.0, t);
            double lean = 0.03 + 0.25 * crouch + 0.07 * lift;
            var pelvis = new Vec3(0, -0.005 * legScale * (1 - lift),
                standingPelvis - 0.11 * legScale * crouch + Conventions.FlyHeight * lift);
            HumanoidPoseBuilder b = Torso(pelvis, lean, headPitch: -0.05 * lift);
            if (flap)
            {
                double open = MathUtil.Smoothstep(0, 0.6, t);
                // from hanging at the sides out to the first hover-flap frame, with one hard stroke forward and back
                double armLift = -1.3 * (1 - open) + 0.25 * open;
                double sweep = (HoverSweep(0) + 0.5 * Math.Sin(2 * Math.PI * MathUtil.Smoothstep(0.3, 1, t))) * open;
                SpreadArms(b, ChestFrame(lean), sweep, armLift, t < 0.3 ? 1 : Math.Cos(2 * Math.PI * MathUtil.Smoothstep(0.3, 1, t)));
            }
            else
            {
                RelaxedArms(b, ChestFrame(lean), 0, 0.03);
            }
            if (lift < 0.05)
            {
                StandingFeet(b, 0.01);
            }
            else
            {
                DanglingLegs(b, HoverLegAngle, 0, MathUtil.Smoothstep(0.05, 0.7, lift));
            }
            PoseResult pose = b.Build();
            // the wings open out while crouching and beat hard once to lift off, ending as the first hover frame's
            pose.LeftWingSpread = pose.RightWingSpread = MathUtil.Smoothstep(0, 0.6, t);
            pose.WingElevation = HoverWings(0) + 0.5 * Math.Sin(Math.PI * t);
            pose.TailLift = -0.1 * lift;
            return pose;
        }

        /// <summary>
        /// The corpse: a relaxed pose turned to face the camera (south) and laid on its side, centred on
        /// the entity position; the renderer lowers it onto the ground.
        /// </summary>
        private PoseResult Corpse()
        {
            var pelvis = new Vec3(0, 0, standingPelvis);
            HumanoidPoseBuilder b = Torso(pelvis, 0.15, headPitch: 0.2, headYaw: 0.3);
            Quat chest = ChestFrame(0.15);
            foreach (int side in Sides)
            {
                Vec3 s = Shoulder(b, side);
                Vec3 hand = s + chest.Forward * (0.25 * armScale) - chest.Up * (0.25 * armScale) + chest.Right * (side * 0.1 * armScale);
                b.SetArm(side, hand, -chest.Forward + chest.Right * (side * 0.2) - chest.Up, null, 0.4);
                Vec3 hip = HipJoint(b, side);
                Vec3 ball = hip + new Vec3(side * 0.02 * legScale, (side > 0 ? 0.25 : 0.05) * legScale, -0.8 * rig.LegLength);
                b.SetFoot(side, ball, rig.FootForward(side), 0.3, Vec3.UnitY);
            }
            b.SetGroundToLowestPoint(true);
            PoseResult standing = b.Build();
            // face the camera (south), then lie down on the left side: up becomes east
            Quat lying = Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2) * Quat.FromAxisAngle(Vec3.UnitZ, Math.PI);
            // the figure now runs from the feet at x = -pelvis height to the head at x = top - pelvis height
            var centre = new Vec3(pelvis.Z - rig.HeadTopHeight / 2, 0, 0);
            return PoseTransforms.Rigid(standing, lying, pelvis, centre);
        }

        /// <summary>Every animation's frames, for tests and previews: (animation, row, frame) -> pose.</summary>
        public IEnumerable<(string animation, int row, int frame, PoseResult pose)> AllFrames(bool flight)
        {
            foreach (AnimationInfo a in AnimationSpec.ForPack(flight))
            {
                for (int row = 0; row < a.DirectionCount; row++)
                {
                    double move = AnimationSpec.Direction(a.Name, row).Move;
                    for (int f = 0; f < a.FrameCount; f++)
                    {
                        yield return (a.Name, row, f, Pose(a.Name, AnimationSpec.FramePhase(a.Name, f), move));
                    }
                }
            }
        }
    }
}
