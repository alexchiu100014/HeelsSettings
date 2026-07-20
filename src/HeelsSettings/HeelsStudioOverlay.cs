using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KKAPI.Studio;
using KKAPI.Studio.UI;
using KKAPI.Utilities;
using Studio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HeelsSettings
{
    internal class HeelsStudioOverlay : MonoBehaviour
    {
        private const string ToolbarIconResource = "toolbar_icon.png";
        private const string TreeToggleResource = "tree_toggle.png";
        private const string TreeToggleObjectName = "HeelsSettings_Toggle";

        internal static readonly Dictionary<OCIChar, bool> CharacterEnabled = new Dictionary<OCIChar, bool>();

        private readonly Dictionary<TreeNodeObject, GameObject> _toggleObjects = new Dictionary<TreeNodeObject, GameObject>();
        private readonly Dictionary<TreeNodeObject, OCIChar> _nodeCharacters = new Dictionary<TreeNodeObject, OCIChar>();

        private Sprite _spriteOn;
        private Sprite _spriteOff;
        private ToolbarToggle _toolbarToggle;

        internal void Init()
        {
            try
            {
                var asm = typeof(HeelsPlugin).Assembly;

                var toolbarTex = LoadEmbeddedTexture(ToolbarIconResource, asm) ?? CreatePlaceholderTexture();
                _toolbarToggle = CustomToolbarButtons.AddLeftToolbarToggle(toolbarTex, false, OnMasterToggle);
                _toolbarToggle.Value = true;

                StartCoroutine(AttachMasterRightClick());

                var sheet = LoadEmbeddedTexture(TreeToggleResource, asm);
                if (sheet != null)
                {
                    _spriteOff = Sprite.Create(sheet, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
                    _spriteOn = Sprite.Create(sheet, new Rect(64, 0, 64, 64), new Vector2(0.5f, 0.5f));
                }

                StudioAPI.StudioLoadedChanged += OnSceneLoaded;
            }
            catch (Exception ex)
            {
                HeelsPlugin.Logger.LogError($"HeelsStudioOverlay Init failed: {ex}");
            }
        }

        private static Texture2D LoadEmbeddedTexture(string resourceName, System.Reflection.Assembly asm)
        {
            try
            {
                byte[] bytes = ResourceUtils.GetEmbeddedResource(resourceName, asm);
                return bytes == null || bytes.Length == 0 ? null : bytes.LoadTexture();
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D CreatePlaceholderTexture()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[32 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private void OnSceneLoaded(object sender, EventArgs e)
        {
            HeelsPlugin.GlobalEnabled = true;
            CharacterEnabled.Clear();
            _toggleObjects.Clear();
            _nodeCharacters.Clear();

            if (_toolbarToggle != null)
                _toolbarToggle.Value = true;

            var tree = global::Studio.Studio.Instance?.treeNodeCtrl;
            if (tree != null)
            {
                tree.onDelete -= OnNodeDeleted;
                tree.onDelete += OnNodeDeleted;
            }

            StartCoroutine(AttachToExistingNodes());
        }

        internal static void OnTreeNodeCreated(TreeNodeObject node)
        {
            var overlay = HeelsPlugin.Overlay;
            if (overlay != null)
                overlay.StartCoroutine(overlay.AttachToggleNextFrame(node));
        }

        private IEnumerator AttachToggleNextFrame(TreeNodeObject node)
        {
            yield return null;

            if (node == null || node.gameObject == null) yield break;
            if (_nodeCharacters.ContainsKey(node)) yield break;

            var oci = ResolveCharacter(node);
            if (oci != null)
                BuildToggle(oci, node);
        }

        private IEnumerator AttachToExistingNodes()
        {
            yield return null;
            yield return null;

            var dict = global::Studio.Studio.Instance?.dicInfo;
            if (dict == null) yield break;

            foreach (var pair in dict)
            {
                if (!(pair.Value is OCIChar oci)) continue;
                if (_nodeCharacters.ContainsKey(pair.Key)) continue;
                BuildToggle(oci, pair.Key);
            }
        }

        private void BuildToggle(OCIChar oci, TreeNodeObject node)
        {
            try
            {
                var nodeTransform = node.gameObject.transform;
                if (nodeTransform.childCount < 2) return;

                var template = nodeTransform.GetChild(1).gameObject;
                var toggle = Instantiate(template, nodeTransform);
                toggle.name = TreeToggleObjectName;

                var staleButton = toggle.GetComponent<Button>();
                if (staleButton != null) DestroyImmediate(staleButton);

                var button = toggle.AddComponent<Button>();
                button.onClick.AddListener(() => OnCharacterToggleClicked(oci, node));

                float x = template.activeSelf ? 40f : 20f;

                // Leave room for ABMXStateSync toggle if present.
                var abmxToggle = nodeTransform.Find("ABMXSS_Toggle");
                if (abmxToggle != null && abmxToggle.gameObject.activeSelf)
                    x += 20f;

                var betterScaling = nodeTransform.Find("BS_ScaleChildren");
                if (betterScaling != null && betterScaling.gameObject.activeSelf)
                    x += 20f;

                toggle.transform.localPosition = new Vector3(x, 0f, 0f);
                toggle.SetActive(HeelsPlugin.GlobalEnabled);

                var ctrl = HeelsPlugin.GetController(oci.charInfo);
                bool enabled = ctrl == null || ctrl.TriggerEnabled;
                CharacterEnabled[oci] = enabled;
                _toggleObjects[node] = toggle;
                _nodeCharacters[node] = oci;
                SetToggleSprite(toggle, enabled);

                node.RecalcSelectButtonPos();
            }
            catch (Exception ex)
            {
                HeelsPlugin.Logger.LogWarning($"Failed to create heels character toggle: {ex.Message}");
            }
        }

        private void OnMasterToggle(bool active)
        {
            HeelsPlugin.GlobalEnabled = active;

            var studioCharacters = global::Studio.Studio.Instance?.dicInfo?.Values
                .OfType<OCIChar>()
                .Distinct()
                .ToList();

            if (studioCharacters != null)
            {
                foreach (var oci in studioCharacters)
                {
                    var ctrl = HeelsPlugin.GetController(oci.charInfo);
                    if (ctrl == null) continue;

                    if (active)
                        ctrl.RefreshShoeState();
                    else
                        ctrl.ClearFromABMX();
                }
            }

            foreach (var pair in _toggleObjects)
            {
                if (pair.Value == null) continue;
                pair.Value.SetActive(active);

                if (active && _nodeCharacters.TryGetValue(pair.Key, out var oci))
                    SetToggleSprite(pair.Value, CharacterEnabled.TryGetValue(oci, out bool on) && on);

                pair.Key?.RecalcSelectButtonPos();
            }
        }

        private IEnumerator AttachMasterRightClick()
        {
            for (int frames = 0; frames < 600; frames++)
            {
                if (_toolbarToggle != null && _toolbarToggle.ControlObject != null)
                    break;
                yield return null;
            }

            var go = _toolbarToggle?.ControlObject;
            if (go == null) yield break;

            var relay = go.GetComponent<HeelsRightClickRelay>() ?? go.AddComponent<HeelsRightClickRelay>();
            relay.OnRightClick = OnMasterRightClick;
        }

        private void OnMasterRightClick()
        {
            if (_toolbarToggle == null || !_toolbarToggle.Value)
            {
                if (_toolbarToggle != null)
                    _toolbarToggle.Value = true;
                else
                    OnMasterToggle(true);
                return;
            }

            // Setting Value=false triggers OnMasterToggle(false), including the
            // unconditional ABMX clear required by the right-click hard reset.
            _toolbarToggle.Value = false;
        }

        private void OnCharacterToggleClicked(OCIChar oci, TreeNodeObject node)
        {
            bool newState = !(CharacterEnabled.TryGetValue(oci, out bool current) && current);
            SetCharacterState(oci, node, newState);

            var selected = global::Studio.Studio.Instance?.treeNodeCtrl?.selectNodes;
            if (selected == null) return;

            foreach (var other in selected)
            {
                if (other == node) continue;
                if (_nodeCharacters.TryGetValue(other, out var otherOci))
                    SetCharacterState(otherOci, other, newState);
            }
        }

        private void SetCharacterState(OCIChar oci, TreeNodeObject node, bool enabled)
        {
            CharacterEnabled[oci] = enabled;

            if (_toggleObjects.TryGetValue(node, out var toggle))
                SetToggleSprite(toggle, enabled);

            var ctrl = HeelsPlugin.GetController(oci.charInfo);
            if (ctrl == null) return;

            ctrl.TriggerEnabled = enabled;
            ctrl.RefreshShoeState();
        }

        private void SetToggleSprite(GameObject toggle, bool enabled)
        {
            if (_spriteOn == null && _spriteOff == null) return;
            var image = toggle?.GetComponent<Image>();
            if (image != null)
                image.sprite = enabled ? _spriteOn : _spriteOff;
        }

        private static OCIChar ResolveCharacter(TreeNodeObject node)
        {
            var dict = global::Studio.Studio.Instance?.dicInfo;
            if (dict != null && dict.TryGetValue(node, out ObjectCtrlInfo info))
                return info as OCIChar;
            return null;
        }

        private void OnNodeDeleted(TreeNodeObject node)
        {
            if (_nodeCharacters.TryGetValue(node, out var oci))
            {
                CharacterEnabled.Remove(oci);
                _nodeCharacters.Remove(node);
            }
            if (_toggleObjects.TryGetValue(node, out var toggle))
            {
                if (toggle != null) Destroy(toggle);
                _toggleObjects.Remove(node);
            }
        }

        private void OnDestroy()
        {
            StudioAPI.StudioLoadedChanged -= OnSceneLoaded;
            CharacterEnabled.Clear();
            _toggleObjects.Clear();
            _nodeCharacters.Clear();
        }
    }

    internal class HeelsRightClickRelay : MonoBehaviour, IPointerClickHandler
    {
        internal Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                OnRightClick?.Invoke();
        }
    }
}
