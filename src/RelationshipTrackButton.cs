using System;
using Chicken.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// Adds a "Track" button to each NPC card in the game's Relationships panel, wired to Dead
    /// Reckoning's tracker. Insight borrowed from lockyaw's Quest Tracker (patch
    /// RelationshipListWidget.OnSetup, read the row's NpcConfigAsset via Data), but the button and
    /// wiring are our own. Purely additive — nothing in the existing tracking path is touched.
    /// </summary>
    [HarmonyPatch(typeof(RelationshipListWidget), "OnSetup")]
    internal static class RelationshipListWidgetOnSetupPatch
    {
        private static void Postfix(RelationshipListWidget __instance)
        {
            try { RelationshipTrackButton.Attach(__instance); }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Track button attach failed: {e.Message}"); }
        }
    }

    internal static class RelationshipTrackButton
    {
        private const string ButtonName = "DR_TrackButton";
        private static readonly Color Tracking = new Color(0.62f, 1f, 0.72f);

        /// <summary>Re-label every visible card (after the tracked NPC changes).</summary>
        internal static void RefreshAll()
        {
            foreach (RelationshipListWidget w in UnityEngine.Object.FindObjectsByType<RelationshipListWidget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                try { Attach(w); } catch { }
            }
        }

        private static bool probed;

        internal static void Attach(RelationshipListWidget w)
        {
            if (w == null) return;
            NpcConfigAsset cfg = ((ListWidget<RelationshipWidgetData>)w).Data.NpcConfigAsset;
            if (cfg == null) return;

            if (!probed && DeadReckoningPlugin.VerboseLogging.Value)
            {
                probed = true;
                try { DumpHierarchy(w); } catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Card probe failed: {e.Message}"); }
            }

            Transform existing = w.transform.Find(ButtonName);
            Button btn;
            TextMeshProUGUI label;
            if (existing != null)
            {
                btn = existing.GetComponent<Button>();
                label = existing.GetComponentInChildren<TextMeshProUGUI>();
            }
            else
            {
                Build(w, out btn, out label);
            }
            if (btn == null || label == null) return;

            bool tracked = SkullGuide.Instance != null && SkullGuide.Instance.IsTracked(cfg);
            label.text = tracked ? "Tracking" : "Track";
            label.color = tracked ? Tracking : Color.white;

            NpcConfigAsset captured = cfg;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                SkullGuide.Instance?.ToggleTrack(captured);
                RefreshAll();
            });
        }

        /// <summary>One-shot: log a relationship card's UI hierarchy (component types, sizes, sprites)
        /// so we can match/clone the native gift/speech icon buttons for a proper icon Track button.</summary>
        private static void DumpHierarchy(RelationshipListWidget w)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("DR-CARDPROBE relationship card hierarchy:");
            Walk(w.transform, 0, sb, w.transform);
            DeadReckoningPlugin.Log.LogInfo(sb.ToString());
        }

        private static void Walk(Transform t, int depth, System.Text.StringBuilder sb, Transform root)
        {
            string indent = new string(' ', depth * 2);
            var parts = new System.Collections.Generic.List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (c is Image img) tn += $"(sprite={(img.sprite != null ? img.sprite.name : "null")})";
                else if (c is TextMeshProUGUI tmp) tn += $"(text='{tmp.text}')";
                else if (c is RectTransform rtc) tn += $"(size={rtc.sizeDelta},pos={rtc.anchoredPosition})";
                parts.Add(tn);
            }
            sb.AppendLine($"{indent}{t.name} [{string.Join(", ", parts)}]");
            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), depth + 1, sb, root);
        }

        private static void Build(RelationshipListWidget w, out Button btn, out TextMeshProUGUI label)
        {
            // Borrow the card's own font so our text matches the game.
            TextMeshProUGUI srcFont = w.GetComponentInChildren<TextMeshProUGUI>();

            var go = new GameObject(ButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(w.transform, false);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-4f, -4f);
            rt.sizeDelta = new Vector2(58f, 20f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.12f, 0.08f, 0.18f, 0.55f);
            btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)txtGo.transform;
            trt.SetParent(go.transform, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            label = txtGo.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 12f;
            label.raycastTarget = false;
            if (srcFont != null)
            {
                if (srcFont.font != null) label.font = srcFont.font;
                if (srcFont.fontSharedMaterial != null) label.fontSharedMaterial = srcFont.fontSharedMaterial;
            }
        }
    }
}
