using System;
using System.Reflection;
using Director.Nodes;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// Adds a "Track Quest" button to the Quest Log's right-hand panel, just above the Objectives
    /// section, wired to Dead Reckoning's quest tracking. Hooks <see cref="QuestScreen"/>.ShowQuestInfo
    /// (which repopulates the panel whenever a quest is selected) so the button always reflects the
    /// currently-shown quest. Purely additive.
    /// </summary>
    [HarmonyPatch(typeof(QuestScreen), "ShowQuestInfo")]
    internal static class QuestScreenShowInfoPatch
    {
        private static void Postfix(QuestScreen __instance, QuestListWidget widget)
        {
            try { QuestTrackButton.Attach(__instance, widget); }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Quest track button attach failed: {e.Message}"); }
        }
    }

    internal static class QuestTrackButton
    {
        private const string ButtonName = "DR_QuestTrackButton";
        private static readonly Color IdleText = new Color(0.96f, 0.85f, 0.54f); // gold, matches headers
        private static readonly Color TrackingText = new Color(0.42f, 1f, 0.55f); // green = tracking

        private static readonly FieldInfo ObjectiveContainerField =
            AccessTools.Field(typeof(QuestScreen), "objectiveContainer");

        internal static void Attach(QuestScreen screen, QuestListWidget widget)
        {
            var container = ObjectiveContainerField?.GetValue(screen) as GameObject;
            if (container == null) return;
            Transform row = container.transform.parent; // the vertical stack that holds the objectives block
            if (row == null) return;

            Transform existing = row.Find(ButtonName);
            bool haveQuest = widget != null && widget.StartQuestNode != null && widget.Data != null;

            if (!haveQuest)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            DRQuestButton btn = existing != null ? existing.GetComponent<DRQuestButton>() : Build(row, container.transform.GetSiblingIndex(), screen);
            if (btn == null) return;

            btn.gameObject.SetActive(true);
            btn.Node = widget.StartQuestNode;
            btn.Data = widget.Data;
            Refresh(btn);
        }

        private static void Refresh(DRQuestButton btn)
        {
            bool tracked = SkullGuide.Instance != null && SkullGuide.Instance.IsQuestTracked(btn.Data);
            if (btn.Label != null)
            {
                btn.Label.text = tracked ? "Seeking Quest" : "Seek Quest";
                btn.Label.color = tracked ? TrackingText : IdleText;
            }
            if (btn.Icon != null) btn.Icon.color = tracked ? TrackingText : Color.white;
        }

        private static DRQuestButton Build(Transform parent, int siblingIndex, QuestScreen screen)
        {
            var go = new GameObject(ButtonName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetSiblingIndex(siblingIndex); // above the Objectives block
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = 46f;

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f); // no box — transparent click area only
            bg.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.targetGraphic = bg;

            TextMeshProUGUI fontSrc = screen.GetComponentInChildren<TextMeshProUGUI>(true);

            // Pin icon on the left.
            Image icon = null;
            Sprite sprite = TrackIcon.Get();
            if (sprite != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var irt = (RectTransform)iconGo.transform;
                irt.SetParent(go.transform, false);
                irt.anchorMin = new Vector2(0f, 0.5f); irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.anchoredPosition = new Vector2(10f, 0f);
                irt.sizeDelta = new Vector2(40f, 40f);
                icon = iconGo.GetComponent<Image>();
                icon.sprite = sprite; icon.preserveAspect = true; icon.raycastTarget = false;
            }

            // Label.
            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)txtGo.transform;
            trt.SetParent(go.transform, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(56f, 0f); trt.offsetMax = new Vector2(-12f, 0f);
            var label = txtGo.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontSize = 22f;
            label.raycastTarget = false;
            if (fontSrc != null)
            {
                if (fontSrc.font != null) label.font = fontSrc.font;
                if (fontSrc.fontSharedMaterial != null) label.fontSharedMaterial = fontSrc.fontSharedMaterial;
            }

            var drb = go.AddComponent<DRQuestButton>();
            drb.Bg = bg; drb.Icon = icon; drb.Label = label;
            if (icon != null) go.AddComponent<HoverScale>().Target = icon.transform;

            button.onClick.AddListener(() =>
            {
                if (SkullGuide.Instance != null && drb.Node != null && drb.Data != null)
                    SkullGuide.Instance.ToggleQuest(drb.Node, drb.Data);
                Refresh(drb);
            });
            return drb;
        }
    }

    /// <summary>Holds the quest button's pieces + the quest it currently represents.</summary>
    internal sealed class DRQuestButton : MonoBehaviour
    {
        internal StartQuestNode Node;
        internal QuestPersistence Data;
        internal Image Bg;
        internal Image Icon;
        internal TextMeshProUGUI Label;
    }
}
