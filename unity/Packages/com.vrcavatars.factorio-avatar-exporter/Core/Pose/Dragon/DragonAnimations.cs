using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The dragon's animations: a literal port of tools/dragon_render/anims.py in double precision,
    /// without the tail and wing calls (<c>set_tail</c>, <c>set_wings</c>), which change nothing else.
    /// <c>t</c> is the animation time (<see cref="AnimationSpec.FramePhase"/>): looping animations repeat
    /// every 1.0. Angles are radians, lengths tiles, in model space (x = right, y = forward, z = up).
    /// The building blocks (<see cref="RelaxedArms"/>, <see cref="StandingLegs"/>,
    /// <see cref="DanglingLegs"/>, <see cref="Turn"/>) are private (underscored) helpers in anims.py,
    /// public here as references for other characters' animations.
    /// </summary>
    public static class DragonAnimations
    {
        /// <summary><c>MINE_ALPHA</c>: pickaxe handle angle of each mine frame, degrees (<see cref="DragonModel.HoldPick"/>).</summary>
        public static readonly IReadOnlyList<int> MineAlpha = new[] { -30, 5, 45, 90, 125, 148, 152, 75, -58, -62, -57, -44 };

        /// <summary>
        /// <c>MINE_LEAN</c>: torso lean of each mine frame (positive leans back, see <see cref="DragonModel.TorsoFrame"/>):
        /// back a little for the wind-up, forward into the blow.
        /// </summary>
        public static readonly IReadOnlyList<double> MineLean = new[]
        {
            -0.08, 0.04, 0.08, 0.03, -0.05, -0.09, -0.09, -0.05, -0.30, -0.34, -0.30, -0.18,
        };

        /// <summary><c>MINE_FRAMES</c>: frames of one swing (12, as <see cref="AnimationSpec.Mine"/> renders).</summary>
        public static int MineFrames => MineAlpha.Count;

        /// <summary>1-based frame of the dragon's 12-frame swing in which the pick hits the ground.</summary>
        public const int MineImpactFrame = 9;

        /// <summary>
        /// <c>_relaxed_arms(p, body, swing=0.0, spread=0.0)</c>: both arms hanging slightly forward and
        /// out, the hands about 0.4 below the shoulders. <paramref name="swing"/> moves the right hand
        /// forward and the left hand back (tiles along the torso forward); <paramref name="spread"/> moves
        /// both hands outwards.
        /// </summary>
        public static void RelaxedArms(DragonPose p, BodyShape body, double swing = 0.0, double spread = 0.0)
        {
            foreach (int side in DragonModel.Sides)
            {
                Vec3 s = DragonModel.Shoulder(p, body, side);
                Vec3 target = s + p.TorsoRight * side * (0.055 + spread) + p.TorsoForward * (0.07 + swing * side)
                    - p.TorsoUp * 0.40;
                DragonModel.SetArm(p, body, side, target, -p.TorsoForward + p.TorsoRight * side * 0.35);
            }
        }

        /// <summary>
        /// <c>_standing_legs(p, body, stance=0.0, front=0.0)</c>: both feet on the ground, slightly
        /// forward. <paramref name="stance"/> widens the stance; <paramref name="front"/> moves the right
        /// foot forward and the left foot back.
        /// </summary>
        public static void StandingLegs(DragonPose p, BodyShape body, double stance = 0.0, double front = 0.0)
        {
            RequireBody(body);
            foreach (int side in DragonModel.Sides)
            {
                var ball = new Vec3(side * (body.HipWidth + 0.018 + stance), 0.07 + front * side, 0.035);
                DragonModel.SetLeg(p, body, side, ball);
            }
        }

        /// <summary>
        /// <c>_turn(v, move)</c>: turns a model space vector clockwise (seen from above) by
        /// <paramref name="move"/> radians about the vertical axis through the origin.
        /// </summary>
        public static Vec3 Turn(Vec3 v, double move) =>
            move != 0 ? MathUtil.RotAbout(v, new Vec3(0, 0, 0), Vec3.UnitZ, -move) : v;

        /// <summary>
        /// <c>idle(body, t, gun=False)</c>: standing and breathing, slightly leaning back, the head
        /// nodding and turning a little. With <paramref name="gun"/> it holds a gun, otherwise the arms hang.
        /// </summary>
        public static DragonPose Idle(BodyShape body, double t, bool gun = false)
        {
            RequireBody(body);
            double b = Math.Sin(MathUtil.Tau * t);
            var pel = new Vec3(0, -0.005, 0.70 + 0.005 * b);
            DragonPose p = DragonModel.BuildPose(pel, lean: 0.05, breath: b,
                headPitch: -0.03 + 0.025 * Math.Sin(MathUtil.Tau * t + 1.2), headYaw: 0.05 * Math.Sin(MathUtil.Tau * t));
            if (gun)
            {
                DragonModel.HoldGun(p, body);
            }
            else
            {
                RelaxedArms(p, body, swing: 0.0);
            }
            StandingLegs(p, body);
            return p;
        }

        /// <summary>
        /// <c>run(body, t, gun=False, move=0.0)</c>: one running stride per cycle; the left foot is at the
        /// front at t = 0, the right one half a cycle later. <paramref name="move"/> is the running direction
        /// relative to the facing (gun) direction, clockwise radians: the hips and legs run that way while
        /// the torso, head and gun keep facing forward.
        /// </summary>
        public static DragonPose Run(BodyShape body, double t, bool gun = false, double move = 0.0)
        {
            RequireBody(body);
            double ph = MathUtil.Tau * t;
            var pel = new Vec3(0, 0.0, 0.675 - 0.028 * Math.Cos(2 * ph));
            DragonPose p = DragonModel.BuildPose(pel, lean: !gun ? 0.27 : 0.18, twist: 0.10 * Math.Sin(ph),
                hipTwist: -0.12 * Math.Sin(ph) - move, headPitch: !gun ? -0.12 : -0.06);
            foreach ((int side, double phase) in StridePhases(t))
            {
                double phi = MathUtil.PositiveModulo(phase, 1.0);
                // as long a stride as the legs reach: the planted foot slides less
                double y = 0.05 + 0.30 * Math.Cos(MathUtil.Tau * phi);
                double swing = phi > 0.5 ? Math.Sin(MathUtil.Tau * (phi - 0.5)) : 0.0;
                double z = 0.035 + 0.16 * swing;
                Vec3 ball = Turn(new Vec3(side * (body.HipWidth + 0.012), y, z), move);
                DragonModel.SetLeg(p, body, side, ball, heelLift: 0.13 + 0.05 * swing, toePitch: 0.5 * swing);
            }
            if (gun)
            {
                DragonModel.HoldGun(p, body);
            }
            else
            {
                // the arms use the phase without wrapping it, unlike the legs
                foreach ((int side, double phi) in StridePhases(t))
                {
                    double a = Math.Cos(MathUtil.Tau * (phi + 0.5));
                    Vec3 s = DragonModel.Shoulder(p, body, side);
                    Vec3 target = s + p.TorsoRight * side * 0.07 + p.TorsoForward * (0.10 + 0.20 * a)
                        - p.TorsoUp * (0.30 - 0.06 * a);
                    DragonModel.SetArm(p, body, side, target,
                        -p.TorsoForward * 1.0 + p.TorsoRight * side * 0.3 - p.TorsoUp * 0.2);
                }
            }
            return p;
        }

        /// <summary>
        /// <c>mine(body, t)</c>: swinging a pickaxe, one swing per cycle. The frame is
        /// <c>int(round(t * MINE_FRAMES)) % MINE_FRAMES</c> with Python's rounding (halves to even) and
        /// modulo (never negative), so the pose only changes at frame boundaries; the pick hits the ground in
        /// <see cref="AnimationSpec.MineImpactFrame"/>.
        /// </summary>
        public static DragonPose Mine(BodyShape body, double t)
        {
            RequireBody(body);
            int i = (int)MathUtil.PositiveModulo(Math.Round(t * MineFrames, MidpointRounding.ToEven), MineFrames);
            double alpha = MathUtil.Radians(MineAlpha[i]);
            double lean = MineLean[i];
            double raise = MathUtil.Smoothstep(40, 150, MineAlpha[i]);
            // crouch into the blow so the pick reaches the ground in front of the feet
            double crouch = MathUtil.Smoothstep(0.1, 0.3, -lean);
            var pel = new Vec3(0, -0.02 + 0.04 * crouch, 0.665 - 0.10 * crouch + 0.03 * raise);
            DragonPose p = DragonModel.BuildPose(pel, lean: lean, headPitch: -0.15 + 0.25 * (lean - 0.1));
            DragonModel.HoldPick(p, body, alpha);
            foreach (int side in DragonModel.Sides)
            {
                var ball = new Vec3(side * (body.HipWidth + 0.055), side < 0 ? 0.15 : -0.05, 0.035);
                DragonModel.SetLeg(p, body, side, ball);
            }
            return p;
        }

        /// <summary>
        /// <c>_dangling_legs(p, body, back=0.0, move=0.0)</c>: legs hanging below the hips in the air, toes
        /// pointing down. <paramref name="back"/> swings the feet backwards (tiles) and points the toes
        /// further down; <paramref name="move"/> turns the feet as in <see cref="Run"/>.
        /// </summary>
        public static void DanglingLegs(DragonPose p, BodyShape body, double back = 0.0, double move = 0.0)
        {
            foreach (int side in DragonModel.Sides)
            {
                Vec3 hip = DragonModel.HipJoint(p, body, side);
                Vec3 ball = hip + Turn(new Vec3(side * 0.025, 0.05 - back, -0.52 + back * 0.25), move);
                DragonModel.SetLeg(p, body, side, ball, heelLift: 0.16, heelBack: 0.03 + back * 0.1,
                    toePitch: 0.9 + back);
            }
        }

        /// <summary>
        /// <c>hover(body, t, gun=False, elevation=FLY_HEIGHT, phase_shift=0.0)</c>: flying in place,
        /// <paramref name="elevation"/> above the standing height (default <see cref="Conventions.FlyHeight"/>),
        /// bobbing with the wing beat (phase <c>TAU * t + phase_shift</c>), the legs dangling.
        /// </summary>
        public static DragonPose Hover(BodyShape body, double t, bool gun = false,
            double elevation = Conventions.FlyHeight, double phaseShift = 0.0)
        {
            RequireBody(body);
            double ph = MathUtil.Tau * t + phaseShift;
            var pel = new Vec3(0, 0, 0.70 + elevation + 0.035 * Math.Sin(ph));
            DragonPose p = DragonModel.BuildPose(pel, lean: 0.10, headPitch: -0.05);
            if (gun)
            {
                DragonModel.HoldGun(p, body);
            }
            else
            {
                RelaxedArms(p, body, spread: 0.03);
            }
            DanglingLegs(p, body);
            return p;
        }

        /// <summary>
        /// <c>fly(body, t, gun=False, move=0.0)</c>: flying, <see cref="Conventions.FlyHeight"/> up, the
        /// torso tilted far (lean 0.55) and the legs trailing. <paramref name="move"/> as in <see cref="Run"/>:
        /// the hips and legs point along the flight direction.
        /// </summary>
        public static DragonPose Fly(BodyShape body, double t, bool gun = false, double move = 0.0)
        {
            RequireBody(body);
            double ph = MathUtil.Tau * t;
            var pel = new Vec3(0, 0, 0.70 + Conventions.FlyHeight + 0.03 * Math.Sin(ph));
            DragonPose p = DragonModel.BuildPose(pel, lean: 0.55, hipTwist: -move, headPitch: -0.35);
            if (gun)
            {
                DragonModel.HoldGun(p, body);
            }
            else
            {
                RelaxedArms(p, body, swing: 0.0, spread: 0.02);
            }
            DanglingLegs(p, body, back: 0.28, move: move);
            return p;
        }

        /// <summary>
        /// <c>take_off(body, t)</c>: t in [0, 1], from standing (t = 0) through a crouch to the first frame of
        /// the <see cref="Hover"/> cycle (t = 1, identical to <c>Hover(body, 0)</c> for every ported field).
        /// </summary>
        public static DragonPose TakeOff(BodyShape body, double t)
        {
            RequireBody(body);
            double crouch = MathUtil.Smoothstep(0.0, 0.28, t) * (1 - MathUtil.Smoothstep(0.28, 0.55, t));
            double lift = MathUtil.Smoothstep(0.3, 1.0, t);
            double spread = MathUtil.Smoothstep(0.2, 0.6, t);
            var pel = new Vec3(0, 0, 0.70 - 0.11 * crouch + Conventions.FlyHeight * lift);
            DragonPose p = DragonModel.BuildPose(pel, lean: 0.05 + 0.25 * crouch + 0.05 * lift, headPitch: -0.05);
            RelaxedArms(p, body, spread: 0.03 * spread);
            if (lift < 0.05)
            {
                StandingLegs(p, body);
            }
            else
            {
                foreach (int side in DragonModel.Sides)
                {
                    Vec3 hip = DragonModel.HipJoint(p, body, side);
                    var groundBall = new Vec3(side * (body.HipWidth + 0.018), 0.07, 0.035);
                    Vec3 dangle = hip + new Vec3(side * 0.025, 0.05, -0.52);
                    Vec3 ball = Vec3.Lerp(groundBall, dangle, MathUtil.Smoothstep(0.05, 0.7, lift));
                    DragonModel.SetLeg(p, body, side, ball, heelLift: 0.13 + 0.03 * lift,
                        heelBack: 0.10 - 0.07 * lift, toePitch: 0.9 * lift);
                }
            }
            return p;
        }

        /// <summary>
        /// <c>corpse_pose(body)</c>: the pose the corpse sprite is rendered from. The Python renderer then
        /// turns the whole model onto its side (generate.py's corpse_transform); this pose is upright.
        /// </summary>
        public static DragonPose CorpsePose(BodyShape body)
        {
            RequireBody(body);
            var pel = new Vec3(0, 0, 0.66);
            DragonPose p = DragonModel.BuildPose(pel, lean: 0.15, headPitch: 0.25, headYaw: 0.3);
            foreach (int side in DragonModel.Sides)
            {
                Vec3 s = DragonModel.Shoulder(p, body, side);
                DragonModel.SetArm(p, body, side,
                    s + p.TorsoForward * 0.25 - p.TorsoUp * 0.18 + p.TorsoRight * side * 0.05,
                    -p.TorsoForward + p.TorsoRight * side * 0.2 - p.TorsoUp);
            }
            foreach (int side in DragonModel.Sides)
            {
                Vec3 hip = DragonModel.HipJoint(p, body, side);
                Vec3 ball = hip + new Vec3(side * 0.02, 0.18 + 0.06 * side, -0.38);
                DragonModel.SetLeg(p, body, side, ball, heelLift: 0.10, heelBack: 0.12);
            }
            return p;
        }

        /// <summary>The (side, phase) pairs run() loops over: the left side at t, the right half a cycle later.</summary>
        private static (int side, double phase)[] StridePhases(double t) => new[] { (-1, t), (1, t + 0.5) };

        private static void RequireBody(BodyShape body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
        }
    }
}
