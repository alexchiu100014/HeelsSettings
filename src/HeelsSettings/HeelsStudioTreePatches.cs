using System;
using HarmonyLib;
using KKAPI.Studio;
using Studio;
using UnityEngine;

namespace HeelsSettings
{
    internal static class HeelsStudioTreePatches
    {
        internal static void Register(Harmony harmony)
        {
            if (!StudioAPI.InsideStudio) return;

            try
            {
                var onStart = AccessTools.Method(typeof(TreeNodeObject), "Start");
                if (onStart != null)
                    harmony.Patch(onStart,
                        postfix: new HarmonyMethod(typeof(HeelsStudioTreePatches), nameof(AfterTreeNodeStart)));

                var onRecalc = AccessTools.Method(typeof(TreeNodeObject),
                    nameof(TreeNodeObject.RecalcSelectButtonPos));
                if (onRecalc != null)
                    harmony.Patch(onRecalc,
                        postfix: new HarmonyMethod(typeof(HeelsStudioTreePatches), nameof(AfterRecalcSelectButtonPos)));
            }
            catch (Exception ex)
            {
                HeelsPlugin.Logger.LogWarning($"Failed to register studio tree patches: {ex.Message}");
            }
        }

        private static void AfterTreeNodeStart(TreeNodeObject __instance)
        {
            HeelsStudioOverlay.OnTreeNodeCreated(__instance);
        }

        private static void AfterRecalcSelectButtonPos(TreeNodeObject __instance)
        {
            try
            {
                var studio = global::Studio.Studio.Instance;
                if (studio?.dicInfo == null ||
                    !studio.dicInfo.TryGetValue(__instance, out ObjectCtrlInfo info) ||
                    !(info is OCIChar))
                    return;

                var toggle = __instance.gameObject.transform.Find("HeelsSettings_Toggle");
                if (toggle == null || !toggle.gameObject.activeSelf)
                    return;

                var t = Traverse.Create(__instance);
                var selectRect = t.Field("m_TransSelect").GetValue<RectTransform>();
                if (selectRect == null) return;

                float shift = t.Field("textPosX").GetValue<float>() * 0.5f;
                var pos = selectRect.anchoredPosition;
                selectRect.anchoredPosition = new Vector2(pos.x + shift, pos.y);
            }
            catch
            {
            }
        }
    }
}
