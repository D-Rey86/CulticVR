using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using Uuvr;

namespace CulticVR.UiTrial
{
    [BepInPlugin("culticvr.uitrial", "CULTIC VR Selective UI", "0.5.8")]
    [BepInDependency("raicuparta.uuvr-modern")]
    [BepInDependency("culticvr.worldstereotrial")]
    public sealed partial class UiTrialPlugin : BaseUnityPlugin
    {
        private const float TrialScale = 0.55f;
        // CULTIC's native UI coordinate system is 512x288 (16:9). Capture it at
        // an integer 5x scale so VR UI quality and framing do not inherit the
        // desktop window size, monitor aspect ratio, or in-game render scale.
        private const int UiTextureWidth = 2560;
        private const int UiTextureHeight = 1440;
        private readonly Dictionary<Canvas, CanvasState> _canvases = new Dictionary<Canvas, CanvasState>();
        private readonly Dictionary<GameObject, int> _layers = new Dictionary<GameObject, int>();
        private readonly HashSet<UiLayerObserver> _layerObservers = new HashSet<UiLayerObserver>();
        private readonly HashSet<Transform> _dirtyLayerRoots = new HashSet<Transform>();
        private bool _requested = true;
        private bool _settingsApplied;
        private bool _quitting;
        private bool _loadingPresentation;
        private bool _nativeMenuPresentation;
        private NativeMenuCameraState[]? _nativeMenuCameras;
        private Camera? _source;
        private Camera? _capture;
        private RenderTexture? _texture;
        private Renderer? _quad;
        private Camera? _overlayCamera;
        private Camera? _positionedOverlayCamera;
        private Vector3 _overlayCameraBaseLocalPosition;
        private Texture? _oldQuadTexture;
        private Vector3 _oldQuadLocalPosition;
        private Quaternion _oldQuadLocalRotation;
        private ModConfiguration.UiPatchMode _oldPatchMode;
        private ModConfiguration.UiRenderMode _oldRenderMode;
        private float _oldScale;

        private readonly struct CanvasState
        {
            public readonly RenderMode Mode;
            public readonly Camera? Camera;
            public readonly float Distance;
            public CanvasState(Canvas canvas)
            {
                Mode = canvas.renderMode;
                Camera = canvas.worldCamera;
                Distance = canvas.planeDistance;
            }
        }

        private void Awake()
        {
            InitializeVrMenu();
            SceneManager.activeSceneChanged += OnSceneChanged;
            Application.onBeforeRender += UpdateHeadLockedQuad;
            Canvas.preWillRenderCanvases += RefreshDirtyLayers;
        }

        private void OnApplicationQuit() => _quitting = true;

        private void OnDestroy()
        {
            RemoveVrMenu();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Application.onBeforeRender -= UpdateHeadLockedQuad;
            Canvas.preWillRenderCanvases -= RefreshDirtyLayers;
            Restore(!_quitting);
        }

        private void OnSceneChanged(Scene previous, Scene current) => Restore();

        private void Update()
        {
            if (!_requested || !HasTrackedHeadset())
            {
                Restore();
                return;
            }
            var source = IsSourceCameraValid(_source) ? _source : Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(IsSourceCameraValid);
            if (source == null)
            {
                if (SceneManager.GetActiveScene().name == "sceneLoading")
                    EnsureLoadingCapture();
                else
                    Restore();
                return;
            }
            if (_loadingPresentation || _source != source || _capture == null)
            {
                Restore();
                try
                {
                    if (!Create(source)) return;
                }
                catch (Exception exception)
                {
                    Logger.LogError($"Selective UI setup failed; restoring original presentation: {exception}");
                    Restore();
                    _requested = false;
                    return;
                }
            }
            if (_capture != null)
            {
                // Native title cameras already own their canvas layout, motion,
                // clipping, authored FOV and pass ordering. Never reassign their
                // parent Canvas or read XR FOV back into a capture camera.
                if (_nativeMenuPresentation) return;
                _capture.fieldOfView = source.fieldOfView;
                RefreshDirtyLayers();
                foreach (var canvas in _canvases.Keys)
                {
                    if (canvas == null) continue;
                    if (canvas.worldCamera != _capture) canvas.worldCamera = _capture;
                }
            }
        }

        private void LateUpdate() => UpdateHeadLockedQuad();

        private void EnsureLoadingCapture()
        {
            if (_loadingPresentation) return;
            Restore();
            var settings = ModConfiguration.Instance;
            var quad = FindQuad();
            if (settings == null || quad == null) return;
            var scene = SceneManager.GetActiveScene();
            var candidates = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(c => c != null && c.gameObject.scene == scene && c.gameObject.activeInHierarchy &&
                    c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .ToArray();
            if (candidates.Length == 0) return;

            SaveUiState(settings, quad);
            _settingsApplied = true;
            try
            {
                var layer = LayerHelper.GetVrUiLayer();
                var texture = new RenderTexture(UiTextureWidth, UiTextureHeight, 24,
                    RenderTextureFormat.ARGB32)
                {
                    name = "CulticVR Loading UI",
                    filterMode = FilterMode.Bilinear
                };
                texture.Create();
                var objectWithCamera = new GameObject("CulticVR Loading UI Capture Camera");
                var capture = objectWithCamera.AddComponent<Camera>();
                capture.targetTexture = texture;
                capture.stereoTargetEye = StereoTargetEyeMask.None;
                capture.cullingMask = 1 << layer;
                capture.clearFlags = CameraClearFlags.SolidColor;
                capture.backgroundColor = Color.clear;
                capture.depth = 100f;
                capture.nearClipPlane = 0.01f;
                capture.farClipPlane = 100f;
                capture.fieldOfView = 60f;
                capture.aspect = (float)UiTextureWidth / UiTextureHeight;
                capture.allowHDR = false;
                capture.allowMSAA = false;
                _capture = capture;
                _texture = texture;

                foreach (var canvas in candidates)
                {
                    _canvases.Add(canvas, new CanvasState(canvas));
                    SetLayer(canvas.transform, layer);
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = capture;
                    canvas.planeDistance = 10f;
                }

                _loadingPresentation = true;
                SetSettings(settings, ModConfiguration.UiPatchMode.None,
                    ModConfiguration.UiRenderMode.OverlayCamera, TrialScale);
                quad.material.mainTexture = texture;
            }
            catch (Exception exception)
            {
                Logger.LogError($"Loading UI capture failed: {exception}");
                Restore();
                _requested = false;
            }
        }

        private void UpdateHeadLockedQuad()
        {
            if (!_settingsApplied || _quad == null || ModConfiguration.Instance == null) return;
            if (_overlayCamera == null || !_overlayCamera.gameObject.activeInHierarchy)
                _overlayCamera = Resources.FindObjectsOfTypeAll<Camera>()
                    .FirstOrDefault(c => c != null && c.gameObject.activeInHierarchy &&
                        c.GetComponent<UuvrPoseDriver>() != null &&
                        c.GetComponentInParent<Uuvr.VrUi.UiOverlayRenderMode>() != null);
            if (_overlayCamera == null) return;
            ApplyMenuBackground(_overlayCamera);
            if (_positionedOverlayCamera != _overlayCamera)
            {
                _positionedOverlayCamera = _overlayCamera;
                _overlayCameraBaseLocalPosition = _overlayCamera.transform.localPosition;
            }
            // Use UUVR's own tracked overlay-camera pose. It uses XRNode.CenterEye,
            // but UUVR 0.4.0's pose driver writes only rotation. Apply the same
            // cached tracking-space displacement as the stereo world before
            // placing the quad so the existing camera and HUD remain head-locked.
            var pose = _overlayCamera.transform;
            pose.localPosition = _overlayCameraBaseLocalPosition +
                WorldStereoTrialPlugin.TrackingPositionCompensation;
            var quad = _quad.transform;
            quad.rotation = pose.rotation;
            quad.position = pose.TransformPoint(ModConfiguration.Instance.VrUiPosition.Value);
        }

        private static Renderer? FindQuad()
        {
            var quadTransform = Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(t => t != null && t.gameObject.scene.IsValid() && t.name == "VrUiQuad");
            return quadTransform != null ? quadTransform.GetComponent<Renderer>() : null;
        }

        private bool IsSourceCameraValid(Camera? camera) =>
            camera != null && camera.gameObject.scene.IsValid() && camera.name == "UIcamera" &&
            (camera.targetTexture == null || (_nativeMenuPresentation && camera == _source && camera.targetTexture == _texture)) &&
            camera.gameObject.activeInHierarchy && camera.enabled &&
            (camera.cullingMask & (1 << 5)) != 0;

        private void SaveUiState(ModConfiguration settings, Renderer quad)
        {
            _quad = quad;
            _oldQuadTexture = quad.material.mainTexture;
            _oldQuadLocalPosition = quad.transform.localPosition;
            _oldQuadLocalRotation = quad.transform.localRotation;
            _oldPatchMode = settings.PreferredUiPatchMode.Value;
            _oldRenderMode = settings.PreferredUiRenderMode.Value;
            _oldScale = settings.VrUiScale.Value;
        }

        private bool Create(Camera source)
        {
            var settings = ModConfiguration.Instance;
            if (settings == null) return false;
            var quad = FindQuad();
            if (quad == null) return false;
            if (source.gameObject.scene.name == "sceneMainMenu")
            {
                var intro = source.GetComponentInParent<scrIntroController>();
                if (intro != null && intro.menuCamera == source.gameObject)
                    return CreateNativeMenu(source, intro, settings, quad);
            }
            var candidates = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.activeInHierarchy &&
                    c.isRootCanvas && c.name != "renderCanvas" && c.name != "scalingCanvas" &&
                    (c.worldCamera == source || c.transform == source.transform.parent))
                .ToArray();
            if (candidates.Length == 0) return false;

            var layer = LayerHelper.GetVrUiLayer();
            const int width = UiTextureWidth;
            const int height = UiTextureHeight;
            // Native scrMenuPanel.Mask needs stencil. A live D16-only control
            // drew scrolled rows over help/footer; use the existing UI target
            // with a stencil-capable depth format, not another clipping surface.
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "CulticVR Selective UI", filterMode = FilterMode.Bilinear
            };
            texture.Create();
            var objectWithCamera = new GameObject("CulticVR UI Capture Camera");
            var capture = objectWithCamera.AddComponent<Camera>();
            capture.CopyFrom(source);
            capture.transform.position = source.transform.position + source.transform.right * 100f;
            capture.transform.rotation = source.transform.rotation;
            capture.targetTexture = texture;
            capture.stereoTargetEye = StereoTargetEyeMask.None;
            capture.cullingMask = 1 << layer;
            capture.clearFlags = CameraClearFlags.SolidColor;
            capture.backgroundColor = Color.clear;
            capture.depth = 100;
            capture.allowHDR = false;
            capture.allowMSAA = false;

            _source = source;
            _capture = capture;
            _texture = texture;
            SaveUiState(settings, quad);
            foreach (var canvas in candidates)
            {
                _canvases.Add(canvas, new CanvasState(canvas));
                SetLayer(canvas.transform, layer);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = capture;
                canvas.planeDistance = 10f;
            }
            _settingsApplied = true;
            SetSettings(settings, ModConfiguration.UiPatchMode.None,
                ModConfiguration.UiRenderMode.OverlayCamera, TrialScale);
            quad.material.mainTexture = texture;
            return true;
        }

        private bool CreateNativeMenu(Camera source, scrIntroController intro, ModConfiguration settings, Renderer quad)
        {
            // Both cameras were measured in the native flat control. The main
            // pass clears/draws the tunnel; UIcamera draws layered title art and
            // menus without clearing. Keep those SAME cameras/canvases/passes.
            var mainTransform = intro.transform.Find("Main Camera");
            var main = mainTransform != null ? mainTransform.GetComponent<Camera>() : null;
            var canvas = intro.GetComponent<Canvas>();
            if (main == null || canvas == null || canvas.worldCamera != source || source.transform.parent != intro.transform ||
                main.targetTexture != null || main.depth >= source.depth) return false;
            var layer = LayerHelper.GetVrUiLayer();
            if ((source.cullingMask & (1 << layer)) != 0)
                throw new InvalidOperationException("VR UI layer overlaps the native title UI layer; native capture cannot safely include its own projection quad.");

            SaveUiState(settings, quad);
            _settingsApplied = true;
            _nativeMenuPresentation = true;
            _source = source;
            _capture = source; // Alias only: Restore MUST NOT destroy this game-owned camera.
            _nativeMenuCameras = new[] { new NativeMenuCameraState(main), new NativeMenuCameraState(source) };
            _texture = new RenderTexture(UiTextureWidth, UiTextureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "CulticVR Native Title and Menu", filterMode = FilterMode.Bilinear
            };
            _texture.Create();
            foreach (var camera in _nativeMenuCameras) camera.Capture(_texture, layer);
            SetSettings(settings, ModConfiguration.UiPatchMode.None,
                ModConfiguration.UiRenderMode.OverlayCamera, TrialScale);
            quad.material.mainTexture = _texture;
            return true;
        }

        private sealed class NativeMenuCameraState
        {
            private readonly Camera _camera;
            private readonly RenderTexture? _target;
            private readonly StereoTargetEyeMask _eye;
            private readonly int _mask;
            private readonly Uuvr.VrCamera.VrCamera? _vr;
            private readonly bool _vrEnabled;
            private readonly UuvrPoseDriver? _pose;
            private readonly bool _poseEnabled;
            private readonly Camera[] _children;
            private readonly bool[] _childrenEnabled;

            public NativeMenuCameraState(Camera camera)
            {
                _camera = camera;
                _target = camera.targetTexture;
                _eye = camera.stereoTargetEye;
                _mask = camera.cullingMask;
                _vr = camera.GetComponent<Uuvr.VrCamera.VrCamera>();
                _vrEnabled = _vr != null && _vr.enabled;
                _pose = camera.GetComponent<UuvrPoseDriver>();
                _poseEnabled = _pose != null && _pose.enabled;
                // Scene/setup only. No repeated search, device enumeration or
                // hierarchy scan in the native menu's Update/render path.
                _children = camera.GetComponentsInChildren<Camera>(true)
                    .Where(c => c != camera && c.name == "VrChildCamera" && c.GetComponent<UuvrPoseDriver>() != null).ToArray();
                _childrenEnabled = new bool[_children.Length];
                for (var i = 0; i < _children.Length; i++) _childrenEnabled[i] = _children[i].enabled;
            }

            public void Capture(RenderTexture texture, int projectionLayer)
            {
                // Stop UUVR's writers (including absolute/relative-matrix modes)
                // before giving the game its original monoscopic camera back.
                if (_vr != null) _vr.enabled = false;
                if (_pose != null) _pose.enabled = false;
                foreach (var child in _children) child.enabled = false;
                _camera.stereoTargetEye = StereoTargetEyeMask.None;
                _camera.targetTexture = texture;
                _camera.cullingMask = _mask & ~(1 << projectionLayer);
                _camera.ResetStereoViewMatrices();
                _camera.ResetStereoProjectionMatrices();
                _camera.ResetWorldToCameraMatrix();
                _camera.ResetProjectionMatrix();
                // Both native camera local rotations are identity in serialized
                // scene data AND all three flat render samples. Remove a startup
                // UUVR pose write once; subsequent motion stays game-owned.
                _camera.transform.localRotation = Quaternion.identity;
            }

            public void Restore()
            {
                if (_camera == null) return;
                _camera.targetTexture = _target;
                _camera.stereoTargetEye = _eye;
                _camera.cullingMask = _mask;
                _camera.ResetWorldToCameraMatrix();
                _camera.ResetProjectionMatrix();
                _camera.ResetStereoViewMatrices();
                _camera.ResetStereoProjectionMatrices();
                for (var i = 0; i < _children.Length; i++)
                    if (_children[i] != null) _children[i].enabled = _childrenEnabled[i];
                if (_pose != null) _pose.enabled = _poseEnabled;
                if (_vr != null) _vr.enabled = _vrEnabled;
            }
        }

        private void Restore(bool restoreSettings = true)
        {
            // Restore before settings changes can replace the overlay, and even
            // when the source camera was destroyed during a scene transition.
            RestoreMenuBackground();
            // Observers have no Update and remain inert until this hierarchy is
            // captured again. Reuse them if Restore/Create happen in one frame;
            // Destroy would leave a pending-deletion component on that path.
            foreach (var observer in _layerObservers)
                if (observer != null) observer.Owner = null;
            _layerObservers.Clear();
            _dirtyLayerRoots.Clear();
            if (_source == null && !_settingsApplied) return;
            foreach (var entry in _canvases)
            {
                if (entry.Key == null) continue;
                entry.Key.renderMode = entry.Value.Mode;
                entry.Key.worldCamera = entry.Value.Camera;
                entry.Key.planeDistance = entry.Value.Distance;
            }
            _canvases.Clear();
            foreach (var entry in _layers)
                if (entry.Key != null) entry.Key.layer = entry.Value;
            _layers.Clear();
            if (_quad != null) _quad.material.mainTexture = _oldQuadTexture;
            if (_settingsApplied && restoreSettings && ModConfiguration.Instance != null)
                SetSettings(ModConfiguration.Instance, _oldPatchMode, _oldRenderMode, _oldScale);
            if (_quad != null)
            {
                _quad.transform.localPosition = _oldQuadLocalPosition;
                _quad.transform.localRotation = _oldQuadLocalRotation;
            }
            if (_nativeMenuCameras != null)
                foreach (var camera in _nativeMenuCameras) camera.Restore();
            if (_capture != null && !_nativeMenuPresentation) Destroy(_capture.gameObject);
            if (_texture != null) Destroy(_texture);
            if (_positionedOverlayCamera != null)
                _positionedOverlayCamera.transform.localPosition = _overlayCameraBaseLocalPosition;
            _source = null;
            _capture = null;
            _texture = null;
            _quad = null;
            _oldQuadTexture = null;
            _overlayCamera = null;
            _positionedOverlayCamera = null;
            _overlayCameraBaseLocalPosition = Vector3.zero;
            _settingsApplied = false;
            _loadingPresentation = false;
            _nativeMenuPresentation = false;
            _nativeMenuCameras = null;
        }


        private void SetLayer(Transform root, int layer)
        {
            if (!_layers.ContainsKey(root.gameObject)) _layers.Add(root.gameObject, root.gameObject.layer);
            if (root.gameObject.layer != layer) root.gameObject.layer = layer;
            var observer = root.GetComponent<UiLayerObserver>();
            if (observer == null) observer = root.gameObject.AddComponent<UiLayerObserver>();
            if (observer.Owner != this)
            {
                observer.Owner = this;
                _layerObservers.Add(observer);
            }
            for (var index = 0; index < root.childCount; index++) SetLayer(root.GetChild(index), layer);
        }

        internal void InvalidateLayers(Transform root) => _dirtyLayerRoots.Add(root);

        internal void ForgetLayers(UiLayerObserver observer)
        {
            // Wheel and damage-indicator objects are short lived. Do not retain
            // their observer or restoration entries for the rest of the map.
            _layerObservers.Remove(observer);
            _dirtyLayerRoots.Remove(observer.transform);
            _layers.Remove(observer.gameObject);
        }

        private void RefreshDirtyLayers()
        {
            if (_dirtyLayerRoots.Count == 0) return;
            var layer = LayerHelper.GetVrUiLayer();
            foreach (var root in _dirtyLayerRoots)
            {
                if (root == null) continue;
                // Wrist HUD and reticles deliberately leave the capture tree.
                // Their observers must not overwrite their world-space layers.
                foreach (var canvas in _canvases.Keys)
                {
                    if (canvas == null || !root.IsChildOf(canvas.transform)) continue;
                    SetLayer(root, layer);
                    break;
                }
            }
            _dirtyLayerRoots.Clear();
        }

        private static void SetSettings(ModConfiguration settings, ModConfiguration.UiPatchMode patch,
            ModConfiguration.UiRenderMode mode, float scale)
        {
            var saveOnSet = settings.Config.SaveOnConfigSet;
            settings.Config.SaveOnConfigSet = false;
            try
            {
                settings.PreferredUiPatchMode.Value = patch;
                settings.PreferredUiRenderMode.Value = mode;
                settings.VrUiScale.Value = scale;
            }
            finally { settings.Config.SaveOnConfigSet = saveOnSet; }
        }

        private static bool HasTrackedHeadset()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid && (!head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,
                out bool tracked) || tracked);
        }

    }

    internal sealed class UiLayerObserver : MonoBehaviour
    {
        internal UiTrialPlugin? Owner;
        private void OnTransformChildrenChanged() => Owner?.InvalidateLayers(transform);
        private void OnTransformParentChanged() => Owner?.InvalidateLayers(transform);
        // Children can be created under an inactive subtree. Resynchronize on
        // activation even when Unity did not deliver its hierarchy callbacks.
        private void OnEnable() => Owner?.InvalidateLayers(transform);
        private void OnDestroy() => Owner?.ForgetLayers(this);
    }
}
