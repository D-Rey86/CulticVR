using BepInEx.Configuration;
using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    public sealed partial class WorldStereoTrialPlugin
    {
        private ConfigEntry<bool> _skipHiddenWorldGeometry = null!;
        private HiddenWorldGeometryGate? _sourceGeometryGate;
        private HiddenWorldGeometryGate? _skyGeometryGate;

        private void InitializeHiddenWorldGeometry()
        {
            _skipHiddenWorldGeometry = Config.Bind("Performance", "SkipHiddenWorldGeometry", false,
                "Experimental: reduce hidden flat-world rendering during tracked gameplay. Keep off until headset validation.");
        }

        private void AttachHiddenWorldGeometry()
        {
            if (_source != null)
            {
                _sourceGeometryGate = _source.GetComponent<HiddenWorldGeometryGate>() ??
                    _source.gameObject.AddComponent<HiddenWorldGeometryGate>();
                _sourceGeometryGate.Initialize(this, _source);
            }
            if (_skySource != null)
            {
                _skyGeometryGate = _skySource.GetComponent<HiddenWorldGeometryGate>() ??
                    _skySource.gameObject.AddComponent<HiddenWorldGeometryGate>();
                _skyGeometryGate.Initialize(this, _skySource);
            }
        }

        internal bool CanSkipHiddenWorldGeometry(Camera camera)
        {
            if (!_skipHiddenWorldGeometry.Value || !_reportedReady || _source == null || _stereo == null ||
                !_stereo.isActiveAndEnabled || _worldVrChild == null || _worldImage == null || _worldImage.enabled ||
                _player == null || _player.state != 0 || _player.playerHUD == null ||
                _player.playerHUD.cutsceneState != 0 || scrGameControl.Instance == null ||
                scrGameControl.Instance.gameState != 0) return false;
            if (_skySource != null && (_skyStereo == null || !_skyStereo.isActiveAndEnabled || _skyVrChild == null))
                return false;
            // Every shipping manual Camera.Render call switches to a thumbnail
            // target first. Keep those renders, resized targets and fallback
            // presentation native. Do not disable the Camera: Camera.main must
            // remain available to gameplay, screen resizing and saves.
            var target = _source.targetTexture;
            return target != null && _worldImage.texture == target && camera.targetTexture == target &&
                (camera == _source || camera == _skySource);
        }

        private void DetachHiddenWorldGeometry()
        {
            _sourceGeometryGate?.Detach();
            _skyGeometryGate?.Detach();
            _sourceGeometryGate = null;
            _skyGeometryGate = null;
        }
    }

    // The original camera still exists and runs its image effects. Only its
    // hidden geometry pass is suppressed, within the built-in render callback.
    // Retain an inert component on restore for safe same-frame recapture.
    internal sealed class HiddenWorldGeometryGate : MonoBehaviour
    {
        private WorldStereoTrialPlugin? _owner;
        private Camera? _camera;
        private int _mask;
        private bool _suppressed;

        internal void Initialize(WorldStereoTrialPlugin owner, Camera camera)
        {
            Detach();
            _owner = owner;
            _camera = camera;
        }

        internal void Detach()
        {
            RestoreMask();
            _owner = null;
        }

        private void OnPreCull()
        {
            RestoreMask();
            if (_owner == null || _camera == null || !_owner.CanSkipHiddenWorldGeometry(_camera)) return;
            _mask = _camera.cullingMask;
            _camera.cullingMask = 0;
            _suppressed = true;
        }

        private void OnPostRender() => RestoreMask();
        private void OnDisable() => RestoreMask();
        private void OnDestroy() => RestoreMask();

        private void RestoreMask()
        {
            if (!_suppressed) return;
            // Preserve a different mask written by another render callback.
            if (_camera != null && _camera.cullingMask == 0) _camera.cullingMask = _mask;
            _suppressed = false;
        }
    }
}
