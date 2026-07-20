using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExtensibleSaveFormat;
using KKABMX.Core;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
using MessagePack;
using UnityEngine;

namespace HeelsSettings
{
    public class HeelsController : CharaCustomFunctionController
    {
        // ABMXStateSync / BonerStateSync interop constants.
        private const string BssExtDataKey = "BonerStateSync";
        private const string BssRuleListKey = "BonerPropertyList";

        private const int FloatsPerCoord = 4;

        internal static readonly string[] PairedBonesL = { "cf_j_leg03_L", "cf_j_foot_L", "cf_j_toes_L" };
        internal static readonly string[] PairedBonesR = { "cf_j_leg03_R", "cf_j_foot_R", "cf_j_toes_R" };
        internal const string HeightBone = "cf_n_height";

        private static readonly HashSet<string> HeelBoneNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "cf_j_leg03_L", "cf_j_leg03_R", "cf_j_foot_L", "cf_j_foot_R",
            "cf_j_toes_L", "cf_j_toes_R", "cf_n_height"
        };

        internal static string PairedBoneL(int index) => PairedBonesL[index];
        internal static string PairedBoneR(int index) => PairedBonesR[index];

        // Per-coordinate: [0]=leg03rot, [1]=footRot, [2]=toesRot, [3]=heightShoes
        private readonly Dictionary<int, float[]> _coordData = new Dictionary<int, float[]>();
        internal float GlobalHeight;
        internal bool TriggerEnabled = true;

        private bool _shoesWorn;

        internal int ActiveCoordinate => (int)CurrentCoordinate.Value;

        internal float[] GetCoordValues(int coord)
        {
            if (!_coordData.TryGetValue(coord, out var arr))
            {
                arr = new float[FloatsPerCoord];
                _coordData[coord] = arr;
            }
            return arr;
        }

        internal void CopyCoordValues(int srcCoord, int dstCoord)
        {
            var src = GetCoordValues(srcCoord);
            _coordData[dstCoord] = (float[])src.Clone();
        }

        // ---- ExtData: read/write via BonerStateSync format for interop ----

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
            if (maintainState) return;

            _coordData.Clear();
            GlobalHeight = 0f;
            TriggerEnabled = true;

            LoadFromBssExtData(
                MakerAPI.InsideMaker ? (MakerAPI.LastLoadedChaFile ?? ChaFileControl) : ChaFileControl);

            // Own ExtData stores only the toggle.
            var own = GetExtendedData();
            if (own?.data != null && own.data.TryGetValue("Enabled", out var eObj) && eObj is byte[] eBytes && eBytes.Length >= 1)
                TriggerEnabled = eBytes[0] != 0;

            StartCoroutine(DeferredShoeEval());
        }

        private IEnumerator DeferredShoeEval()
        {
            yield return null;
            yield return null;
            RefreshShoeState();
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            SaveToBssExtData();

            var own = new PluginData();
            own.data["Enabled"] = new byte[] { TriggerEnabled ? (byte)1 : (byte)0 };
            SetExtendedData(own);
        }

        protected override void OnCoordinateBeingLoaded(ChaFileCoordinate coordinate, bool maintainState)
        {
            if (maintainState) return;

            int coord = ActiveCoordinate;
            _coordData.Remove(coord);

            var ext = ExtendedSave.GetExtendedDataById(coordinate, BssExtDataKey);
            if (ext?.data != null)
                LoadCoordFromBssRules(ext, coord);

            StartCoroutine(DeferredShoeEval());
        }

        protected override void OnCoordinateBeingSaved(ChaFileCoordinate coordinate)
        {
            int coord = ActiveCoordinate;
            if (!_coordData.ContainsKey(coord)) return;

            SaveCoordToBssExtData(coordinate, coord);
        }

        // ---- BonerStateSync interop: read ----

        private void LoadFromBssExtData(ChaFile source)
        {
            try
            {
                var ext = ExtendedSave.GetExtendedDataById(source, BssExtDataKey);
                if (ext?.data == null) return;
                LoadHeelRulesFromPluginData(ext, -1);
            }
            catch (Exception ex)
            {
                HeelsPlugin.Logger.LogWarning($"Failed to read BonerStateSync data: {ex.Message}");
            }
        }

        private void LoadCoordFromBssRules(PluginData ext, int coord)
        {
            try
            {
                LoadHeelRulesFromPluginData(ext, coord);
            }
            catch (Exception ex)
            {
                HeelsPlugin.Logger.LogWarning($"Failed to read coordinate BonerStateSync data: {ex.Message}");
            }
        }

        private void LoadHeelRulesFromPluginData(PluginData ext, int coordOverride)
        {
            if (!ext.data.TryGetValue(BssRuleListKey, out var obj) || !(obj is byte[] bytes))
                return;

            var rules = MessagePackSerializer.Deserialize<List<BssRule>>(bytes);
            if (rules == null) return;

            foreach (var rule in rules)
            {
                if (!IsManagedHeelRule(rule)) continue;
                if (rule.Modifier == null) continue;

                if (coordOverride >= 0)
                {
                    var patched = new BssRule
                    {
                        Coordinate = coordOverride,
                        Slot = rule.Slot,
                        State = rule.State,
                        Name = rule.Name,
                        Modifier = rule.Modifier,
                        Priority = rule.Priority
                    };
                    ExtractHeelValue(patched);
                }
                else
                {
                    ExtractHeelValue(rule);
                }
            }
        }

        private void ExtractHeelValue(BssRule rule)
        {
            bool isHeight = rule.Name == HeightBone;

            // Global body height: coord=-1, slot=-1
            if (isHeight && rule.Coordinate == -1 && rule.Slot == -1)
            {
                GlobalHeight = rule.Modifier.PositionModifier.y;
                return;
            }

            int coord = rule.Coordinate;
            if (coord < 0) return;

            var vals = GetCoordValues(coord);

            if (isHeight)
            {
                vals[3] = rule.Modifier.PositionModifier.y;
            }
            else if (rule.Name.EndsWith("_L", StringComparison.Ordinal))
            {
                int idx = BoneToIndex(rule.Name);
                if (idx >= 0)
                    vals[idx] = rule.Modifier.RotationModifier.x;
            }
        }

        // ---- BonerStateSync interop: write ----

        private void SaveToBssExtData()
        {
            try
            {
                var ext = ExtendedSave.GetExtendedDataById(ChaFileControl, BssExtDataKey);
                var payload = MergeBssRules(ext, BuildHeelRules());
                ExtendedSave.SetExtendedDataById(ChaFileControl, BssExtDataKey, payload);
            }
            catch (Exception ex)
            {
                HeelsPlugin.Logger.LogWarning($"Failed to write BonerStateSync data: {ex.Message}");
            }
        }

        private void SaveCoordToBssExtData(ChaFileCoordinate coordinate, int coord)
        {
            try
            {
                var ext = ExtendedSave.GetExtendedDataById(coordinate, BssExtDataKey);
                var payload = MergeBssRules(ext, BuildCoordRules(coord, 0));
                ExtendedSave.SetExtendedDataById(coordinate, BssExtDataKey, payload);
            }
            catch (Exception ex)
            {
                HeelsPlugin.Logger.LogWarning($"Failed to write coordinate BonerStateSync data: {ex.Message}");
            }
        }

        private PluginData MergeBssRules(PluginData existing, List<BssRule> newRules)
        {
            var payload = new PluginData { version = existing == null ? 1 : existing.version };
            if (existing?.data != null)
            {
                foreach (var pair in existing.data)
                    payload.data[pair.Key] = pair.Value;
            }

            List<BssRule> previous = null;
            if (payload.data.TryGetValue(BssRuleListKey, out var obj))
            {
                if (!(obj is byte[] bytes))
                    throw new InvalidOperationException("BonerStateSync rule data has an unexpected format; save was cancelled.");

                try
                {
                    previous = MessagePackSerializer.Deserialize<List<BssRule>>(bytes);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("BonerStateSync rule data could not be read; save was cancelled.", ex);
                }
            }

            var rules = previous?.Where(r => !IsManagedHeelRule(r)).ToList()
                        ?? new List<BssRule>();
            rules.AddRange(newRules);

            if (rules.Count == 0)
                payload.data.Remove(BssRuleListKey);
            else
                payload.data[BssRuleListKey] = MessagePackSerializer.Serialize(rules);

            return payload.data.Count == 0 ? null : payload;
        }

        private static bool IsManagedHeelRule(BssRule rule)
        {
            if (rule == null || rule.Name == null || !HeelBoneNames.Contains(rule.Name))
                return false;
            if (rule.State != 0 || rule.Priority != 0)
                return false;

            if (rule.Name == HeightBone && rule.Coordinate == -1 && rule.Slot == -1)
                return true;

            return rule.Coordinate >= 0 && HeelsPlugin.IsShoeSlot(rule.Slot);
        }

        private List<BssRule> BuildHeelRules()
        {
            var rules = new List<BssRule>();

            // Global height body rule.
            if (!Mathf.Approximately(GlobalHeight, 0f))
            {
                var mod = new BoneModifierData();
                mod.PositionModifier = new Vector3(0f, GlobalHeight, 0f);
                rules.Add(new BssRule { Coordinate = -1, Slot = -1, State = 0, Name = HeightBone, Modifier = mod, Priority = 0 });
            }

            // Per-coordinate shoe slot rules.
            foreach (var kvp in _coordData)
                rules.AddRange(BuildCoordRules(kvp.Key, kvp.Key));

            return rules;
        }

        private List<BssRule> BuildCoordRules(int coord, int saveCoord)
        {
            var rules = new List<BssRule>();
            if (!_coordData.TryGetValue(coord, out var vals)) return rules;

            bool allZero = true;
            for (int i = 0; i < FloatsPerCoord; i++)
                if (!Mathf.Approximately(vals[i], 0f)) { allZero = false; break; }
            if (allZero) return rules;

            foreach (int slot in HeelsPlugin.ShoeSlots)
            {
                for (int i = 0; i < PairedBonesL.Length; i++)
                {
                    if (Mathf.Approximately(vals[i], 0f)) continue;
                    var mod = new BoneModifierData();
                    mod.RotationModifier = new Vector3(vals[i], 0f, 0f);
                    rules.Add(new BssRule { Coordinate = saveCoord, Slot = slot, State = 0, Name = PairedBonesL[i], Modifier = mod, Priority = 0 });
                    rules.Add(new BssRule { Coordinate = saveCoord, Slot = slot, State = 0, Name = PairedBonesR[i], Modifier = mod, Priority = 0 });
                }

                if (!Mathf.Approximately(vals[3], 0f))
                {
                    var mod = new BoneModifierData();
                    mod.PositionModifier = new Vector3(0f, vals[3], 0f);
                    rules.Add(new BssRule { Coordinate = saveCoord, Slot = slot, State = 0, Name = HeightBone, Modifier = mod, Priority = 0 });
                }
            }

            return rules;
        }

        // ---- Shoe state ----

        internal void OnShoeStateChanged()
        {
            if (!TriggerEnabled || !HeelsPlugin.GlobalEnabled) return;

            bool wearing = IsWearingShoes();
            if (wearing == _shoesWorn) return;
            _shoesWorn = wearing;

            if (wearing)
                ApplyToABMX();
            else
                ClearFromABMX();
        }

        internal void RefreshShoeState()
        {
            if (!TriggerEnabled || !HeelsPlugin.GlobalEnabled)
            {
                _shoesWorn = false;
                ClearFromABMX();
                return;
            }

            bool wearing = IsWearingShoes();
            _shoesWorn = wearing;
            if (wearing)
                ApplyToABMX();
            else
                ClearFromABMX();
        }

        internal bool IsWearingShoes()
        {
            var cha = ChaControl;
            if (cha == null) return false;

            var states = cha.fileStatus?.clothesState;
            if (states == null) return false;

            foreach (int slot in HeelsPlugin.ShoeSlots)
            {
                if (slot >= states.Length) continue;
                if (cha.GetClothesStateKind(slot) == null) continue;
                if (states[slot] == 0)
                    return true;
            }
            return false;
        }

        // ---- ABMX read/write ----

        internal void ApplyToABMX()
        {
            var abmx = ChaControl?.gameObject.GetComponent<BoneController>();
            if (abmx == null) return;

            int coord = ActiveCoordinate;
            var vals = GetCoordValues(coord);

            for (int i = 0; i < PairedBonesL.Length; i++)
            {
                SetRotationX(abmx, PairedBonesL[i], coord, vals[i]);
                SetRotationX(abmx, PairedBonesR[i], coord, vals[i]);
            }

            SetPositionY(abmx, HeightBone, coord, vals[3] + GlobalHeight);
        }

        internal void ClearFromABMX()
        {
            var abmx = ChaControl?.gameObject.GetComponent<BoneController>();
            if (abmx == null) return;

            int coord = ActiveCoordinate;

            for (int i = 0; i < PairedBonesL.Length; i++)
            {
                SetRotationX(abmx, PairedBonesL[i], coord, 0f);
                SetRotationX(abmx, PairedBonesR[i], coord, 0f);
            }

            SetPositionY(abmx, HeightBone, coord, GlobalHeight);
        }

        // ---- ABMX helpers ----

        internal static void SetRotationX(BoneController abmx, string boneName, int coord, float value)
        {
            var mod = abmx.GetOrAddModifier(boneName, BoneLocation.BodyTop);
            var data = GetModData(mod, coord);
            if (data == null) return;
            data.RotationModifier = new Vector3(value, data.RotationModifier.y, data.RotationModifier.z);
        }

        internal static void SetPositionY(BoneController abmx, string boneName, int coord, float value)
        {
            var mod = abmx.GetOrAddModifier(boneName, BoneLocation.BodyTop);
            var data = GetModData(mod, coord);
            if (data == null) return;
            data.PositionModifier = new Vector3(data.PositionModifier.x, value, data.PositionModifier.z);
        }

        internal static BoneModifierData GetModData(BoneModifier mod, int coord)
        {
            var coords = mod?.CoordinateModifiers;
            if (coords == null || coords.Length == 0) return null;
            if (mod.IsCoordinateSpecific() && coord >= 0 && coord < coords.Length)
                return coords[coord];
            return coords[0];
        }

        private static int BoneToIndex(string boneName)
        {
            if (boneName.StartsWith("cf_j_leg03", StringComparison.Ordinal)) return 0;
            if (boneName.StartsWith("cf_j_foot", StringComparison.Ordinal)) return 1;
            if (boneName.StartsWith("cf_j_toes", StringComparison.Ordinal)) return 2;
            return -1;
        }

        [MessagePackObject(true)]
        public class BssRule
        {
            public int Coordinate { get; set; }
            public int Slot { get; set; }
            public int State { get; set; }
            public string Name { get; set; }
            public BoneModifierData Modifier { get; set; }
            public int Priority { get; set; }
        }
    }
}
