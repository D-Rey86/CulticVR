using System;
using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    public static class ScopeSightingMath
    {
        // Solve A * eye + translation = 0. Do not assume parallel eyes,
        // hard-code IPD, or approximate the inverse by transposing a scaled view.
        public static bool TryGetEyeOrigin(Matrix4x4 view, out Vector3 eye)
        {
            eye = Vector3.zero;
            for (var i = 0; i < 16; i++) if (!Finite(view[i])) return false;
            if (view.m30 != 0f || view.m31 != 0f || view.m32 != 0f || view.m33 != 1f) return false;
            double a = view.m00, b = view.m01, c = view.m02;
            double d = view.m10, e = view.m11, f = view.m12;
            double g = view.m20, h = view.m21, i2 = view.m22;
            var det = a * (e * i2 - f * h) - b * (d * i2 - f * g) + c * (d * h - e * g);
            if (Math.Abs(det) < 1e-12) return false;
            double x = -view.m03, y = -view.m13, z = -view.m23;
            eye = new Vector3(
                (float)(((e * i2 - f * h) * x + (c * h - b * i2) * y + (b * f - c * e) * z) / det),
                (float)(((f * g - d * i2) * x + (a * i2 - c * g) * y + (c * d - a * f) * z) / det),
                (float)(((d * h - e * g) * x + (b * g - a * h) * y + (a * e - b * d) * z) / det));
            return Finite(eye);
        }

        public static bool TryGetDirection(Vector3 eye, Vector3 crosshair, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!Finite(eye) || !Finite(crosshair)) return false;
            var delta = crosshair - eye;
            double lengthSquared = (double)delta.x * delta.x + (double)delta.y * delta.y + (double)delta.z * delta.z;
            if (lengthSquared < 1e-8) return false;
            direction = delta * (float)(1.0 / Math.Sqrt(lengthSquared));
            return Finite(direction);
        }

        // Measured horizontal crossbar pixel centers in the original 480x220
        // sprites, NOT the trimmed Sprite.image export. X is 240 in every frame.
        public static bool TryGetFg42Crosshair(string spriteName, out Vector2 pixel)
        {
            float y;
            switch (spriteName)
            {
                case "sprWeaponFG42_32": y = 110.5f; break;
                case "sprWeaponFG42_33": y = 97.5f; break;
                case "sprWeaponFG42_34": y = 95.5f; break;
                case "sprWeaponFG42_35": y = 96.5f; break;
                case "sprWeaponFG42_36": y = 101.5f; break;
                default: pixel = Vector2.zero; return false;
            }
            pixel = new Vector2(240f, y);
            return true;
        }

        // Original 480x220 Lee-Enfield pixels: the thin horizontal crossbar
        // and center post are offset from the image center. Officer recoil
        // frames differ by 1-4 pixels; do not alias them to the standard skin.
        public static bool TryGetRifleCrosshair(string spriteName, out Vector2 pixel)
        {
            switch (spriteName)
            {
                case "sprWeaponLeeEnfield_39":
                case "sprWeaponLeeEnfieldOfficer_39": pixel = new Vector2(199.5f, 110.5f); return true;
                case "sprWeaponLeeEnfield_40": pixel = new Vector2(200.5f, 83.5f); return true;
                case "sprWeaponLeeEnfield_41": pixel = new Vector2(199.5f, 97.5f); return true;
                case "sprWeaponLeeEnfield_42": pixel = new Vector2(199.5f, 112.5f); return true;
                case "sprWeaponLeeEnfieldOfficer_40": pixel = new Vector2(200.5f, 82.5f); return true;
                case "sprWeaponLeeEnfieldOfficer_41": pixel = new Vector2(200.5f, 93.5f); return true;
                case "sprWeaponLeeEnfieldOfficer_42": pixel = new Vector2(199.5f, 111.5f); return true;
                default: pixel = Vector2.zero; return false;
            }
        }

        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
