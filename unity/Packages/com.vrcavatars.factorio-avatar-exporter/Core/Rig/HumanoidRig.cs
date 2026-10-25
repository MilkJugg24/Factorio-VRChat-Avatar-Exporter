using System;
using System.Collections.Generic;
using System.Globalization;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The rest (T-pose) skeleton of a humanoid avatar, measured from a <see cref="RigInput"/> and scaled to
    /// the character's height in tiles.
    ///
    /// Rest positions are in model space (x = right, y = forward, z = up) and in TILES, with the ground (the
    /// lowest mesh point) at z = 0 and the Hips bone at x = y = 0, so the origin is the ground point under
    /// the pelvis: the entity position. Every bone's rest frame is the model frame, which is what
    /// <see cref="PoseResult.WorldDeltas"/> rotate from. Sides are +1 = right, -1 = left.
    /// </summary>
    public sealed class HumanoidRig
    {
        /// <summary>
        /// Height of the Head bone above the ground times this estimates the avatar's height when the mesh
        /// bounds are unusable.
        /// </summary>
        public const double HeightPerHeadHeight = 1.12;

        /// <summary>
        /// Without a Toes bone the ball of the foot is this fraction of the way from the ankle to the toe tip.
        /// </summary>
        public const double BallFraction = 0.72;

        /// <summary>
        /// Without a Toes bone or a measured toe tip, the toe tip is estimated this far (times the leg length)
        /// ahead of the ankle, on the ground.
        /// </summary>
        public const double ToeReachPerLegLength = 0.23;

        /// <summary>An estimated heel is this fraction of the ankle to toe tip distance behind the ankle.</summary>
        public const double HeelFraction = 0.3;

        /// <summary>Ankles higher than this times the leg length make a <see cref="LegStyle.Digitigrade"/> rig.</summary>
        public const double DigitigradeAnkleRatio = 0.2;

        /// <summary>
        /// Without a Shoulder bone, <see cref="TorsoRest"/> puts it this fraction of the way from the
        /// <see cref="NeckBase"/> to the shoulder joint (UpperArm).
        /// </summary>
        public const double ShoulderBoneFraction = 0.2;

        // A limb segment whose direction is this close (sine of the angle) to the forward axis is not in a T-pose.
        private const double MinSidewaysLimb = 0.1;

        // Shorter segments (tiles) are degenerate.
        private const double MinSegment = 1e-6;

        // A Toes bone or toe tip gives the foot's direction only when the horizontal part of its offset from the
        // ankle is at least this fraction of the offset, and within 60 degrees of forward (cosine of the angle).
        private const double MinFootHorizontal = 0.25;
        private const double MinFootForward = 0.5;

        private static readonly int[] Sides = { 1, -1 };

        private readonly Dictionary<HumanBone, Vec3> rest;
        private readonly Dictionary<HumanBone, HumanBone?> parents = new Dictionary<HumanBone, HumanBone?>();
        private readonly FootRest[] feet = new FootRest[2];
        private readonly Vec3[] elbowAxes = new Vec3[2];
        private readonly Vec3[] kneeAxes = new Vec3[2];

        private HumanoidRig(Dictionary<HumanBone, Vec3> rest, double height, double tilesPerMeter, double heightTiles,
            Vec3?[] toeTips, Vec3?[] heels, List<string> warnings)
        {
            this.rest = rest;
            Height = height;
            TilesPerMeter = tilesPerMeter;
            HeadTopHeight = heightTiles;
            Warnings = warnings.AsReadOnly();

            var bones = new List<HumanBone>();
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                parents.Add(bone, NearestExistingAncestor(bone));
                if (Has(bone))
                {
                    bones.Add(bone);
                }
            }
            Bones = bones.AsReadOnly();

            double legs = 0, arms = 0, ankles = 0;
            foreach (int side in Sides)
            {
                int i = Index(side);
                double leg = ThighLength(side) + ShinLength(side);
                legs += leg;
                arms += UpperArmLength(side) + ForearmLength(side);
                ankles += AnkleHeight(side);
                // elbows bend the forearm towards the front, knees the shin backwards
                elbowAxes[i] = Vec3.Cross(LimbDirection(HumanBoneHierarchy.UpperArm(side), HumanBoneHierarchy.Hand(side)),
                    Vec3.UnitY).Normalized;
                kneeAxes[i] = Vec3.Cross(LimbDirection(HumanBoneHierarchy.UpperLeg(side), HumanBoneHierarchy.Foot(side)),
                    -Vec3.UnitY).Normalized;
                feet[i] = MeasureFoot(side, toeTips[i], heels[i], leg);
            }
            LegLength = legs / 2;
            ArmLength = arms / 2;
            LegStyle = ankles / 2 > DigitigradeAnkleRatio * LegLength ? LegStyle.Digitigrade : LegStyle.Plantigrade;
            ShoulderWidth = Vec3.Distance(Rest(HumanBone.LeftUpperArm), Rest(HumanBone.RightUpperArm));
            HipWidth = Vec3.Distance(Rest(HumanBone.LeftUpperLeg), Rest(HumanBone.RightUpperLeg));
            NeckBase = Has(HumanBone.Neck)
                ? Rest(HumanBone.Neck)
                : Vec3.Lerp(Rest(HumanBone.LeftUpperArm), Rest(HumanBone.RightUpperArm), 0.5);
        }

        /// <summary>
        /// Measures an avatar. Throws <see cref="ArgumentException"/> when required bones are missing (listing
        /// all of them), values are not finite, a limb segment has no length or points forward or back (the
        /// input is not a T-pose), or the avatar has no height at all.
        /// </summary>
        /// <param name="input">The avatar's T-pose, meters.</param>
        /// <param name="targetHeightTiles">
        /// The height the character gets, tiles: the avatar's <see cref="Height"/> is scaled to it.
        /// </param>
        public static HumanoidRig Measure(RigInput input, double targetHeightTiles = Conventions.DefaultCharacterHeight)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (!double.IsFinite(targetHeightTiles) || targetHeightTiles <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetHeightTiles), targetHeightTiles,
                    "the target height must be a positive number of tiles");
            }
            CheckInput(input);

            var warnings = new List<string>();
            double height = MeasureHeight(input, warnings);
            double tilesPerMeter = targetHeightTiles / height;
            Vec3 hips = input.Bones[HumanBone.Hips];
            var origin = new Vec3(hips.X, hips.Y, input.MeshBottom);
            Vec3 ToTiles(Vec3 p) => (p - origin) * tilesPerMeter;
            Vec3? SoleToTiles(Vec3? p) => p.HasValue ? ToTiles(p.Value) : (Vec3?)null;

            var rest = new Dictionary<HumanBone, Vec3>();
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                if (input.Bones.TryGetValue(bone, out Vec3 p))
                {
                    rest.Add(bone, ToTiles(p));
                }
            }
            CheckLimbs(rest);
            Vec3?[] toeTips = { SoleToTiles(input.LeftToeTip), SoleToTiles(input.RightToeTip) };
            Vec3?[] heels = { SoleToTiles(input.LeftHeel), SoleToTiles(input.RightHeel) };
            return new HumanoidRig(rest, height, tilesPerMeter, targetHeightTiles, toeTips, heels, warnings);
        }

        /// <summary>
        /// The avatar's height in METERS: MeshTop - MeshBottom, or, when that is not positive,
        /// <see cref="HeightPerHeadHeight"/> times the Head bone's height (with a <see cref="Warnings"/> entry).
        /// </summary>
        public double Height { get; }

        /// <summary>Scale from the avatar's meters to tiles.</summary>
        public double TilesPerMeter { get; }

        /// <summary>Height of the top of the head (MeshTop), tiles: the target height given to <see cref="Measure"/>.</summary>
        public double HeadTopHeight { get; }

        /// <summary>Height of the Hips bone above the ground, tiles.</summary>
        public double HipsHeight => Rest(HumanBone.Hips).Z;

        /// <summary>Distance between the shoulder joints (UpperArm bones), tiles.</summary>
        public double ShoulderWidth { get; }

        /// <summary>Distance between the hip joints (UpperLeg bones), tiles.</summary>
        public double HipWidth { get; }

        /// <summary>Thigh plus shin, averaged over both legs, tiles.</summary>
        public double LegLength { get; }

        /// <summary>Upper arm plus forearm, averaged over both arms, tiles.</summary>
        public double ArmLength { get; }

        /// <summary>
        /// <see cref="LegStyle.Digitigrade"/> when the average ankle height exceeds
        /// <see cref="DigitigradeAnkleRatio"/> times <see cref="LegLength"/>, else <see cref="LegStyle.Plantigrade"/>.
        /// </summary>
        public LegStyle LegStyle { get; }

        /// <summary>Things the exporter should tell the user, e.g. that the height had to be estimated.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>The bones the avatar has, parents before children (<see cref="HumanBones.ParentFirst"/> order).</summary>
        public IReadOnlyList<HumanBone> Bones { get; }

        /// <summary>
        /// Rest position of the base of the neck, tiles: the Neck bone, or without one the point half way between
        /// the shoulder joints.
        /// </summary>
        public Vec3 NeckBase { get; }

        public bool Has(HumanBone bone) => rest.ContainsKey(bone);

        /// <summary>Rest position of a bone the avatar has, tiles.</summary>
        public Vec3 Rest(HumanBone bone)
        {
            if (!rest.TryGetValue(bone, out Vec3 p))
            {
                throw new ArgumentException("the rig has no " + bone + " bone", nameof(bone));
            }
            return p;
        }

        /// <summary>
        /// Rest position of a bone of <see cref="HumanBoneHierarchy.TorsoChain"/>, tiles, whether or not the avatar
        /// has it: the bone itself, or an estimate for a missing optional bone (Chest: half way from the Spine to
        /// the <see cref="NeckBase"/>; UpperChest: half way from the Chest to the neck base; Neck: the neck base;
        /// Shoulder: <see cref="ShoulderBoneFraction"/> of the way from the neck base to the UpperArm). An
        /// estimated point moves with the bone's <see cref="Parent"/>.
        /// </summary>
        public Vec3 TorsoRest(HumanBone bone)
        {
            switch (bone)
            {
                case HumanBone.Hips:
                case HumanBone.Spine:
                case HumanBone.Head:
                case HumanBone.LeftUpperArm:
                case HumanBone.RightUpperArm:
                case HumanBone.LeftUpperLeg:
                case HumanBone.RightUpperLeg:
                    return Rest(bone);
                case HumanBone.Chest:
                    return Has(bone) ? Rest(bone) : Vec3.Lerp(Rest(HumanBone.Spine), NeckBase, 0.5);
                case HumanBone.UpperChest:
                    return Has(bone) ? Rest(bone) : Vec3.Lerp(TorsoRest(HumanBone.Chest), NeckBase, 0.5);
                case HumanBone.Neck:
                    return NeckBase;
                case HumanBone.LeftShoulder:
                    return ShoulderRest(-1);
                case HumanBone.RightShoulder:
                    return ShoulderRest(1);
                default:
                    throw new ArgumentOutOfRangeException(nameof(bone), bone,
                        "not a bone of the torso chain (HumanBoneHierarchy.TorsoChain)");
            }
        }

        /// <summary>
        /// The nearest ancestor of <paramref name="bone"/> that the avatar has, or null for the Hips. Transforms
        /// in between (missing humanoid bones, non-humanoid bones) move rigidly with this ancestor. Defined for
        /// bones the avatar lacks too.
        /// </summary>
        public HumanBone? Parent(HumanBone bone)
        {
            if (!parents.TryGetValue(bone, out HumanBone? parent))
            {
                throw new ArgumentOutOfRangeException(nameof(bone), bone, "not a humanoid bone Core poses");
            }
            return parent;
        }

        /// <summary>UpperArm to LowerArm, tiles.</summary>
        public double UpperArmLength(int side) =>
            Segment(HumanBoneHierarchy.UpperArm(side), HumanBoneHierarchy.LowerArm(side));

        /// <summary>LowerArm to Hand, tiles.</summary>
        public double ForearmLength(int side) => Segment(HumanBoneHierarchy.LowerArm(side), HumanBoneHierarchy.Hand(side));

        /// <summary>UpperLeg to LowerLeg, tiles.</summary>
        public double ThighLength(int side) => Segment(HumanBoneHierarchy.UpperLeg(side), HumanBoneHierarchy.LowerLeg(side));

        /// <summary>LowerLeg to Foot (the ankle), tiles.</summary>
        public double ShinLength(int side) => Segment(HumanBoneHierarchy.LowerLeg(side), HumanBoneHierarchy.Foot(side));

        /// <summary>Height of the ankle (Foot bone) above the ground, tiles.</summary>
        public double AnkleHeight(int side) => Rest(HumanBoneHierarchy.Foot(side)).Z;

        /// <summary>
        /// Rest position of the ball of the foot, where the foot bends, tiles: the Toes bone, or else
        /// <see cref="BallFraction"/> of the way from the ankle to the <see cref="ToeTip"/>.
        /// </summary>
        public Vec3 Ball(int side) => feet[Index(side)].Ball;

        /// <summary>
        /// Front-most sole point, tiles: from <see cref="RigInput"/>, or else on the ground ahead of the ankle (at
        /// 1 / <see cref="BallFraction"/> of the ankle to Toes vector, or <see cref="ToeReachPerLegLength"/>
        /// times the leg length along <see cref="FootForward"/> without a Toes bone).
        /// </summary>
        public Vec3 ToeTip(int side) => feet[Index(side)].ToeTip;

        /// <summary>
        /// Back-most sole point, tiles: from <see cref="RigInput"/>, or else on the ground behind the ankle,
        /// <see cref="HeelFraction"/> of the horizontal ankle to toe tip distance.
        /// </summary>
        public Vec3 Heel(int side) => feet[Index(side)].Heel;

        /// <summary>
        /// Horizontal unit vector the rest foot points along: ankle to Toes, else ankle to the measured toe
        /// tip, else forward (+y). Each direction counts only when it is clearly away from vertical and within
        /// 60 degrees of forward, so Toes under a digitigrade ankle leave the foot pointing forward.
        /// </summary>
        public Vec3 FootForward(int side) => feet[Index(side)].Forward;

        /// <summary>
        /// Unit axis the rest arm's elbow bends about: flexing turns the forearm about it, positively, towards
        /// the body's front. Perpendicular to the shoulder to hand direction (+z for the right arm in a T-pose).
        /// </summary>
        public Vec3 ElbowBendAxis(int side) => elbowAxes[Index(side)];

        /// <summary>
        /// Unit axis the rest leg's knee bends about: flexing turns the shin about it, positively, backwards.
        /// Perpendicular to the hip to ankle direction (-x for a leg pointing straight down).
        /// </summary>
        public Vec3 KneeBendAxis(int side) => kneeAxes[Index(side)];

        private Vec3 ShoulderRest(int side)
        {
            HumanBone shoulder = HumanBoneHierarchy.Shoulder(side);
            return Has(shoulder)
                ? Rest(shoulder)
                : Vec3.Lerp(NeckBase, Rest(HumanBoneHierarchy.UpperArm(side)), ShoulderBoneFraction);
        }

        private static int Index(int side)
        {
            HumanoidArgs.CheckSide(side);
            return side > 0 ? 1 : 0;
        }

        private double Segment(HumanBone from, HumanBone to) => Vec3.Distance(Rest(from), Rest(to));

        private Vec3 LimbDirection(HumanBone root, HumanBone end) => (Rest(end) - Rest(root)).Normalized;

        private HumanBone? NearestExistingAncestor(HumanBone bone)
        {
            HumanBone? parent = HumanBoneHierarchy.Parent(bone);
            while (parent.HasValue && !Has(parent.Value))
            {
                parent = HumanBoneHierarchy.Parent(parent.Value);
            }
            return parent;
        }

        private FootRest MeasureFoot(int side, Vec3? measuredToeTip, Vec3? measuredHeel, double legLength)
        {
            Vec3 ankle = Rest(HumanBoneHierarchy.Foot(side));
            bool hasToes = rest.TryGetValue(HumanBoneHierarchy.Toes(side), out Vec3 toes);
            // Toes under, behind or beside the ankle (a vertical digitigrade foot) give no direction: then the toe
            // tip does, or else the foot is assumed to point forward
            Vec3 forward = Vec3.UnitY;
            if (hasToes && IsFootDirection(toes - ankle))
            {
                forward = (toes - ankle).Horizontal.Normalized;
            }
            else if (measuredToeTip.HasValue && IsFootDirection(measuredToeTip.Value - ankle))
            {
                forward = (measuredToeTip.Value - ankle).Horizontal.Normalized;
            }
            Vec3 toeTip = measuredToeTip ?? (hasToes
                ? (ankle + (toes - ankle) / BallFraction).WithZ(0)
                : (ankle + forward * (ToeReachPerLegLength * legLength)).WithZ(0));
            Vec3 ball = hasToes ? toes : ankle + (toeTip - ankle) * BallFraction;
            Vec3 heel = measuredHeel ?? (ankle - forward * (HeelFraction * (toeTip - ankle).Horizontal.Length)).WithZ(0);
            return new FootRest(ball, toeTip, heel, forward);
        }

        // an offset from the ankle a foot can point along: far enough from vertical to tell, and forward
        private static bool IsFootDirection(Vec3 offset)
        {
            Vec3 horizontal = offset.Horizontal;
            return horizontal.Length > MinSegment && horizontal.Length >= MinFootHorizontal * offset.Length &&
                   horizontal.Normalized.Y >= MinFootForward;
        }

        private static void CheckInput(RigInput input)
        {
            var missing = new List<HumanBone>();
            foreach (HumanBone bone in HumanBones.Required)
            {
                if (!input.Has(bone))
                {
                    missing.Add(bone);
                }
            }
            if (missing.Count > 0)
            {
                throw new ArgumentException("the avatar lacks required humanoid bones: " + string.Join(", ", missing),
                    nameof(input));
            }
            foreach (KeyValuePair<HumanBone, Vec3> bone in input.Bones)
            {
                HumanoidArgs.CheckFinite(bone.Value, "input.Bones[" + bone.Key + "]");
            }
            HumanoidArgs.CheckFinite(input.MeshTop, "input.MeshTop");
            HumanoidArgs.CheckFinite(input.MeshBottom, "input.MeshBottom");
            CheckFinite(input.LeftToeTip, "input.LeftToeTip");
            CheckFinite(input.RightToeTip, "input.RightToeTip");
            CheckFinite(input.LeftHeel, "input.LeftHeel");
            CheckFinite(input.RightHeel, "input.RightHeel");
        }

        private static void CheckFinite(Vec3? point, string name)
        {
            if (point.HasValue)
            {
                HumanoidArgs.CheckFinite(point.Value, name);
            }
        }

        private static double MeasureHeight(RigInput input, List<string> warnings)
        {
            double height = input.MeshTop - input.MeshBottom;
            if (height > 0)
            {
                return height;
            }
            double head = input.Bones[HumanBone.Head].Z - input.MeshBottom;
            if (!(head > 0))
            {
                throw new ArgumentException(
                    "cannot measure the avatar's height: neither MeshTop nor the Head bone is above MeshBottom",
                    nameof(input));
            }
            double estimate = head * HeightPerHeadHeight;
            warnings.Add(string.Format(CultureInfo.InvariantCulture,
                "the mesh bounds are empty (MeshTop {0} m is not above MeshBottom {1} m): the avatar's height, " +
                "{2:0.###} m, is estimated from its Head bone", input.MeshTop, input.MeshBottom, estimate));
            return estimate;
        }

        private static void CheckLimbs(Dictionary<HumanBone, Vec3> rest)
        {
            foreach (int side in Sides)
            {
                CheckLimb(rest, HumanBoneHierarchy.UpperArm(side), HumanBoneHierarchy.LowerArm(side),
                    HumanBoneHierarchy.Hand(side));
                CheckLimb(rest, HumanBoneHierarchy.UpperLeg(side), HumanBoneHierarchy.LowerLeg(side),
                    HumanBoneHierarchy.Foot(side));
            }
        }

        // every segment of a limb must have a length and point sideways or up or down, as in a T-pose
        private static void CheckLimb(Dictionary<HumanBone, Vec3> rest, HumanBone root, HumanBone joint, HumanBone end)
        {
            CheckSegment(rest, root, joint);
            CheckSegment(rest, joint, end);
            CheckSegment(rest, root, end);
        }

        private static void CheckSegment(Dictionary<HumanBone, Vec3> rest, HumanBone from, HumanBone to)
        {
            Vec3 segment = rest[to] - rest[from];
            if (segment.Length < MinSegment)
            {
                throw new ArgumentException(to + " is at the same place as " + from, "input");
            }
            if (Vec3.Cross(segment.Normalized, Vec3.UnitY).Length < MinSidewaysLimb)
            {
                throw new ArgumentException(from + " to " + to + " points (almost) straight forward or back: " +
                    "the rig must be measured in its T-pose", "input");
            }
        }

        private readonly struct FootRest
        {
            public readonly Vec3 Ball;
            public readonly Vec3 ToeTip;
            public readonly Vec3 Heel;
            public readonly Vec3 Forward;

            public FootRest(Vec3 ball, Vec3 toeTip, Vec3 heel, Vec3 forward)
            {
                Ball = ball;
                ToeTip = toeTip;
                Heel = heel;
                Forward = forward;
            }
        }
    }
}
