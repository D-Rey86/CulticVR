using System;
using System.Collections.Generic;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.UI;

namespace CulticVR.AimPreview
{
    public sealed partial class AimPreviewPlugin
    {
        private RectTransform? _muzzleRect;
        private Image? _muzzleImage;
        private Sprite? _muzzleSprite;
        private WeaponLandmark _muzzleLandmark;
        private bool _muzzleKnown;

        // Annotation coordinates use the ORIGINAL 480x220 sprite, not its
        // trimmed atlas rectangle. Never invent a hotspot for unknown artwork.
        public static bool TryGetWeaponSpawnPoint(scrPlayerControl player, Vector3 rawOrigin,
            Quaternion rawRotation, bool casing, out Vector3 point)
        {
            point = rawOrigin;
            var self = Instance;
            var camera = player.viewmodelCameraComponent;
            var weapon = player.playerWeapon;
            if (self == null || camera == null || !camera.orthographic || weapon == null ||
                self._viewmodelCamera != camera || self._viewmodelTexture == null ||
                self._kickOnUiQuad) return false;
            if (self._muzzleRect != weapon || self._muzzleImage == null)
            {
                self._muzzleRect = weapon;
                self._muzzleImage = weapon.GetComponent<Image>();
                self._muzzleSprite = null;
            }
            var image = self._muzzleImage;
            if (image == null || !image.isActiveAndEnabled) return false;
            var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            if (sprite == null) return false;
            if (self._muzzleSprite != sprite)
            {
                self._muzzleSprite = sprite;
                self._muzzleKnown = WeaponLandmarks.TryGet(sprite.name, out self._muzzleLandmark);
            }
            if (!self._muzzleKnown || (casing && !self._muzzleLandmark.HasCasing) ||
                image.type != Image.Type.Simple || image.preserveAspect || image.useSpriteMesh ||
                sprite.rect.width != 480f || sprite.rect.height != 220f) return false;
            var pixel = casing ? self._muzzleLandmark.Casing : self._muzzleLandmark.Muzzle;
            var rect = image.GetPixelAdjustedRect();
            var native = weapon.TransformPoint(new Vector3(rect.x + rect.width * pixel.x / 480f,
                rect.y + rect.height * pixel.y / 220f, 0f));
            var uv = camera.WorldToViewportPoint(native);
            if (!ScopeSightingMath.Finite(uv) || uv.z <= 0f) return false;
            ApplyCalibration(ref rawOrigin, ref rawRotation, self._rightPositionOffset, self._rightRotationOffset);
            var baseHeight = camera.orthographicSize * 2f * ViewmodelWorldScale;
            var height = baseHeight * VrSettings.WeaponHeight;
            var texture = self._viewmodelTexture;
            point = rawOrigin + rawRotation * new Vector3(
                WeaponSpriteMirror.HorizontalScale(baseHeight * texture.width / texture.height * VrSettings.WeaponWidth,VrSettings.LeftWeapon) * (uv.x - 0.5f), height * uv.y, 0f);
            return ScopeSightingMath.Finite(point);
        }
    }

    internal readonly struct WeaponLandmark
    {
        internal readonly Vector2 Muzzle;
        internal readonly Vector2 Casing;
        internal readonly bool HasCasing;
        internal WeaponLandmark(float x, float y, float cx, float cy)
        {
            Muzzle = new Vector2(x, y); Casing = new Vector2(cx, cy); HasCasing = cx >= 0f;
        }
    }

    internal static class WeaponLandmarks
    {
        // Lip/receiver annotations from shipping sharedassets0 sprites. Native
        // firing-event frames and their idle/recoil neighbours are explicit;
        // scoped FG42's optical exit is handled by the accepted eye-ray owner.
        private static readonly Dictionary<string, WeaponLandmark> Points = Build();
        internal static bool TryGet(string name, out WeaponLandmark point) => Points.TryGetValue(name, out point);
        private static Dictionary<string, WeaponLandmark> Build()
        {
            var result = new Dictionary<string, WeaponLandmark>(StringComparer.Ordinal);
            void Add(string family, float x, float y, float cx, float cy, params int[] frames)
            {
                foreach (var frame in frames) result.Add(family + "_" + frame, new WeaponLandmark(x, y, cx, cy));
            }
            Add("sprWeaponC96", 307, 95, 343, 76, 0);
            Add("sprWeaponC96", 315, 94, 346, 78, 1);
            Add("sprWeaponC96", 326, 134, 365, 97, 2);
            Add("sprWeaponC96", 316, 123, 347, 87, 3);
            Add("sprWeaponC96", 301, 77, 332, 70, 4);
            Add("sprWeaponLAR", 297, 64, 337, 32, 0, 5);
            Add("sprWeaponLAR", 304, 64, 330, 29, 1);
            Add("sprWeaponLAR", 357, 73, 371, 30, 2, 3, 4);
            Add("sprWeaponLAR", 296, 60, 322, 29, 27);
            Add("sprWeaponLAR", 303, 60, 324, 27, 28);
            Add("sprWeaponLAR", 355, 151, 365, 102, 29, 30);
            Add("sprWeaponLAR", 300, 67, 335, 32, 50);
            Add("sprWeaponLAR", 271, 50, 302, 20, 52);
            Add("sprWeaponLAR", 253, 39, 284, 18, 53);
            Add("sprWeaponShotgun", 294, 76, 331, 28, 0, 6, 12);
            Add("sprWeaponShotgun", 296, 77, 331, 28, 1, 7, 13);
            Add("sprWeaponShotgun", 350, 93, 369, 34, 2);
            Add("sprWeaponShotgun", 348, 94, 368, 34, 8);
            Add("sprWeaponShotgun", 370, 48, 389, 17, 14);
            Add("sprWeaponShotgun", 315, 42, 315, 42, 25);
            Add("sprWeaponShotgun", 285, 44, 285, 44, 45);
            // STEN's long left projection is its SIDE MAGAZINE. The muzzle is
            // above the magazine well, at the authored firing flash/front sight.
            Add("sprWeaponSTEN", 310, 90, 308, 71, 0, 1, 2);
            Add("sprWeaponSTEN", 316, 91, 310, 73, 3);
            Add("sprWeaponSTEN", 315, 82, 314, 68, 4);
            Add("sprWeaponSTEN", 304, 68, 117, 49, 23);
            Add("sprWeaponChinaLake", 305, 74, -1, -1, 0, 1, 17, 47, 48);
            Add("sprWeaponChinaLake", 359, 58, -1, -1, 2, 49);
            Add("sprWeaponChinaLake", 330, 76, -1, -1, 3, 50);
            Add("sprWeaponChinaLake", 312, 80, -1, -1, 4, 51, 52);
            Add("sprWeaponFG42", 281, 76, 346, 49, 0, 1);
            Add("sprWeaponFG42", 314, 81, 366, 39, 2, 3);
            Add("sprWeaponFG42", 287, 73, 346, 42, 4);
            Add("sprWeaponIncinerator", 313, 95, -1, -1, 0, 3, 4, 5, 6);
            Add("sprWeaponIncinerator", 320, 81, -1, -1, 1, 2, 34);
            Add("sprWeaponIncinerator", 314, 100, -1, -1, 8);
            Add("sprWeaponIncinerator", 336, 87, -1, -1, 9);
            Add("sprWeaponIncinerator", 320, 81, -1, -1, 10);
            Add("sprWeaponIncinerator", 307, 88, -1, -1, 11, 12);
            Add("sprWeaponIncinerator", 307, 90, -1, -1, 13, 14, 15, 16);
            Add("sprWeaponIncinerator", 306, 91, -1, -1, 17, 18, 19, 20, 21, 22, 23, 24, 25);
            Add("sprWeaponIncinerator", 310, 92, -1, -1, 26, 27, 28, 29, 30, 31, 32, 33);
            Add("sprWeaponIncinerator", 345, 70, -1, -1, 35);
            Add("sprWeaponIncinerator", 325, 87, -1, -1, 36);
            Add("sprWeaponIncinerator", 315, 95, -1, -1, 37);
            Add("sprBazooka", 86, 128, -1, -1, 0);
            Add("sprBazooka", 149, 140, -1, -1, 1);
            Add("sprBazooka", 165, 110, -1, -1, 2);
            Add("sprBazooka", 201, 80, -1, -1, 3);
            Add("sprBazooka", 185, 85, -1, -1, 4);
            Add("sprBazooka", 171, 100, -1, -1, 5);
            Add("sprBazooka", 135, 56, -1, -1, 6);
            Add("sprBazooka", 100, 40, -1, -1, 7);
            Add("sprBazooka", 75, 9, -1, -1, 8);
            Add("sprWeaponM1917A1", 295, 103, 355, 51, 16);
            Add("sprWeaponM1917A1", 300, 110, 370, 46, 17);
            Add("sprWeaponM1917A1", 289, 117, 357, 44, 18);
            Add("sprWeaponM1917A1", 292, 104, 355, 44, 19);
            Add("sprWeaponM1917A1", 294, 99, 353, 47, 20, 21, 22);
            Add("sprLP42V2", 308, 81, -1, -1, 0, 1);
            Add("sprLP42V2", 386, 151, -1, -1, 2);
            Add("sprLP42V2", 378, 156, -1, -1, 3);
            Add("sprLP42V2", 334, 111, -1, -1, 4);
            Add("sprLP42V2", 318, 59, -1, -1, 5, 6);
            return result;
        }
    }
}
