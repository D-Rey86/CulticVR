using UnityEngine;

namespace CulticVR.AimPreview
{
    internal static class MolotovCloth
    {
        // Sprite21780 alpha-boundary, crop coordinates (top-left origin).
        // Cloth is the left hanging component, rows13..55. The upper cut at
        // x32 excludes the bottle neck; the transparent tail notch is retained.
        // Collinear pixel edges removed offline, not simplified across corners.
        internal const float CropX=254.0429077f, CropTop=83f;
        internal static readonly Vector2[] Outline={
            new Vector2(2,46),new Vector2(3,46),new Vector2(3,43),new Vector2(4,43),
            new Vector2(4,41),new Vector2(5,41),new Vector2(5,38),new Vector2(6,38),
            new Vector2(6,36),new Vector2(7,36),new Vector2(7,33),new Vector2(8,33),
            new Vector2(8,31),new Vector2(9,31),new Vector2(9,28),new Vector2(10,28),
            new Vector2(10,25),new Vector2(11,25),new Vector2(11,20),new Vector2(12,20),
            new Vector2(12,14),new Vector2(13,14),new Vector2(13,13),new Vector2(32,13),
            new Vector2(32,20),new Vector2(31,20),new Vector2(31,25),new Vector2(30,25),
            new Vector2(30,27),new Vector2(29,27),new Vector2(29,28),new Vector2(30,28),
            new Vector2(30,32),new Vector2(31,32),new Vector2(31,35),new Vector2(32,35),
            new Vector2(32,38),new Vector2(33,38),new Vector2(33,41),new Vector2(32,41),
            new Vector2(32,46),new Vector2(31,46),new Vector2(31,50),new Vector2(30,50),
            new Vector2(30,55),new Vector2(29,55),new Vector2(29,56),new Vector2(28,56),
            new Vector2(28,55),new Vector2(25,55),new Vector2(25,54),new Vector2(23,54),
            new Vector2(23,53),new Vector2(21,53),new Vector2(21,54),new Vector2(13,54),
            new Vector2(13,53),new Vector2(10,53),new Vector2(10,52),new Vector2(7,52),
            new Vector2(7,51),new Vector2(5,51),new Vector2(5,50),new Vector2(4,50),
            new Vector2(4,49),new Vector2(3,49),new Vector2(3,48),new Vector2(2,48)
        };
    }
}
