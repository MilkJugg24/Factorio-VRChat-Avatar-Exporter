using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>Prepares a copy of an avatar for rendering.</summary>
    internal static class AvatarCleaner
    {
        /// <summary>
        /// Removes every script (VRChat descriptors, PhysBones, constraints and other tools' components) and every
        /// EditorOnly object from <paramref name="avatar"/>, so only meshes, bones and materials remain, disables
        /// its Animators and makes skinned meshes follow their bones exactly at every render. Returns a line
        /// saying what was removed.
        /// </summary>
        public static string Strip(GameObject avatar)
        {
            foreach (Transform t in avatar.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t != null && t != avatar.transform && t.CompareTag("EditorOnly"))
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }
            int missing = 0;
            foreach (Transform t in avatar.GetComponentsInChildren<Transform>(true))
            {
                missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            var removed = new SortedDictionary<string, int>();
            // a component another one requires can only go after it, so repeat until nothing changes
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (MonoBehaviour script in avatar.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (script == null || IsRequiredByAnother(script))
                    {
                        continue;
                    }
                    string name = script.GetType().Name;
                    Object.DestroyImmediate(script);
                    if (script == null) // Unity refuses, with a Console error, when something else still needs it
                    {
                        removed[name] = removed.TryGetValue(name, out int n) ? n + 1 : 1;
                        changed = true;
                    }
                }
            }
            foreach (Animator animator in avatar.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }
            foreach (SkinnedMeshRenderer skinned in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
            return "removed scripts: " + (removed.Count == 0 ? "none" :
                string.Join(", ", removed.Select(r => r.Value == 1 ? r.Key : $"{r.Key} x{r.Value}"))) +
                (missing > 0 ? $", {missing} missing scripts" : "");
        }

        private static bool IsRequiredByAnother(Component component)
        {
            Type type = component.GetType();
            foreach (Component other in component.GetComponents<Component>())
            {
                if (other == null || other == component)
                {
                    continue;
                }
                foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if (Requires(require.m_Type0, type) || Requires(require.m_Type1, type) || Requires(require.m_Type2, type))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool Requires(Type required, Type type) => required != null && required.IsAssignableFrom(type);
    }
}
