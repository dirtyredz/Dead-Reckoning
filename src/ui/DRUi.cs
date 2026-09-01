using UnityEngine;

namespace DeadReckoning
{
    /// <summary>Small UI/transform helpers shared across the mod.</summary>
    internal static class DRUi
    {
        /// <summary>Depth-first search for a descendant (or the root itself) named <paramref name="name"/>.
        /// Returns null if none matches. Shared by the map/picker highlight code and the button builders.</summary>
        internal static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
