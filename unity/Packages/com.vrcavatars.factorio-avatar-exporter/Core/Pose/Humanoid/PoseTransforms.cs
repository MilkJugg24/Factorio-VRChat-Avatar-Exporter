using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Whole-pose transformations of <see cref="PoseResult"/>s.</summary>
    public static class PoseTransforms
    {
        /// <summary>
        /// A copy of <paramref name="pose"/> moved as one rigid body: every point p goes to
        /// <c>pivot + rotation.Rotate(p - pivot) + translation</c> (model space, tiles). Every world delta is
        /// pre-multiplied by <paramref name="rotation"/> (normalized), and the Hips get one even when the pose
        /// lists none (their implied delta is the identity), so bones that inherit their rotation turn too;
        /// <see cref="PoseResult.HipsPosition"/>, <see cref="PoseResult.Joints"/> and the
        /// <see cref="PoseResult.Prop"/> are transformed the same way, so forward kinematics of the result is the
        /// transformed forward kinematics of the pose. Grips and <see cref="PoseResult.GroundToLowestPoint"/> are
        /// copied. Used e.g. to lay a standing pose down as a corpse.
        /// </summary>
        public static PoseResult Rigid(PoseResult pose, Quat rotation, Vec3 pivot, Vec3 translation)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }
            Quat r = HumanoidArgs.CheckRotation(rotation, nameof(rotation));
            HumanoidArgs.CheckFinite(pivot, nameof(pivot));
            HumanoidArgs.CheckFinite(translation, nameof(translation));
            Vec3 Move(Vec3 p) => pivot + r.Rotate(p - pivot) + translation;

            var result = new PoseResult
            {
                HipsPosition = Move(pose.HipsPosition),
                LeftGrip = pose.LeftGrip,
                RightGrip = pose.RightGrip,
                GroundToLowestPoint = pose.GroundToLowestPoint,
            };
            foreach (KeyValuePair<HumanBone, Quat> delta in pose.WorldDeltas)
            {
                result.WorldDeltas.Add(delta.Key, (r * delta.Value.Normalized).Normalized);
            }
            if (!result.WorldDeltas.ContainsKey(HumanBone.Hips))
            {
                result.WorldDeltas.Add(HumanBone.Hips, r);
            }
            foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
            {
                result.Joints.Add(joint.Key, Move(joint.Value));
            }
            if (pose.Prop != null)
            {
                result.Prop = new PropPose(pose.Prop.Kind, Move(pose.Prop.Base), Move(pose.Prop.Tip), r.Rotate(pose.Prop.Up));
            }
            return result;
        }
    }
}
