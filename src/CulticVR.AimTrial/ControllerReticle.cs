using System;
using System.Collections.Generic;
using System.Linq;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Uuvr;

namespace CulticVR.AimTrial
{
    public sealed partial class AimTrialPlugin
    {
        private const float ReticleRayLength = 500f;
        private const float ReticleOpenAirDistance = 20f;
        private const float ReticleSurfaceMargin = 0.005f;
        private const string UiDepthTestProperty = "unity_GUIZTestMode";
        private GameObject? _reticleObject;
        private Canvas? _reticleCanvas;
        private RectTransform? _liveGroup;
        private readonly List<OriginalRect> _movedRects = new List<OriginalRect>();
        private RectTransform? _hudCanvasRect;
        private Renderer? _uiQuad;
        private Mesh? _uiQuadMesh;
        private Camera? _uiOverlayCamera;
        private string? _reticleError;
        private bool _unexpectedHudHierarchyLogged;
        private readonly List<ReticleMaterialOverride> _reticleMaterialOverrides =
            new List<ReticleMaterialOverride>();
        private GameObject? _interactionReticleObject;
        private OriginalRect? _interactionOriginal;
        private RectTransform? _interactionGroup;
        private RectTransform? _interactionHudCanvasRect;
        private Renderer? _interactionUiQuad;
        private Mesh? _interactionUiQuadMesh;
        private Camera? _interactionUiOverlayCamera;
        private string? _interactionReticleError;
        private bool _unexpectedInteractionHierarchyLogged;
        private readonly List<ReticleMaterialOverride> _interactionMaterialOverrides =
            new List<ReticleMaterialOverride>();
        private sealed class ReticleMaterialOverride
        {
            public Graphic Graphic = null!;
            public Material? Original;
            public Material DepthMaterial = null!;
        }
        private readonly struct OriginalRect
        {
            public readonly RectTransform Rect;
            public readonly Transform Parent;
            public readonly int SiblingIndex;
            public readonly Vector2 AnchorMin;
            public readonly Vector2 AnchorMax;
            public readonly Vector2 AnchoredPosition;
            public readonly Vector2 SizeDelta;
            public readonly Vector2 Pivot;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 LocalScale;

            public OriginalRect(RectTransform rect)
            {
                Rect = rect;
                Parent = rect.parent;
                SiblingIndex = rect.GetSiblingIndex();
                AnchorMin = rect.anchorMin;
                AnchorMax = rect.anchorMax;
                AnchoredPosition = rect.anchoredPosition;
                SizeDelta = rect.sizeDelta;
                Pivot = rect.pivot;
                LocalRotation = rect.localRotation;
                LocalScale = rect.localScale;
            }

            public void Restore()
            {
                if (Rect == null || Parent == null) return;
                Rect.SetParent(Parent, false);
                Rect.SetSiblingIndex(SiblingIndex);
                Rect.anchorMin = AnchorMin;
                Rect.anchorMax = AnchorMax;
                Rect.anchoredPosition = AnchoredPosition;
                Rect.sizeDelta = SizeDelta;
                Rect.pivot = Pivot;
                Rect.localRotation = LocalRotation;
                Rect.localScale = LocalScale;
                SetReticleLayer(Rect, Parent.gameObject.layer);
            }
        }

        private void LateUpdate()
        {
            UpdatePhysicalDynamiteThrow();
            try
            {
                UpdateReticle();
                _reticleError = null;
            }
            catch (Exception error)
            {
                RestoreReticle();
                if (_reticleError != error.Message)
                    Logger.LogError($"Controller reticle failed and the live HUD elements were restored: {error}");
                _reticleError = error.Message;
            }
            try
            {
                UpdateInteractionReticle();
                _interactionReticleError = null;
            }
            catch (Exception error)
            {
                RestoreInteractionReticle();
                if (_interactionReticleError != error.Message)
                    Logger.LogError($"Controller interaction marker failed and CULTIC's HUD marker was restored: {error}");
                _interactionReticleError = error.Message;
            }
        }

        private void UpdateReticle()
        {
            var player = LocalPlayer();
            if (player == null || !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player) ||
                !(UseControllerAim(player) || UseBazookaAim(player) || UseDisposableAim(player) || UseQuickThrowAim(player) || UsePitchforkThrowReticle(player)) || player.playerHUD == null ||
                player.playerHUD.crosshairGroup == null || player.playerCameraComponent == null ||
                !player.playerHUD.crosshairGroup.gameObject.activeInHierarchy ||
                scrGameControl.Instance == null || scrGameControl.Instance.gameState != 0)
            {
                RestoreReticle();
                return;
            }

            var direction = _rotation * Vector3.forward;
            var rayHit = Physics.Raycast(_origin, direction, out var hit, ReticleRayLength,
                player.canRaycastAgainst);
            var distance = rayHit ? hit.distance : ReticleOpenAirDistance;
            var hud = player.playerHUD;
            if (_liveGroup != hud.crosshairGroup || _reticleObject == null)
            {
                RestoreReticle();
                if (!CreateReticle(hud)) return;
            }

            var reticleObject = _reticleObject;
            var hudCanvasRect = _hudCanvasRect;
            var uiQuad = _uiQuad;
            var uiOverlayCamera = _uiOverlayCamera;
            if (reticleObject == null || hudCanvasRect == null || uiQuad == null ||
                uiOverlayCamera == null || !hudCanvasRect.gameObject.activeInHierarchy ||
                !uiQuad.gameObject.activeInHierarchy || !uiOverlayCamera.gameObject.activeInHierarchy)
            {
                RestoreReticle();
                return;
            }

            if (_reticleCanvas != null) _reticleCanvas.enabled = !PhysicalThrowPreviewActive;
            if (PhysicalThrowPreviewActive) return;

            // Keep the center at the actual unspread ray hit. Orientation is
            // independent: billboard the UI toward the tracked viewer without
            // the old geometry-derived clearance that moved the marker away
            // from glancing-angle shots.
            var target = rayHit && hit.normal.sqrMagnitude > 0.5f ?
                hit.point + hit.normal * ReticleSurfaceMargin :
                _origin + direction * distance;
            var cameraTransform = player.playerCameraComponent.transform;
            var trackingYaw = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f) *
                WorldStereoTrialPlugin.TrackingYawCompensation;
            var eye = cameraTransform.position +
                trackingYaw * WorldStereoTrialPlugin.TrackingPositionCompensation;
            var eyeDistance = Vector3.Distance(eye, target);
            // UiTrial positions the quad from UUVR's tracked overlay camera.
            var uiDistance = Vector3.Distance(uiOverlayCamera.transform.position,
                uiQuad.transform.position);
            var quadMesh = _uiQuadMesh;
            var quadWidth = Mathf.Abs(uiQuad.transform.lossyScale.x) *
                (quadMesh != null ? quadMesh.bounds.size.x : 1f);
            var hudWidth = hudCanvasRect.rect.width;
            if (eyeDistance < 0.01f || uiDistance < 0.1f || uiDistance > 3f ||
                quadWidth < 0.01f || hudWidth < 1f)
            {
                RestoreReticle();
                return;
            }

            var scale = quadWidth / hudWidth * eyeDistance / uiDistance;
            var rotation = ReticleBillboardRotation(target, eye, uiOverlayCamera.transform);
            reticleObject.transform.SetPositionAndRotation(target, rotation);
            reticleObject.transform.localScale = Vector3.one * scale;
        }

        private void UpdateInteractionReticle()
        {
            var player = LocalPlayer();
            var game = scrGameControl.Instance;
            if (player == null || game == null || game.gameState != 0 ||
                !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player) || !UseInteractionAim(player) ||
                player.playerHUD == null || player.playerCameraComponent == null)
            {
                RestoreInteractionReticle();
                return;
            }

            var hud = player.playerHUD;
            if (_interactionReticleObject == null || _interactionGroup == null)
            {
                RestoreInteractionReticle();
                if (!CreateInteractionReticle(hud)) return;
            }

            var root = _interactionReticleObject;
            var group = _interactionGroup;
            var hudCanvas = _interactionHudCanvasRect;
            var quad = _interactionUiQuad;
            var overlayCamera = _interactionUiOverlayCamera;
            if (root == null || group == null || hudCanvas == null || quad == null || overlayCamera == null ||
                !hudCanvas.gameObject.activeInHierarchy || !quad.gameObject.activeInHierarchy ||
                !overlayCamera.gameObject.activeInHierarchy)
            {
                RestoreInteractionReticle();
                return;
            }

            // PlayerControl writes a viewport position to this root during Update.
            // The root now lives on a world canvas, so its only valid local position
            // is the center of that canvas.
            group.anchorMin = new Vector2(0.5f, 0.5f);
            group.anchorMax = new Vector2(0.5f, 0.5f);
            group.anchoredPosition = Vector2.zero;
            if (hud.interactAlpha <= 0.002f && hud.interactAmount <= 0f) return;

            var hit = _interactLastHitRef != null ? _interactLastHitRef(player) : default;
            var direction = _rotation * Vector3.forward;
            var hasHit = hit.collider != null && hit.distance > 0f && hit.distance <= 2.1f;
            var target = hasHit && hit.normal.sqrMagnitude > 0.5f ?
                hit.point + hit.normal * ReticleSurfaceMargin : _origin + direction * 2f;
            var cameraTransform = player.playerCameraComponent.transform;
            var trackingYaw = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f) *
                WorldStereoTrialPlugin.TrackingYawCompensation;
            var eye = cameraTransform.position +
                trackingYaw * WorldStereoTrialPlugin.TrackingPositionCompensation;
            var eyeDistance = Vector3.Distance(eye, target);
            var uiDistance = Vector3.Distance(overlayCamera.transform.position, quad.transform.position);
            var quadMesh = _interactionUiQuadMesh;
            var quadWidth = Mathf.Abs(quad.transform.lossyScale.x) *
                (quadMesh != null ? quadMesh.bounds.size.x : 1f);
            var hudWidth = hudCanvas.rect.width;
            if (eyeDistance < 0.01f || uiDistance < 0.1f || uiDistance > 3f ||
                quadWidth < 0.01f || hudWidth < 1f)
            {
                RestoreInteractionReticle();
                return;
            }

            var scale = quadWidth / hudWidth * eyeDistance / uiDistance;
            var rotation = ReticleBillboardRotation(target, eye, overlayCamera.transform);
            root.transform.SetPositionAndRotation(target, rotation);
            root.transform.localScale = Vector3.one * scale;
        }

        private static Quaternion ReticleBillboardRotation(Vector3 target, Vector3 eye,
            Transform trackedViewer)
        {
            var forward = target - eye;
            var up = Vector3.ProjectOnPlane(trackedViewer.up, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(trackedViewer.right, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward);
            return Quaternion.LookRotation(forward, up);
        }

        private bool CreateInteractionReticle(scrPlayerHUD hud)
        {
            var press = hud.interactionGroupPress;
            var hold = hud.interactionGroupHold;
            var selector = hud.interactionSelector;
            var group = selector != null && selector.Length == 5 ? selector[4] : null;
            var canvas = group != null ? group.GetComponentInParent<Canvas>() : null;
            var hudCanvas = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            var quad = Resources.FindObjectsOfTypeAll<Renderer>()
                .FirstOrDefault(renderer => renderer != null && renderer.gameObject.scene.IsValid() &&
                    renderer.name == "VrUiQuad" && renderer.gameObject.activeInHierarchy);
            var overlayCamera = Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(camera => camera != null && camera.gameObject.activeInHierarchy &&
                    camera.GetComponent<UuvrPoseDriver>() != null &&
                    camera.GetComponentInParent<Uuvr.VrUi.UiOverlayRenderMode>() != null);
            if (group == null || press == null || hold == null || hudCanvas == null || quad == null ||
                overlayCamera == null || group.parent != hudCanvas || press.transform.parent != group ||
                hold.transform.parent != group)
            {
                if (!_unexpectedInteractionHierarchyLogged)
                    Logger.LogWarning("Controller interaction marker kept on HUD: CULTIC's interaction hierarchy differs from the inspected prefab.");
                _unexpectedInteractionHierarchyLogged = true;
                return false;
            }
            _unexpectedInteractionHierarchyLogged = false;

            var root = new GameObject("CulticVR Live Controller Interaction Marker", typeof(RectTransform), typeof(Canvas));
            _interactionReticleObject = root;
            root.layer = 0;
            ((RectTransform)root.transform).sizeDelta = new Vector2(32f, 32f);
            var worldCanvas = root.GetComponent<Canvas>();
            _reticleCanvas = worldCanvas;
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.overrideSorting = true;
            worldCanvas.sortingOrder = 101;
            _interactionOriginal = new OriginalRect(group);
            group.SetParent(root.transform, false);
            group.anchorMin = new Vector2(0.5f, 0.5f);
            group.anchorMax = new Vector2(0.5f, 0.5f);
            group.anchoredPosition = Vector2.zero;
            SetReticleLayer(group, 0);
            ApplyReticleDepthOverride(root.transform, _interactionMaterialOverrides);
            _interactionGroup = group;
            _interactionHudCanvasRect = hudCanvas;
            _interactionUiQuad = quad;
            _interactionUiQuadMesh = quad.GetComponent<MeshFilter>()?.sharedMesh;
            _interactionUiOverlayCamera = overlayCamera;
            return true;
        }

        private void RestoreInteractionReticle()
        {
            if (_interactionOriginal.HasValue) _interactionOriginal.Value.Restore();
            _interactionOriginal = null;
            RestoreReticleDepthOverrides(_interactionMaterialOverrides);
            if (_interactionReticleObject != null) Destroy(_interactionReticleObject);
            _interactionReticleObject = null;
            _interactionGroup = null;
            _interactionHudCanvasRect = null;
            _interactionUiQuad = null;
            _interactionUiQuadMesh = null;
            _interactionUiOverlayCamera = null;
        }

        private bool CreateReticle(scrPlayerHUD hud)
        {
            var group = hud.crosshairGroup;
            var canvas = group.GetComponentInParent<Canvas>();
            var hudCanvas = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            var quad = Resources.FindObjectsOfTypeAll<Renderer>()
                .FirstOrDefault(renderer => renderer != null && renderer.gameObject.scene.IsValid() &&
                    renderer.name == "VrUiQuad" && renderer.gameObject.activeInHierarchy);
            var overlayCamera = Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(camera => camera != null && camera.gameObject.activeInHierarchy &&
                    camera.GetComponent<UuvrPoseDriver>() != null &&
                    camera.GetComponentInParent<Uuvr.VrUi.UiOverlayRenderMode>() != null);
            var radial = hud.throwRadial;
            var flash = hud.throwFlashRadial;
            if (hudCanvas == null || quad == null || overlayCamera == null ||
                radial == null || flash == null || hud.crossHairs == null ||
                hud.crossHairs.Length != 4 ||
                hud.crossHairs.Any(image => image == null || image.transform.parent != group) ||
                group.parent != hudCanvas || radial.transform.parent != hudCanvas ||
                !IsCentered(group) || !IsCentered(radial.rectTransform) ||
                (flash.transform != radial.transform && flash.transform.parent != hudCanvas &&
                    !flash.transform.IsChildOf(group) &&
                    !flash.transform.IsChildOf(radial.transform)) ||
                (flash.transform.parent == hudCanvas && !IsCentered(flash.rectTransform)))
            {
                if (!_unexpectedHudHierarchyLogged)
                    Logger.LogWarning("Controller reticle kept on HUD: CULTIC's center HUD hierarchy differs from the inspected prefab.");
                _unexpectedHudHierarchyLogged = true;
                return false;
            }
            _unexpectedHudHierarchyLogged = false;

            var root = new GameObject("CulticVR Live Controller Reticle", typeof(RectTransform), typeof(Canvas));
            _reticleObject = root;
            root.layer = 0;
            ((RectTransform)root.transform).sizeDelta = new Vector2(32f, 32f);
            var worldCanvas = root.GetComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.overrideSorting = true;
            worldCanvas.sortingOrder = 100;
            MoveToReticle(group, root.transform);
            if (radial.transform != group && !radial.transform.IsChildOf(group))
                MoveToReticle(radial.rectTransform, root.transform);
            if (flash.transform != group && !flash.transform.IsChildOf(group) &&
                flash.transform != radial.transform && !flash.transform.IsChildOf(radial.transform))
                MoveToReticle(flash.rectTransform, root.transform);
            ApplyReticleDepthOverride(root.transform, _reticleMaterialOverrides);
            _liveGroup = group;
            _hudCanvasRect = hudCanvas;
            _uiQuad = quad;
            _uiQuadMesh = quad.GetComponent<MeshFilter>()?.sharedMesh;
            _uiOverlayCamera = overlayCamera;
            return true;
        }

        private static bool IsCentered(RectTransform rect) =>
            (rect.anchorMin - new Vector2(0.5f, 0.5f)).sqrMagnitude < 0.001f &&
            (rect.anchorMax - new Vector2(0.5f, 0.5f)).sqrMagnitude < 0.001f &&
            rect.anchoredPosition.sqrMagnitude < 4f;

        private void MoveToReticle(RectTransform rect, Transform parent)
        {
            _movedRects.Add(new OriginalRect(rect));
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            SetReticleLayer(rect, 0);
        }

        private static void SetReticleLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (var index = 0; index < root.childCount; index++)
                SetReticleLayer(root.GetChild(index), layer);
        }

        private void RestoreReticle()
        {
            for (var index = _movedRects.Count - 1; index >= 0; index--)
                _movedRects[index].Restore();
            _movedRects.Clear();
            RestoreReticleDepthOverrides(_reticleMaterialOverrides);
            if (_reticleObject != null) Destroy(_reticleObject);
            _reticleObject = null;
            _reticleCanvas = null;
            _liveGroup = null;
            _hudCanvasRect = null;
            _uiQuad = null;
            _uiQuadMesh = null;
            _uiOverlayCamera = null;
        }

        private static void ApplyReticleDepthOverride(Transform root,
            List<ReticleMaterialOverride> overrides)
        {
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                var source = graphic.materialForRendering;
                if (source == null) continue;
                var depthMaterial = new Material(source)
                {
                    name = source.name + " (CulticVR Reticle No Surface Clip)"
                };
                // Unity's built-in UI shader exposes its ZTest through this
                // material value. The controller ray has already selected the
                // nearest valid collision, so keeping these few pixels visible
                // avoids surface intersection without moving the hit marker.
                depthMaterial.SetInt(UiDepthTestProperty, (int)CompareFunction.Always);
                overrides.Add(new ReticleMaterialOverride
                {
                    Graphic = graphic,
                    Original = graphic.material,
                    DepthMaterial = depthMaterial
                });
                graphic.material = depthMaterial;
            }
        }

        private static void RestoreReticleDepthOverrides(List<ReticleMaterialOverride> overrides)
        {
            foreach (var entry in overrides)
            {
                if (entry.Graphic != null) entry.Graphic.material = entry.Original;
                if (entry.DepthMaterial != null) Destroy(entry.DepthMaterial);
            }
            overrides.Clear();
        }
    }
}
