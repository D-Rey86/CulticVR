using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using Uuvr;

namespace CulticVR.WorldStereoTrial
{
    [BepInPlugin("culticvr.worldstereotrial", "CULTIC VR World Stereo", "0.8.3")]
    [DefaultExecutionOrder(-100)]
    public sealed partial class WorldStereoTrialPlugin : BaseUnityPlugin
    {
        private static WorldStereoTrialPlugin? _instance;
        public static Quaternion TrackingYawCompensation { get; private set; } = Quaternion.identity;
        public static Vector3 TrackingPositionCompensation { get; private set; } = Vector3.zero;
        // AimTrial owns the sole config/menu setting. This provider reads it
        // live; rendering must not acquire a separate menu-owned eye choice.
        public static Func<Camera.StereoscopicEye>? ScopeSightingEyeProvider { get; set; }

        private const float TrackingPositionDiscontinuity = 0.5f;
        private static readonly AccessTools.FieldRef<scrPlayerControl, int> ZoomState =
            AccessTools.FieldRefAccess<scrPlayerControl, int>("zoomState");
        private static readonly AccessTools.FieldRef<scrPlayerControl, int> Fg42ZoomLevel =
            AccessTools.FieldRefAccess<scrPlayerControl, int>("FG42ZoomLevel");

        private Camera? _source;
        private scrPlayerControl? _player;
        private Camera? _stereo;
        private Camera? _skySource;
        private Camera? _skyStereo;
        private StereoProjectionScale? _worldProjectionScale;
        private StereoProjectionScale? _skyProjectionScale;
        private Transform? _worldVrChild;
        private Transform? _skyVrChild;
        private RawImage? _worldImage;
        private bool _originalWorldImageEnabled;
        private float _createdAt;
        private bool _reportedReady;
        private bool _reportedMissingDriver;
        private bool _savedXrScale;
        private Harmony? _resolutionHarmony;
        public static float ActiveResolutionScale { get; private set; } = 1f;
        private float _originalEyeTextureScale;
        private float _originalViewportScale;
        private bool _quitting;
        private scrPlayerControl? _physicalYawPlayer;
        private bool _physicalYawInitialized;
        private float _lastPhysicalHeadYaw;
        private float _absorbedPhysicalHeadYaw;
        private int _physicalYawFrame = -1;
        private bool _physicalPositionInitialized;
        private Vector3 _physicalHeadPositionOrigin;
        private Vector3 _lastPhysicalHeadPosition;
        private float _baseVerticalFov = 90f;
        private float _normalScopeMagnification;
        private float _scopeVerticalFov = 90f;

        private void Awake()
        {
            VrSettings.Bind(Config);
            _instance = this;
            InitializeXrResolutionPolicy();
            InitializePoseRotationFastPath();
            InitializeHiddenWorldGeometry();
            InitializeHeadCollision();
            InitializePhysicalLocomotion();
            SceneManager.activeSceneChanged += OnSceneChanged;
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDestroy()
        {
            RemoveArtificialLocomotion();
            if (_instance == this) _instance = null;
            _locomotionHarmony?.UnpatchSelf();
            _resolutionHarmony?.UnpatchSelf();
            _poseRotationHarmony?.UnpatchSelf();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Application.onBeforeRender -= OnBeforeRender;
            Restore();
            ResetPhysicalYaw();
            if (!_quitting) RestoreXrResolutionScale();
        }

        private void OnApplicationQuit() => _quitting = true;

        public static bool TryGetSightingEyeOffset(scrPlayerControl player, Camera.StereoscopicEye eye,
            out Vector3 headLocalOffset)
        {
            headLocalOffset = Vector3.zero;
            var instance = _instance;
            if (instance == null || instance._player != player || !instance._reportedReady ||
                instance._source != player.playerCameraComponent || instance._stereo == null ||
                !instance._stereo.isActiveAndEnabled || !instance._stereo.stereoEnabled) return false;
            var camera = instance._stereo;
            // A live eye-setting change may select the previously limited eye
            // before LateUpdate renders the new pair. Shooting always needs its
            // PHYSICAL offset, not the other eye's virtual scoped render offset.
            if (instance._worldProjectionScale != null && instance._worldProjectionScale.HasCustomViews)
                return instance._worldProjectionScale.TryGetPhysicalEyeOffset(eye, out headLocalOffset);
            if (!ScopeSightingMath.TryGetEyeOrigin(camera.GetStereoViewMatrix(eye), out var worldEye)) return false;
            // Pair the runtime view with its owning camera transform. Retain only
            // the runtime eye offset, then compose with the current firing pose:
            // game Update/animation events precede our LateUpdate render pose.
            headLocalOffset = camera.transform.InverseTransformPoint(worldEye);
            return ScopeSightingMath.Finite(headLocalOffset);
        }

        private void OnSceneChanged(Scene previous, Scene current)
        {
            Restore();
            // The OpenXR tracking origin survives scene loads. Keep the
            // accumulated compensation so a carried/saved camRot does not get
            // composed with the same physical yaw a second time.
            _physicalYawPlayer = null;
            _physicalYawFrame = -1;
        }

        private void LateUpdate()
        {
            if (HasTrackedHeadset()) ApplyXrResolutionPolicy();
            if (!HasTrackedHeadset())
            {
                Restore();
                return;
            }


            var activeScene = SceneManager.GetActiveScene();
            if (!IsGameplayScene(activeScene))
            {
                Restore();
                return;
            }

            var player = FindPlayer(activeScene);
            var camera = player != null ? player.playerCameraComponent : null;
            var image = player != null && player.playerHUD != null ? player.playerHUD.renderTexture : null;
            if (camera == null || image == null || camera.targetTexture == null || !camera.isActiveAndEnabled)
            {
                Restore();
                return;
            }
            var sky = _skySource;
            if (_stereo == null)
            {
                sky = null;
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Camera>())
                {
                    if (candidate == null || !candidate.gameObject.scene.IsValid() ||
                        candidate.gameObject.scene != activeScene || candidate.name != "skyboxCamera" ||
                        !candidate.isActiveAndEnabled) continue;
                    sky = candidate;
                    break;
                }
            }
            else if (sky != null &&
                (!sky.gameObject.scene.IsValid() || sky.gameObject.scene != activeScene || !sky.isActiveAndEnabled))
            {
                Restore();
                return;
            }

            if (_source != camera || _worldImage != image || _stereo == null ||
                (_skySource != null && _skyStereo == null))
            {
                Restore();
                Create(camera, sky, image);
            }

            if (_stereo == null) return;
            UpdateScopeProjection(player!);
            _stereo.enabled = camera.enabled;
            _stereo.fieldOfView = camera.fieldOfView;
            _stereo.nearClipPlane = camera.nearClipPlane;
            _stereo.farClipPlane = camera.farClipPlane;
            _stereo.cullingMask = camera.cullingMask;
            _stereo.clearFlags = camera.clearFlags;
            _stereo.backgroundColor = camera.backgroundColor;
            _stereo.depth = camera.depth;
            if (sky != null && _skyStereo != null)
            {
                _skyStereo.enabled = sky.enabled;
                _skyStereo.fieldOfView = sky.fieldOfView;
                _skyStereo.nearClipPlane = sky.nearClipPlane;
                _skyStereo.farClipPlane = sky.farClipPlane;
                _skyStereo.cullingMask = sky.cullingMask;
                _skyStereo.clearFlags = sky.clearFlags;
                _skyStereo.backgroundColor = sky.backgroundColor;
                _skyStereo.depth = Mathf.Min(camera.depth - 1f, sky.depth + 1f);
            }
            if (_worldVrChild == null)
            {
                _worldVrChild = _stereo.transform.Find("VrCameraOffset/VrChildCamera");
                if (_worldVrChild != null && _worldProjectionScale != null)
                    _worldProjectionScale.RuntimeProjectionSource = _worldVrChild.GetComponent<Camera>();
            }
            if (_skyStereo != null && _skyVrChild == null)
            {
                _skyVrChild = _skyStereo.transform.Find("VrCameraOffset/VrChildCamera");
                if (_skyVrChild != null && _skyProjectionScale != null)
                    _skyProjectionScale.RuntimeProjectionSource = _skyVrChild.GetComponent<Camera>();
            }
            if (_worldVrChild != null && (_skyStereo == null || _skyVrChild != null))
            {
                ApplyHeadPose(true);
                image.enabled = false;
                if (!_reportedReady)
                {
                    _stereo.gameObject.AddComponent<VrBrightness>().Initialize(_stereo,player!);
                    _reportedReady = true;
                }
            }
            else if (!_reportedMissingDriver && Time.unscaledTime - _createdAt > 3f)
            {
                Logger.LogWarning("UUVR has not attached to both trial cameras; original world RawImage remains visible.");
                _reportedMissingDriver = true;
            }
        }

        private void UpdateScopeProjection(scrPlayerControl player)
        {
            var targetFov = _baseVerticalFov;
            var scoped = false;
            var game = scrGameControl.Instance;
            if (game != null && player.weapon >= 0 && ZoomState(player) == 1)
            {
                try
                {
                    var weapon = game.gamePlayers[player.playerID].playerLoadout[player.weapon];
                    if (weapon.weaponTableID == 9)
                    {
                        var upgradedZoom = game.gamePlayers[player.playerID].playerUpgrades[14, 2] *
                            Fg42ZoomLevel(player);
                        targetFov *= 0.55f - 0.3f * upgradedZoom;
                        scoped = true;
                    }
                }
                catch
                {
                    scoped = false;
                    targetFov = _baseVerticalFov;
                }
            }

            _scopeVerticalFov = Mathf.Lerp(_scopeVerticalFov, targetFov,
                0.2f * (Time.deltaTime * 60f));
            if (!scoped && Mathf.Abs(_scopeVerticalFov - _baseVerticalFov) < 0.01f)
                _scopeVerticalFov = _baseVerticalFov;
            var projectionScale = _scopeVerticalFov == _baseVerticalFov ? 1f :
                Mathf.Tan(_baseVerticalFov * 0.5f * Mathf.Deg2Rad) /
                Mathf.Tan(_scopeVerticalFov * 0.5f * Mathf.Deg2Rad);
            var separationScale = ScopeStereoMath.SeparationScale(projectionScale, _normalScopeMagnification);
            var sightingEye = separationScale < 1f && ScopeSightingEyeProvider != null ?
                ScopeSightingEyeProvider() : Camera.StereoscopicEye.Right;
            if (_worldProjectionScale != null)
            {
                _worldProjectionScale.Scale = projectionScale;
                _worldProjectionScale.EyeSeparationScale = separationScale;
                _worldProjectionScale.BaselineScale = VrSettings.Separation.Value;
                _worldProjectionScale.SightingEye = sightingEye;
            }
            if (_skyProjectionScale != null)
            {
                _skyProjectionScale.Scale = projectionScale;
                _skyProjectionScale.EyeSeparationScale = separationScale;
                _skyProjectionScale.BaselineScale = VrSettings.Separation.Value;
                _skyProjectionScale.SightingEye = sightingEye;
            }

        }

        private void InitializeXrResolutionPolicy()
        {
            // The shipped loader creates its display in Initialize, then UUVR
            // starts it. Allocate at the chosen scale before any XR render frame,
            // never when a slider changes in an already-running session.
            // A typed reference loads the bundled assembly before bootstrap;
            // name-only discovery cannot see it yet at this plugin's Awake.
            var initialize = AccessTools.Method(typeof(OpenXRLoaderBase),"Initialize",Type.EmptyTypes);
            if (initialize == null) { Logger.LogError("VR resolution startup hook unavailable; retaining runtime resolution."); return; }
            _resolutionHarmony = new Harmony("culticvr.worldstereotrial.resolution");
            _resolutionHarmony.Patch(initialize,postfix:new HarmonyMethod(AccessTools.Method(typeof(WorldStereoTrialPlugin),nameof(XrLoaderInitialized))));
        }
        private static void XrLoaderInitialized(bool __result)
        {
            var instance = _instance;
            if (!__result || instance == null || instance._savedXrScale) return;
            instance._originalEyeTextureScale = XRSettings.eyeTextureResolutionScale;
            instance._originalViewportScale = XRSettings.renderViewportScale;
            instance._savedXrScale = true;
            ActiveResolutionScale = Mathf.Clamp(VrSettings.Resolution.Value,.5f,2f);
            if (!Mathf.Approximately(XRSettings.eyeTextureResolutionScale,ActiveResolutionScale))
                XRSettings.eyeTextureResolutionScale = ActiveResolutionScale;
            XRSettings.renderViewportScale = 1f;
        }
        private void ApplyXrResolutionPolicy()
        {
            if (!_savedXrScale)
            {
                _originalEyeTextureScale = XRSettings.eyeTextureResolutionScale;
                _originalViewportScale = XRSettings.renderViewportScale;
                _savedXrScale = true;
            }

            // A pending menu choice is for the next launch. Full viewport keeps
            // the current allocation intact and avoids desktop-setting coupling.
            if (!Mathf.Approximately(XRSettings.renderViewportScale, 1f))
                XRSettings.renderViewportScale = 1f;

        }

        private void RestoreXrResolutionScale()
        {
            if (!_savedXrScale) return;
            XRSettings.eyeTextureResolutionScale = _originalEyeTextureScale;
            XRSettings.renderViewportScale = _originalViewportScale;
            _savedXrScale = false;
        }

        private void OnBeforeRender()
        {
            if (_reportedReady && _stereo != null)
                ApplyHeadPose(false);
        }

        private void ApplyHeadPose(bool updatePosition)
        {
            if (_source == null || _stereo == null) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid || !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation))
                return;

            // CULTIC restores camRot.y (pitch) from saves and applies it to the
            // original camera. In VR, the HMD supplies pitch and roll; only the
            // game's horizontal facing belongs in the rendering base. Set the
            // clone's world rotation so inherited parent pitch/recoil is canceled
            // without changing gameplay camera, movement, or shot transforms.
            UpdatePhysicalYaw(_player, _source, rotation);
            var horizontalFacing = Quaternion.Euler(0f, _source.transform.eulerAngles.y, 0f);
            var vrFacing = horizontalFacing * TrackingYawCompensation * rotation;
            if (updatePosition)
            {
                if (head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 headPosition))
                    UpdatePhysicalPosition(headPosition);
                var trackingFacing = horizontalFacing * TrackingYawCompensation;
                var desired = _source.transform.position + trackingFacing * TrackingPositionCompensation;
                var resolved = ConstrainHeadPosition(desired);
                TrackingPositionCompensation = Quaternion.Inverse(trackingFacing) *
                    (resolved - _source.transform.position);
                _stereo.transform.position = resolved;
            }
            // Before-render rotation remains late-latched. Do not overwrite the
            // resolved translation with an unchecked tracking sample, or rotate
            // its offset around the native camera again after collision.
            _stereo.transform.rotation = vrFacing;
            if (_skyStereo != null) _skyStereo.transform.rotation = vrFacing;
        }

        private void UpdatePhysicalPosition(Vector3 headPosition)
        {
            if (!_physicalPositionInitialized)
            {
                _physicalPositionInitialized = true;
                _physicalHeadPositionOrigin = headPosition;
                _lastPhysicalHeadPosition = headPosition;
                TrackingPositionCompensation = Vector3.zero;
                return;
            }

            var frameDelta = headPosition - _lastPhysicalHeadPosition;
            _lastPhysicalHeadPosition = headPosition;
            if (frameDelta.sqrMagnitude > TrackingPositionDiscontinuity * TrackingPositionDiscontinuity)
            {
                // A tracking-origin recenter is discontinuous. Move the stored
                // origin by the same amount so the world does not jump.
                _physicalHeadPositionOrigin += frameDelta;
                if (_standingHeightKnown) _standingTrackingHeight += frameDelta.y;
            }
            TrackingPositionCompensation = headPosition - _physicalHeadPositionOrigin;
            ApplyPhysicalCrouchHeight(headPosition);
        }

        private void UpdatePhysicalYaw(scrPlayerControl? player, Camera source, Quaternion headRotation)
        {
            if (_physicalYawFrame == Time.frameCount) return;
            _physicalYawFrame = Time.frameCount;
            var game = scrGameControl.Instance;
            if (player == null || game == null ||
                game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID)
            {
                TrackingYawCompensation = Quaternion.identity;
                return;
            }
            TrackingYawCompensation = Quaternion.AngleAxis(-_absorbedPhysicalHeadYaw, Vector3.up);
            if (game.gameState != 0 || player.isDead || !Application.isFocused) return;

            var headForward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
            if (headForward.sqrMagnitude <= 0.000001f) return;
            var headYaw = Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg;
            if (!_physicalYawInitialized)
            {
                _physicalYawPlayer = player;
                _physicalYawInitialized = true;
                _lastPhysicalHeadYaw = headYaw;
                // The raw OpenXR pose can already have a non-zero yaw when a
                // scene or save becomes playable. Treat that current pose as
                // neutral. Otherwise rendering includes the initial yaw while
                // CULTIC rotates locomotion only by camRot, leaving every
                // forward input permanently offset from the visible heading.
                _absorbedPhysicalHeadYaw = headYaw;
                TrackingYawCompensation = Quaternion.AngleAxis(-headYaw, Vector3.up);
                return;
            }
            if (_physicalYawPlayer != player)
            {
                _physicalYawPlayer = player;
                _lastPhysicalHeadYaw = headYaw;
                // A new player can appear after a map transition or save load
                // while the headset continued rotating through the loading
                // screen. Re-pair the compensation with the current pose so
                // the new player's authored/restored camRot is also the visible
                // and locomotion-forward direction.
                _absorbedPhysicalHeadYaw = headYaw;
                TrackingYawCompensation = Quaternion.AngleAxis(-headYaw, Vector3.up);
                return;
            }

            var deltaYaw = Mathf.DeltaAngle(_lastPhysicalHeadYaw, headYaw);
            _lastPhysicalHeadYaw = headYaw;
            if (Mathf.Abs(deltaYaw) <= 0.0001f) return;
            player.camRot = new Vector2(Mathf.Repeat(player.camRot.x + deltaYaw, 360f), player.camRot.y);
            source.transform.rotation = Quaternion.AngleAxis(deltaYaw, Vector3.up) * source.transform.rotation;
            _absorbedPhysicalHeadYaw += deltaYaw;
            TrackingYawCompensation = Quaternion.AngleAxis(-_absorbedPhysicalHeadYaw, Vector3.up);
        }

        private void ResetPhysicalYaw()
        {
            _physicalYawPlayer = null;
            _physicalYawInitialized = false;
            _lastPhysicalHeadYaw = 0f;
            _absorbedPhysicalHeadYaw = 0f;
            _physicalYawFrame = -1;
            TrackingYawCompensation = Quaternion.identity;
            ResetPhysicalPosition();
        }

        private void ResetPhysicalPosition()
        {
            _physicalPositionInitialized = false;
            _physicalHeadPositionOrigin = Vector3.zero;
            _lastPhysicalHeadPosition = Vector3.zero;
            TrackingPositionCompensation = Vector3.zero;
            ResetHeadCollision();
            ResetPhysicalLocomotion();
        }

        private void Create(Camera source, Camera? skySource, RawImage image)
        {
            var objectName = "CulticVR Stereo World Trial";
            var stereoObject = new GameObject(objectName);
            stereoObject.transform.SetParent(source.transform, false);
            stereoObject.transform.localPosition = Vector3.zero;
            stereoObject.transform.localRotation = Quaternion.identity;
            stereoObject.transform.localScale = Vector3.one;
            stereoObject.tag = "Untagged";

            var stereoCamera = stereoObject.AddComponent<Camera>();
            stereoCamera.CopyFrom(source);
            stereoCamera.targetTexture = null;
            stereoCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            // CULTIC's aggressively baked single-view visibility cells can
            // select different renderer sets for the two tracked eye positions
            // near cell boundaries. That makes whole static objects pop in one
            // eye before the other. Frustum culling remains enabled; only the
            // incompatible baked occlusion data is bypassed for this VR camera.
            stereoCamera.useOcclusionCulling = false;
            var worldProjectionScale = stereoObject.AddComponent<StereoProjectionScale>();

            Camera? skyCamera = null;
            if (skySource != null)
            {
                var skyObject = new GameObject("CulticVR Stereo Sky Trial");
                skyObject.transform.SetParent(skySource.transform, false);
                skyObject.transform.localPosition = Vector3.zero;
                skyObject.transform.localRotation = Quaternion.identity;
                skyObject.transform.localScale = Vector3.one;
                skyObject.tag = "Untagged";
                skyCamera = skyObject.AddComponent<Camera>();
                skyCamera.CopyFrom(skySource);
                skyCamera.targetTexture = null;
                skyCamera.depth = Mathf.Min(source.depth - 1f, skySource.depth + 1f);
                skyCamera.stereoTargetEye = StereoTargetEyeMask.Both;
                _skyProjectionScale = skyObject.AddComponent<StereoProjectionScale>();
            }

            _source = source;
            _stereo = stereoCamera;
            _worldProjectionScale = worldProjectionScale;
            _skySource = skySource;
            _skyStereo = skyCamera;
            _worldImage = image;
            _originalWorldImageEnabled = image.enabled;
            var game = scrGameControl.Instance;
            _baseVerticalFov = game != null ?
                70f + game.prefsList["fieldOfView"] * 50f : 90f;
            _normalScopeMagnification = Mathf.Tan(_baseVerticalFov * 0.5f * Mathf.Deg2Rad) /
                Mathf.Tan(_baseVerticalFov * 0.55f * 0.5f * Mathf.Deg2Rad);
            _scopeVerticalFov = _baseVerticalFov;
            _createdAt = Time.unscaledTime;
            _reportedReady = false;
            _reportedMissingDriver = false;
            AttachHiddenWorldGeometry();
            ResetPhysicalPosition();
        }

        private void Restore()
        {
            DetachHiddenWorldGeometry();
            if (_worldImage != null) _worldImage.enabled = _originalWorldImageEnabled;
            if (_stereo != null) Destroy(_stereo.gameObject);
            if (_skyStereo != null) Destroy(_skyStereo.gameObject);
            _source = null;
            _player = null;
            _stereo = null;
            _worldProjectionScale = null;
            _skySource = null;
            _skyStereo = null;
            _skyProjectionScale = null;
            _worldVrChild = null;
            _skyVrChild = null;
            _worldImage = null;
            ResetPhysicalPosition();
        }

        public static bool IsGameplayScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            var name = scene.name;
            if (name.Length < 9 || !name.StartsWith("sceneE", System.StringComparison.Ordinal)) return false;

            var index = 6;
            var episodeStart = index;
            while (index < name.Length && name[index] >= '0' && name[index] <= '9') index++;
            if (index == episodeStart || index >= name.Length || name[index] != 'M') return false;

            index++;
            var mapStart = index;
            while (index < name.Length && name[index] >= '0' && name[index] <= '9') index++;
            return index > mapStart && index == name.Length;
        }

        public static bool IsActiveGameplayPlayer(scrPlayerControl? player)
        {
            if (player == null || !player.gameObject.activeInHierarchy) return false;
            var activeScene = SceneManager.GetActiveScene();
            return IsGameplayScene(activeScene) && player.gameObject.scene == activeScene;
        }

        private scrPlayerControl? FindPlayer(Scene activeScene)
        {
            if (_player != null && _player.gameObject.scene == activeScene && _player.gameObject.activeInHierarchy)
                return _player;
            _player = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<scrPlayerControl>())
            {
                if (candidate == null || candidate.gameObject.scene != activeScene ||
                    !candidate.gameObject.activeInHierarchy) continue;
                _player = candidate;
                break;
            }
            return _player;
        }

        private static bool HasTrackedHeadset()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid && (!head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,
                out bool tracked) || tracked);
        }
    }

    internal sealed class StereoProjectionScale : MonoBehaviour
    {
        internal float Scale = 1f;
        internal float BaselineScale = 1f;
        internal float EyeSeparationScale = 1f;
        internal Camera.StereoscopicEye SightingEye = Camera.StereoscopicEye.Right;
        internal Camera? RuntimeProjectionSource;
        private Camera? _camera;
        private bool _customProjection;
        internal bool HasCustomViews { get; private set; }

        private void Awake() => _camera = GetComponent<Camera>();

        private void OnPreCull()
        {
            var camera = _camera;
            if (camera == null || !camera.stereoEnabled)
            {
                RestoreViews();
                return;
            }
            if (Scale <= 1.0001f)
            {
                RestoreProjection();
                UpdateViews();
                return;
            }
            if (!TryGetProjections(Scale, out var left, out var right))
            {
                RestoreProjection();
                RestoreViews();
                return;
            }
            camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, left);
            camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, right);
            _customProjection = true;
            UpdateViews();
        }

        private bool TryGetLocalViews(out Matrix4x4 left, out Matrix4x4 right)
        {
            left = right = default;
            var source = RuntimeProjectionSource;
            if (source == null || source == _camera || !source.stereoEnabled) return false;
            // Read the untouched runtime child, paired with ITS owning transform
            // at this callback. Rebase onto the parent only after extracting eye
            // geometry: using the child's world view directly double-rotates HMD.
            var localToWorld = source.transform.localToWorldMatrix;
            return ScopeStereoMath.TryGetLocalView(source.GetStereoViewMatrix(Camera.StereoscopicEye.Left), localToWorld, out left) &&
                ScopeStereoMath.TryGetLocalView(source.GetStereoViewMatrix(Camera.StereoscopicEye.Right), localToWorld, out right);
        }

        internal bool TryGetPhysicalEyeOffset(Camera.StereoscopicEye eye, out Vector3 offset)
        {
            offset = default;
            var source = RuntimeProjectionSource;
            if (source == null || source == _camera || !source.stereoEnabled) return false;
            return ScopeStereoMath.TryGetLocalView(source.GetStereoViewMatrix(eye), source.transform.localToWorldMatrix, out var local) &&
                TryGetScaledEyeOffset(eye, local, out offset);
        }
        private bool TryGetScaledEyeOffset(Camera.StereoscopicEye eye, Matrix4x4 local, out Vector3 offset)
        {
            offset = default;
            if (BaselineScale == 1f) return ScopeSightingMath.TryGetEyeOrigin(local,out offset);
            return TryGetLocalViews(out var left,out var right) &&
                VrStereoMath.TryScaleBaseline(left,right,BaselineScale,out left,out right) &&
                ScopeSightingMath.TryGetEyeOrigin(eye==Camera.StereoscopicEye.Left?left:right,out offset);
        }

        private void UpdateViews()
        {
            // This mod owns the parent render pose in the configured default
            // RelativeTransform mode. RelativeMatrix would let UUVR overwrite
            // our views later; Child/Absolute have different pose owners. Do not
            // apply this effect across an unverified ownership contract.
            if ((EyeSeparationScale >= 1f && BaselineScale == 1f) || ModConfiguration.Instance == null ||
                ModConfiguration.Instance.CameraTracking.Value != ModConfiguration.CameraTrackingMode.RelativeTransform)
            {
                RestoreViews();
                return;
            }
            var camera = _camera;
            if (camera == null || !TryGetLocalViews(out var left, out var right) ||
                !VrStereoMath.TryScaleBaseline(left,right,BaselineScale,out left,out right) ||
                !ScopeStereoMath.TryLimitSeparation(left, right, EyeSeparationScale,
                    SightingEye == Camera.StereoscopicEye.Left, out left, out right))
            {
                RestoreViews();
                return;
            }
            var parentWorldToLocal = camera.transform.worldToLocalMatrix;
            left *= parentWorldToLocal;
            right *= parentWorldToLocal;
            if (!ScopeStereoMath.Valid(left) || !ScopeStereoMath.Valid(right))
            {
                RestoreViews();
                return;
            }
            // Both optical projections and eye bases remain runtime-authored.
            // Only extra stereo baseline amplification is limited, anchored on
            // the unchanged physical sighting eye. No self-feedback/read/reset.
            camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, left);
            camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, right);
            HasCustomViews = true;
        }

        private void RestoreViews()
        {
            if (!HasCustomViews || _camera == null) return;
            if (TryGetLocalViews(out var left, out var right) &&
                ScopeStereoMath.TryLimitSeparation(left, right, 1f, false, out left, out right))
            {
                var parentWorldToLocal = _camera.transform.worldToLocalMatrix;
                left *= parentWorldToLocal;
                right *= parentWorldToLocal;
                if (ScopeStereoMath.Valid(left) && ScopeStereoMath.Valid(right))
                {
                    _camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, left);
                    _camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, right);
                }
            }
            _camera.ResetStereoViewMatrices();
            HasCustomViews = false;
        }

        private bool TryGetProjections(float scale, out Matrix4x4 left, out Matrix4x4 right)
        {
            left = default;
            right = default;
            var source = RuntimeProjectionSource;
            var camera = _camera;
            if (source == null || source == camera || camera == null || !source.stereoEnabled) return false;
            // UUVR's already-existing child has untouched runtime eye projections.
            // The parent getter can return our previous override in multipass:
            // the captured right eye overflowed while this child's stayed finite.
            // Never reset/read/rescale the camera we are overriding.
            // Keep depth terms authoritative if CULTIC changes its clip planes.
            if (source.nearClipPlane != camera.nearClipPlane) source.nearClipPlane = camera.nearClipPlane;
            if (source.farClipPlane != camera.farClipPlane) source.farClipPlane = camera.farClipPlane;
            return StereoProjectionMath.TryScale(source.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), scale, out left) &&
                StereoProjectionMath.TryScale(source.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), scale, out right);
        }

        private void RestoreProjection()
        {
            if (!_customProjection || _camera == null) return;
            // Explicitly seed both eyes with their unscaled runtime projection
            // before releasing the override; do not leave a stale scaled right eye.
            if (TryGetProjections(1f, out var left, out var right))
            {
                _camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, left);
                _camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, right);
            }
            _camera.ResetStereoProjectionMatrices();
            _customProjection = false;
        }

        private void OnDisable()
        {
            RestoreProjection();
            RestoreViews();
        }
    }
}
