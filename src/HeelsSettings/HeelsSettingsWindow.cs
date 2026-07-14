using System;
using System.Globalization;
using System.Text;
using KKABMX.Core;
using KKAPI.Maker;
using KKAPI.Utilities;
using UnityEngine;

namespace HeelsSettings
{
    internal class HeelsSettingsWindow : MonoBehaviour
    {
        private const float WinWidth = 400f;
        private const float WinHeight = 320f;

        private const float SliderMin = -90f;
        private const float SliderMax = 90f;
        private const float HeightMin = -1f;
        private const float HeightMax = 1f;
        private const float RotStep = 0.1f;
        private const float PosStep = 0.001f;

#if KKS
        private static readonly string[] CoordNames =
        {
            "School 01", "School 02", "Gym", "Swim", "Club", "Casual",
            "Nightwear", "Bathroom"
        };
#else
        private static readonly string[] CoordNames =
        {
            "School", "Going Out", "Exercise", "Swim", "Club", "Casual",
            "Nightwear"
        };
#endif

        private static readonly EntryDef[] Entries =
        {
            new EntryDef("Whole Foot Rotation", 0, true,  SliderMin, SliderMax, RotStep),
            new EntryDef("Ankle Rotation",      1, true,  SliderMin, SliderMax, RotStep),
            new EntryDef("Toes Rotation",       2, true,  SliderMin, SliderMax, RotStep),
            new EntryDef("Height (Shoes)",      3, false, HeightMin, HeightMax, PosStep),
            new EntryDef("Height (Body)",       -1, false, HeightMin, HeightMax, PosStep),
        };

        private static readonly string[] ControlNames = BuildControlNames();
        private readonly float[] _values = new float[Entries.Length];
        private readonly string[] _textBuf = new string[Entries.Length];
        private readonly bool[] _editing = new bool[Entries.Length];

        private int _controlId;
        private Rect _rect;
        private GUISkin _solidSkin;
        private GUIStyle _activeButtonStyle;
        private GUIStyle _boldLabel;
        private bool _stylesReady;
        private bool _showCoordDropdown;
        private int _displayedCoordinate = -1;

        private HeelsController _ctrl;
        private BoneController _abmx;

        private void Awake()
        {
            DontDestroyOnLoad(this);
            enabled = false;
            _controlId = unchecked(HeelsPlugin.GUID.GetHashCode() + 1);
            _rect = new Rect(HeelsPlugin.SettingWindowX.Value, HeelsPlugin.SettingWindowY.Value,
                             WinWidth, WinHeight);
        }

        private void OnEnable()
        {
            RefreshReferences();
            ReadCurrentValues();
        }

        private void OnDisable()
        {
            if (HeelsPlugin.SettingWindowX != null)
            {
                HeelsPlugin.SettingWindowX.Value = _rect.x;
                HeelsPlugin.SettingWindowY.Value = _rect.y;
            }
        }

        private void RefreshReferences()
        {
            var cha = MakerAPI.GetCharacterControl();
            if (cha == null)
            {
                _ctrl = null;
                _abmx = null;
                _displayedCoordinate = -1;
                return;
            }

            var nextController = cha.gameObject.GetComponent<HeelsController>();
            if (!ReferenceEquals(nextController, _ctrl))
                _displayedCoordinate = -1;
            _ctrl = nextController;
            _abmx = cha.gameObject.GetComponent<BoneController>();
        }

        private void SetupStyles()
        {
            _solidSkin = IMGUIUtils.SolidBackgroundGuiSkin;
            _activeButtonStyle = new GUIStyle(_solidSkin.button);
            _activeButtonStyle.normal.textColor = Color.cyan;
            _activeButtonStyle.hover.textColor = Color.cyan;
            _activeButtonStyle.fontStyle = FontStyle.Bold;
            _boldLabel = new GUIStyle(_solidSkin.label) { fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            if (!enabled || !MakerAPI.InsideMaker) return;

            if (!_stylesReady) { SetupStyles(); _stylesReady = true; }

            var prev = GUI.skin;
            GUI.skin = _solidSkin;
            _rect = GUILayout.Window(_controlId, _rect, DrawWindow, "Heels Settings",
                GUILayout.Width(WinWidth), GUILayout.Height(WinHeight));
            IMGUIUtils.EatInputInRect(_rect);
            GUI.skin = prev;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                enabled = false;
                if (HeelsPlugin.SidebarToggle != null)
                    HeelsPlugin.SidebarToggle.Value = false;
            }
            GUILayout.EndHorizontal();

            if (_ctrl == null) RefreshReferences();
            if (_ctrl == null || _abmx == null)
            {
                GUILayout.Label("No character loaded.");
                GUI.DragWindow();
                return;
            }

            if (_displayedCoordinate != _ctrl.ActiveCoordinate)
                ReadCurrentValues();

            for (int i = 0; i < Entries.Length; i++)
                DrawEntry(i);

            GUILayout.Space(6);
            if (GUILayout.Button("Save Heels Settings", GUILayout.Height(28)))
                SaveAll();

            GUILayout.Space(4);
            DrawCopyBar();

            GUI.DragWindow();
        }

        // ---- Per-entry row ----

        private void DrawEntry(int i)
        {
            ref var def = ref Entries[i];
            float prev = _values[i];

            GUILayout.BeginHorizontal();
            GUILayout.Label(def.Label, _boldLabel, GUILayout.Width(140));

            float sliderVal = GUILayout.HorizontalSlider(_values[i], def.Min, def.Max, GUILayout.Width(120));
            if (!_editing[i] && !Mathf.Approximately(sliderVal, _values[i]))
            {
                _values[i] = sliderVal;
                _textBuf[i] = FormatValue(_values[i], def.IsRotation);
            }

            string controlName = ControlNames[i];
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            bool enterHit = focused && _editing[i] &&
                Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            if (enterHit)
                Event.current.Use();

            GUI.SetNextControlName(controlName);
            string newText = GUILayout.TextField(_textBuf[i], GUILayout.Width(50));
            if (newText != _textBuf[i])
            {
                _textBuf[i] = newText;
                _editing[i] = true;
            }

            if (enterHit)
            {
                if (float.TryParse(_textBuf[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    _values[i] = Mathf.Clamp(parsed, def.Min, def.Max);
                    _textBuf[i] = FormatValue(_values[i], def.IsRotation);
                }
                _editing[i] = false;
                GUI.FocusControl(null);
            }

            bool stillFocused = GUI.GetNameOfFocusedControl() == controlName;
            if (_editing[i] && !stillFocused && !enterHit)
            {
                _textBuf[i] = FormatValue(_values[i], def.IsRotation);
                _editing[i] = false;
            }

            if (GUILayout.Button("-", GUILayout.Width(20)))
            {
                _values[i] = Mathf.Clamp(_values[i] - def.Step, def.Min, def.Max);
                _textBuf[i] = FormatValue(_values[i], def.IsRotation);
            }
            if (GUILayout.Button("+", GUILayout.Width(20)))
            {
                _values[i] = Mathf.Clamp(_values[i] + def.Step, def.Min, def.Max);
                _textBuf[i] = FormatValue(_values[i], def.IsRotation);
            }

            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(_values[i], prev))
                ApplyPreview(i);
        }

        // ---- Copy bar ----

        private void DrawCopyBar()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy to Coord", GUILayout.ExpandWidth(true)))
                _showCoordDropdown = !_showCoordDropdown;

            if (GUILayout.Button("Copy Text", GUILayout.ExpandWidth(true)))
                GUIUtility.systemCopyBuffer = SerializeToText();

            if (GUILayout.Button("Paste Text", GUILayout.ExpandWidth(true)))
            {
                if (DeserializeFromText(GUIUtility.systemCopyBuffer))
                    ApplyAllPreviews();
            }

            GUILayout.EndHorizontal();

            if (_showCoordDropdown)
                DrawCoordDropdown();
        }

        private void DrawCoordDropdown()
        {
            if (_ctrl == null) return;
            int currentCoord = _ctrl.ActiveCoordinate;

            GUILayout.BeginVertical(GUI.skin.box);

            if (GUILayout.Button("All Coords"))
            {
                CopyToAllCoords(currentCoord);
                _showCoordDropdown = false;
            }

            for (int i = 0; i < CoordNames.Length; i++)
            {
                if (i == currentCoord) continue;
                if (GUILayout.Button(CoordNames[i]))
                {
                    CopyToCoord(currentCoord, i);
                    _showCoordDropdown = false;
                }
            }

            GUILayout.EndVertical();
        }

        private void CopyToCoord(int srcCoord, int dstCoord)
        {
            if (_ctrl == null) return;
            SaveAll();
            _ctrl.CopyCoordValues(srcCoord, dstCoord);
        }

        private void CopyToAllCoords(int srcCoord)
        {
            if (_ctrl == null) return;
            SaveAll();
            for (int i = 0; i < CoordNames.Length; i++)
            {
                if (i == srcCoord) continue;
                _ctrl.CopyCoordValues(srcCoord, i);
            }
        }

        // ---- ABMX preview ----

        private void ApplyPreview(int i)
        {
            if (_ctrl == null || _abmx == null) return;
            ref var def = ref Entries[i];
            int coord = _ctrl.ActiveCoordinate;

            if (def.CoordIndex >= 0)
            {
                if (def.IsRotation)
                {
                    HeelsController.SetRotationX(_abmx, HeelsController.PairedBoneL(def.CoordIndex), coord, _values[i]);
                    HeelsController.SetRotationX(_abmx, HeelsController.PairedBoneR(def.CoordIndex), coord, _values[i]);
                }
                else
                {
                    HeelsController.SetPositionY(_abmx, HeelsController.HeightBone, coord, _values[i] + _values[4]);
                }
            }
            else
            {
                HeelsController.SetPositionY(_abmx, HeelsController.HeightBone, coord, _values[3] + _values[4]);
            }

            _abmx.NeedsBaselineUpdate = true;
        }

        private void ApplyAllPreviews()
        {
            for (int i = 0; i < Entries.Length; i++)
                ApplyPreview(i);
        }

        // ---- Read / Save ----

        private void ReadCurrentValues()
        {
            if (_ctrl == null) return;
            int coord = _ctrl.ActiveCoordinate;
            _displayedCoordinate = coord;
            var vals = _ctrl.GetCoordValues(coord);
            for (int i = 0; i < 4; i++)
            {
                _values[i] = vals[i];
                _textBuf[i] = FormatValue(_values[i], Entries[i].IsRotation);
                _editing[i] = false;
            }
            _values[4] = _ctrl.GlobalHeight;
            _textBuf[4] = FormatValue(_values[4], false);
            _editing[4] = false;
        }

        private void SaveAll()
        {
            if (_ctrl == null) return;
            int coord = _ctrl.ActiveCoordinate;
            var vals = _ctrl.GetCoordValues(coord);
            for (int i = 0; i < 4; i++)
                vals[i] = _values[i];
            _ctrl.GlobalHeight = _values[4];

            if (_ctrl.IsWearingShoes())
                _ctrl.ApplyToABMX();
        }

        // ---- Text serialization ----

        private string SerializeToText()
        {
            var sb = new StringBuilder();
            sb.Append("leg03:").Append(F(_values[0]));
            sb.Append("|foot:").Append(F(_values[1]));
            sb.Append("|toes:").Append(F(_values[2]));
            sb.Append("|hShoes:").Append(F(_values[3]));
            sb.Append("|hBody:").Append(F(_values[4]));
            return sb.ToString();
        }

        private bool DeserializeFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                foreach (var part in text.Split('|'))
                {
                    int sep = part.IndexOf(':');
                    if (sep < 0) continue;
                    string key = part.Substring(0, sep).Trim();
                    if (!float.TryParse(part.Substring(sep + 1).Trim(),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                        continue;

                    int idx;
                    switch (key)
                    {
                        case "leg03":  idx = 0; break;
                        case "foot":   idx = 1; break;
                        case "toes":   idx = 2; break;
                        case "hShoes": idx = 3; break;
                        case "hBody":  idx = 4; break;
                        default: continue;
                    }
                    _values[idx] = Mathf.Clamp(val, Entries[idx].Min, Entries[idx].Max);
                    _textBuf[idx] = FormatValue(_values[idx], Entries[idx].IsRotation);
                    _editing[idx] = false;
                }
                return true;
            }
            catch { return false; }
        }

        private static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

        private static string FormatValue(float v, bool isRotation) =>
            isRotation ? v.ToString("F1", CultureInfo.InvariantCulture)
                       : v.ToString("F3", CultureInfo.InvariantCulture);

        private static string[] BuildControlNames()
        {
            var names = new string[Entries.Length];
            for (int i = 0; i < names.Length; i++)
                names[i] = "hsTF" + i;
            return names;
        }

        private struct EntryDef
        {
            public readonly string Label;
            public readonly int CoordIndex;
            public readonly bool IsRotation;
            public readonly float Min, Max, Step;

            public EntryDef(string label, int coordIndex, bool isRotation,
                            float min, float max, float step)
            {
                Label = label;
                CoordIndex = coordIndex;
                IsRotation = isRotation;
                Min = min;
                Max = max;
                Step = step;
            }
        }
    }
}
