using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    public enum PropKind
    {
        Gun,
        Pickaxe,
    }

    /// <summary>A held tool, in model space (tiles).</summary>
    public sealed class PropPose
    {
        public PropPose(PropKind kind, Vec3 basePoint, Vec3 tip, Vec3 up)
        {
            Kind = kind;
            Base = basePoint;
            Tip = tip;
            Up = up;
        }

        public PropKind Kind { get; }

        /// <summary>Gun: the stock. Pickaxe: the bottom of the handle.</summary>
        public Vec3 Base { get; }

        /// <summary>Gun: the muzzle. Pickaxe: the top of the handle, where the head is.</summary>
        public Vec3 Tip { get; }

        /// <summary>
        /// Unit vector perpendicular to Base -> Tip. Gun: towards its top (the magazine hangs the other way).
        /// Pickaxe: along the head; the pick's point extends towards -Up (model.py's <c>e</c>).
        /// </summary>
        public Vec3 Up { get; }
    }

    /// <summary>Names of the points in <see cref="PoseResult.Joints"/>. Sides: ".L" = left, ".R" = right.</summary>
    public static class JointNames
    {
        public const string Pelvis = "pelvis";
        public const string Waist = "waist";
        public const string Chest = "chest";
        public const string NeckBase = "neck_base";
        public const string NeckTop = "neck_top";
        public const string Head = "head";
        public const string HeadTop = "head_top";

        public static string Shoulder(int side) => "shoulder" + Suffix(side);
        public static string Elbow(int side) => "elbow" + Suffix(side);
        public static string Hand(int side) => "hand" + Suffix(side);
        public static string Hip(int side) => "hip" + Suffix(side);
        public static string Knee(int side) => "knee" + Suffix(side);
        public static string Ankle(int side) => "ankle" + Suffix(side);
        public static string Ball(int side) => "ball" + Suffix(side);
        public static string Toe(int side) => "toe" + Suffix(side);

        /// <summary>Side as in model.py: +1 = right (positive x), -1 = left.</summary>
        public static string Suffix(int side) => side > 0 ? ".R" : ".L";
    }

    /// <summary>
    /// One pose of the avatar, in model space (tiles). The renderer applies it as:
    /// <list type="number">
    /// <item>in <see cref="HumanBones.ParentFirst"/> order, every bone in <see cref="WorldDeltas"/> gets
    /// the model space rotation <c>WorldDeltas[bone] * restRotation(bone)</c>; bones not listed (and
    /// every non-humanoid transform) keep their rotation relative to their parent;</item>
    /// <item>the Hips bone is moved to <see cref="HipsPosition"/>;</item>
    /// <item>fingers curl by <see cref="LeftGrip"/> / <see cref="RightGrip"/>;</item>
    /// <item>the <see cref="Prop"/>, if any, is drawn.</item>
    /// </list>
    /// </summary>
    public sealed class PoseResult
    {
        /// <summary>Where the Hips bone goes, model space, tiles.</summary>
        public Vec3 HipsPosition { get; set; }

        /// <summary>Model space rotation of each posed bone relative to its rest (T-pose) rotation.</summary>
        public Dictionary<HumanBone, Quat> WorldDeltas { get; } = new Dictionary<HumanBone, Quat>();

        /// <summary>
        /// Named points of the pose (<see cref="JointNames"/>), model space, tiles: the stick figure
        /// renderer draws these, and tests compare them with forward kinematics of <see cref="WorldDeltas"/>.
        /// </summary>
        public Dictionary<string, Vec3> Joints { get; } = new Dictionary<string, Vec3>();

        public PropPose Prop { get; set; }

        /// <summary>
        /// Finger curl, 0 = relaxed open hand, 1 = closed around a handle. Fingers curl about the width of the
        /// palm (the Hand bone's rest +y, model space, in the palms-down T-pose), so a pose that closes a hand round
        /// a handle turns that axis along the handle.
        /// </summary>
        public double LeftGrip { get; set; }
        public double RightGrip { get; set; }

        /// <summary>
        /// The renderer moves the posed model down (or up) so its lowest visible point touches the ground
        /// (used by the corpse, whose height depends on the mesh).
        /// </summary>
        public bool GroundToLowestPoint { get; set; }

        /// <summary>
        /// How far the left and right wing are spread, for avatars with wings (bones named "wing"; ignored
        /// otherwise): 0 leaves a wing as the avatar was saved (often folded down the back), 1 turns it all the way
        /// to <see cref="WingElevation"/>. The renderer turns each whole wing about its hinge (where it joins the
        /// body) and the chest's forward axis.
        /// </summary>
        public double LeftWingSpread { get; set; }
        public double RightWingSpread { get; set; }

        /// <summary>
        /// Where a fully spread wing points, radians above straight out sideways (negative below), measured in the
        /// chest's own frame, whatever the angle the wing was saved at.
        /// </summary>
        public double WingElevation { get; set; }

        /// <summary>
        /// The tail's sideways swing, for avatars with tail bones (named "tail"): each tail bone, from the root,
        /// turns <c>TailSwing * sin(TailPhase - TailWaveDelay * index)</c> radians about the hips' up axis
        /// (positive to the avatar's right), so a wave runs down the tail and its tip trails. A tail of more than
        /// <see cref="TailBones"/> bones scales both by TailBones / bones.
        /// </summary>
        public double TailSwing { get; set; }
        public double TailPhase { get; set; }

        /// <summary>How far the tail is raised, radians, spread over its bones (positive up).</summary>
        public double TailLift { get; set; }

        /// <summary>Phase delay of the tail's wave per bone, radians.</summary>
        public const double TailWaveDelay = 0.7;

        /// <summary>
        /// The tail length, in bones, that <see cref="TailSwing"/> and <see cref="TailWaveDelay"/> are per bone of:
        /// a longer tail shares them out over its bones, so it sways no further in all.
        /// </summary>
        public const int TailBones = 5;
    }

    /// <summary>Timing values written to the pack (docs/vrc-avatar-format.md, "timing").</summary>
    public sealed class PackTiming
    {
        public double DistancePerFrame { get; set; } = 0.238;

        /// <summary>The sprint's distance per frame (<see cref="AnimationSpec.SprintCycleScale"/> times the run's).</summary>
        public double SprintDistancePerFrame { get; set; } = 0.238 * AnimationSpec.SprintCycleScale;
        public int MineImpactFrame { get; set; } = AnimationSpec.MineImpactFrame;
        public IReadOnlyList<int> LeftFootFrames { get; set; } = AnimationSpec.LeftFootFrames;
        public IReadOnlyList<int> RightFootFrames { get; set; } = AnimationSpec.RightFootFrames;
    }

    /// <summary>Produces the pose of every frame.</summary>
    public interface IPoseSource
    {
        /// <summary>
        /// The pose of <paramref name="animation"/> at time <paramref name="t"/>
        /// (<see cref="AnimationSpec.FramePhase"/>), moving <paramref name="move"/> radians clockwise from
        /// the facing direction (<see cref="DirectionInfo.Move"/>).
        /// </summary>
        PoseResult Pose(string animation, double t, double move);

        /// <summary>Timing values matching these poses (stride length, impact and foot frames).</summary>
        PackTiming Timing { get; }
    }
}
