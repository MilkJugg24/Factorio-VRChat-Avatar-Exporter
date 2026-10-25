using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The posing half of tools/dragon_render/model.py, ported literally in double precision (same
    /// formulas, same order of operations) so poses match the Python renderer to about 1e-12. The tail,
    /// the wings and the geometry (<c>add_*</c>) are not ported. Model space: x = right, y = forward,
    /// z = up, tiles. Sides are +1 (right) and -1 (left), as in model.py.
    /// </summary>
    public static class DragonModel
    {
        /// <summary><c>HEAD_SCALE</c>: the renderer draws the head this much larger than its base measurements.</summary>
        public const double HeadScale = 1.28;

        /// <summary>Upper arm length of <see cref="SetArm"/>.</summary>
        public const double UpperArmLength = 0.235;

        /// <summary>Forearm length of <see cref="SetArm"/>.</summary>
        public const double ForearmLength = 0.215;

        /// <summary>Thigh length of <see cref="SetLeg"/> (hip to knee).</summary>
        public const double ThighLength = 0.300;

        /// <summary>Shin length of <see cref="SetLeg"/> (knee to ankle).</summary>
        public const double ShinLength = 0.300;

        /// <summary>The sides, in the order model.py loops over them: right (+1), then left (-1).</summary>
        public static readonly IReadOnlyList<int> Sides = new[] { 1, -1 };

        /// <summary>
        /// <c>torso_frame(lean, twist=0.0, roll=0.0)</c>: a (right, forward, up) frame of unit vectors.
        /// <paramref name="lean"/> (radians) turns about x; as in model.py, a POSITIVE lean tilts up
        /// BACKWARDS (towards -y) and forward upwards, the opposite of what the name suggests: the dragon's
        /// "lean = 0.27" while running leans its torso back. <paramref name="twist"/> then turns the frame
        /// about its up vector (positive = counterclockwise seen from above, towards its left), and
        /// <paramref name="roll"/> about its forward vector (positive = up tilts towards the right).
        /// Twists and rolls of at most 1e-9 are skipped, like model.py.
        /// </summary>
        public static (Vec3 right, Vec3 forward, Vec3 up) TorsoFrame(double lean, double twist = 0.0, double roll = 0.0)
        {
            Vec3 up = MathUtil.Rot(Vec3.UnitZ, Vec3.UnitX, lean);
            Vec3 forward = MathUtil.Rot(Vec3.UnitY, Vec3.UnitX, lean);
            Vec3 right = Vec3.UnitX;
            if (Math.Abs(twist) > 1e-9)
            {
                forward = MathUtil.Rot(forward, up, twist);
                right = MathUtil.Rot(right, up, twist);
            }
            if (Math.Abs(roll) > 1e-9)
            {
                up = MathUtil.Rot(up, forward, roll);
                right = MathUtil.Rot(right, forward, roll);
            }
            return (right, forward, up);
        }

        /// <summary>
        /// <c>build_pose(body, pelvis, lean=0.0, twist=0.0, roll=0.0, hip_twist=0.0, breath=0.0,
        /// head_pitch=0.0, head_yaw=0.0, head_roll=0.0)</c>: places the pelvis, spine, neck and head; the
        /// limbs are set afterwards. (model.py also takes the body, which build_pose does not use.)
        /// The torso frame is <c>TorsoFrame(lean, twist, roll)</c>, the hip frame
        /// <c>TorsoFrame(lean * 0.5, hip_twist, roll * 0.5)</c>, the head frame
        /// <c>TorsoFrame(lean * 0.35 + head_pitch, twist * 0.5 + head_yaw, head_roll)</c>; angles in radians
        /// (see <see cref="TorsoFrame"/> for their signs), <paramref name="breath"/> as in
        /// <see cref="DragonPose.Breath"/>.
        /// </summary>
        public static DragonPose BuildPose(Vec3 pelvis, double lean = 0.0, double twist = 0.0, double roll = 0.0,
            double hipTwist = 0.0, double breath = 0.0, double headPitch = 0.0, double headYaw = 0.0,
            double headRoll = 0.0)
        {
            var p = new DragonPose { Pelvis = pelvis, Breath = breath };
            (Vec3 tr, Vec3 tf, Vec3 tu) = TorsoFrame(lean, twist, roll);
            p.TorsoRight = tr;
            p.TorsoForward = tf;
            p.TorsoUp = tu;
            (Vec3 hipR, Vec3 hipF, Vec3 hipU) = TorsoFrame(lean * 0.5, hipTwist, roll * 0.5);
            p.HipRight = hipR;
            p.HipForward = hipF;
            p.HipUp = hipU;
            p.Waist = pelvis + tu * 0.165 + tf * 0.008;
            p.Chest = pelvis + tu * 0.345 + tf * 0.012 + tu * breath * 0.006;
            p.NeckBase = pelvis + tu * 0.475 + tf * 0.03;
            // the neck curves forward; the head stays closer to level than the torso
            Vec3 neckDir = (tu * 0.85 + tf * 0.5).Normalized;
            p.NeckTop = p.NeckBase + neckDir * 0.13;
            (Vec3 hr, Vec3 hf, Vec3 hu) = TorsoFrame(lean * 0.35 + headPitch, twist * 0.5 + headYaw, headRoll);
            p.HeadRight = hr;
            p.HeadForward = hf;
            p.HeadUp = hu;
            p.Head = p.NeckTop + hu * 0.075 + hf * 0.035;
            return p;
        }

        /// <summary><c>shoulder(p, body, side)</c>: the shoulder joint of a side.</summary>
        public static Vec3 Shoulder(DragonPose p, BodyShape body, int side)
        {
            CheckPoseAndBody(p, body);
            CheckSide(side);
            return p.Chest + p.TorsoRight * (side * body.ShoulderWidth) + p.TorsoUp * (0.105 + p.Breath * 0.004)
                - p.TorsoForward * 0.005;
        }

        /// <summary>
        /// <c>set_arm(p, body, side, hand_target, pole, hand_dir=None)</c>: two-bone IK
        /// (<see cref="Ik.TwoBone"/>, <see cref="UpperArmLength"/> and <see cref="ForearmLength"/>) from the
        /// shoulder to <paramref name="handTarget"/>, the elbow bending towards the direction
        /// <paramref name="pole"/>. <paramref name="handDir"/> defaults to the forearm's direction.
        /// </summary>
        public static void SetArm(DragonPose p, BodyShape body, int side, Vec3 handTarget, Vec3 pole, Vec3? handDir = null)
        {
            Vec3 s = Shoulder(p, body, side);
            (Vec3 elbow, Vec3 hand) = Ik.TwoBone(s, handTarget, UpperArmLength, ForearmLength, pole);
            p.Arms[side] = new DragonArm(s, elbow, hand, handDir ?? (hand - elbow).Normalized);
        }

        /// <summary><c>hip_joint(p, body, side)</c>: the hip joint of a side.</summary>
        public static Vec3 HipJoint(DragonPose p, BodyShape body, int side)
        {
            CheckPoseAndBody(p, body);
            CheckSide(side);
            return p.Pelvis + p.HipRight * (side * body.HipWidth) - p.HipUp * 0.035;
        }

        /// <summary>
        /// <c>set_leg(p, body, side, ball, heel_lift=0.13, heel_back=0.10, toe_pitch=0.0, pole=None)</c>:
        /// puts the ball of the foot at <paramref name="ball"/> with the heel <paramref name="heelLift"/>
        /// above and <paramref name="heelBack"/> behind it (along the hips' horizontal forward), reaching the
        /// ankle with two-bone IK (<see cref="ThighLength"/>, <see cref="ShinLength"/>). The knee bends
        /// towards <paramref name="pole"/>, by default forward and a quarter outwards. The toes point
        /// forward, pitched down by <paramref name="toePitch"/> radians.
        /// </summary>
        public static void SetLeg(DragonPose p, BodyShape body, int side, Vec3 ball, double heelLift = 0.13,
            double heelBack = 0.10, double toePitch = 0.0, Vec3? pole = null)
        {
            Vec3 hip = HipJoint(p, body, side);
            Vec3 fwd = new Vec3(p.HipForward.X, p.HipForward.Y, 0.0).Normalized;
            Vec3 ankleTarget = ball - fwd * heelBack + Vec3.UnitZ * heelLift;
            Vec3 kneePole = pole ?? (fwd + p.HipRight * side * 0.25);
            (Vec3 knee, Vec3 ankle) = Ik.TwoBone(hip, ankleTarget, ThighLength, ShinLength, kneePole);
            Vec3 toeDir = MathUtil.Rot(fwd, Vec3.Cross(fwd, Vec3.UnitZ) * -1.0, toePitch).Normalized;
            Vec3 toe = ball + toeDir * 0.085 - Vec3.UnitZ * 0.012;
            p.Legs[side] = new DragonLeg(hip, knee, ankle, ball, toe);
        }

        /// <summary>
        /// <c>hold_gun(p, body, aim_pitch=0.0)</c>: a gun in front of the chest, aimed along the torso's
        /// horizontal forward (pitched down by <paramref name="aimPitch"/> radians), held with both hands.
        /// Sets both arms and <see cref="DragonPose.Prop"/> (a <see cref="DragonGun"/>). The left hand's grip
        /// near the middle of the gun is out of the arm's reach in every dragon animation, so that arm ends
        /// stretched up to 0.1 short of it, as in model.py.
        /// </summary>
        public static void HoldGun(DragonPose p, BodyShape body, double aimPitch = 0.0)
        {
            CheckPoseAndBody(p, body);
            Vec3 aim = new Vec3(p.TorsoForward.X, p.TorsoForward.Y, 0.0).Normalized;
            if (aimPitch != 0)
            {
                aim = MathUtil.Rot(aim, Vec3.Cross(aim, Vec3.UnitZ), -aimPitch);
            }
            Vec3 side = Vec3.Cross(aim, Vec3.UnitZ).Normalized;
            Vec3 stock = p.Chest + side * 0.055 + aim * 0.075 - Vec3.UnitZ * 0.075;
            Vec3 muzzle = stock + aim * 0.52;
            Vec3 gripR = stock + aim * 0.13 - Vec3.UnitZ * 0.035;
            Vec3 gripL = stock + aim * 0.31 - Vec3.UnitZ * 0.02;
            SetArm(p, body, 1, gripR, -p.TorsoForward * 0.4 + p.TorsoRight * 0.6 - Vec3.UnitZ * 0.8, aim);
            SetArm(p, body, -1, gripL, -p.TorsoForward * 0.2 - p.TorsoRight * 0.7 - Vec3.UnitZ * 0.8, aim);
            p.Prop = new DragonGun(stock, muzzle, aim);
        }

        /// <summary><c>PICK_LENGTH</c>: handle length from the upper hand to the head, long enough that the blade
        /// reaches the ground in front of the feet at the blow.</summary>
        public const double PickLength = 0.70;

        /// <summary>
        /// <c>hold_pick(p, body, alpha)</c>: a pickaxe held with both hands, its handle at
        /// <paramref name="alpha"/> radians from the torso's horizontal forward, up positive (past pi/2 it
        /// points backwards over the head). Sets both arms and <see cref="DragonPose.Prop"/>
        /// (a <see cref="DragonPick"/>).
        /// </summary>
        public static void HoldPick(DragonPose p, BodyShape body, double alpha)
        {
            CheckPoseAndBody(p, body);
            Vec3 fwd = new Vec3(p.TorsoForward.X, p.TorsoForward.Y, 0.0).Normalized;
            Vec3 d = fwd * Math.Cos(alpha) + Vec3.UnitZ * Math.Sin(alpha);
            Vec3 e = -fwd * Math.Sin(alpha) + Vec3.UnitZ * Math.Cos(alpha);
            Vec3 pivot = p.Chest + p.TorsoUp * 0.09 + fwd * 0.03;
            Vec3 grip = pivot + d * 0.33;
            Vec3 side = Vec3.Cross(fwd, Vec3.UnitZ).Normalized;
            SetArm(p, body, 1, grip + side * 0.035, -fwd * 0.3 + side * 0.8 - Vec3.UnitZ * 0.5, d);
            SetArm(p, body, -1, grip - side * 0.035 - d * 0.07, -fwd * 0.3 - side * 0.8 - Vec3.UnitZ * 0.5, d);
            Vec3 basePoint = grip - d * 0.10;
            Vec3 top = grip + d * PickLength;
            p.Prop = new DragonPick(basePoint, top, d, e);
        }

        private static void CheckPoseAndBody(DragonPose p, BodyShape body)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
        }

        private static void CheckSide(int side)
        {
            if (side != 1 && side != -1)
            {
                throw new ArgumentOutOfRangeException(nameof(side), side, "side must be +1 (right) or -1 (left)");
            }
        }
    }
}
