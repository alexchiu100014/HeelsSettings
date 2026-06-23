using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
using KKAPI.Maker.UI.Sidebar;
using KKAPI.Studio;
using KKAPI.Studio.UI;
using UniRx;
using UnityEngine;

namespace HeelsSettings
{
#if KKS
    [BepInPlugin(GUID, PluginName, PluginVersion)]
    [BepInDependency(KoikatuAPI.GUID, "1.37.0")]
    [BepInDependency("KKABMX.Core", "5.0")]
#else
    [BepInPlugin(GUID, PluginName, PluginVersion)]
    [BepInDependency(KoikatuAPI.GUID, "1.37.0")]
    [BepInDependency("KKABMX.Core", "5.0")]
#endif
    public class HeelsPlugin : BaseUnityPlugin
    {
#if KKS
        public const string GUID = "unknown.kks.heelssettings";
        private const string ABMXStateSyncGUID = "unknown.kks.abmxstatesync";
#else
        public const string GUID = "unknown.kk.heelssettings";
        private const string ABMXStateSyncGUID = "unknown.kk.abmxstatesync";
#endif
        public const string PluginName = "HeelsSettings";
        public const string PluginVersion = "1.0.0";
        public const string ExtDataKey = "HeelsSettings";

        internal new static ManualLogSource Logger;

        internal static HeelsSettingsWindow Window;
        internal static HeelsStudioOverlay Overlay;
        internal static SidebarToggle SidebarToggle;

        internal static ConfigEntry<float> SettingWindowX;
        internal static ConfigEntry<float> SettingWindowY;

        internal static bool GlobalEnabled = true;

#if KKS
        internal static readonly int[] ShoeSlots = { 7 };
#else
        internal static readonly int[] ShoeSlots = { 7, 8 };
#endif

        private void Awake() => Logger = base.Logger;

        private void Start()
        {
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ABMXStateSyncGUID))
            {
                Logger.LogWarning(
                    "HeelsSettings disabled: ABMXStateSync detected. " +
                    "Use ABMXStateSync's Heels panel instead.");
                enabled = false;
                return;
            }

            var hidden = new ConfigurationManagerAttributes { Browsable = false };
            SettingWindowX = Config.Bind("UI", "Window X", 540f,
                new ConfigDescription("", null, hidden));
            SettingWindowY = Config.Bind("UI", "Window Y", 80f,
                new ConfigDescription("", null, hidden));

            var harmony = Harmony.CreateAndPatchAll(typeof(HeelsPlugin), GUID);
            CharacterApi.RegisterExtraBehaviour<HeelsController>(ExtDataKey);

            HeelsStudioTreePatches.Register(harmony);

            if (StudioAPI.InsideStudio)
                WireStudioControls();
            else
                WireMakerControls();

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void WireMakerControls()
        {
            MakerAPI.RegisterCustomSubCategories += (sender, args) =>
            {
                Window = gameObject.AddComponent<HeelsSettingsWindow>();
                SidebarToggle = args.AddSidebarControl(new SidebarToggle("Show Heels", false, this));
                SidebarToggle.ValueChanged.Subscribe(Observer.Create<bool>(v =>
                {
                    if (Window != null)
                        Window.enabled = v;
                }));
            };

            MakerAPI.MakerExiting += (sender, args) =>
            {
                if (Window != null)
                {
                    Destroy(Window);
                    Window = null;
                }
                SidebarToggle = null;
            };
        }

        private void WireStudioControls()
        {
            Overlay = gameObject.AddComponent<HeelsStudioOverlay>();
            Overlay.Init();

            StudioAPI.GetOrCreateCurrentStateCategory("HeelsSettings")
                .AddControl(new CurrentStateCategorySwitch("Enable", controller =>
                {
                    if (controller is global::Studio.OCIChar oci)
                    {
                        var ctrl = GetController(oci.charInfo);
                        return ctrl != null && ctrl.TriggerEnabled;
                    }
                    return false;
                }))
                .Value.Subscribe(new Action<bool>(v =>
                {
                    var oci = GetSelectedStudioCharacter();
                    var ctrl = oci == null ? null : GetController(oci.charInfo);
                    if (ctrl == null) return;

                    ctrl.TriggerEnabled = v;
                    ctrl.RefreshShoeState();
                }));
        }

        internal static global::Studio.OCIChar GetSelectedStudioCharacter()
        {
            try
            {
                var tree = global::Studio.Studio.Instance?.treeNodeCtrl;
                var selected = tree?.selectNodes;
                if (selected == null || selected.Length == 0) return null;
                var dict = global::Studio.Studio.Instance?.dicInfo;
                if (dict != null && dict.TryGetValue(selected[0], out var info))
                    return info as global::Studio.OCIChar;
            }
            catch { }
            return null;
        }

        // ---- Harmony patches ----

        internal static bool IsShoeSlot(int slot)
        {
            foreach (int s in ShoeSlots)
                if (s == slot) return true;
            return false;
        }

        internal static HeelsController GetController(ChaControl cha)
        {
            if (cha == null) return null;
            return cha.gameObject.GetComponent<HeelsController>();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetClothesState))]
        private static void AfterSetClothesState(ChaControl __instance, int clothesKind)
        {
            if (!IsShoeSlot(clothesKind)) return;
            GetController(__instance)?.OnShoeStateChanged();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCustomClothes))]
        private static void AfterChangeCustomClothes(ChaControl __instance, int kind)
        {
            if (!IsShoeSlot(kind)) return;
            GetController(__instance)?.OnShoeStateChanged();
        }
    }
}
