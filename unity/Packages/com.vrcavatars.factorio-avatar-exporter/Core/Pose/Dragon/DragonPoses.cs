using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The dragon's pose for every pack animation: the dispatch of anims.py's <c>ANIMATIONS</c> table,
    /// plus <see cref="AnimationSpec.Corpse"/> (<c>corpse_pose</c>).
    /// </summary>
    public static class DragonPoses
    {
        private static readonly Dictionary<string, Func<BodyShape, double, DragonPose>> Fixed =
            new Dictionary<string, Func<BodyShape, double, DragonPose>>(StringComparer.Ordinal)
            {
                { AnimationSpec.Idle, (b, t) => DragonAnimations.Idle(b, t, false) },
                { AnimationSpec.IdleGun, (b, t) => DragonAnimations.Idle(b, t, true) },
                { AnimationSpec.Run, (b, t) => DragonAnimations.Run(b, t, false) },
                // the dragon has no sprint of its own
                { AnimationSpec.Sprint, (b, t) => DragonAnimations.Run(b, t, false) },
                { AnimationSpec.Mine, DragonAnimations.Mine },
                { AnimationSpec.Hover, (b, t) => DragonAnimations.Hover(b, t, false) },
                { AnimationSpec.HoverGun, (b, t) => DragonAnimations.Hover(b, t, true) },
                { AnimationSpec.Fly, (b, t) => DragonAnimations.Fly(b, t, false) },
                { AnimationSpec.TakeOff, DragonAnimations.TakeOff },
                { AnimationSpec.Corpse, (b, t) => DragonAnimations.CorpsePose(b) },
            };

        private static readonly Dictionary<string, Func<BodyShape, double, double, DragonPose>> Moving =
            new Dictionary<string, Func<BodyShape, double, double, DragonPose>>(StringComparer.Ordinal)
            {
                { AnimationSpec.RunGun, (b, t, move) => DragonAnimations.Run(b, t, true, move) },
                { AnimationSpec.FlyGun, (b, t, move) => DragonAnimations.Fly(b, t, true, move) },
            };

        /// <summary>
        /// The dragon's pose in <paramref name="animation"/> (an <see cref="AnimationSpec"/> name) at time
        /// <paramref name="t"/> (<see cref="AnimationSpec.FramePhase"/>; the corpse ignores it).
        /// <paramref name="move"/> (<see cref="DirectionInfo.Move"/>, clockwise radians) turns the hips and
        /// legs of run-gun and fly-gun; like anims.py, the other animations take no move, so it must be 0.
        /// </summary>
        /// <exception cref="ArgumentException">Unknown animation, or a move for an animation without one.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="t"/> or <paramref name="move"/> is not finite.</exception>
        public static DragonPose Pose(string animation, BodyShape body, double t, double move = 0)
        {
            if (animation == null)
            {
                throw new ArgumentNullException(nameof(animation));
            }
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            if (double.IsNaN(t) || double.IsInfinity(t))
            {
                throw new ArgumentOutOfRangeException(nameof(t), t, "t must be finite");
            }
            if (double.IsNaN(move) || double.IsInfinity(move))
            {
                throw new ArgumentOutOfRangeException(nameof(move), move, "move must be finite");
            }
            if (Moving.TryGetValue(animation, out Func<BodyShape, double, double, DragonPose> moving))
            {
                return moving(body, t, move);
            }
            if (!Fixed.TryGetValue(animation, out Func<BodyShape, double, DragonPose> pose))
            {
                throw new ArgumentException("unknown animation \"" + animation + "\"", nameof(animation));
            }
            if (move != 0)
            {
                throw new ArgumentException(
                    "only " + AnimationSpec.RunGun + " and " + AnimationSpec.FlyGun + " take a move direction, not " + animation,
                    nameof(move));
            }
            return pose(body, t);
        }
    }
}
