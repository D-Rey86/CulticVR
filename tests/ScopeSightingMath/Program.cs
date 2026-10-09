using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CulticVR.WorldStereoTrial;
using UnityEngine;

// Replay the recorded selected-eye projection geometry, not Unity callbacks or
// physics. A nearby flat reticle can align with only the selected eye at range.
var trace = File.ReadAllLines(args.Length > 0 ? args[0] :
    "deployment/backups/fg42-shot-trace-20261004/20261004-014523-822-shots.txt");
bool scoped = false;
string eye = "";
Vector3 crosshair = default, cameraPosition = default;
Quaternion cameraRotation = default;
Matrix4x4 projection = default;
int replays = 0;
for (var n = 0; n < trace.Length; n++)
{
    var line = trace[n].Trim();
    if (line.StartsWith("shot=")) scoped = line.Contains("zoom=1 ");
    if (!scoped) continue;
    if (line.StartsWith("renderedEye=")) eye = line.Substring("renderedEye=".Length);
    if (line.StartsWith("scopeQuad position=")) crosshair = ParseVector(line);
    if (line.StartsWith("worldCamera position="))
    {
        cameraPosition = ParseVector(line);
        var q = Values(Regex.Matches(line, @"\(([^)]+)\)")[1].Groups[1].Value);
        cameraRotation = new Quaternion(q[0], q[1], q[2], q[3]);
    }
    if (line.StartsWith("projection=")) projection = ParseMatrix(trace, n, "projection=");
    if (!line.StartsWith("view=")) continue;
    var view = ParseMatrix(trace, n, "view=");
    Require(ScopeSightingMath.TryGetEyeOrigin(view, out var eyeOrigin), "captured view invertible");
    var conjugate = new Quaternion(-cameraRotation.x, -cameraRotation.y, -cameraRotation.z, cameraRotation.w);
    var localEye = conjugate * (eyeOrigin - cameraPosition);
    Require(Math.Abs(localEye.x - (eye == "Right" ? 0.030375f : -0.030375f)) < 0.0001f,
        "runtime eye offset agrees across all camera poses, no guessed IPD");
    Require(Math.Abs(localEye.y) < 0.0001f && Math.Abs(localEye.z) < 0.0001f, "view paired with owning camera");
    Require(ScopeSightingMath.TryGetDirection(eyeOrigin, crosshair, out var direction), "eye through reticle");
    var target = Project(projection, view, crosshair);
    foreach (var distance in new[] { 1f, 10f, 100f, 500f })
    {
        var ndc = Project(projection, view, crosshair + direction * distance);
        Require(Math.Abs(ndc.x - target.x) < 0.00015f && Math.Abs(ndc.y - target.y) < 0.00015f,
            "selected-eye alignment independent of distance");
    }
    var highZoom = projection;
    highZoom.m00 *= 2.5f; highZoom.m11 *= 2.5f;
    var closeNdc = Project(highZoom, view, crosshair);
    var farNdc = Project(highZoom, view, crosshair + direction * 500f);
    Require(Math.Abs(closeNdc.x - farNdc.x) < 0.0004f && Math.Abs(closeNdc.y - farNdc.y) < 0.0004f,
        "high zoom does not change sight-line contract");
    replays++;
}
Require(replays == 12, "all six scoped shots replayed for BOTH selectable eyes");
foreach (var pair in new[] { ("32", 110.5f), ("33", 97.5f), ("34", 95.5f), ("35", 96.5f), ("36", 101.5f) })
{
    Require(ScopeSightingMath.TryGetFg42Crosshair("sprWeaponFG42_" + pair.Item1, out var point), "scoped sprite known");
    Require(point.x == 240f && point.y == pair.Item2, "measured original-sprite anchor");
}
Require(!ScopeSightingMath.TryGetFg42Crosshair("sprWeaponFG42_31", out _), "transition artwork not guessed");
Require(!ScopeSightingMath.TryGetDirection(Vector3.zero, Vector3.zero, out _), "coincident eye/reticle rejected");
Require(!ScopeSightingMath.TryGetDirection(new Vector3(float.NaN, 0, 0), Vector3.one, out _), "bad pose rejected");
var invalid = new Matrix4x4();
Require(!ScopeSightingMath.TryGetEyeOrigin(invalid, out _), "singular view rejected");
invalid = Matrix4x4.identity; invalid.m03 = float.PositiveInfinity;
Require(!ScopeSightingMath.TryGetEyeOrigin(invalid, out _), "non-finite view rejected");
var scaledView = Matrix4x4.identity;
scaledView.m00 = 2; scaledView.m11 = 3; scaledView.m22 = -4;
scaledView.m03 = -6; scaledView.m13 = -12; scaledView.m23 = 20;
Require(ScopeSightingMath.TryGetEyeOrigin(scaledView, out var scaledEye), "scaled affine view supported without transpose approximation");
Require(scaledEye.x == 3 && scaledEye.y == 4 && scaledEye.z == 5, "exact affine solve");
Console.WriteLine("PASS: 12 captured eye views, current-pose offset pairing, all ranges/high zoom, five animation anchors, invalid-data rejection. Native callbacks/visual accuracy unverified.");

static float[] Values(string value) => Array.ConvertAll(value.Split(new[] { ',', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries),
    x => float.Parse(x, CultureInfo.InvariantCulture));
static Vector3 ParseVector(string line)
{
    var v = Values(Regex.Match(line, @"\(([^)]+)\)").Groups[1].Value);
    return new Vector3(v[0], v[1], v[2]);
}
static Matrix4x4 ParseMatrix(string[] lines, int n, string prefix)
{
    var result = new Matrix4x4();
    for (var row = 0; row < 4; row++)
    {
        var values = Values(row == 0 ? lines[n].Trim().Substring(prefix.Length) : lines[n + row]);
        for (var col = 0; col < 4; col++) result[row, col] = values[col];
    }
    return result;
}
static Vector2 Project(Matrix4x4 p, Matrix4x4 v, Vector3 point)
{
    var world = new Vector4(point.x, point.y, point.z, 1);
    var clip = p * (v * world);
    return new Vector2(clip.x / clip.w, clip.y / clip.w);
}
static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
