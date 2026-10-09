using System;
using BepInEx.Configuration;
using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace CulticVR.AimPreview
{
    public sealed partial class AimPreviewPlugin
    {
        private const float WristMenuShowPalmDot = 0.62f;
        private const float WristMenuHidePalmDot = 0.38f;
        private const float WristMenuPoseDebounce = 0.12f;
        private const float WristMenuWorldScale = 0.0005f;
        private const float WristMenuHalfSize = 160f;
        private const float WristMenuButtonRadius = 48f;
        private const float WristMenuTriggerPress = 0.55f;
        private const float WristMenuTriggerRelease = 0.25f;
        private static readonly Vector2[] WristMenuButtonCenters =
        {
            new Vector2(-92f, 0f), new Vector2(0f, 92f),
            new Vector2(92f, 0f), new Vector2(0f, -92f)
        };
        private static readonly GamepadButton[] WristMenuButtons =
        {
            GamepadButton.DpadLeft, GamepadButton.DpadUp,
            GamepadButton.DpadRight, GamepadButton.DpadDown
        };
        private static readonly string[] WristMenuLabels =
        {
            "QUICK TNT", "LIGHTER", "FIELD KIT", "ABILITY"
        };

        private ConfigEntry<bool> _wristQuickMenuEnabled = null!;
        private GameObject? _wristMenuRoot;
        private RectTransform? _wristMenuRect;
        private scrPlayerHUD? _wristMenuHud;
        private readonly Image?[] _wristMenuFrames = new Image?[4];
        private readonly Image?[] _wristMenuIcons = new Image?[4];
        private readonly Image?[] _wristMenuSourceIcons = new Image?[4];
        private Image? _wristMenuPointer;
        private AudioSource? _wristMenuAudio;
        private bool _wristMenuVisible;
        private bool _wristMenuPendingVisible;
        private float _wristMenuPoseChangeTime = -1f;
        private bool _wristMenuTriggerArmed;
        private int _wristMenuHover = -1;
        private Vector2 _wristMenuPointerLocal;

        private void BindWristQuickMenu()
        {
            _wristQuickMenuEnabled = Config.Bind("Wrist Quick Menu", "Enabled", true,
                "Show CULTIC's four D-pad shortcuts on a palm-up left-wrist menu. Intended for a future in-game VR menu toggle.");
            _wristQuickMenuEnabled.SettingChanged += OnWristQuickMenuSettingChanged;
        }

        private void UnbindWristQuickMenu()
        {
            if (_wristQuickMenuEnabled != null)
                _wristQuickMenuEnabled.SettingChanged -= OnWristQuickMenuSettingChanged;
        }

        private void OnWristQuickMenuSettingChanged(object sender, EventArgs args)
        {
            if (!_wristQuickMenuEnabled.Value) HideWristQuickMenu();
        }

        private void UpdateWristQuickMenu(scrPlayerControl player, Vector3 eyePosition,
            Quaternion bodyYaw, Vector3 headTrackingPosition, bool leftTracked,
            Vector3 leftTrackingPosition, Quaternion leftTrackingRotation,
            Vector3 rightPointerOrigin, Quaternion rightPointerRotation,
            UnityEngine.XR.InputDevice rightDevice)
        {
            var game = scrGameControl.Instance;
            var hud = player.playerHUD;
            if (!_wristQuickMenuEnabled.Value || !leftTracked || game == null || hud == null ||
                game.gameState != 0 || !hud.gameObject.activeInHierarchy ||
                hud.wepWheelState != 0 || hud.invWheelState != 0)
            {
                HideWristQuickMenu();
                return;
            }

            var leftOrigin = eyePosition + bodyYaw * (leftTrackingPosition - headTrackingPosition);
            var leftOrientation = bodyYaw * leftTrackingRotation;
            var palmNormal = leftOrientation * (VrSettings.LeftWeapon?Vector3.left:Vector3.right);

            if (_wristMenuRoot == null || _wristMenuHud != hud)
            {
                DestroyWristQuickMenu();
                if (!CreateWristQuickMenu(hud)) return;
            }
            SyncWristQuickMenuIcons(hud);

            var palmDot = Vector3.Dot(palmNormal, Vector3.up);
            var requestedVisible = _wristMenuVisible ?
                palmDot >= WristMenuHidePalmDot : palmDot >= WristMenuShowPalmDot;
            if (requestedVisible != _wristMenuPendingVisible)
            {
                _wristMenuPendingVisible = requestedVisible;
                _wristMenuPoseChangeTime = Time.unscaledTime;
            }
            if (_wristMenuPoseChangeTime >= 0f &&
                Time.unscaledTime - _wristMenuPoseChangeTime >= WristMenuPoseDebounce)
            {
                _wristMenuVisible = _wristMenuPendingVisible;
                _wristMenuPoseChangeTime = -1f;
                if (!_wristMenuVisible) ResetWristQuickMenuInteraction();
            }
            if (!_wristMenuVisible)
            {
                SetWristQuickMenuActive(false);
                return;
            }

            var wristPosition = leftOrigin - leftOrientation * Vector3.forward * 0.115f + palmNormal * 0.035f;
            var toWrist = wristPosition - eyePosition;
            if (toWrist.sqrMagnitude < 0.01f)
            {
                HideWristQuickMenu();
                return;
            }
            _wristMenuRoot!.transform.SetPositionAndRotation(wristPosition,
                Quaternion.LookRotation(toWrist, Vector3.up));
            _wristMenuRoot.transform.localScale = Vector3.one * WristMenuWorldScale;
            SetWristQuickMenuActive(true);

            var pointerDirection = rightPointerRotation * Vector3.forward;
            var pointerProjected = TryProjectWristPointer(rightPointerOrigin, pointerDirection,
                out var pointerLocal, out var hover);
            if (pointerProjected) _wristMenuPointerLocal = pointerLocal;
            // The dot belongs to the visible menu even when the ray misses it.
            // Invalid/parallel/behind rays retain the last bounded dot position
            // (center on opening) but cannot activate a stale selection.
            OpenXRControllersPlugin.SetWristQuickMenuCapture(true);
            if (_wristMenuPointer != null)
            {
                _wristMenuPointer.gameObject.SetActive(true);
                _wristMenuPointer.rectTransform.anchoredPosition = _wristMenuPointerLocal;
            }
            SetWristQuickMenuHover(hover);

            var trigger = ReadRightTrigger(rightDevice);
            if (!pointerProjected)
            {
                _wristMenuTriggerArmed = false;
                return;
            }
            if (trigger <= WristMenuTriggerRelease)
                _wristMenuTriggerArmed = true;
            if (!_wristMenuTriggerArmed || trigger < WristMenuTriggerPress || hover < 0) return;
            _wristMenuTriggerArmed = false;
            OpenXRControllersPlugin.QueueWristQuickMenuDpad(WristMenuButtons[hover]);
            PlayWristMenuTick(1f);
        }

        private bool CreateWristQuickMenu(scrPlayerHUD hud)
        {
            var root = new GameObject("CulticVR Left Wrist Quick Menu", typeof(RectTransform), typeof(Canvas));
            DontDestroyOnLoad(root);
            _wristMenuRoot = root;
            _wristMenuRect = (RectTransform)root.transform;
            _wristMenuRect.sizeDelta = Vector2.one * WristMenuHalfSize * 2f;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 160;

            var backgroundSprite = hud.wepWheelBack != null ?
                hud.wepWheelBack.overrideSprite ?? hud.wepWheelBack.sprite : null;
            var background = CreateWristImage("CULTIC Wheel Back", _wristMenuRect,
                Vector2.zero, Vector2.one * WristPointerBounds.BackgroundSize, backgroundSprite,
                new Color(0.08f, 0.055f, 0.045f, 0.92f));
            background.raycastTarget = false;

            _wristMenuSourceIcons[0] = FindWristSourceImage(hud.padLeftIcon);
            _wristMenuSourceIcons[1] = FindWristSourceImage(hud.padUpIcon);
            _wristMenuSourceIcons[2] = FindWristSourceImage(hud.padRightIcon);
            _wristMenuSourceIcons[3] = hud.abilityIcon;
            var frameSprite = hud.radialSprites != null && hud.radialSprites.Length != 0 ?
                hud.radialSprites[0] : null;
            var textTemplate = hud.weaponWheelMessage != null ? hud.weaponWheelMessage : hud.padLeftKey;
            for (var index = 0; index < 4; index++)
            {
                var center = WristMenuButtonCenters[index];
                _wristMenuFrames[index] = CreateWristImage("Frame " + WristMenuLabels[index],
                    _wristMenuRect, center, new Vector2(88f, 88f), frameSprite,
                    new Color(0.72f, 0.66f, 0.56f, 0.48f));
                _wristMenuIcons[index] = CreateWristImage("Icon " + WristMenuLabels[index],
                    _wristMenuRect, center + new Vector2(0f, 7f), new Vector2(44f, 44f),
                    SourceSprite(_wristMenuSourceIcons[index]), Color.white);
                CreateWristLabel(WristMenuLabels[index], _wristMenuRect,
                    center + new Vector2(0f, -34f), textTemplate);
            }
            CreateWristLabel("QUICK ACCESS", _wristMenuRect, Vector2.zero, textTemplate, 13f);
            var pointerSprite = hud.crossHairs != null && hud.crossHairs.Length != 0 && hud.crossHairs[0] != null ?
                hud.crossHairs[0].overrideSprite ?? hud.crossHairs[0].sprite : null;
            _wristMenuPointer = CreateWristImage("Pointer", _wristMenuRect, Vector2.zero,
                Vector2.one * WristPointerBounds.PointerSize, pointerSprite, new Color(1f, 0.48f, 0.38f, 1f));
            _wristMenuPointer.transform.SetAsLastSibling();
            _wristMenuPointer.gameObject.SetActive(false);

            _wristMenuAudio = root.AddComponent<AudioSource>();
            _wristMenuAudio.playOnAwake = false;
            _wristMenuAudio.loop = false;
            _wristMenuAudio.spatialBlend = 0f;
            var game = scrGameControl.Instance;
            if (game != null) _wristMenuAudio.outputAudioMixerGroup = game.masterMixGroup;
            _wristMenuHud = hud;
            root.SetActive(false);
            return true;
        }

        private static Image CreateWristImage(string name, Transform parent, Vector2 position,
            Vector2 size, Sprite? sprite, Color color)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = root.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = sprite != null;
            image.raycastTarget = false;
            return image;
        }

        private static void CreateWristLabel(string value, Transform parent, Vector2 position,
            TextMeshProUGUI? template, float fontSize = 11f)
        {
            if (template == null) return;
            var root = new GameObject(value + " Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(100f, 22f);
            var label = root.GetComponent<TextMeshProUGUI>();
            label.font = template.font;
            label.fontSharedMaterial = template.fontSharedMaterial;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.93f, 0.84f, 0.68f, 1f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.text = value;
        }

        private bool TryProjectWristPointer(Vector3 origin, Vector3 direction,
            out Vector2 localPoint, out int hover)
        {
            localPoint = Vector2.zero;
            hover = -1;
            if (_wristMenuRoot == null) return false;
            var normal = _wristMenuRoot.transform.forward;
            var denominator = Vector3.Dot(direction, normal);
            if (Mathf.Abs(denominator) < 0.0001f) return false;
            var distance = Vector3.Dot(_wristMenuRoot.transform.position - origin, normal) / denominator;
            if (distance < 0.03f || distance > 2f) return false;
            var local = _wristMenuRoot.transform.InverseTransformPoint(origin + direction * distance);
            if (!WristPointerBounds.TryClamp(local.x, local.y, out var x, out var y)) return false;
            localPoint = new Vector2(x, y);
            var bestDistance = WristMenuButtonRadius * WristMenuButtonRadius;
            for (var index = 0; index < WristMenuButtonCenters.Length; index++)
            {
                var squareDistance = (localPoint - WristMenuButtonCenters[index]).sqrMagnitude;
                if (squareDistance > bestDistance) continue;
                bestDistance = squareDistance;
                hover = index;
            }
            return true;
        }

        private void SetWristQuickMenuHover(int hover)
        {
            if (_wristMenuHover == hover) return;
            _wristMenuHover = hover;
            for (var index = 0; index < _wristMenuFrames.Length; index++)
            {
                if (_wristMenuFrames[index] != null)
                    _wristMenuFrames[index]!.color = index == hover ?
                        new Color(1f, 0.48f, 0.38f, 0.95f) :
                        new Color(0.72f, 0.66f, 0.56f, 0.48f);
            }
            if (hover >= 0) PlayWristMenuTick(0.35f);
        }

        private void SyncWristQuickMenuIcons(scrPlayerHUD hud)
        {
            _wristMenuSourceIcons[3] = hud.abilityIcon;
            for (var index = 0; index < _wristMenuIcons.Length; index++)
            {
                if (_wristMenuIcons[index] != null)
                    _wristMenuIcons[index]!.sprite = SourceSprite(_wristMenuSourceIcons[index]);
            }
        }

        private void PlayWristMenuTick(float volume)
        {
            var game = scrGameControl.Instance;
            if (_wristMenuAudio == null || game == null || game.masterAudioList == null ||
                game.masterAudioList.Length == 0 || game.masterAudioList[0] == null) return;
            _wristMenuAudio.PlayOneShot(game.masterAudioList[0], volume);
        }

        private static Image? FindWristSourceImage(GameObject? root)
        {
            if (root == null) return null;
            var images = root.GetComponentsInChildren<Image>(true);
            for (var index = 0; index < images.Length; index++)
                if (SourceSprite(images[index]) != null) return images[index];
            return null;
        }

        private static Sprite? SourceSprite(Image? image) =>
            image == null ? null : image.overrideSprite ?? image.sprite;

        private static float ReadRightTrigger(UnityEngine.XR.InputDevice device)
        {
            if (!device.isValid) return 0f;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float trigger))
                return trigger;
            return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool select) && select ? 1f : 0f;
        }

        private void SetWristQuickMenuActive(bool active)
        {
            if (_wristMenuRoot != null && _wristMenuRoot.activeSelf != active)
                _wristMenuRoot.SetActive(active);
            if (!active) OpenXRControllersPlugin.SetWristQuickMenuCapture(false);
        }

        private void ResetWristQuickMenuInteraction()
        {
            OpenXRControllersPlugin.SetWristQuickMenuCapture(false);
            _wristMenuPointerLocal = Vector2.zero;
            _wristMenuTriggerArmed = false;
            SetWristQuickMenuHover(-1);
            if (_wristMenuPointer != null) _wristMenuPointer.gameObject.SetActive(false);
        }

        private void HideWristQuickMenu()
        {
            _wristMenuVisible = false;
            _wristMenuPendingVisible = false;
            _wristMenuPoseChangeTime = -1f;
            ResetWristQuickMenuInteraction();
            SetWristQuickMenuActive(false);
        }

        private void DestroyWristQuickMenu()
        {
            if (_wristStatusRoot != null && _wristMenuRoot != null &&
                _wristStatusRoot.transform.IsChildOf(_wristMenuRoot.transform))
                DestroyWristStatusHud();
            HideWristQuickMenu();
            if (_wristMenuRoot != null) Destroy(_wristMenuRoot);
            _wristMenuRoot = null;
            _wristMenuRect = null;
            _wristMenuHud = null;
            _wristMenuPointer = null;
            _wristMenuAudio = null;
            for (var index = 0; index < 4; index++)
            {
                _wristMenuFrames[index] = null;
                _wristMenuIcons[index] = null;
                _wristMenuSourceIcons[index] = null;
            }
        }
    }
}
