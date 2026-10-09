using UnityEngine;

namespace CulticVR.UiTrial
{
    public sealed partial class UiTrialPlugin
    {
        private Camera? _menuClearCamera;
        private CameraClearFlags _oldMenuClearFlags;
        private Color _oldMenuBackgroundColor;

        private void ApplyMenuBackground(Camera camera)
        {
            // UUVR's overlay normally clears depth only. In the native menu all
            // scene cameras draw into our UI texture, so nothing clears the XR
            // color buffers after leaving gameplay. Reuse the existing overlay
            // to clear black, only for this presentation, once per camera.
            if (!_nativeMenuPresentation) { RestoreMenuBackground(); return; }
            if (_menuClearCamera == camera) return;
            RestoreMenuBackground();
            _menuClearCamera = camera;
            _oldMenuClearFlags = camera.clearFlags;
            _oldMenuBackgroundColor = camera.backgroundColor;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }

        private void RestoreMenuBackground()
        {
            if (_menuClearCamera != null)
            {
                _menuClearCamera.clearFlags = _oldMenuClearFlags;
                _menuClearCamera.backgroundColor = _oldMenuBackgroundColor;
            }
            _menuClearCamera = null;
        }
    }
}
