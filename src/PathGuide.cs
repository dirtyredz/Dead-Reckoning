using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

namespace DeadReckoning
{
    /// <summary>
    /// Computes an obstacle-avoiding path (via the game's A* Pathfinding Project graph) from the player
    /// to the current target, and returns a point a set distance ALONG that path — so the skull leads
    /// you around furniture/walls instead of cutting a straight line.
    ///
    /// The path SHAPE is recomputed only on a throttle, but the lead point is derived every frame by
    /// projecting the player's CURRENT position onto that path and walking forward — so it glides
    /// smoothly as you move instead of jumping each time the path is recalculated. Returns false when
    /// no path is available so the caller can fall back to a straight line.
    /// </summary>
    internal sealed class PathGuide
    {
        private List<Vector3> path;
        private float nextCalc;
        private Vector3 lastTo;
        private bool haveLast;
        private Vector3 smoothed;
        private bool haveSmoothed;

        internal bool TryLead(Vector3 from, Vector3 to, float alongDist, out Vector3 lead)
        {
            lead = default;

            // Recompute the path SHAPE occasionally (target moved, or throttle elapsed). Keep the old
            // path if a recompute fails, so we don't flicker back to straight-line mid-walk.
            bool stale = !haveLast || Time.time >= nextCalc || (to - lastTo).sqrMagnitude > 4f;
            if (stale)
            {
                nextCalc = Time.time + 0.4f;
                lastTo = to; haveLast = true;
                List<Vector3> fresh = Compute(from, to);
                if (fresh != null && fresh.Count >= 2) path = fresh;
            }

            if (path == null || path.Count < 2) return false;

            // Project the player's CURRENT position onto the path (flat, ignoring height) → the cumulative
            // distance sProj along the path at the nearest point.
            float bestDist = float.MaxValue;
            float sProj = 0f;
            float acc = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i], b = path[i + 1];
                Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z), p2 = new Vector2(from.x, from.z);
                Vector2 ab = b2 - a2;
                float abLen2 = ab.sqrMagnitude;
                float t = abLen2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / abLen2) : 0f;
                Vector2 proj = a2 + ab * t;
                float d = (p2 - proj).sqrMagnitude;
                float segLen = (b - a).magnitude;
                if (d < bestDist) { bestDist = d; sProj = acc + segLen * t; }
                acc += segLen;
            }
            float total = acc;
            if (total < 0.01f) return false;

            // Lead a standoff ahead of the player along the path, but never past ~90% of the way there.
            float s = Mathf.Min(sProj + alongDist, total * 0.9f);
            Vector3 raw = PointAt(s);

            // Smooth out the small jumps that happen when the path SHAPE is recomputed (frame-rate
            // independent low-pass).
            smoothed = haveSmoothed ? Vector3.Lerp(smoothed, raw, 1f - Mathf.Exp(-3.5f * Time.deltaTime)) : raw;
            haveSmoothed = true;
            lead = smoothed;
            return true;
        }

        internal void Clear() { path = null; haveLast = false; haveSmoothed = false; }

        /// <summary>The world point at cumulative distance <paramref name="s"/> along the path.</summary>
        private Vector3 PointAt(float s)
        {
            float acc = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i], b = path[i + 1];
                float len = (b - a).magnitude;
                if (s <= acc + len || i == path.Count - 2)
                {
                    float t = len > 1e-4f ? Mathf.Clamp01((s - acc) / len) : 0f;
                    return Vector3.Lerp(a, b, t);
                }
                acc += len;
            }
            return path[path.Count - 1];
        }

        private static List<Vector3> Compute(Vector3 from, Vector3 to)
        {
            try
            {
                if (AstarPath.active == null) return null;
                if (NavigationGraph.Exists && NavigationGraph.Instance != null && NavigationGraph.Instance.IsProcessing)
                    return null; // graph is regenerating (room switch) — skip this tick

                ABPath p = ABPath.Construct(from, to, null);
                // Route like a land NPC: the navigation mask excludes the water tag (so it uses bridges
                // instead of cutting through rivers), and the tag penalties prefer proper paths.
                p.enabledTags = NavigationGraphHelper.GetNavigationMask(NpcConfigAsset.MovementType.Land, false);
                p.tagPenalties = NavigationGraphHelper.GetTagPenalties(preferPreferredPaths: true, preferWater: false, inscene: true);
                AstarPath.StartPath(p);
                p.BlockUntilCalculated();
                if (p.error || p.vectorPath == null || p.vectorPath.Count == 0) return null;
                return new List<Vector3>(p.vectorPath);
            }
            catch
            {
                return null;
            }
        }
    }
}
