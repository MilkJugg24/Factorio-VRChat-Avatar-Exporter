using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>One arm of a <see cref="DragonPose"/>: model.py's <c>(shoulder, elbow, hand, hand_dir)</c>.</summary>
    public readonly struct DragonArm
    {
        public DragonArm(Vec3 shoulder, Vec3 elbow, Vec3 hand, Vec3 handDirection)
        {
            Shoulder = shoulder;
            Elbow = elbow;
            Hand = hand;
            HandDirection = handDirection;
        }

        public Vec3 Shoulder { get; }
        public Vec3 Elbow { get; }

        /// <summary>The wrist, where the hand starts.</summary>
        public Vec3 Hand { get; }

        /// <summary><c>hand_dir</c>: unit vector the hand and claws point along.</summary>
        public Vec3 HandDirection { get; }
    }

    /// <summary>One leg of a <see cref="DragonPose"/>: model.py's <c>(hip, knee, ankle, ball, toe)</c>.</summary>
    public readonly struct DragonLeg
    {
        public DragonLeg(Vec3 hip, Vec3 knee, Vec3 ankle, Vec3 ball, Vec3 toe)
        {
            Hip = hip;
            Knee = knee;
            Ankle = ankle;
            Ball = ball;
            Toe = toe;
        }

        public Vec3 Hip { get; }
        public Vec3 Knee { get; }

        /// <summary>The heel joint (the dragon walks on its toes, so it is off the ground).</summary>
        public Vec3 Ankle { get; }

        /// <summary>The ball of the foot, as asked for (the IK may leave the ankle short of it).</summary>
        public Vec3 Ball { get; }

        /// <summary>The tip of the toes.</summary>
        public Vec3 Toe { get; }
    }

    /// <summary>
    /// A posed dragon skeleton: a port of model.py's <c>Pose</c> without the tail and wings. Points are in
    /// model space (x = right, y = forward, z = up, the feet on z = 0), tiles; the frames are unit
    /// vectors. Build one with <see cref="DragonModel.BuildPose"/> and the <c>Set*</c> / <c>Hold*</c>
    /// functions, or take a finished one from <see cref="DragonPoses.Pose"/>.
    /// </summary>
    public sealed class DragonPose
    {
        /// <summary><c>pelvis</c>: centre of the pelvis.</summary>
        public Vec3 Pelvis { get; set; }

        /// <summary><c>tr</c>: torso right.</summary>
        public Vec3 TorsoRight { get; set; }

        /// <summary><c>tf</c>: torso forward.</summary>
        public Vec3 TorsoForward { get; set; }

        /// <summary><c>tu</c>: torso up.</summary>
        public Vec3 TorsoUp { get; set; }

        /// <summary><c>hip_r</c>: pelvis frame right (the hips and legs use this frame).</summary>
        public Vec3 HipRight { get; set; }

        /// <summary><c>hip_f</c>: pelvis frame forward.</summary>
        public Vec3 HipForward { get; set; }

        /// <summary><c>hip_u</c>: pelvis frame up.</summary>
        public Vec3 HipUp { get; set; }

        public Vec3 Waist { get; set; }
        public Vec3 Chest { get; set; }

        /// <summary><c>neck_base</c>: where the neck leaves the torso.</summary>
        public Vec3 NeckBase { get; set; }

        /// <summary><c>neck_top</c>: where the neck meets the head.</summary>
        public Vec3 NeckTop { get; set; }

        /// <summary><c>head</c>: centre of the skull.</summary>
        public Vec3 Head { get; set; }

        /// <summary><c>hr</c>: head right.</summary>
        public Vec3 HeadRight { get; set; }

        /// <summary><c>hf</c>: head forward (towards the snout).</summary>
        public Vec3 HeadForward { get; set; }

        /// <summary><c>hu</c>: head up.</summary>
        public Vec3 HeadUp { get; set; }

        /// <summary><c>breath</c>: -1 (out) .. 1 (in); raises the chest and shoulders a little.</summary>
        public double Breath { get; set; }

        /// <summary><c>jaw_open</c>: 0 closed .. 1 open (no animation opens it).</summary>
        public double JawOpen { get; set; }

        /// <summary><c>arms</c>: side (+1 right, -1 left) to arm.</summary>
        public Dictionary<int, DragonArm> Arms { get; } = new Dictionary<int, DragonArm>();

        /// <summary><c>legs</c>: side (+1 right, -1 left) to leg.</summary>
        public Dictionary<int, DragonLeg> Legs { get; } = new Dictionary<int, DragonLeg>();

        /// <summary><c>prop</c>: the held tool, or null.</summary>
        public DragonProp Prop { get; set; }

        /// <summary>
        /// This pose as a <see cref="PoseResult"/>, for previews (the stick figure renderer) and tests;
        /// it has no skeleton, so <see cref="PoseResult.WorldDeltas"/> stays empty and avatars cannot be
        /// posed from it. Joints use <see cref="JointNames"/>; <see cref="JointNames.HeadTop"/> is
        /// <c>head + hu * 0.094 * HEAD_SCALE</c>, the top of the skull ellipsoid. The hips go to the pelvis,
        /// both grips are 1 when a prop is held and 0 otherwise, and the pose is not moved to the ground.
        /// </summary>
        /// <exception cref="InvalidOperationException">An arm or a leg has not been set.</exception>
        public PoseResult ToPoseResult()
        {
            var result = new PoseResult { HipsPosition = Pelvis, GroundToLowestPoint = false };
            result.Joints[JointNames.Pelvis] = Pelvis;
            result.Joints[JointNames.Waist] = Waist;
            result.Joints[JointNames.Chest] = Chest;
            result.Joints[JointNames.NeckBase] = NeckBase;
            result.Joints[JointNames.NeckTop] = NeckTop;
            result.Joints[JointNames.Head] = Head;
            result.Joints[JointNames.HeadTop] = Head + HeadUp * 0.094 * DragonModel.HeadScale;
            foreach (int side in DragonModel.Sides)
            {
                DragonArm arm = RequireLimb(Arms, side, "arm");
                result.Joints[JointNames.Shoulder(side)] = arm.Shoulder;
                result.Joints[JointNames.Elbow(side)] = arm.Elbow;
                result.Joints[JointNames.Hand(side)] = arm.Hand;
                DragonLeg leg = RequireLimb(Legs, side, "leg");
                result.Joints[JointNames.Hip(side)] = leg.Hip;
                result.Joints[JointNames.Knee(side)] = leg.Knee;
                result.Joints[JointNames.Ankle(side)] = leg.Ankle;
                result.Joints[JointNames.Ball(side)] = leg.Ball;
                result.Joints[JointNames.Toe(side)] = leg.Toe;
            }
            if (Prop != null)
            {
                result.Prop = Prop.ToPropPose();
                result.LeftGrip = 1;
                result.RightGrip = 1;
            }
            return result;
        }

        private static T RequireLimb<T>(Dictionary<int, T> limbs, int side, string what)
        {
            if (!limbs.TryGetValue(side, out T limb))
            {
                throw new InvalidOperationException(
                    "the pose has no " + (side > 0 ? "right " : "left ") + what + "; set both before converting it");
            }
            return limb;
        }
    }
}
