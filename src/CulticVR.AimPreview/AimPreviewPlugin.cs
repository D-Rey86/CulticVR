using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using CulticVR.WorldStereoTrial;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace CulticVR.AimPreview
{
    [BepInPlugin("culticvr.aimpreview", "CULTIC VR Controller Viewmodel", "0.8.13")]
    [BepInDependency("culticvr.worldstereotrial")]
    [BepInDependency("culticvr.openxrcontrollers")]
    public sealed partial class AimPreviewPlugin : BaseUnityPlugin
    {
        public static AimPreviewPlugin? Instance { get; private set; }
        private const int ViewmodelTextureWidth = 2560;
        private const int ViewmodelTextureHeight = 1440;
        private const float ViewmodelWorldScale = 0.18f;
        // The source sprites occupy a 480x220 rect. These measured points are
        // the visible flame tip and unlit TNT wick tip in that rect, expressed
        // relative to the Image pivot at the lower center.
        private static readonly Vector3 LighterFlamePoint = new Vector3(-100f, 92f, 0f);
        private static readonly Vector3 DynamiteWickPoint = new Vector3(33f, 58f, 0f);
        // At the measured canvas scale, 4.45 sprite pixels is approximately
        // 0.80 cm. Keep the corrected per-Image hotspot mapping unchanged.
        private const float IgnitionContactPixels = 4.45f;
        // The calibrated flat sprites can visibly meet while their independently
        // oriented planes remain separated in depth. The headset trial measured
        // 8.2-9.2 cm at visible contact, so reject only depth gaps beyond 12 cm.
        private const float MaximumIgnitionDepthSeparation = 0.12f;
        // Headset-accepted author calibration. Fresh profiles start here while
        // retaining VR Options adjustment and per-user config overrides.
        private static readonly Vector3 DefaultRightPositionOffset =
            new Vector3(-0.1722667f, -0.02603554f, -0.07761036f);
        private static readonly Vector3 DefaultRightRotationOffset =
            new Vector3(350.904f, 2.726394f, 356.1222f);
        private static readonly Vector3 DefaultLeftPositionOffset =
            new Vector3(0.1179352f, -0.01536016f, 0.01023406f);
        private static readonly Vector3 DefaultLeftRotationOffset =
            new Vector3(356.2905f, 340.2216f, 358.2362f);

        private sealed class PointerControls
        {
            internal UnityEngine.InputSystem.InputDevice? Device;
            internal Vector3Control? Position;
            internal QuaternionControl? Rotation;

            internal void Clear()
            {
                Device = null;
                Position = null;
                Rotation = null;
            }
        }

        private string? _reportedFailure;
        private scrPlayerControl? _player;
        private readonly PointerControls _rightPointer = new PointerControls();
        private GameObject? _handObject;
        private Material? _handMaterial;
        private Mesh? _handMesh;
        private Renderer? _handRenderer;
        private RawImage? _flatViewmodel;
        private bool _flatViewmodelWasEnabled;
        private Transform? _uiQuad;
        private Renderer? _uiQuadRenderer;
        private bool _kickOnUiQuad;
        private bool _gamepadViewmodel;
        private int _handLayer;
        private int _handSortingLayer;
        private int _handSortingOrder;
        private int _handRenderQueue;
        private Camera? _viewmodelCamera;
        private RenderTexture? _viewmodelTexture;
        private RenderTexture? _originalViewmodelTarget;
        private float _originalViewmodelAspect;
        private GameObject? _rightDynamiteCanvas;
        private GameObject? _leftLighterCanvas;
        private Image? _rightDynamiteImage;
        private Image? _leftLighterImage;
        private Image? _rightDynamiteSource;
        private Image? _leftLighterSource;
        private Image? _culledOffHandSource;
        private bool _culledOffHandWasCulled;
        private RectTransform? _culledOffHandUnlitSource;
        private CanvasRenderer? _culledOffHandUnlitRenderer;
        private bool _culledOffHandUnlitWasCulled;
        private AccessTools.FieldRef<scrPlayerControl, int>? _throwStateRef;
        private AccessTools.FieldRef<scrPlayerControl, bool>? _tntIsLitRef;
        private AccessTools.FieldRef<scrPlayerControl, bool>? _tntIsBoundRef;
        private AccessTools.FieldRef<scrPlayerControl, int>? _lighterStateRef;
        private bool _ignitionStartedForContact;
        private bool _ignitionSeparatedAfterStart;
        private ConfigEntry<float> _rightPositionX = null!;
        private ConfigEntry<float> _rightPositionY = null!;
        private ConfigEntry<float> _rightPositionZ = null!;
        private ConfigEntry<float> _rightRotationX = null!;
        private ConfigEntry<float> _rightRotationY = null!;
        private ConfigEntry<float> _rightRotationZ = null!;
        private ConfigEntry<float> _leftPositionX = null!;
        private ConfigEntry<float> _leftPositionY = null!;
        private ConfigEntry<float> _leftPositionZ = null!;
        private ConfigEntry<float> _leftRotationX = null!;
        private ConfigEntry<float> _leftRotationY = null!;
        private ConfigEntry<float> _leftRotationZ = null!;
        private ConfigEntry<float> _shieldPositionX = null!;
        private ConfigEntry<float> _shieldPositionY = null!;
        private ConfigEntry<float> _shieldPositionZ = null!;
        private ConfigEntry<float> _shieldRotationX = null!;
        private ConfigEntry<float> _shieldRotationY = null!;
        private ConfigEntry<float> _shieldRotationZ = null!;
        private Vector3 _rightPositionOffset;
        private Quaternion _rightRotationOffset = Quaternion.identity;
        private RectTransform? _scopeWeaponRect;
        private Image? _scopeWeaponImage;
        private Sprite? _scopeSprite;
        private Vector2 _scopeCrosshairPixel;
        private bool _scopeCrosshairKnown;
        private Vector3 _leftPositionOffset;
        private Quaternion _leftRotationOffset = Quaternion.identity;
        private Vector3 _shieldPositionOffset;
        private Quaternion _shieldRotationOffset = Quaternion.identity;

        private void Awake()
        {
            Instance = this;
            SceneManager.activeSceneChanged += OnSceneChanged;
            BindCalibration();
            BindWristQuickMenu();
            BindWristStatusHud();
            InitializeMolotovIgnition();
            try
            {
                _throwStateRef = AccessTools.FieldRefAccess<scrPlayerControl, int>("throwState");
                _tntIsLitRef = AccessTools.FieldRefAccess<scrPlayerControl, bool>("tntIsLit");
                _tntIsBoundRef = AccessTools.FieldRefAccess<scrPlayerControl, bool>("tntIsBound");
                _lighterStateRef = AccessTools.FieldRefAccess<scrPlayerControl, int>("lighterState");
            }
            catch (Exception exception)
            {
                Logger.LogError($"Physical dynamite ignition unavailable: {exception.Message}");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            UnbindCalibration();
            UnbindWristQuickMenu();
            UnbindWristStatusHud();
            DestroyWristStatusHud();
            DestroyWristQuickMenu();
            DestroyHand();
            _player = null;
            _rightPointer.Clear();
        }

        private void OnSceneChanged(Scene previous, Scene current)
        {
            DestroyHand();
            DestroyWristStatusHud();
            DestroyWristQuickMenu();
            _player = null;
        }

        private void LateUpdate()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!WorldStereoTrialPlugin.IsGameplayScene(activeScene) || !XRSettings.isDeviceActive)
            {
                HideWristQuickMenu();
                DestroyWristStatusHud();
                if (_handObject != null) DestroyHand();
                return;
            }
            var player = FindPlayer(activeScene);
            var camera = player != null ? player.playerCameraComponent : null;
            if (camera == null)
            {
                HideWristQuickMenu();
                DestroyWristStatusHud();
                ReportFailure("gameplay camera unavailable");
                if (_handObject != null) DestroyHand();
                return;
            }
            if (!VrSettings.MotionControls)
            {
                HideWristQuickMenu(); DestroyWristStatusHud();
                UpdateGamepadViewmodel(player!);
                return;
            }
            if (_gamepadViewmodel) { RestoreControllerPresentation(); _gamepadViewmodel=false; }
            var rightDevice = InputDevices.GetDeviceAtXRNode(VrHandPose.WeaponNode);
            if (!VrHandPose.TryPointer(VrHandPose.WeaponNode,out var pointerPosition,out var pointerRotation))
            {
                HideWristQuickMenu();
                DestroyWristStatusHud();
                ReportFailure("weapon controller pointer unavailable");
                if (_handObject != null) DestroyHand();
                return;
            }
            if (!VrHandPose.TryHead(out var headPosition,out _))
            {
                HideWristQuickMenu();
                DestroyWristStatusHud();
                ReportFailure("tracked head position unavailable");
                if (_handObject != null) DestroyHand();
                return;
            }
            _reportedFailure = null;

            // The body yaw and HMD/controller poses share the OpenXR tracking
            // origin. Use the hand's offset from the head; CULTIC's camera gives
            // the world-space eye anchor. The viewmodel does not change CULTIC's
            // gameplay camera or firing methods.
            var yaw = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f) *
                WorldStereoTrialPlugin.TrackingYawCompensation;
            var trackedEyePosition = camera.transform.position +
                yaw * WorldStereoTrialPlugin.TrackingPositionCompensation;
            var rawRightOrigin = trackedEyePosition + yaw * (pointerPosition - headPosition);
            var rawRightOrientation = yaw * pointerRotation;
            var origin = rawRightOrigin;
            var orientation = rawRightOrientation;
            ApplyCalibration(ref origin, ref orientation, _rightPositionOffset, _rightRotationOffset);

            var leftGripPosition = Vector3.zero;
            var leftGripRotation = Quaternion.identity;
            var rightGripRotation = Quaternion.identity;
            var leftDevice = InputDevices.GetDeviceAtXRNode(VrHandPose.OffhandNode);
            var leftTracked = TryGetTrackedDevicePose(leftDevice, out leftGripPosition, out leftGripRotation);
            var dynamiteEquipped = IsLocalEquippedDynamite(player!);
            var lighterEquipped = IsLocalLighterEquipped(player!);
            var shieldEquipped = IsLocalShieldEquipped(player!);
            var leftPoseValid = (lighterEquipped || shieldEquipped) && leftTracked &&
                TryGetTrackedDevicePose(rightDevice, out _, out rightGripRotation);
            var rawLeftOrigin = Vector3.zero;
            var rawLeftOrientation = Quaternion.identity;
            var leftOrigin = Vector3.zero;
            var leftOrientation = Quaternion.identity;
            if (leftPoseValid)
            {
                // The custom left aim action aliases the right pose on this
                // runtime. Preserve the standard left grip position, but apply
                // the live, measured right aim-from-grip rotation so the left
                // artwork has the same aim alignment without inheriting the
                // right controller's pose.
                var aimFromGrip = Quaternion.Inverse(rightGripRotation) * pointerRotation;
                rawLeftOrigin = trackedEyePosition + yaw * (leftGripPosition - headPosition);
                rawLeftOrientation = yaw * leftGripRotation * aimFromGrip;
                leftOrigin = rawLeftOrigin;
                leftOrientation = rawLeftOrientation;
                ApplyCalibration(ref leftOrigin, ref leftOrientation,
                    shieldEquipped ? _shieldPositionOffset : _leftPositionOffset,
                    shieldEquipped ? _shieldRotationOffset : _leftRotationOffset);
            }
            UpdateWristQuickMenu(player!, trackedEyePosition, yaw, headPosition,
                leftTracked, leftGripPosition, leftGripRotation, origin, orientation, rightDevice);
            UpdateWristStatusHud(player!, trackedEyePosition, yaw, headPosition,
                leftTracked, leftGripPosition, leftGripRotation);
            UpdateHand(player!, trackedEyePosition, origin, orientation,
                leftPoseValid, dynamiteEquipped, lighterEquipped, shieldEquipped,
                leftOrigin, leftOrientation);
        }

        private void BindCalibration()
        {
            const string section = "Viewmodel Calibration";
            _rightPositionX = Config.Bind(section, "RightPositionX", DefaultRightPositionOffset.x, "Global weapon X offset in local metres; adjustable in VR Options.");
            _rightPositionY = Config.Bind(section, "RightPositionY", DefaultRightPositionOffset.y, "Global right-hand Y offset in local metres; applies to every weapon.");
            _rightPositionZ = Config.Bind(section, "RightPositionZ", DefaultRightPositionOffset.z, "Global right-hand Z offset in local metres; applies to every weapon.");
            _rightRotationX = Config.Bind(section, "RightRotationX", DefaultRightRotationOffset.x, "Global right-hand X rotation in degrees; applies to every weapon.");
            _rightRotationY = Config.Bind(section, "RightRotationY", DefaultRightRotationOffset.y, "Global right-hand Y rotation in degrees; applies to every weapon.");
            _rightRotationZ = Config.Bind(section, "RightRotationZ", DefaultRightRotationOffset.z, "Global right-hand Z rotation in degrees; applies to every weapon.");
            _leftPositionX = Config.Bind(section, "LeftPositionX", DefaultLeftPositionOffset.x, "Offhand lighter X offset in local metres. The default is the accepted embedded alignment.");
            _leftPositionY = Config.Bind(section, "LeftPositionY", DefaultLeftPositionOffset.y, "Left lighter Y offset in local metres.");
            _leftPositionZ = Config.Bind(section, "LeftPositionZ", DefaultLeftPositionOffset.z, "Left lighter Z offset in local metres.");
            _leftRotationX = Config.Bind(section, "LeftRotationX", DefaultLeftRotationOffset.x, "Left lighter X rotation in degrees.");
            _leftRotationY = Config.Bind(section, "LeftRotationY", DefaultLeftRotationOffset.y, "Left lighter Y rotation in degrees.");
            _leftRotationZ = Config.Bind(section, "LeftRotationZ", DefaultLeftRotationOffset.z, "Left lighter Z rotation in degrees.");
            _shieldPositionX = Config.Bind(section, "ShieldPositionX", DefaultLeftPositionOffset.x, "Offhand shield X offset in local metres. The default is the accepted embedded alignment.");
            _shieldPositionY = Config.Bind(section, "ShieldPositionY", DefaultLeftPositionOffset.y, "Left shield Y offset in local metres.");
            _shieldPositionZ = Config.Bind(section, "ShieldPositionZ", DefaultLeftPositionOffset.z, "Left shield Z offset in local metres.");
            _shieldRotationX = Config.Bind(section, "ShieldRotationX", DefaultLeftRotationOffset.x, "Left shield X rotation in degrees.");
            _shieldRotationY = Config.Bind(section, "ShieldRotationY", DefaultLeftRotationOffset.y, "Left shield Y rotation in degrees.");
            _shieldRotationZ = Config.Bind(section, "ShieldRotationZ", DefaultLeftRotationOffset.z, "Left shield Z rotation in degrees.");
            foreach (var entry in CalibrationEntries()) entry.SettingChanged += OnCalibrationChanged;
            RefreshCalibration();
        }

        private void UnbindCalibration()
        {
            foreach (var entry in CalibrationEntries()) entry.SettingChanged -= OnCalibrationChanged;
        }

        private ConfigEntry<float>[] CalibrationEntries() => new[]
        {
            _rightPositionX, _rightPositionY, _rightPositionZ,
            _rightRotationX, _rightRotationY, _rightRotationZ,
            _leftPositionX, _leftPositionY, _leftPositionZ,
            _leftRotationX, _leftRotationY, _leftRotationZ,
            _shieldPositionX, _shieldPositionY, _shieldPositionZ,
            _shieldRotationX, _shieldRotationY, _shieldRotationZ
        };

        private void OnCalibrationChanged(object sender, EventArgs args) => RefreshCalibration();

        private void RefreshCalibration()
        {
            _rightPositionOffset = new Vector3(_rightPositionX.Value, _rightPositionY.Value, _rightPositionZ.Value);
            _rightRotationOffset = Quaternion.Euler(_rightRotationX.Value, _rightRotationY.Value, _rightRotationZ.Value);
            _leftPositionOffset = new Vector3(_leftPositionX.Value, _leftPositionY.Value, _leftPositionZ.Value);
            _leftRotationOffset = Quaternion.Euler(_leftRotationX.Value, _leftRotationY.Value, _leftRotationZ.Value);
            _shieldPositionOffset = new Vector3(_shieldPositionX.Value, _shieldPositionY.Value, _shieldPositionZ.Value);
            _shieldRotationOffset = Quaternion.Euler(_shieldRotationX.Value, _shieldRotationY.Value, _shieldRotationZ.Value);
        }

        private static void ApplyCalibration(ref Vector3 origin, ref Quaternion orientation,
            Vector3 positionOffset, Quaternion rotationOffset)
        {
            positionOffset = WeaponSpriteMirror.Position(positionOffset,VrSettings.LeftWeapon);
            rotationOffset = WeaponSpriteMirror.Rotation(rotationOffset,VrSettings.LeftWeapon);
            origin += orientation * positionOffset;
            orientation *= rotationOffset;
        }

        public static bool TryGetRightHandOpticalPose(scrPlayerControl player,
            ref Vector3 origin, ref Quaternion orientation)
        {
            var instance = Instance;
            var sourceCamera = player.viewmodelCameraComponent;
            if (instance == null || sourceCamera == null) return false;
            ApplyCalibration(ref origin, ref orientation,
                instance._rightPositionOffset, instance._rightRotationOffset);
            // Legacy quad-center pose, retained only as the fallback when live
            // sprite/runtime data is unavailable. Its forward normal is NOT a
            // selected eye's sight line; do not use it to claim scope accuracy.
            origin += orientation * Vector3.up *
                (sourceCamera.orthographicSize * ViewmodelWorldScale * VrSettings.WeaponHeight);
            return true;
        }

        public static bool TryGetRightHandScopeCrosshair(scrPlayerControl player,
            Vector3 rawOrigin, Quaternion rawOrientation, out Vector3 crosshair, out Quaternion orientation)
        {
            crosshair = rawOrigin;
            orientation = rawOrientation;
            var instance = Instance;
            var sourceCamera = player.viewmodelCameraComponent;
            var weaponRect = player.playerWeapon;
            if (instance == null || sourceCamera == null || !sourceCamera.orthographic || weaponRect == null ||
                instance._viewmodelCamera != sourceCamera || instance._viewmodelTexture == null) return false;
            if (instance._scopeWeaponRect != weaponRect || instance._scopeWeaponImage == null)
            {
                instance._scopeWeaponRect = weaponRect;
                instance._scopeWeaponImage = weaponRect.GetComponent<Image>();
                instance._scopeSprite = null;
                instance._scopeCrosshairKnown = false;
            }
            var image = instance._scopeWeaponImage;
            if (image == null) return false;
            var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            if (sprite == null) return false;
            if (instance._scopeSprite != sprite)
            {
                instance._scopeSprite = sprite;
                instance._scopeCrosshairKnown = ScopeSightingMath.TryGetFg42Crosshair(
                    sprite.name, out instance._scopeCrosshairPixel);
            }
            if (!instance._scopeCrosshairKnown) return false;
            // Native Image is Simple, preserveAspect=false, 480x220, lower-center
            // RectTransform pivot. Map original sprite pixels through its LIVE
            // rect/camera so native bob, scope animation and recoil are retained.
            if (image.type != Image.Type.Simple || image.preserveAspect || image.useSpriteMesh ||
                sprite.rect.width != 480f || sprite.rect.height != 220f) return false;
            var rect = image.GetPixelAdjustedRect();
            var pixel = instance._scopeCrosshairPixel;
            var nativePoint = weaponRect.TransformPoint(new Vector3(
                rect.x + rect.width * pixel.x / 480f, rect.y + rect.height * pixel.y / 220f, 0f));
            var uv = sourceCamera.WorldToViewportPoint(nativePoint);
            if (!ScopeSightingMath.Finite(uv) || uv.z <= 0f) return false;
            var origin = rawOrigin;
            ApplyCalibration(ref origin, ref orientation, instance._rightPositionOffset, instance._rightRotationOffset);
            var baseHeight = sourceCamera.orthographicSize * 2f * ViewmodelWorldScale;
            var height = baseHeight * VrSettings.WeaponHeight;
            var texture = instance._viewmodelTexture;
            var width = WeaponSpriteMirror.HorizontalScale(baseHeight * texture.width / texture.height * VrSettings.WeaponWidth,VrSettings.LeftWeapon);
            // Same lower-center anchor, dimensions and UVs as UpdateHand.
            crosshair = origin + orientation * new Vector3(width * (uv.x - 0.5f), height * uv.y, 0f);
            return ScopeSightingMath.Finite(crosshair);
        }


        private void UpdateHand(scrPlayerControl player, Vector3 viewerPosition,
            Vector3 origin, Quaternion orientation, bool leftPoseValid, bool dynamiteEquipped,
            bool lighterEquipped, bool shieldEquipped, Vector3 leftOrigin, Quaternion leftOrientation)
        {
            var hud = player.playerHUD;
            var sourceCamera = player.viewmodelCameraComponent;
            if (sourceCamera == null && player.viewmodelCamera != null)
                sourceCamera = player.viewmodelCamera.GetComponent<Camera>();
            if (hud == null || hud.viewmodelRenderTexture == null ||
                hud.viewmodelRenderTexture.texture == null || sourceCamera == null)
            {
                ReportFailure("animated viewmodel image or camera unavailable");
                if (_handObject != null) DestroyHand();
                return;
            }
            if (_viewmodelCamera != null && _viewmodelCamera != sourceCamera) DestroyHand();
            if (!EnsureViewmodelTarget(sourceCamera)) return;
            var texture = _viewmodelTexture!;
            if (_handObject == null && !CreateHand(hud.viewmodelRenderTexture, texture, sourceCamera)) return;
            var showKickOnHud = player.state == 1 && scrGameControl.Instance != null &&
                scrGameControl.Instance.gameState == 0 &&
                player.offHandItem != scrPlayerControl.OffHandItem.Shield;
            if (showKickOnHud && PlaceKickOnUiQuad(texture))
            {
                HideDynamiteHands();
                if (_handRenderer != null) _handRenderer.enabled = true;
                return;
            }
            if (_kickOnUiQuad) RestoreControllerPresentation();
            if (_flatViewmodel != null) _flatViewmodel.enabled = false;
            if (_handMaterial != null) _handMaterial.mainTexture = texture;
            var splitDynamiteHands = dynamiteEquipped && lighterEquipped;
            if (leftPoseValid && splitDynamiteHands && UpdateDynamiteHands(player, sourceCamera,
                    viewerPosition, origin, orientation, leftOrigin, leftOrientation))
            {
                if (_handRenderer != null) _handRenderer.enabled = false;
                return;
            }
            if (!leftPoseValid || splitDynamiteHands || (!lighterEquipped && !shieldEquipped) ||
                !UpdateStandaloneOffHand(player, sourceCamera, leftOrigin, leftOrientation))
                HideDynamiteHands();
            if (_handRenderer != null) _handRenderer.enabled = true;
            var baseHeight = sourceCamera.orthographicSize * 2f * ViewmodelWorldScale;
            var height = baseHeight * VrSettings.WeaponHeight;
            var width = WeaponSpriteMirror.HorizontalScale(baseHeight * texture.width / texture.height * VrSettings.WeaponWidth,VrSettings.LeftWeapon);
            _handObject!.transform.localScale = new Vector3(width, height, 1f);
            _handObject.transform.rotation = orientation;
            // The RT is an orthographic view of the original weapon canvas.
            // Keep its lower center at the verified controller pointer pose.
            _handObject.transform.position = origin + orientation * new Vector3(0f, height * 0.5f, 0f);
            TryIgniteMolotov(player,viewerPosition,sourceCamera);
        }

        private bool UpdateDynamiteHands(scrPlayerControl player, Camera sourceCamera,
            Vector3 viewerPosition, Vector3 rightOrigin, Quaternion rightOrientation,
            Vector3 leftOrigin, Quaternion leftOrientation)
        {
            if (player.armCanvas == null ||
                player.playerWeapon == null || player.playerOffHand == null)
                return false;

            RestoreCulledOffHandSource();

            var rightSource = _rightDynamiteSource;
            if (rightSource == null || rightSource.rectTransform != player.playerWeapon)
                rightSource = player.playerWeapon.GetComponent<Image>();
            var leftSource = _leftLighterSource;
            if (leftSource == null || leftSource.rectTransform != player.playerOffHand)
                leftSource = player.playerOffHand.GetComponent<Image>();
            if (rightSource == null || leftSource == null) return false;
            if (_rightDynamiteSource != rightSource || _leftLighterSource != leftSource ||
                _rightDynamiteCanvas == null || _leftLighterCanvas == null ||
                _rightDynamiteImage == null || _leftLighterImage == null)
            {
                DestroyDynamiteHands();
                if (!CreateDynamiteHands(player.armCanvas, rightSource, leftSource)) return false;
            }

            CopyAnimatedImage(rightSource, _rightDynamiteImage!);
            CopyAnimatedImage(leftSource, _leftLighterImage!);
            var canvasScale = Mathf.Abs(player.armCanvas.lossyScale.y) * ViewmodelWorldScale;
            if (canvasScale <= 0.00001f) return false;
            var centerOffset = sourceCamera.orthographicSize * ViewmodelWorldScale;
            PlaceDynamiteCanvas(_rightDynamiteCanvas!, rightOrigin, rightOrientation, centerOffset, canvasScale);
            _rightDynamiteCanvas!.transform.localScale = new Vector3(WeaponSpriteMirror.HorizontalScale(canvasScale*VrSettings.WeaponWidth,VrSettings.LeftWeapon),canvasScale*VrSettings.WeaponHeight,canvasScale);
            _rightDynamiteCanvas.transform.position = rightOrigin+rightOrientation*Vector3.up*(centerOffset*VrSettings.WeaponHeight);
            PlaceDynamiteCanvas(_leftLighterCanvas!, leftOrigin, leftOrientation, centerOffset, canvasScale);
            _rightDynamiteCanvas!.SetActive(true);
            _leftLighterCanvas!.SetActive(true);
            TryIgniteDynamite(player, viewerPosition, canvasScale);
            return true;
        }

        private bool UpdateStandaloneOffHand(scrPlayerControl player, Camera sourceCamera,
            Vector3 leftOrigin, Quaternion leftOrientation)
        {
            if (player.armCanvas == null || player.playerOffHand == null)
                return false;

            var leftSource = _leftLighterSource;
            if (leftSource == null || leftSource.rectTransform != player.playerOffHand)
                leftSource = player.playerOffHand.GetComponent<Image>();
            if (leftSource == null) return false;
            if (_rightDynamiteCanvas != null || _leftLighterSource != leftSource ||
                _leftLighterCanvas == null || _leftLighterImage == null)
            {
                DestroyDynamiteHands();
                _leftLighterCanvas = CreateImageCanvas("CulticVR Left Offhand", player.armCanvas,
                    leftSource, out _leftLighterImage);
                if (_leftLighterCanvas == null || _leftLighterImage == null)
                {
                    DestroyDynamiteHands();
                    return false;
                }
                _leftLighterSource = leftSource;
            }

            CopyAnimatedImage(leftSource, _leftLighterImage!);
            var canvasScale = Mathf.Abs(player.armCanvas.lossyScale.y) * ViewmodelWorldScale;
            if (canvasScale <= 0.00001f) return false;
            var centerOffset = sourceCamera.orthographicSize * ViewmodelWorldScale;
            PlaceDynamiteCanvas(_leftLighterCanvas!, leftOrigin, leftOrientation,
                centerOffset, canvasScale);
            _leftLighterCanvas!.SetActive(true);
            CullOffHandSources(leftSource, player.playerOffHandUnlit);
            return true;
        }

        private bool CreateDynamiteHands(RectTransform sourceCanvas, Image rightSource, Image leftSource)
        {
            _rightDynamiteCanvas = CreateImageCanvas("CulticVR Right Dynamite", sourceCanvas,
                rightSource, out _rightDynamiteImage);
            _leftLighterCanvas = CreateImageCanvas("CulticVR Left Lighter", sourceCanvas,
                leftSource, out _leftLighterImage);
            if (_rightDynamiteCanvas == null || _leftLighterCanvas == null ||
                _rightDynamiteImage == null || _leftLighterImage == null)
            {
                DestroyDynamiteHands();
                return false;
            }
            _rightDynamiteSource = rightSource;
            _leftLighterSource = leftSource;
            return true;
        }

        private GameObject? CreateImageCanvas(string name, RectTransform sourceCanvas,
            Image sourceImage, out Image? cloneImage)
        {
            cloneImage = null;
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            root.layer = _handLayer;
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = sourceCanvas.rect.size;
            rootRect.pivot = sourceCanvas.pivot;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingLayerID = _handSortingLayer;
            canvas.sortingOrder = _handSortingOrder;

            var clone = Instantiate(sourceImage.gameObject, root.transform, false);
            clone.name = name + " Image";
            clone.layer = _handLayer;
            clone.SetActive(true);
            cloneImage = clone.GetComponent<Image>();
            if (cloneImage == null)
            {
                Destroy(root);
                return null;
            }
            cloneImage.raycastTarget = false;
            return root;
        }

        private static void CopyAnimatedImage(Image source, Image clone)
        {
            var sourceRect = source.rectTransform;
            var cloneRect = clone.rectTransform;
            cloneRect.anchorMin = sourceRect.anchorMin;
            cloneRect.anchorMax = sourceRect.anchorMax;
            cloneRect.anchoredPosition3D = sourceRect.anchoredPosition3D;
            cloneRect.sizeDelta = sourceRect.sizeDelta;
            cloneRect.pivot = sourceRect.pivot;
            cloneRect.localRotation = sourceRect.localRotation;
            cloneRect.localScale = sourceRect.localScale;
            clone.sprite = source.sprite;
            clone.overrideSprite = source.overrideSprite;
            clone.color = source.color;
            clone.material = source.material;
            clone.type = source.type;
            clone.preserveAspect = source.preserveAspect;
            clone.fillCenter = source.fillCenter;
            clone.fillMethod = source.fillMethod;
            clone.fillAmount = source.fillAmount;
            clone.fillClockwise = source.fillClockwise;
            clone.fillOrigin = source.fillOrigin;
            clone.enabled = source.enabled && source.gameObject.activeInHierarchy && source.sprite != null;
        }

        private void CullOffHandSources(Image source, RectTransform unlitSource)
        {
            if (_culledOffHandSource != source || _culledOffHandUnlitSource != unlitSource)
            {
                RestoreCulledOffHandSource();
                _culledOffHandSource = source;
                _culledOffHandWasCulled = source.canvasRenderer.cull;
                _culledOffHandUnlitSource = unlitSource;
                if (unlitSource != null)
                {
                    _culledOffHandUnlitRenderer = unlitSource.GetComponent<CanvasRenderer>();
                    if (_culledOffHandUnlitRenderer != null)
                        _culledOffHandUnlitWasCulled = _culledOffHandUnlitRenderer.cull;
                }
            }
            source.canvasRenderer.cull = true;
            if (_culledOffHandUnlitRenderer != null)
                _culledOffHandUnlitRenderer.cull = true;
        }

        private void RestoreCulledOffHandSource()
        {
            if (_culledOffHandSource != null)
                _culledOffHandSource.canvasRenderer.cull = _culledOffHandWasCulled;
            if (_culledOffHandUnlitRenderer != null)
                _culledOffHandUnlitRenderer.cull = _culledOffHandUnlitWasCulled;
            _culledOffHandSource = null;
            _culledOffHandWasCulled = false;
            _culledOffHandUnlitSource = null;
            _culledOffHandUnlitRenderer = null;
            _culledOffHandUnlitWasCulled = false;
        }

        private static void PlaceDynamiteCanvas(GameObject canvasObject, Vector3 origin,
            Quaternion orientation, float centerOffset, float canvasScale)
        {
            var transform = canvasObject.transform;
            transform.SetPositionAndRotation(origin + orientation * new Vector3(0f, centerOffset, 0f), orientation);
            transform.localScale = new Vector3(WeaponSpriteMirror.HorizontalScale(canvasScale,VrSettings.LeftWeapon), canvasScale, canvasScale);
        }

        private void TryIgniteDynamite(scrPlayerControl player, Vector3 viewerPosition, float canvasScale)
        {
            if (_rightDynamiteImage == null || _leftLighterImage == null ||
                !_rightDynamiteImage.enabled || !_leftLighterImage.enabled)
                return;
            var flame = _leftLighterImage.rectTransform.TransformPoint(
                SpritePointToImageRect(_leftLighterImage, LighterFlamePoint));
            var wick = _rightDynamiteImage.rectTransform.TransformPoint(
                SpritePointToImageRect(_rightDynamiteImage, DynamiteWickPoint));
            var contactRadius = IgnitionContactPixels * canvasScale;
            var flameFromViewer = flame - viewerPosition;
            var wickFromViewer = wick - viewerPosition;
            var flameDepth = flameFromViewer.magnitude;
            var wickDepth = wickFromViewer.magnitude;
            if (flameDepth <= 0.00001f || wickDepth <= 0.00001f) return;
            // Compare the endpoints in the viewer's angular image at the nearer
            // endpoint's depth. This is the allocation-free small-angle chord
            // equivalent of comparing their rendered screen positions.
            var apparentDistance = Vector3.Distance(flameFromViewer / flameDepth,
                wickFromViewer / wickDepth) * Mathf.Min(flameDepth, wickDepth);
            var depthSeparation = Mathf.Abs(flameDepth - wickDepth);
            var inContact = apparentDistance <= contactRadius &&
                depthSeparation <= MaximumIgnitionDepthSeparation;
            if (_ignitionStartedForContact)
            {
                if (!inContact) _ignitionSeparatedAfterStart = true;
                if (_tntIsLitRef != null && !_tntIsLitRef(player) && _ignitionSeparatedAfterStart)
                {
                    _ignitionStartedForContact = false;
                    _ignitionSeparatedAfterStart = false;
                }
                else
                {
                    return;
                }
            }
            if (!inContact)
                return;
            var block = GetIgnitionBlock(player);
            if (block != null) return;
            _ignitionStartedForContact = true;
            _ignitionSeparatedAfterStart = false;

            // The player's physical hand movement replaces CULTIC's canned
            // lighting motion. Invoke the same public animation-event handler
            // at contact; it owns cook time, Fuse, audio and weapon state.
            player.tntLight();
            // The native TNT Light state normally transitions here after its
            // tntLight event. Physical contact bypasses that canned motion, so
            // enter the controller's verified lit-idle state explicitly.
            player.armAnim.Play("TNT Idle Lit", 0, 0f);
            if (!_tntIsLitRef!(player))
                Logger.LogError("Physical dynamite contact reached tntLight, but CULTIC did not retain the lit-fuse state.");
        }

        private static Vector3 SpritePointToImageRect(Image image, Vector3 spritePoint)
        {
            var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
                return spritePoint;
            var rect = image.rectTransform.rect;
            return new Vector3(
                rect.xMin + (spritePoint.x / sprite.rect.width + 0.5f) * rect.width,
                rect.yMin + spritePoint.y / sprite.rect.height * rect.height,
                spritePoint.z);
        }

        private string? GetIgnitionBlock(scrPlayerControl player)
        {
            if (_throwStateRef == null || _tntIsLitRef == null ||
                _tntIsBoundRef == null || _lighterStateRef == null)
                return "private state access unavailable";
            var game = scrGameControl.Instance;
            if (game == null || game.gameState != 0) return "gameplay is not active";
            if (game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID) return "not the local offline player";
            if (!Application.isFocused) return "game window is not focused";
            if (game.inputCooldown > 0f) return "game input cooldown is active";
            if (player.fireCD > 0f) return "weapon cooldown is active";
            if (player.weaponState != 0) return "weapon animation is busy";
            if (_throwStateRef(player) != 0) return "throw is already charging";
            if (_tntIsLitRef(player)) return "dynamite is already lit";
            if (!_tntIsBoundRef(player)) return "dynamite bundle is unbound";
            if (_lighterStateRef(player) != 1) return "lighter flame is not active";
            if (player.offHandState != scrPlayerControl.OffHandState.Idle) return "offhand animation is busy";
            if (player.offHandItem != scrPlayerControl.OffHandItem.Lighter) return "lighter is not equipped";
            if (player.playerHUD == null) return "HUD is unavailable";
            if (player.playerHUD.wepWheelState != 0 || player.playerHUD.invWheelState != 0 ||
                player.playerHUD.wepWheelCD > 0f) return "weapon or inventory wheel is active";
            try
            {
                var weapon = game.gamePlayers[player.playerID].playerLoadout[player.weapon];
                if (weapon.weaponTableID != 6) return "dynamite is not equipped";
                if (game.gamePlayers[player.playerID].ammo[weapon.weaponAmmoID] <= 0)
                    return "no dynamite ammo remains";
                return null;
            }
            catch
            {
                return "weapon state is unavailable";
            }
        }

        private static bool IsLocalEquippedDynamite(scrPlayerControl player)
        {
            var game = scrGameControl.Instance;
            if (game == null || game.gameState != 0 || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID || player.weapon < 0 || player.tempWeapon != null)
                return false;
            try
            {
                return game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID == 6;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLocalLighterEquipped(scrPlayerControl player)
        {
            var game = scrGameControl.Instance;
            return game != null && game.gameState == 0 &&
                game.connectionStatus == scrGameControl.ConnectionStatus.Offline &&
                player.playerID == game.localPlayerID &&
                player.offHandItem == scrPlayerControl.OffHandItem.Lighter;
        }

        private static bool IsLocalShieldEquipped(scrPlayerControl player)
        {
            var game = scrGameControl.Instance;
            return game != null && game.gameState == 0 &&
                game.connectionStatus == scrGameControl.ConnectionStatus.Offline &&
                player.playerID == game.localPlayerID &&
                player.offHandItem == scrPlayerControl.OffHandItem.Shield;
        }

        private void HideDynamiteHands()
        {
            ResetMolotovIgnition();
            RestoreCulledOffHandSource();
            if (_rightDynamiteCanvas != null) _rightDynamiteCanvas.SetActive(false);
            if (_leftLighterCanvas != null) _leftLighterCanvas.SetActive(false);
            _ignitionStartedForContact = false;
            _ignitionSeparatedAfterStart = false;
        }

        private void DestroyDynamiteHands()
        {
            ResetMolotovIgnition();
            RestoreCulledOffHandSource();
            if (_rightDynamiteCanvas != null) Destroy(_rightDynamiteCanvas);
            if (_leftLighterCanvas != null) Destroy(_leftLighterCanvas);
            _rightDynamiteCanvas = null;
            _leftLighterCanvas = null;
            _rightDynamiteImage = null;
            _leftLighterImage = null;
            _rightDynamiteSource = null;
            _leftLighterSource = null;
            _ignitionStartedForContact = false;
            _ignitionSeparatedAfterStart = false;
        }

        private static bool TryGetTrackedDevicePose(UnityEngine.XR.InputDevice device,
            out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!device.isValid ||
                !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position) ||
                !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation))
                return false;
            return Quaternion.Dot(rotation, rotation) > 0.5f;
        }

        private bool EnsureViewmodelTarget(Camera sourceCamera)
        {
            if (_viewmodelCamera == null)
            {
                _viewmodelCamera = sourceCamera;
                _originalViewmodelTarget = sourceCamera.targetTexture;
                _originalViewmodelAspect = sourceCamera.aspect;
            }

            // updateScreenSize can replace the game's target while VR is active.
            // Remember the newest game-owned target so teardown restores it.
            if (sourceCamera.targetTexture != _viewmodelTexture)
                _originalViewmodelTarget = sourceCamera.targetTexture;

            if (_viewmodelTexture == null)
            {
                _viewmodelTexture = new RenderTexture(ViewmodelTextureWidth, ViewmodelTextureHeight, 16,
                    RenderTextureFormat.ARGB32)
                {
                    name = "CulticVR Controller Viewmodel",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Mirror
                };
            }
            if (!_viewmodelTexture.IsCreated()) _viewmodelTexture.Create();
            if (!_viewmodelTexture.IsCreated())
            {
                ReportFailure("fixed VR viewmodel render texture could not be created");
                return false;
            }

            sourceCamera.targetTexture = _viewmodelTexture;
            sourceCamera.aspect = (float)ViewmodelTextureWidth / ViewmodelTextureHeight;
            return true;
        }

        private bool CreateHand(RawImage flat, Texture texture, Camera sourceCamera)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                ReportFailure("transparent viewmodel shader unavailable");
                return false;
            }
            _handObject = new GameObject("CulticVR Controller Viewmodel");
            _handObject.layer = 0;
            _handMesh = new Mesh { name = "CulticVR Controller Viewmodel Quad" };
            _handMesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            _handMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            _handMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _handMesh.RecalculateNormals();
            _handObject.AddComponent<MeshFilter>().sharedMesh = _handMesh;
            _handMaterial = new Material(shader) { mainTexture = texture, color = Color.white };
            _handRenderer = _handObject.AddComponent<MeshRenderer>();
            _handRenderer.sharedMaterial = _handMaterial;
            _handRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _handRenderer.receiveShadows = false;
            _handLayer = _handObject.layer;
            _handSortingLayer = _handRenderer.sortingLayerID;
            _handSortingOrder = _handRenderer.sortingOrder;
            _handRenderQueue = _handMaterial.renderQueue;
            _flatViewmodel = flat;
            _flatViewmodelWasEnabled = flat.enabled;
            return true;
        }

        private bool PlaceKickOnUiQuad(Texture texture)
        {
            if (_uiQuad == null || !_uiQuad.gameObject.activeInHierarchy)
            {
                _uiQuad = Resources.FindObjectsOfTypeAll<Transform>()
                    .FirstOrDefault(candidate => candidate != null && candidate.gameObject.scene.IsValid() &&
                        candidate.gameObject.activeInHierarchy && candidate.name == "VrUiQuad");
                _uiQuadRenderer = _uiQuad != null ? _uiQuad.GetComponent<Renderer>() : null;
            }
            if (_uiQuad == null || _uiQuadRenderer == null || _handObject == null ||
                _handRenderer == null || _handMaterial == null)
                return false;
            if (!_kickOnUiQuad)
            {
                _handObject.transform.SetParent(_uiQuad, false);
                _handObject.transform.localPosition = Vector3.zero;
                _handObject.transform.localRotation = Quaternion.identity;
                _handObject.transform.localScale = Vector3.one;
                _handObject.layer = _uiQuad.gameObject.layer;
                _handRenderer.sortingLayerID = _uiQuadRenderer.sortingLayerID;
                _handRenderer.sortingOrder = _uiQuadRenderer.sortingOrder + 1;
                if (_uiQuadRenderer.sharedMaterial != null)
                    _handMaterial.renderQueue = _uiQuadRenderer.sharedMaterial.renderQueue;
                _kickOnUiQuad = true;
            }
            _handMaterial.mainTexture = texture;
            return true;
        }

        private void UpdateGamepadViewmodel(scrPlayerControl player)
        {
            // Reuse the accepted head-locked kick presentation: complete native
            // animated texture, unit UV/quad, existing stereo overlay camera.
            // The old renderCanvas RawImage is not a fused VR presentation.
            var source = player.viewmodelCameraComponent;
            var flat = scrGameControl.Instance?.hudScript?.viewmodelRenderTexture;
            if (source == null || flat == null || !EnsureViewmodelTarget(source)) return;
            if (_handObject == null && !CreateHand(flat, _viewmodelTexture!, source)) return;
            HideDynamiteHands();
            if (!PlaceKickOnUiQuad(_viewmodelTexture!))
            { ReportFailure("head-locked weapon overlay unavailable"); DestroyHand(); return; }
            flat.enabled = false;
            _handRenderer!.enabled = true;
            _handRenderer.sortingOrder = _uiQuadRenderer!.sortingOrder-1;
            if (_uiQuadRenderer.sharedMaterial != null)
                _handMaterial!.renderQueue = _uiQuadRenderer.sharedMaterial.renderQueue-1;
            _gamepadViewmodel = true;
            _reportedFailure = null;
        }

        private void RestoreControllerPresentation()
        {
            if (_handObject != null)
            {
                _handObject.transform.SetParent(null, true);
                _handObject.layer = _handLayer;
            }
            if (_handRenderer != null)
            {
                _handRenderer.sortingLayerID = _handSortingLayer;
                _handRenderer.sortingOrder = _handSortingOrder;
            }
            if (_handMaterial != null) _handMaterial.renderQueue = _handRenderQueue;
            _kickOnUiQuad = false;
        }

        private void DestroyHand()
        {
            DestroyDynamiteHands();
            if (_flatViewmodel != null) _flatViewmodel.enabled = _flatViewmodelWasEnabled;
            if (_handObject != null) Destroy(_handObject);
            if (_handMaterial != null) Destroy(_handMaterial);
            if (_handMesh != null) Destroy(_handMesh);
            if (_viewmodelCamera != null && _viewmodelCamera.targetTexture == _viewmodelTexture)
            {
                _viewmodelCamera.targetTexture = _originalViewmodelTarget;
                _viewmodelCamera.aspect = _originalViewmodelAspect;
            }
            if (_viewmodelTexture != null) Destroy(_viewmodelTexture);
            _handObject = null;
            _handMaterial = null;
            _handMesh = null;
            _handRenderer = null;
            _flatViewmodel = null;
            _uiQuad = null;
            _uiQuadRenderer = null;
            _kickOnUiQuad = false;
            _viewmodelCamera = null;
            _gamepadViewmodel = false;
            _viewmodelTexture = null;
            _originalViewmodelTarget = null;
        }

        private void ReportFailure(string reason)
        {
            if (_reportedFailure == reason) return;
            Logger.LogWarning($"Controller viewmodel unavailable: {reason}");
            _reportedFailure = reason;
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

        private bool TryGetPointerPose(UnityEngine.XR.InputDevice trackedDevice, string handUsage,
            PointerControls controls,
            out Vector3 position, out Quaternion rotation, out string failure)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            failure = "right-hand OpenXR device unavailable";
            if (!EnsurePointerControls(handUsage, controls)) return false;
            // The custom OpenXR action device supplies valid pointer pose values,
            // but its separate pointer/isTracked bit can remain false. The right
            // controller's XR tracking state is the verified validity check.
            failure = "right controller is not tracked";
            if (!trackedDevice.isValid ||
                !trackedDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || !tracked)
                return false;
            failure = "right-hand pointer position or rotation control unavailable";
            position = controls.Position!.ReadValue();
            rotation = controls.Rotation!.ReadValue();
            failure = "right-hand pointer rotation is invalid";
            return Quaternion.Dot(rotation, rotation) > 0.5f;
        }

        private static bool EnsurePointerControls(string handUsage, PointerControls controls)
        {
            if (controls.Device != null && controls.Device.added &&
                controls.Position != null && controls.Rotation != null)
                return true;
            controls.Clear();
            foreach (var device in InputSystem.devices)
            {
                if (device.description.interfaceName?.IndexOf("XR", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var matchingHand = false;
                foreach (var usage in device.usages)
                {
                    if (usage.ToString() != handUsage) continue;
                    matchingHand = true;
                    break;
                }
                if (!matchingHand) continue;
                foreach (var control in device.allControls)
                {
                    if (controls.Position == null && control is Vector3Control pointerPosition &&
                        pointerPosition.path.EndsWith("/pointer/position", StringComparison.OrdinalIgnoreCase))
                        controls.Position = pointerPosition;
                    else if (controls.Rotation == null && control is QuaternionControl pointerRotation &&
                        pointerRotation.path.EndsWith("/pointer/rotation", StringComparison.OrdinalIgnoreCase))
                        controls.Rotation = pointerRotation;
                }
                if (controls.Position == null || controls.Rotation == null)
                {
                    controls.Position = null;
                    controls.Rotation = null;
                    continue;
                }
                controls.Device = device;
                return true;
            }
            return false;
        }
    }
}
