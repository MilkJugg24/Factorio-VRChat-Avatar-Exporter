using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// A rig in a pose (<see cref="HumanoidSkeleton.Pose"/>): the position and world delta of every bone it has,
    /// model space, tiles.
    /// </summary>
    public sealed class PosedSkeleton
    {
        private readonly Dictionary<HumanBone, Vec3> positions;
        private readonly Dictionary<HumanBone, Quat> deltas;

        internal PosedSkeleton(HumanoidRig rig, Dictionary<HumanBone, Vec3> positions, Dictionary<HumanBone, Quat> deltas)
        {
            Rig = rig;
            this.positions = positions;
            this.deltas = deltas;
        }

        public HumanoidRig Rig { get; }

        /// <summary>Posed position of every bone the rig has.</summary>
        public IReadOnlyDictionary<HumanBone, Vec3> Positions => positions;

        /// <summary>Rotation of every bone the rig has relative to its rest rotation (listed or inherited).</summary>
        public IReadOnlyDictionary<HumanBone, Quat> Deltas => deltas;

        /// <summary>Posed position of a bone the rig has.</summary>
        public Vec3 Position(HumanBone bone)
        {
            if (!positions.TryGetValue(bone, out Vec3 p))
            {
                throw new ArgumentException("the rig has no " + bone + " bone", nameof(bone));
            }
            return p;
        }

        /// <summary>World delta of a bone the rig has.</summary>
        public Quat Delta(HumanBone bone)
        {
            if (!deltas.TryGetValue(bone, out Quat q))
            {
                throw new ArgumentException("the rig has no " + bone + " bone", nameof(bone));
            }
            return q;
        }

        /// <summary>Where a point given in rest space goes when it moves rigidly with <paramref name="bone"/>.</summary>
        public Vec3 Attached(HumanBone bone, Vec3 restPoint) => Position(bone) + Delta(bone).Rotate(restPoint - Rig.Rest(bone));

        /// <summary>
        /// Posed position of a bone of <see cref="HumanBoneHierarchy.TorsoChain"/>, whether or not the rig has it:
        /// the bone's own position, or for a missing optional bone its estimated rest point
        /// (<see cref="HumanoidRig.TorsoRest"/>) carried by its nearest existing ancestor.
        /// </summary>
        public Vec3 TorsoPoint(HumanBone bone)
        {
            Vec3 restPoint = Rig.TorsoRest(bone);
            // a missing bone is optional, so it has an ancestor: the Hips at least
            return Rig.Has(bone) ? Position(bone) : Attached(Rig.Parent(bone).Value, restPoint);
        }
    }
}
