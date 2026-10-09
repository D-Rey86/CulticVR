using System;
using UnityEngine;

namespace CulticVR.OpenXRControllers
{
    public enum MenuDirection { None, Left, Right, Up, Down }

    // Value-only input state machine. Native menu actions own repetition and
    // preference changes. No timers, Unity object lookup or action rebinding.
    public struct OptionsStickRouter
    {
        public const float EngageThreshold = 0.5f;
        public const float ReleaseThreshold = 0.25f;
        private int _context;
        private bool _waitForNeutral;
        private MenuDirection _direction;

        public Vector2 Route(Vector2 raw, int context, out MenuDirection direction)
        {
            direction = MenuDirection.None;
            if (!Finite(raw.x) || !Finite(raw.y))
            {
                _direction = MenuDirection.None;
                _waitForNeutral = true;
                return Vector2.zero;
            }
            var neutral = Math.Max(Math.Abs(raw.x), Math.Abs(raw.y)) <= ReleaseThreshold;
            if (_context != context)
            {
                _context = context;
                _direction = MenuDirection.None;
                // Do not carry a held menu intent into another menu/gameplay,
                // nor turn the stick used to enter options into an adjustment.
                _waitForNeutral = !neutral;
            }
            if (_waitForNeutral)
            {
                if (!neutral) return Vector2.zero;
                _waitForNeutral = false;
            }
            if (context == 0) return raw; // Exact analog passthrough outside options.
            if (neutral) _direction = MenuDirection.None;
            else if (_direction == MenuDirection.None &&
                Math.Max(Math.Abs(raw.x), Math.Abs(raw.y)) >= EngageThreshold)
            {
                // The larger axis wins; exact diagonals choose navigation.
                // Lock the direction until the stick returns to centre.
                _direction = Math.Abs(raw.x) > Math.Abs(raw.y)
                    ? (raw.x > 0f ? MenuDirection.Right : MenuDirection.Left)
                    : (raw.y > 0f ? MenuDirection.Up : MenuDirection.Down);
            }
            // Release the digital button as soon as its own axis relaxes.
            // Keep the lock until BOTH axes are neutral, so drifting sideways
            // during a vertical deflection cannot begin an adjustment.
            switch (_direction)
            {
                case MenuDirection.Left: if (raw.x < -ReleaseThreshold) direction = _direction; break;
                case MenuDirection.Right: if (raw.x > ReleaseThreshold) direction = _direction; break;
                case MenuDirection.Up: if (raw.y > ReleaseThreshold) direction = _direction; break;
                case MenuDirection.Down: if (raw.y < -ReleaseThreshold) direction = _direction; break;
            }
            // No duplicate normalized left-stick Menu Move alongside D-pad.
            return Vector2.zero;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
