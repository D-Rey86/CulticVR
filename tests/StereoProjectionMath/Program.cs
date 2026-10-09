using System;
using CulticVR.WorldStereoTrial;
using UnityEngine;

// Uses the shipping Unity value type and the exact production math implementation.
// These tests cannot prove native Camera callback order or headset appearance.
var left = new Matrix4x4();
left.m00 = 0.894154f;
left.m11 = 0.765410f;
left.m02 = -0.253014f;
left.m22 = -1.000020f;
left.m23 = -0.100001f;
left.m32 = -1f;
var right = left;
right.m02 = 0.253014f;
var scale = (float)(Math.Tan(Math.PI / 4) / Math.Tan(49.5 * Math.PI / 360));
Require(StereoProjectionMath.TryScale(left, scale, out var scaledLeft), "left scales");
Require(StereoProjectionMath.TryScale(right, scale, out var scaledRight), "right scales");
Require(Math.Abs(scaledLeft.m00 - 1.939568f) < 0.00001f, "captured left magnification reproduced");
Require(scaledLeft.m00 == scaledRight.m00 && scaledLeft.m11 == scaledRight.m11, "symmetric magnification");
for (var index = 0; index < 16; index++)
{
    if (index == 0 || index == 5) continue;
    Require(scaledLeft[index] == left[index] && scaledRight[index] == right[index], "all unscaled terms preserved");
}
for (var frame = 0; frame < 10000; frame++)
{
    Require(StereoProjectionMath.TryScale(right, scale, out var result), "finite long hold");
    Require(result.m00 == scaledRight.m00, "no accumulation across frames or eyes");
}
Require(StereoProjectionMath.TryScale(right, 1f, out var restored), "scope out");
for (var index = 0; index < 16; index++) Require(restored[index] == right[index], "exact restoration");
Require(StereoProjectionMath.TryScale(right, 4.5f, out _), "high zoom");
var invalid = right;
invalid.m00 = float.PositiveInfinity;
Require(!StereoProjectionMath.TryScale(invalid, scale, out _), "reject captured overflow");
invalid = right;
invalid.m23 = float.NaN;
Require(!StereoProjectionMath.TryScale(invalid, scale, out _), "reject invalid depth");
Require(!StereoProjectionMath.TryScale(right, float.NaN, out _), "reject invalid scale");
Require(!StereoProjectionMath.TryScale(scaledRight, float.MaxValue, out _), "reject output overflow");
Console.WriteLine("PASS: captured asymmetry, magnification, 10,000 frames, restoration, high zoom, non-finite rejection. Native callbacks and visual correctness unverified.");

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
