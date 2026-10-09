using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CulticVR.WorldStereoTrial;
using UnityEngine;

// Pure numerical replay using the exact production helper and shipping value
// types. Does not execute Unity's native render/culling callbacks.
var root = args.Length > 0 ? args[0] : "deployment/backups/enhanced-scope-observations-20261004-1340";
var paths = Directory.GetFiles(root, "state.txt", SearchOption.AllDirectories);
Require(paths.Length == 5, "five captured states required");
var normal = (float)(1 / Math.Tan(49.5 * Math.PI / 360));
var replays = 0;
foreach (var path in paths)
{
    var text = File.ReadAllText(path);
    var label = Regex.Match(text, @"label=(\S+)").Groups[1].Value;
    var cameras = ReadCameras(text);
    foreach (var cameraName in new[] { "CulticVR Stereo World Trial", "CulticVR Stereo Sky Trial" })
    {
        var parent = cameras.Find(c => c.Phase == "setup" && c.Path.EndsWith(cameraName))!;
        var child = cameras.Find(c => c.Phase == "setup" && c.Path.EndsWith(cameraName + "/VrCameraOffset/VrChildCamera"))!;
        Require(parent != null && child != null, "camera pair present");
        var renderedLeft = cameras.Find(c => c.Phase == "render" && c.Path.EndsWith(cameraName) && c.Views.ContainsKey("Left"))!;
        var renderedChildLeft = cameras.Find(c => c.Phase == "render" && c.Path.EndsWith(cameraName + "/VrCameraOffset/VrChildCamera") && c.Views.ContainsKey("Left"))!;
        var renderedChildRight = cameras.Find(c => c.Phase == "render" && c.Path.EndsWith(cameraName + "/VrCameraOffset/VrChildCamera") && c.Views.ContainsKey("Right"))!;
        // Setup precedes onBeforeRender tracking. Pair every runtime view with
        // its actual owning transform from the SAME render phase.
        var parentInverse = Pose(renderedLeft.Position, renderedLeft.Rotation, true);
        Require(ScopeStereoMath.TryGetLocalView(renderedChildLeft.Views["Left"], Pose(renderedChildLeft.Position, renderedChildLeft.Rotation, false), out var left), "local left derived");
        Require(ScopeStereoMath.TryGetLocalView(renderedChildRight.Views["Right"], Pose(renderedChildRight.Position, renderedChildRight.Rotation, false), out var right), "local right derived");
        foreach (var eye in new[] { "Left", "Right" })
        {
            var rendered = cameras.Find(c => c.Phase == "render" && c.Path.EndsWith(cameraName) && c.Views.ContainsKey(eye))!;
            var derived = (eye == "Left" ? left : right) * Pose(rendered.Position, rendered.Rotation, true);
            // Captures round sky translation at world position 1000 to four
            // decimal places. This is a receipt precision limit, not a runtime
            // transform approximation: production reads full native matrices.
            if (!Near(derived, rendered.Views[eye], cameraName.Contains("Sky") ? .004f : .00012f))
                Console.WriteLine($"Ownership mismatch {label}/{cameraName}/{eye}: derived={derived.ToString("F6")} recorded={rendered.Views[eye].ToString("F6")}");
            Require(Near(derived, rendered.Views[eye], cameraName.Contains("Sky") ? .004f : .00012f),
                label + "/" + cameraName + "/" + eye + " native parent view reconstructed from clean child");
        }
        var magnification = parent.Projections["Left"].m00 / child.Projections["Left"].m00;
        var scale = ScopeStereoMath.SeparationScale(magnification, normal);
        if (label != "enhanced-scope") Require(scale == 1, "normal/released path exactly unmodified");
        foreach (var sightLeft in new[] { false, true })
        {
            Require(ScopeStereoMath.TryLimitSeparation(left, right, scale, sightLeft, out var limitedLeft, out var limitedRight), "measured stereo pair limited");
            Require(Exact(sightLeft ? limitedLeft : limitedRight, sightLeft ? left : right), "sighting local view bit-identical");
            Require(ScopeSightingMath.TryGetEyeOrigin(left, out var leftOrigin), "original left origin valid");
            Require(ScopeSightingMath.TryGetEyeOrigin(right, out var rightOrigin), "original right origin valid");
            Require(ScopeSightingMath.TryGetEyeOrigin(limitedLeft, out var limitedLeftOrigin), "limited left origin valid");
            Require(ScopeSightingMath.TryGetEyeOrigin(limitedRight, out var limitedRightOrigin), "limited right origin valid");
            Require(Math.Abs(Length(limitedRightOrigin - limitedLeftOrigin) - scale * Length(rightOrigin - leftOrigin)) < .000001f, "baseline scaled exactly, nonzero");
            if (cameraName.Contains("World") && parent.Crosshair.HasValue)
            {
                var q = parent.Crosshair.Value;
                var lp = parent.Projections["Left"];
                var rp = parent.Projections["Right"];
                var viewLeft = limitedLeft * parentInverse;
                var viewRight = limitedRight * parentInverse;
                var optical = -lp.m02 + rp.m02;
                var disparity = Project(lp, viewLeft, q).x - Project(rp, viewRight, q).x - optical;
                var lpNormal = lp; var rpNormal = rp;
                lpNormal.m00 *= normal / magnification; lpNormal.m11 *= normal / magnification;
                rpNormal.m00 *= normal / magnification; rpNormal.m11 *= normal / magnification;
                var expected = Project(lpNormal, left * parentInverse, q).x - Project(rpNormal, right * parentInverse, q).x - optical;
                if (label == "enhanced-scope") Require(Math.Abs(disparity - expected) < .00007f, "enhanced crosshair disparity matches normal at same pose");
                var sightView = sightLeft ? viewLeft : viewRight;
                var sightP = sightLeft ? lp : rp;
                Require(ScopeSightingMath.TryGetEyeOrigin(sightView, out var sightOrigin), "sighting origin valid");
                Require(ScopeSightingMath.TryGetDirection(sightOrigin, q, out var direction), "sighting ray valid");
                foreach (var range in new[] { 1f, 10f, 100f, 500f })
                {
                    var close = Project(sightP, sightView, q);
                    var distant = Project(sightP, sightView, q + direction * range);
                    Require(Math.Abs(close.x - distant.x) < .0002f && Math.Abs(close.y - distant.y) < .0002f,
                        "both sighting-eye choices retain crosshair/shot alignment");
                }
                Console.WriteLine($"{label}/{(sightLeft ? "Left" : "Right")}: separationScale={scale:F8}, near disparity={disparity:F6}, normal reference={expected:F6}");
            }
            replays++;
        }
    }
}
Require(replays == 20, "five states x world/sky x both sighting choices");
Require(ScopeStereoMath.SeparationScale(float.NaN, normal) == 1 && ScopeStereoMath.SeparationScale(5, float.PositiveInfinity) == 1, "invalid scales fail closed");
var fixtureLeft = Matrix4x4.identity; fixtureLeft.m22 = -1; fixtureLeft.m03 = .031f;
var fixtureRight = fixtureLeft; fixtureRight.m03 = -.031f;
for (var n = 0; n < 10000; n++)
{
    // Asymmetric, translated, rotated/canted views; no parallel-eye assumption
    // enters the helper. Only translation may change, never an eye's basis.
    var cant = n % 2 == 0 ? .11 : -.17;
    var eyeView = fixtureLeft;
    eyeView.m00 = (float)Math.Cos(cant); eyeView.m02 = (float)Math.Sin(cant);
    eyeView.m20 = (float)Math.Sin(cant); eyeView.m22 = -(float)Math.Cos(cant);
    var factor = .2f + (n % 100) / 125f;
    Require(ScopeStereoMath.TryLimitSeparation(eyeView, fixtureRight, factor, false, out var result, out var unchanged), "canted pair valid");
    for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++) Require(result[row, col] == eyeView[row, col], "full eye basis preserved");
    Require(Exact(unchanged, fixtureRight), "sighting view preserved exactly");
    Require(ScopeSightingMath.TryGetEyeOrigin(eyeView, out var beforeEye), "canted origin invertible");
    Require(ScopeSightingMath.TryGetEyeOrigin(fixtureRight, out var anchor), "anchor origin invertible");
    Require(ScopeSightingMath.TryGetEyeOrigin(result, out var afterEye), "result origin invertible");
    Require(Length(afterEye - (anchor + (beforeEye - anchor) * factor)) < .000001f, "canted/offset origin follows exact baseline interpolation");
}
Require(!ScopeStereoMath.TryLimitSeparation(new Matrix4x4(), fixtureRight, .5f, false, out _, out _), "singular input rejected");
Require(!ScopeStereoMath.TryLimitSeparation(fixtureLeft, fixtureRight, 0, false, out _, out _), "mono scale rejected");
Require(!ScopeStereoMath.TryLimitSeparation(fixtureLeft, fixtureRight, float.NaN, false, out _, out _), "invalid scale rejected");
Console.WriteLine("PASS: 20 captured camera/eye choices, parent-view ownership, exact sighting-view preservation, high-zoom disparity normalization, shot-line replay, 10,000 canted/asymmetric cases and invalid-data rejection. Native render/culling/headset behavior unverified.");

static List<CameraRecord> ReadCameras(string text)
{
    var result = new List<CameraRecord>();
    foreach (var chunk in Regex.Split(text, @"(?=^camera phase=)", RegexOptions.Multiline))
    {
        var h = Regex.Match(chunk, @"camera phase=(\w+) path=(.*?) enabled=.*?position=\((.*?)\) rotation=\((.*?)\)");
        if (!h.Success) continue;
        var pos = Values(h.Groups[3].Value); var rot = Values(h.Groups[4].Value);
        var record = new CameraRecord { Phase = h.Groups[1].Value, Path = h.Groups[2].Value,
            Position = new Vector3(pos[0], pos[1], pos[2]), Rotation = new Quaternion(rot[0], rot[1], rot[2], rot[3]) };
        foreach (Match eye in Regex.Matches(chunk, @"^  (Left|Right) projection=(.*?)\n view=(.*?)\n\s*\n", RegexOptions.Multiline | RegexOptions.Singleline))
        {
            record.Projections[eye.Groups[1].Value] = ParseMatrix(eye.Groups[2].Value);
            record.Views[eye.Groups[1].Value] = ParseMatrix(eye.Groups[3].Value);
        }
        var crosshair = Regex.Match(chunk, @"crosshairWorld=\((.*?)\)");
        if (crosshair.Success) { var p = Values(crosshair.Groups[1].Value); record.Crosshair = new Vector3(p[0], p[1], p[2]); }
        result.Add(record);
    }
    return result;
}
static float[] Values(string text) => Array.ConvertAll(text.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), x => float.Parse(x, CultureInfo.InvariantCulture));
static Matrix4x4 ParseMatrix(string text)
{
    var values = Values(text); Require(values.Length == 16, "matrix shape");
    var matrix = new Matrix4x4();
    for (var row = 0; row < 4; row++) for (var col = 0; col < 4; col++) matrix[row, col] = values[row * 4 + col];
    return matrix;
}
static Matrix4x4 Pose(Vector3 p, Quaternion q, bool inverse)
{
    // Explicit formula avoids native Matrix4x4.TRS in this offline executable.
    var m = Matrix4x4.identity;
    m.m00 = 1 - 2 * (q.y*q.y + q.z*q.z); m.m01 = 2*(q.x*q.y - q.z*q.w); m.m02 = 2*(q.x*q.z + q.y*q.w);
    m.m10 = 2*(q.x*q.y + q.z*q.w); m.m11 = 1 - 2*(q.x*q.x + q.z*q.z); m.m12 = 2*(q.y*q.z - q.x*q.w);
    m.m20 = 2*(q.x*q.z - q.y*q.w); m.m21 = 2*(q.y*q.z + q.x*q.w); m.m22 = 1 - 2*(q.x*q.x + q.y*q.y);
    if (!inverse) { m.m03 = p.x; m.m13 = p.y; m.m23 = p.z; return m; }
    var t = m; for (var row=0;row<3;row++) for(var col=0;col<3;col++) t[row,col] = m[col,row];
    t.m03 = -(t.m00*p.x+t.m01*p.y+t.m02*p.z); t.m13 = -(t.m10*p.x+t.m11*p.y+t.m12*p.z); t.m23 = -(t.m20*p.x+t.m21*p.y+t.m22*p.z);
    return t;
}
static float Length(Vector3 p) => (float)Math.Sqrt((double)p.x*p.x+(double)p.y*p.y+(double)p.z*p.z);
static Vector2 Project(Matrix4x4 p, Matrix4x4 v, Vector3 q)
{
    var clip = p * (v * new Vector4(q.x,q.y,q.z,1)); return new Vector2(clip.x/clip.w,clip.y/clip.w);
}
static bool Near(Matrix4x4 a, Matrix4x4 b, float tolerance) { for(var n=0;n<16;n++) if(Math.Abs(a[n]-b[n])>tolerance)return false;return true; }
static bool Exact(Matrix4x4 a, Matrix4x4 b) => Near(a,b,0);
static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
sealed class CameraRecord
{
    public string Phase = "", Path = "";
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3? Crosshair;
    public Dictionary<string,Matrix4x4> Projections = new(), Views = new();
}
