using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace CulticVR.AimPreview
{
    public sealed partial class AimPreviewPlugin
    {
        // The menu art reaches about +/-155 authored pixels. The status art
        // extends 52 pixels above its root, so this places it just below.
        private static readonly Vector2 WristStatusMenuPosition = new Vector2(0f, -208f);

        private sealed class RectTransformState
        {
            internal Transform? Parent;
            internal int SiblingIndex;
            internal Vector2 AnchorMin;
            internal Vector2 AnchorMax;
            internal Vector2 Pivot;
            internal Vector2 SizeDelta;
            internal Vector3 AnchoredPosition;
            internal Quaternion LocalRotation;
            internal Vector3 LocalScale;
            internal bool ActiveSelf;
        }

        private sealed class StatusAttachment
        {
            internal RectTransform Rect = null!;
            internal RectTransformState State = null!;
            internal GameObject[] LayerObjects = Array.Empty<GameObject>();
            internal int[] OriginalLayers = Array.Empty<int>();
        }

        private ConfigEntry<bool> _wristStatusHudEnabled = null!;
        private GameObject? _wristStatusRoot;
        private scrPlayerHUD? _wristStatusHud;
        private readonly List<StatusAttachment> _wristStatusAttachments =
            new List<StatusAttachment>();
        private CanvasRenderer[]? _inventoryRenderers;
        private bool[]? _inventoryOriginalCull;

        private void BindWristStatusHud()
        {
            _wristStatusHudEnabled = Config.Bind("Wrist Status HUD", "Enabled", true,
                "Attach CULTIC's live left status strip below the left-wrist quick menu and hide the redundant head-locked item pad. Intended for a future in-game VR menu toggle.");
            _wristStatusHudEnabled.SettingChanged += OnWristStatusHudSettingChanged;
        }

        private void UnbindWristStatusHud()
        {
            if (_wristStatusHudEnabled != null)
                _wristStatusHudEnabled.SettingChanged -= OnWristStatusHudSettingChanged;
        }

        private void OnWristStatusHudSettingChanged(object sender, EventArgs args)
        {
            if (!_wristStatusHudEnabled.Value) DestroyWristStatusHud();
        }

        private void UpdateWristStatusHud(scrPlayerControl player, Vector3 eyePosition,
            Quaternion bodyYaw, Vector3 headTrackingPosition, bool leftTracked,
            Vector3 leftTrackingPosition, Quaternion leftTrackingRotation)
        {
            var hud = player.playerHUD;
            if (!_wristStatusHudEnabled.Value || !leftTracked || hud == null ||
                _wristMenuRoot == null || _wristMenuHud != hud)
            {
                DestroyWristStatusHud();
                return;
            }

            if (_wristStatusRoot == null || _wristStatusHud != hud)
            {
                DestroyWristStatusHud();
                if (!CreateWristStatusHud(hud)) return;
            }

            var game = scrGameControl.Instance;
            var active = game != null && game.gameState == 0 && hud.gameObject.activeInHierarchy &&
                hud.wepWheelState == 0 && hud.invWheelState == 0 && _wristMenuRoot.activeSelf;
            SetWristStatusActive(active);
        }

        private bool CreateWristStatusHud(scrPlayerHUD hud)
        {
            if (_wristMenuRect == null || hud.healthGroup == null || hud.ammoIcon == null ||
                hud.padLeftIcon == null || hud.padRightIcon == null || hud.padUpIcon == null)
                return false;

            var statusSource = FindLowestCommonRectTransform(
                hud.healthGroup.transform, hud.ammoIcon.transform);
            var inventoryRoot = FindLowestCommonRectTransform(hud.padLeftIcon.transform,
                hud.padRightIcon.transform, hud.padUpIcon.transform);
            if (statusSource == null || inventoryRoot == null || statusSource == inventoryRoot)
                return false;
            if (!string.Equals(statusSource.name, "playerHealthGroup", StringComparison.Ordinal) ||
                !string.Equals(inventoryRoot.name, "inventoryPad", StringComparison.Ordinal))
            {
                Logger.LogWarning("Wrist status HUD refused an unexpected CULTIC HUD hierarchy; the original HUD remains unchanged.");
                return false;
            }

            var originalHudParent = statusSource.parent;
            if (originalHudParent == null) return false;

            var root = new GameObject("CulticVR Wrist Menu Status Strip", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(_wristMenuRect, false);
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = statusSource.rect.size;
            rootRect.anchoredPosition = WristStatusMenuPosition;
            rootRect.localRotation = Quaternion.identity;
            rootRect.localScale = Vector3.one;
            rootRect.SetAsLastSibling();

            _wristStatusRoot = root;
            _wristStatusHud = hud;
            AttachStatusRoot(statusSource, rootRect);

            // CULTIC exposes these as independent live references. The shipped
            // layout normally puts them under playerHealthGroup, but include a
            // different direct HUD root so no alternate armor/ability arc can
            // remain attached to the HMD.
            AttachScalingStatusRoot(hud.armorGroup, statusSource, rootRect);
            AttachSupplementalStatusRoot(hud.abilityBarBack != null ?
                hud.abilityBarBack.rectTransform : null, statusSource, originalHudParent, rootRect);
            AttachSupplementalStatusRoot(hud.abilityIcon != null ?
                hud.abilityIcon.rectTransform : null, statusSource, originalHudParent, rootRect);
            AttachSupplementalStatusRoot(hud.cashText != null ?
                hud.cashText.rectTransform : null, statusSource, originalHudParent, rootRect);

            _inventoryRenderers = inventoryRoot.GetComponentsInChildren<CanvasRenderer>(true);
            _inventoryOriginalCull = new bool[_inventoryRenderers.Length];
            for (var index = 0; index < _inventoryRenderers.Length; index++)
            {
                _inventoryOriginalCull[index] = _inventoryRenderers[index].cull;
                _inventoryRenderers[index].cull = true;
            }

            root.SetActive(false);
            return true;
        }

        private void AttachScalingStatusRoot(RectTransform? source,
            RectTransform primarySource, RectTransform holder)
        {
            if (source == null || source == primarySource || source.IsChildOf(primarySource)) return;
            if (!string.Equals(source.name, "playerScalingGroup", StringComparison.Ordinal))
            {
                Logger.LogWarning("Wrist status HUD refused an unexpected CULTIC scaling-group hierarchy; that group remains unchanged.");
                return;
            }
            AttachStatusRoot(source, holder);
        }

        private void AttachSupplementalStatusRoot(Transform? source,
            RectTransform primarySource, Transform originalHudParent, RectTransform holder)
        {
            if (source == null || source == primarySource || source.IsChildOf(primarySource)) return;
            var candidate = source;
            while (candidate.parent != null && candidate.parent != originalHudParent)
                candidate = candidate.parent;
            if (candidate.parent != originalHudParent || !(candidate is RectTransform rect)) return;

            for (var index = 0; index < _wristStatusAttachments.Count; index++)
            {
                var existing = _wristStatusAttachments[index].Rect;
                if (rect == existing || rect.IsChildOf(existing)) return;
            }
            AttachStatusRoot(rect, holder);
        }

        private void AttachStatusRoot(RectTransform source, RectTransform holder)
        {
            var attachment = new StatusAttachment
            {
                Rect = source,
                State = CaptureRectTransform(source)
            };
            var sourceTransforms = source.GetComponentsInChildren<Transform>(true);
            attachment.LayerObjects = new GameObject[sourceTransforms.Length];
            attachment.OriginalLayers = new int[sourceTransforms.Length];
            for (var index = 0; index < sourceTransforms.Length; index++)
            {
                var sourceObject = sourceTransforms[index].gameObject;
                attachment.LayerObjects[index] = sourceObject;
                attachment.OriginalLayers[index] = sourceObject.layer;
            }

            source.SetParent(holder, false);
            ApplyRectTransform(source, attachment.State);
            for (var index = 0; index < attachment.LayerObjects.Length; index++)
                attachment.LayerObjects[index].layer = holder.gameObject.layer;
            _wristStatusAttachments.Add(attachment);
        }

        private static RectTransform? FindLowestCommonRectTransform(params Transform[] transforms)
        {
            if (transforms.Length == 0 || transforms[0] == null) return null;
            for (var candidate = transforms[0]; candidate != null; candidate = candidate.parent)
            {
                var containsAll = true;
                for (var index = 1; index < transforms.Length; index++)
                {
                    if (transforms[index] == null || !transforms[index].IsChildOf(candidate))
                    {
                        containsAll = false;
                        break;
                    }
                }
                if (containsAll && candidate is RectTransform rect) return rect;
            }
            return null;
        }

        private static RectTransformState CaptureRectTransform(RectTransform rect) =>
            new RectTransformState
            {
                Parent = rect.parent,
                SiblingIndex = rect.GetSiblingIndex(),
                AnchorMin = rect.anchorMin,
                AnchorMax = rect.anchorMax,
                Pivot = rect.pivot,
                SizeDelta = rect.sizeDelta,
                AnchoredPosition = rect.anchoredPosition3D,
                LocalRotation = rect.localRotation,
                LocalScale = rect.localScale,
                ActiveSelf = rect.gameObject.activeSelf
            };

        private static void ApplyRectTransform(RectTransform rect, RectTransformState state)
        {
            rect.anchorMin = state.AnchorMin;
            rect.anchorMax = state.AnchorMax;
            rect.pivot = state.Pivot;
            rect.sizeDelta = state.SizeDelta;
            rect.anchoredPosition3D = state.AnchoredPosition;
            rect.localRotation = state.LocalRotation;
            rect.localScale = state.LocalScale;
            rect.gameObject.SetActive(state.ActiveSelf);
        }

        private void SetWristStatusActive(bool active)
        {
            if (_wristStatusRoot != null && _wristStatusRoot.activeSelf != active)
                _wristStatusRoot.SetActive(active);
        }

        private void DestroyWristStatusHud()
        {
            SetWristStatusActive(false);

            if (_inventoryRenderers != null && _inventoryOriginalCull != null)
            {
                var count = Math.Min(_inventoryRenderers.Length, _inventoryOriginalCull.Length);
                for (var index = 0; index < count; index++)
                    if (_inventoryRenderers[index] != null)
                        _inventoryRenderers[index].cull = _inventoryOriginalCull[index];
            }

            for (var index = 0; index < _wristStatusAttachments.Count; index++)
            {
                var attachment = _wristStatusAttachments[index];
                if (attachment.Rect == null || attachment.State.Parent == null) continue;
                attachment.Rect.SetParent(attachment.State.Parent, false);
                ApplyRectTransform(attachment.Rect, attachment.State);
                var layerCount = Math.Min(attachment.LayerObjects.Length,
                    attachment.OriginalLayers.Length);
                for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
                    if (attachment.LayerObjects[layerIndex] != null)
                        attachment.LayerObjects[layerIndex].layer =
                            attachment.OriginalLayers[layerIndex];
            }
            for (var index = 0; index < _wristStatusAttachments.Count; index++)
            {
                var attachment = _wristStatusAttachments[index];
                if (attachment.Rect == null || attachment.State.Parent == null) continue;
                attachment.Rect.SetSiblingIndex(Mathf.Min(attachment.State.SiblingIndex,
                    attachment.State.Parent.childCount - 1));
            }

            _wristStatusAttachments.Clear();
            if (_wristStatusRoot != null) Destroy(_wristStatusRoot);
            _wristStatusRoot = null;
            _wristStatusHud = null;
            _inventoryRenderers = null;
            _inventoryOriginalCull = null;
        }
    }
}
