using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>One animation of a pack (docs/vrc-avatar-format.md, "Animations").</summary>
    public sealed class AnimationInfo
    {
        public AnimationInfo(string name, int directionCount, int frameCount, bool loop, double defaultSpeed,
            bool inAir, bool gun, bool required)
        {
            Name = name;
            DirectionCount = directionCount;
            FrameCount = frameCount;
            Loop = loop;
            DefaultSpeed = defaultSpeed;
            InAir = inAir;
            Gun = gun;
            Required = required;
        }

        /// <summary>The pack format name, e.g. "run-gun".</summary>
        public string Name { get; }
        public int DirectionCount { get; }
        /// <summary>Frames the exporter renders (the format allows any count from 1 to 255).</summary>
        public int FrameCount { get; }
        /// <summary>Looping animations sample t = frame / frames; one-shot ones frame / (frames - 1).</summary>
        public bool Loop { get; }
        /// <summary>The animation_speed vrc-avatars uses unless the pack overrides it.</summary>
        public double DefaultSpeed { get; }
        /// <summary>Part of the flight set (needs <c>flight = true</c>).</summary>
        public bool InAir { get; }
        /// <summary>Holds a gun.</summary>
        public bool Gun { get; }
        /// <summary>Every pack has it (the others are the flight set).</summary>
        public bool Required { get; }

        /// <summary>Uses the 18 (gun direction, moving direction) rows of <see cref="AnimationSpec.GunMoveDirections"/>.</summary>
        public bool GunMoveRows => DirectionCount == 18;
    }

    /// <summary>A sheet row of an animation: where the character faces and which way it moves.</summary>
    public readonly struct DirectionInfo
    {
        public DirectionInfo(double orientation, double move)
        {
            Orientation = orientation;
            Move = move;
        }

        /// <summary>Facing (torso, head, gun), as a fraction of a clockwise turn from north.</summary>
        public double Orientation { get; }

        /// <summary>
        /// Moving direction relative to the facing, clockwise radians (the hips and legs turn this way).
        /// Zero except for gun rows of run-gun / fly-gun.
        /// </summary>
        public double Move { get; }
    }

    /// <summary>The animations the exporter renders: a port of the tables in tools/dragon_render/anims.py.</summary>
    public static class AnimationSpec
    {
        public const string Idle = "idle";
        public const string IdleGun = "idle-gun";
        public const string Run = "run";
        public const string RunGun = "run-gun";
        public const string Sprint = "sprint";
        public const string Mine = "mine";
        public const string Corpse = "corpse";
        public const string Hover = "hover";
        public const string HoverGun = "hover-gun";
        public const string Fly = "fly";
        public const string FlyGun = "fly-gun";
        public const string TakeOff = "take-off";

        /// <summary>1-based frame of the mine animation in which the pick hits the ground.</summary>
        public const int MineImpactFrame = 19;

        /// <summary>
        /// How much further a sprint cycle goes than a run cycle: the sprint's distance_per_frame is the run's times
        /// this, so at the same speed its strides come this many times less often.
        /// </summary>
        public const double SprintCycleScale = 2.2;

        /// <summary>1-based frames of the run (and sprint) cycle in which each foot lands.</summary>
        public static readonly IReadOnlyList<int> LeftFootFrames = new[] { 1 };
        public static readonly IReadOnlyList<int> RightFootFrames = new[] { 7 };

        /// <summary>All animations in the order they are rendered and written.</summary>
        public static readonly IReadOnlyList<AnimationInfo> All = new[]
        {
            // 48 frames over 16 s: a wing stretches every 8 s, the other side each time
            new AnimationInfo(Idle, 8, 48, true, 0.05, false, false, true),
            new AnimationInfo(IdleGun, 8, 48, true, 0.05, false, true, true),
            new AnimationInfo(Run, 8, 12, true, 0.6, false, false, true),
            new AnimationInfo(RunGun, 18, 12, true, 0.6, false, true, true),
            // running fast (exoskeletons): long bounding strides, feet landing on frames 1 and 7 as in the run
            new AnimationInfo(Sprint, 8, 12, true, 0.6, false, false, true),
            // the engineer's own: 26 frames at 0.9, the pick hitting on frame 19
            new AnimationInfo(Mine, 8, 26, true, 0.9, false, false, true),
            new AnimationInfo(Corpse, 1, 1, true, 1.0, false, false, true),
            new AnimationInfo(Hover, 8, 8, true, 0.27, true, false, false),
            new AnimationInfo(HoverGun, 8, 8, true, 0.27, true, true, false),
            new AnimationInfo(Fly, 8, 8, true, 0.3, true, false, false),
            new AnimationInfo(FlyGun, 18, 8, true, 0.3, true, true, false),
            new AnimationInfo(TakeOff, 8, 8, false, 0.3, true, false, false),
        };

        private static readonly Dictionary<string, AnimationInfo> ByName = BuildIndex();

        private static Dictionary<string, AnimationInfo> BuildIndex()
        {
            var d = new Dictionary<string, AnimationInfo>(StringComparer.Ordinal);
            foreach (AnimationInfo a in All)
            {
                d.Add(a.Name, a);
            }
            return d;
        }

        public static AnimationInfo Get(string name)
        {
            if (!ByName.TryGetValue(name, out AnimationInfo info))
            {
                throw new ArgumentException("unknown animation " + name, nameof(name));
            }
            return info;
        }

        /// <summary>The animations of a pack, with or without the flight set.</summary>
        public static IEnumerable<AnimationInfo> ForPack(bool flight)
        {
            foreach (AnimationInfo a in All)
            {
                if (a.Required || flight)
                {
                    yield return a;
                }
            }
        }

        /// <summary>
        /// Rows of the 18-direction gun animations: (gun direction, moving direction) in eighths of a
        /// turn clockwise from north, as in the vanilla engineer's sheet (anims.GUN_MOVE_DIRECTIONS).
        /// </summary>
        public static readonly IReadOnlyList<(int gun, int move)> GunMoveDirections = new[]
        {
            (0, 0), (0, 1), (0, 2),
            (1, 0), (1, 1), (1, 2), (1, 3),
            (2, 0), (2, 1), (2, 2), (2, 3),
            (3, 1), (3, 2), (3, 3), (3, 4),
            (4, 2), (4, 3), (4, 4),
        };

        /// <summary>Sheet row <paramref name="row"/> of an animation: <c>anims.direction()</c>.</summary>
        public static DirectionInfo Direction(string animation, int row)
        {
            AnimationInfo info = Get(animation);
            if (row < 0 || row >= info.DirectionCount)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }
            if (info.GunMoveRows)
            {
                (int gun, int move) = GunMoveDirections[row];
                return new DirectionInfo(gun / 8.0, (move - gun) * MathUtil.Tau / 8.0);
            }
            return new DirectionInfo((double)row / info.DirectionCount, 0.0);
        }

        /// <summary>The animation time t of a frame: <c>anims.frame_phase()</c>.</summary>
        public static double FramePhase(string animation, int frame)
        {
            AnimationInfo info = Get(animation);
            if (frame < 0 || frame >= info.FrameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }
            if (info.Loop || info.FrameCount == 1)
            {
                return (double)frame / info.FrameCount;
            }
            return (double)frame / (info.FrameCount - 1);
        }
    }
}
