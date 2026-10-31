using System;
using UnityEngine;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>
    /// Bones chosen by hand in the exporter window (Advanced: wing and tail bones), instead of the ones found by
    /// name: paths below the avatar's root object (as <c>AnimationUtility.CalculateTransformPath</c> gives them),
    /// empty for "find automatically".
    /// </summary>
    [Serializable]
    internal sealed class BoneChoices
    {
        /// <summary>The left wing's root bone (its shoulder): the whole wing turns from it.</summary>
        public string LeftWing = "";

        /// <summary>The right wing's root bone.</summary>
        public string RightWing = "";

        /// <summary>The tail's first bone; the tail runs from it down through the child with the most bones below it.</summary>
        public string Tail = "";

        public bool ChoosesWings => !string.IsNullOrEmpty(LeftWing) || !string.IsNullOrEmpty(RightWing);

        public bool ChoosesTail => !string.IsNullOrEmpty(Tail);

        public BoneChoices Copy() => new BoneChoices { LeftWing = LeftWing ?? "", RightWing = RightWing ?? "", Tail = Tail ?? "" };
    }
}
