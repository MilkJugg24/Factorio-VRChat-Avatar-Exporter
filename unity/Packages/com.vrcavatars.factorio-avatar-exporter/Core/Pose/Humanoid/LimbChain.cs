using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>A posed two-segment limb: where its joints go and the rotations of its two bones.</summary>
    public readonly struct LimbPose
    {
        public LimbPose(Vec3 joint, Vec3 end, Quat upper, Quat lower, Vec3 bendAxis)
        {
            Joint = joint;
            End = end;
            Upper = upper;
            Lower = lower;
            BendAxis = bendAxis;
        }

        /// <summary>The middle joint (elbow, knee), tiles.</summary>
        public Vec3 Joint { get; }

        /// <summary>The end of the limb (wrist, ankle), tiles.</summary>
        public Vec3 End { get; }

        /// <summary>World delta (<see cref="PoseResult.WorldDeltas"/>) of the upper bone: UpperArm, UpperLeg.</summary>
        public Quat Upper { get; }

        /// <summary>World delta of the lower bone: LowerArm, LowerLeg.</summary>
        public Quat Lower { get; }

        /// <summary>
        /// Unit axis the joint bends about: the lower segment is the upper one turned positively about it. Both
        /// deltas turn the rest bend axis (its part perpendicular to the bone) into this one.
        /// </summary>
        public Vec3 BendAxis { get; }
    }

    /// <summary>
    /// A two-segment limb (arm: UpperArm, LowerArm, Hand; leg: UpperLeg, LowerLeg, Foot) in its rest pose, and
    /// how to pose it. Each bone's rotation comes from a full frame, the bone's direction plus the joint's bend
    /// axis, rather than from the direction alone, so the limb's twist is defined: the rest bend axis (given
    /// by anatomy) turns into the posed joint's axis, and joints only ever flex the anatomical way.
    /// </summary>
    public sealed class LimbChain
    {
        // the rest bend axis must be at least this far (sine of the angle) from the limb's directions
        private const double MinPerpendicular = 0.1;

        // below this sine of the bend angle the posed limb counts as straight
        private const double StraightSine = 1e-6;

        // a target closer than this to the root (tiles) gives no direction; the rest direction is used
        private const double MinReach = 1e-6;

        private readonly Vec3 restDirection;
        private readonly Vec3 restPole;

        /// <summary>
        /// A limb from its rest joint positions (tiles) and the axis its middle joint bends about: flexing turns
        /// the lower segment positively about <paramref name="restBendAxis"/>, which must be roughly
        /// perpendicular to both segments.
        /// </summary>
        public LimbChain(Vec3 restRoot, Vec3 restJoint, Vec3 restEnd, Vec3 restBendAxis)
        {
            HumanoidArgs.CheckFinite(restRoot, nameof(restRoot));
            HumanoidArgs.CheckFinite(restJoint, nameof(restJoint));
            HumanoidArgs.CheckFinite(restEnd, nameof(restEnd));
            HumanoidArgs.CheckFinite(restBendAxis, nameof(restBendAxis));
            Vec3 upper = restJoint - restRoot;
            Vec3 lower = restEnd - restJoint;
            Vec3 whole = restEnd - restRoot;
            if (upper.Length < 1e-12 || lower.Length < 1e-12 || whole.Length < 1e-12)
            {
                throw new ArgumentException("the rest limb's root, joint and end must be at different places",
                    nameof(restJoint));
            }
            Vec3 axis = restBendAxis.Normalized;
            if (Vec3.Cross(axis, upper.Normalized).Length < MinPerpendicular
                || Vec3.Cross(axis, lower.Normalized).Length < MinPerpendicular
                || Vec3.Cross(axis, whole.Normalized).Length < MinPerpendicular)
            {
                throw new ArgumentException("the rest bend axis must be roughly perpendicular to the limb, got " + axis,
                    nameof(restBendAxis));
            }
            RestUpper = upper;
            RestLower = lower;
            RestBendAxis = axis;
            restDirection = whole.Normalized;
            // the side the joint sticks out to when the limb flexes
            restPole = Vec3.Cross(restDirection, axis).Normalized;
        }

        /// <summary>An arm of a rig: UpperArm, LowerArm, Hand, bending about <see cref="HumanoidRig.ElbowBendAxis"/>.</summary>
        public static LimbChain Arm(HumanoidRig rig, int side)
        {
            if (rig == null)
            {
                throw new ArgumentNullException(nameof(rig));
            }
            return new LimbChain(rig.Rest(HumanBoneHierarchy.UpperArm(side)), rig.Rest(HumanBoneHierarchy.LowerArm(side)),
                rig.Rest(HumanBoneHierarchy.Hand(side)), rig.ElbowBendAxis(side));
        }

        /// <summary>A leg of a rig: UpperLeg, LowerLeg, Foot, bending about <see cref="HumanoidRig.KneeBendAxis"/>.</summary>
        public static LimbChain Leg(HumanoidRig rig, int side)
        {
            if (rig == null)
            {
                throw new ArgumentNullException(nameof(rig));
            }
            return new LimbChain(rig.Rest(HumanBoneHierarchy.UpperLeg(side)), rig.Rest(HumanBoneHierarchy.LowerLeg(side)),
                rig.Rest(HumanBoneHierarchy.Foot(side)), rig.KneeBendAxis(side));
        }

        /// <summary>Rest vector from the root to the joint, tiles.</summary>
        public Vec3 RestUpper { get; }

        /// <summary>Rest vector from the joint to the end, tiles.</summary>
        public Vec3 RestLower { get; }

        /// <summary>Unit rest bend axis.</summary>
        public Vec3 RestBendAxis { get; }

        public double UpperLength => RestUpper.Length;
        public double LowerLength => RestLower.Length;

        /// <summary>
        /// Poses the limb from <paramref name="root"/> (where the posed skeleton puts the upper bone) towards
        /// <paramref name="target"/> with <see cref="Ik.TwoBone"/>, which clamps targets out of reach. The joint
        /// bends towards <paramref name="pole"/> (a direction); when the pole is (almost) along the limb, or
        /// zero, it bends the anatomical way, as at rest, carried along by the limb's swing from its rest
        /// direction. A target at the root is taken to lie along the rest direction.
        /// </summary>
        public LimbPose Solve(Vec3 root, Vec3 target, Vec3 pole)
        {
            HumanoidArgs.CheckFinite(root, nameof(root));
            HumanoidArgs.CheckFinite(target, nameof(target));
            HumanoidArgs.CheckFinite(pole, nameof(pole));
            if (Vec3.Distance(root, target) < MinReach)
            {
                target = root + restDirection * MinReach;
            }
            Vec3 direction = (target - root).Normalized;
            Vec3 bend = BendDirection(direction, pole);
            (Vec3 joint, Vec3 end) = Ik.TwoBone(root, target, UpperLength, LowerLength, bend);
            return Pose(root, joint, end, bend);
        }

        /// <summary>
        /// The bone rotations for a limb whose root, joint and end are at the given places (only the segments'
        /// directions matter: forward kinematics keeps the rest lengths). The bend axis is that of the actual
        /// joint; when the limb is straight it comes from <paramref name="pole"/> as in <see cref="Solve"/>.
        /// </summary>
        public LimbPose Pose(Vec3 root, Vec3 joint, Vec3 end, Vec3 pole)
        {
            HumanoidArgs.CheckFinite(root, nameof(root));
            HumanoidArgs.CheckFinite(joint, nameof(joint));
            HumanoidArgs.CheckFinite(end, nameof(end));
            HumanoidArgs.CheckFinite(pole, nameof(pole));
            Vec3 upper = joint - root;
            Vec3 lower = end - joint;
            if (upper.Length < 1e-12 || lower.Length < 1e-12)
            {
                throw new ArgumentException("the posed limb's root, joint and end must be at different places",
                    nameof(joint));
            }
            Vec3 axis = Vec3.Cross(upper, lower);
            if (axis.Length <= StraightSine * upper.Length * lower.Length)
            {
                Vec3 direction = upper.Normalized;
                axis = Vec3.Cross(BendDirection(direction, pole), direction);
            }
            axis = axis.Normalized;
            return new LimbPose(joint, end,
                HumanoidFrames.Map(RestUpper, RestBendAxis, upper, axis),
                HumanoidFrames.Map(RestLower, RestBendAxis, lower, axis),
                axis);
        }

        // Unit direction, perpendicular to the limb, that the joint sticks out to.
        private Vec3 BendDirection(Vec3 direction, Vec3 pole)
        {
            Vec3 p = pole.Normalized;
            Vec3 perpendicular = p - direction * Vec3.Dot(p, direction);
            if (perpendicular.Length < 1e-6)
            {
                perpendicular = Quat.FromTo(restDirection, direction).Rotate(restPole);
            }
            return perpendicular.Normalized;
        }
    }
}
