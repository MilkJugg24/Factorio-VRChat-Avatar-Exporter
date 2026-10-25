using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Builds the <see cref="PoseResult"/> of a <see cref="HumanoidRig"/> from high-level targets: where the
    /// pelvis, hands and feet go and how the hips, chest and head are turned.
    ///
    /// Positions are model space, tiles. Frames are model space rotations from the rest frame, in which x is
    /// the body's right, y its forward and z up (<see cref="Quat.FromBasis"/> makes one from axes); they are
    /// normalized. Sides are +1 = right, -1 = left. What is not set keeps its rest pose relative to its parent
    /// part: the chest follows the hips, the head the chest, and limbs without targets stay as in the T-pose.
    /// Targets out of reach are clamped (<see cref="Ik.TwoBone"/>) without notice, deterministically.
    /// </summary>
    public sealed class HumanoidPoseBuilder
    {
        /// <summary>The part of the turn from the chest frame to the head frame that the Neck bone takes.</summary>
        public const double NeckShare = 0.5;

        // a ball target this close to where the ankle ended up gives the foot no direction to aim along
        private const double MinFootAim = 1e-9;

        // a grip axis must be at least this far (sine of the angle) from the hand direction to give the hand a roll
        private const double MinGripAxis = 1e-6;

        private static readonly int[] Sides = { 1, -1 };
        private static readonly HumanBone[] TorsoBones = { HumanBone.Spine, HumanBone.Chest, HumanBone.UpperChest };

        // per side: [0] = left, [1] = right
        private readonly LimbChain[] arms = new LimbChain[2];
        private readonly LimbChain[] legs = new LimbChain[2];
        private readonly ArmTarget[] armTargets = new ArmTarget[2];
        private readonly FootTarget[] footTargets = new FootTarget[2];

        // how far each torso bone the rig has turns from the hip frame towards the chest frame
        private readonly List<KeyValuePair<HumanBone, double>> torsoShares = new List<KeyValuePair<HumanBone, double>>();

        private Vec3 pelvis;
        private Quat hipFrame = Quat.Identity;
        private Quat? chestFrame;
        private Quat? headFrame;
        private PropPose prop;
        private bool groundToLowestPoint;

        /// <summary>A builder for <paramref name="rig"/>, starting from its rest pose.</summary>
        public HumanoidPoseBuilder(HumanoidRig rig)
        {
            Rig = rig ?? throw new ArgumentNullException(nameof(rig));
            pelvis = rig.Rest(HumanBone.Hips);
            foreach (int side in Sides)
            {
                arms[Index(side)] = LimbChain.Arm(rig, side);
                legs[Index(side)] = LimbChain.Leg(rig, side);
            }
            // the last torso bone, which carries the neck and shoulders, takes the whole chest frame; the others
            // a share by their height
            HumanBone top = HumanBone.Spine;
            foreach (HumanBone bone in TorsoBones)
            {
                if (rig.Has(bone))
                {
                    top = bone;
                }
            }
            double span = rig.Rest(top).Z - rig.HipsHeight;
            foreach (HumanBone bone in TorsoBones)
            {
                if (rig.Has(bone))
                {
                    double share = span > 0 ? MathUtil.Clamp((rig.Rest(bone).Z - rig.HipsHeight) / span, 0, 1) : 1;
                    torsoShares.Add(new KeyValuePair<HumanBone, double>(bone, share));
                }
            }
        }

        public HumanoidRig Rig { get; }

        /// <summary>
        /// Moves the Hips bone to <paramref name="position"/> (tiles) and turns the hips, and with them the
        /// legs' roots, by <paramref name="hipFrame"/>. Default: the rest position, unturned.
        /// </summary>
        public HumanoidPoseBuilder SetPelvis(Vec3 position, Quat hipFrame)
        {
            HumanoidArgs.CheckFinite(position, nameof(position));
            this.hipFrame = HumanoidArgs.CheckRotation(hipFrame, nameof(hipFrame));
            pelvis = position;
            return this;
        }

        /// <summary>
        /// Turns the chest (shoulders, neck) by <paramref name="chestFrame"/>. Spine, Chest and UpperChest share
        /// the turn from the hip frame smoothly, by their height above the Hips: the last of them the rig has,
        /// which carries the neck and shoulders, takes the whole chest frame. Default: the hip frame.
        /// </summary>
        public HumanoidPoseBuilder SetTorso(Quat chestFrame)
        {
            this.chestFrame = HumanoidArgs.CheckRotation(chestFrame, nameof(chestFrame));
            return this;
        }

        /// <summary>
        /// Turns the Head bone by <paramref name="headFrame"/>; the Neck takes <see cref="NeckShare"/> of the turn
        /// from the chest frame. Default: the chest frame.
        /// </summary>
        public HumanoidPoseBuilder SetHead(Quat headFrame)
        {
            this.headFrame = HumanoidArgs.CheckRotation(headFrame, nameof(headFrame));
            return this;
        }

        /// <summary>
        /// Reaches for <paramref name="handTarget"/> (where the Hand bone, the wrist, goes; tiles) with the elbow
        /// bent towards <paramref name="pole"/> (a direction; when it is along the arm, the elbow bends the way it
        /// does in the T-pose). The hand continues the forearm, or points along <paramref name="handDirection"/>
        /// (wrist towards fingers) when given; <paramref name="grip"/> curls the fingers, from 0 (open) to 1
        /// (closed). The hand turns from the forearm without rolling, or, when <paramref name="gripAxis"/> is given
        /// (it needs <paramref name="handDirection"/>), rolls about its direction so that the width of its palm,
        /// from the little finger to the thumb (+y in the palms-down T-pose, both hands), lies along the part of
        /// <paramref name="gripAxis"/> perpendicular to the fingers: closed fingers curl about that axis, so a
        /// handle along it runs through the fist.
        /// </summary>
        public HumanoidPoseBuilder SetArm(int side, Vec3 handTarget, Vec3 pole, Vec3? handDirection = null, double grip = 0,
            Vec3? gripAxis = null)
        {
            HumanoidArgs.CheckSide(side);
            HumanoidArgs.CheckFinite(handTarget, nameof(handTarget));
            HumanoidArgs.CheckFinite(pole, nameof(pole));
            if (handDirection.HasValue)
            {
                HumanoidArgs.CheckFinite(handDirection.Value, nameof(handDirection));
                if (handDirection.Value.Length < 1e-9)
                {
                    throw new ArgumentException("handDirection must not be zero", nameof(handDirection));
                }
            }
            if (!(grip >= 0 && grip <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(grip), grip, "grip must be between 0 and 1");
            }
            if (gripAxis.HasValue)
            {
                HumanoidArgs.CheckFinite(gripAxis.Value, nameof(gripAxis));
                if (!handDirection.HasValue)
                {
                    throw new ArgumentException("gripAxis needs a handDirection", nameof(gripAxis));
                }
                if (Vec3.Cross(handDirection.Value.Normalized, gripAxis.Value.Normalized).Length < MinGripAxis)
                {
                    throw new ArgumentException("gripAxis must not be zero or along handDirection", nameof(gripAxis));
                }
            }
            armTargets[Index(side)] = new ArmTarget(handTarget, pole, handDirection, grip, gripAxis);
            return this;
        }

        /// <summary>
        /// Plants the ball of the foot (<see cref="HumanoidRig.Ball"/>) at <paramref name="ballTarget"/> (tiles),
        /// the foot pointing along the horizontal part of <paramref name="footForward"/> and pitched about the
        /// ball by <paramref name="heelLift"/> radians: positive lifts the heel, tipping the front of the foot
        /// down; 0 keeps the rest attitude, flat on the ground for plantigrade rigs. The ankle target follows
        /// from that; the knee bends towards <paramref name="kneePole"/> (a direction). <paramref name="toeBend"/>
        /// tips the Toes bone's front down by that many radians from its rest attitude, whatever the heel does:
        /// 0 keeps the toes flat on the ground (no effect without a Toes bone). When the ankle cannot reach its
        /// target, the foot turns about the ankle to point at the ball target.
        /// </summary>
        public HumanoidPoseBuilder SetFoot(int side, Vec3 ballTarget, Vec3 footForward, double heelLift, Vec3 kneePole,
            double toeBend = 0)
        {
            HumanoidArgs.CheckSide(side);
            HumanoidArgs.CheckFinite(ballTarget, nameof(ballTarget));
            HumanoidArgs.CheckFinite(footForward, nameof(footForward));
            if (footForward.Horizontal.Length < 1e-9)
            {
                throw new ArgumentException("footForward must have a horizontal part", nameof(footForward));
            }
            HumanoidArgs.CheckFinite(heelLift, nameof(heelLift));
            HumanoidArgs.CheckFinite(kneePole, nameof(kneePole));
            HumanoidArgs.CheckFinite(toeBend, nameof(toeBend));
            footTargets[Index(side)] = new FootTarget(ballTarget, footForward.Horizontal.Normalized, heelLift, kneePole,
                toeBend);
            return this;
        }

        /// <summary>The held tool (model space, tiles), or null for none.</summary>
        public HumanoidPoseBuilder SetProp(PropPose prop)
        {
            this.prop = prop;
            return this;
        }

        /// <summary>Sets <see cref="PoseResult.GroundToLowestPoint"/>.</summary>
        public HumanoidPoseBuilder SetGroundToLowestPoint(bool value)
        {
            groundToLowestPoint = value;
            return this;
        }

        /// <summary>
        /// Where a bone of <see cref="HumanBoneHierarchy.TorsoChain"/> (Hips, Spine, Chest, UpperChest, Neck,
        /// Head, Shoulders, UpperArms = the shoulder joints, UpperLegs = the hip joints) goes with the pelvis,
        /// torso and head set so far: model space, tiles. It depends on nothing else, so it is final as soon as
        /// <see cref="SetPelvis"/>, <see cref="SetTorso"/> and <see cref="SetHead"/> have been called, and equals
        /// what <see cref="Build"/> gives (<see cref="PosedSkeleton.TorsoPoint"/>): animations use it to place
        /// hands relative to the chest and shoulders and feet relative to the hips. Optional bones the rig lacks
        /// give their estimated points (<see cref="HumanoidRig.TorsoRest"/>).
        /// </summary>
        public Vec3 TorsoPoint(HumanBone bone) => HumanoidSkeleton.Pose(Rig, TorsoPose()).TorsoPoint(bone);

        /// <summary>
        /// The pose: deltas for the Hips, every torso bone, the Neck and the Head, and the bones of each limb that
        /// has a target (the bones the rig lacks get none); <see cref="PoseResult.Joints"/> from forward
        /// kinematics of those deltas (<see cref="HumanoidJoints"/>), so they match what the renderer shows.
        /// </summary>
        public PoseResult Build()
        {
            PoseResult result = TorsoPose();
            result.Prop = prop;
            result.GroundToLowestPoint = groundToLowestPoint;

            // the limbs hang from where the torso puts the shoulders and hips
            PosedSkeleton torso = HumanoidSkeleton.Pose(Rig, result);
            foreach (int side in Sides)
            {
                if (armTargets[Index(side)] != null)
                {
                    PoseArm(result, torso, side);
                }
                if (footTargets[Index(side)] != null)
                {
                    PoseLeg(result, torso, side);
                }
            }
            foreach (KeyValuePair<string, Vec3> joint in HumanoidJoints.FromSkeleton(HumanoidSkeleton.Pose(Rig, result)))
            {
                result.Joints.Add(joint.Key, joint.Value);
            }
            return result;
        }

        // the pelvis and the rotations of the hips, torso, neck and head
        private PoseResult TorsoPose()
        {
            Quat chest = chestFrame ?? hipFrame;
            Quat head = headFrame ?? chest;
            var result = new PoseResult { HipsPosition = pelvis };
            result.WorldDeltas.Add(HumanBone.Hips, hipFrame);
            foreach (KeyValuePair<HumanBone, double> share in torsoShares)
            {
                result.WorldDeltas.Add(share.Key, Quat.Slerp(hipFrame, chest, share.Value).Normalized);
            }
            if (Rig.Has(HumanBone.Neck))
            {
                result.WorldDeltas.Add(HumanBone.Neck, Quat.Slerp(chest, head, NeckShare).Normalized);
            }
            result.WorldDeltas.Add(HumanBone.Head, head);
            return result;
        }

        private void PoseArm(PoseResult result, PosedSkeleton torso, int side)
        {
            ArmTarget target = armTargets[Index(side)];
            LimbChain chain = arms[Index(side)];
            LimbPose limb = chain.Solve(torso.Position(HumanBoneHierarchy.UpperArm(side)), target.Hand, target.Pole);
            Quat hand = limb.Lower;
            if (target.GripAxis.HasValue)
            {
                // the rest hand continues the rest forearm with the width of its palm along +y
                hand = HumanoidFrames.Map(chain.RestLower, Vec3.UnitY, target.Direction.Value, target.GripAxis.Value);
            }
            else if (target.Direction.HasValue)
            {
                // the rest hand continues the rest forearm: turn it from there to the wanted direction
                hand = (Quat.FromTo(limb.Lower.Rotate(chain.RestLower), target.Direction.Value) * hand).Normalized;
            }
            result.WorldDeltas.Add(HumanBoneHierarchy.UpperArm(side), limb.Upper);
            result.WorldDeltas.Add(HumanBoneHierarchy.LowerArm(side), limb.Lower);
            result.WorldDeltas.Add(HumanBoneHierarchy.Hand(side), hand);
            if (side > 0)
            {
                result.RightGrip = target.Grip;
            }
            else
            {
                result.LeftGrip = target.Grip;
            }
        }

        private void PoseLeg(PoseResult result, PosedSkeleton torso, int side)
        {
            FootTarget target = footTargets[Index(side)];
            Vec3 restAnkle = Rig.Rest(HumanBoneHierarchy.Foot(side));
            Vec3 restBall = Rig.Ball(side);
            Vec3 restForward = Rig.FootForward(side);
            Quat yaw = Quat.FromAxisAngle(Vec3.UnitZ,
                Math.Atan2(Vec3.Cross(restForward, target.Forward).Z, Vec3.Dot(restForward, target.Forward)));
            // turning the foot negatively about its right side tips its front down
            Vec3 right = Vec3.Cross(restForward, Vec3.UnitZ);
            Quat foot = (yaw * Quat.FromAxisAngle(right, -target.HeelLift)).Normalized;
            Quat toes = (yaw * Quat.FromAxisAngle(right, -target.ToeBend)).Normalized;
            LimbPose limb = legs[Index(side)].Solve(torso.Position(HumanBoneHierarchy.UpperLeg(side)),
                target.Ball + foot.Rotate(restAnkle - restBall), target.KneePole);
            // the identity when the ankle reached its target; else the foot points from the ankle at the ball target
            Vec3 toBall = target.Ball - limb.End;
            Quat aim = toBall.Length > MinFootAim ? Quat.FromTo(foot.Rotate(restBall - restAnkle), toBall) : Quat.Identity;
            result.WorldDeltas.Add(HumanBoneHierarchy.UpperLeg(side), limb.Upper);
            result.WorldDeltas.Add(HumanBoneHierarchy.LowerLeg(side), limb.Lower);
            result.WorldDeltas.Add(HumanBoneHierarchy.Foot(side), (aim * foot).Normalized);
            if (Rig.Has(HumanBoneHierarchy.Toes(side)))
            {
                result.WorldDeltas.Add(HumanBoneHierarchy.Toes(side), (aim * toes).Normalized);
            }
        }

        private static int Index(int side) => side > 0 ? 1 : 0;

        private sealed class ArmTarget
        {
            public readonly Vec3 Hand;
            public readonly Vec3 Pole;
            public readonly Vec3? Direction;
            public readonly double Grip;
            public readonly Vec3? GripAxis;

            public ArmTarget(Vec3 hand, Vec3 pole, Vec3? direction, double grip, Vec3? gripAxis)
            {
                Hand = hand;
                Pole = pole;
                Direction = direction;
                Grip = grip;
                GripAxis = gripAxis;
            }
        }

        private sealed class FootTarget
        {
            public readonly Vec3 Ball;
            public readonly Vec3 Forward;
            public readonly double HeelLift;
            public readonly Vec3 KneePole;
            public readonly double ToeBend;

            public FootTarget(Vec3 ball, Vec3 forward, double heelLift, Vec3 kneePole, double toeBend)
            {
                Ball = ball;
                Forward = forward;
                HeelLift = heelLift;
                KneePole = kneePole;
                ToeBend = toeBend;
            }
        }
    }
}
