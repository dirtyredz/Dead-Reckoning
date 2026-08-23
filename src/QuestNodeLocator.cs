using System;
using System.Linq;
using Director.Core;
using Director.Nodes;
using UnityEngine;

namespace DeadReckoning
{
    /// <summary>
    /// Finds the finer, in-scene target for a "gather / mine <item>" quest objective — the actual
    /// harvestable or mineable node in the loaded room — so the skull can point at the copper vein
    /// itself instead of stopping at the region it lives in.
    ///
    /// The game stores no world position on a quest objective (see <see cref="SkullGuide"/> —
    /// objectives carry only NPC / item / counter requirements plus the region name in the title).
    /// So we bridge from the objective's <b>item</b> to the world: read the <see cref="ItemAsset"/>
    /// the objective is about, then scan the loaded scene for an <see cref="Interactable"/> that
    /// yields it. The node only exists in the scene once the player is in its room, which is exactly
    /// when region-level routing has run out of road — so this kicks in precisely where the skull
    /// used to go idle.
    ///
    /// Deliberately does NOT handle delivery / turn-in objectives: nothing links an objective to the
    /// world zone that accepts a hand-in, and the loot a destructible drops is a randomised,
    /// event-driven table that can't be read without side effects — so we match a mineable node by
    /// its own placed item, not by its drops.
    /// </summary>
    internal static class QuestNodeLocator
    {
        /// <summary>The item a gather/mine objective is about (e.g. Copper Ore), or null when the
        /// objective isn't item-shaped. Prefers the asset injected behind the title's item token
        /// (robust — no rich-text colour parsing); falls back to an explicit item requirement.</summary>
        internal static ItemAsset ObjectiveItem(QuestObjectiveNode node)
        {
            if (node == null) return null;
            try
            {
                SpeechInjectionCollection inj = node.InjectionCollection;
                if (inj != null && inj.Entries != null)
                {
                    foreach (SpeechInjectionEntry e in inj.Entries)
                    {
                        if (e == null || e.VariableRef == null) continue;
                        if (e.VariableRef.GetRawValue() is ItemAsset ia && ia != null) return ia;
                    }
                }

                foreach (QuestObjectiveItemRequirementNode r in node.FilterChildNodes<QuestObjectiveItemRequirementNode>())
                {
                    if (r != null && r.ItemAsset != null) return r.ItemAsset;
                }
            }
            catch (Exception ex) { DeadReckoningPlugin.Log.LogWarning($"Objective item lookup failed: {ex.Message}"); }
            return null;
        }

        /// <summary>The transform of the nearest loaded, still-interactable node that yields
        /// <paramref name="item"/> — a harvestable (plant/tree/pickup) or a mineable destructible —
        /// or null when none is in the scene (player not in its room yet). Nearest by
        /// <paramref name="from"/>.</summary>
        internal static Transform NearestNode(ItemAsset item, Vector3 from)
        {
            if (item == null) return null;
            try
            {
                Transform best = null;
                float bestSqr = float.MaxValue;
                foreach (Interactable it in UnityEngine.Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None))
                {
                    if (it == null || it.IsDestroyed || it.Transform == null) continue;
                    if (!Yields(it, item)) continue;
                    float sqr = (it.ClosestPoint(from) - from).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = it.Transform; }
                }
                return best;
            }
            catch (Exception ex) { DeadReckoningPlugin.Log.LogWarning($"Quest node scan failed: {ex.Message}"); return null; }
        }

        /// <summary>True when the node we're already steering to is still in the scene and still yields
        /// <paramref name="item"/> — so we keep it instead of re-picking (and flip-flopping) each tick.</summary>
        internal static bool StillYields(Transform node, ItemAsset item)
        {
            if (node == null || item == null) return false;
            return Yields(node.GetComponent<Interactable>(), item);
        }

        /// <summary>True when this interactable still hands the player <paramref name="item"/>: a
        /// harvestable whose yield matches (and is ready), or a mineable destructible whose placed
        /// item matches.</summary>
        internal static bool Yields(Interactable it, ItemAsset item)
        {
            if (it == null) return false;

            IHarvestable h = it.GetComponent<IHarvestable>();
            if (h != null) return h.IsHarvestable && Matches(h.ItemAsset, item);

            DestructibleView dv = it.GetComponentInParent<DestructibleView>();
            if (dv != null && !dv.IsDestructed && dv.GridObjectPersistence != null)
                return Matches(dv.GridObjectPersistence.ItemAsset, item);

            return false;
        }

        // Same asset wins outright; otherwise fall back to a normalised name match, because a
        // mineable node's placed item can be a distinct asset from the ore it drops.
        private static bool Matches(ItemAsset candidate, ItemAsset want)
        {
            if (candidate == null || want == null) return false;
            if (candidate == want) return true;
            string a = Normalize(((UnityEngine.Object)candidate).name);
            string b = Normalize(((UnityEngine.Object)want).name);
            return a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b) || b.Contains(a));
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }
    }
}
