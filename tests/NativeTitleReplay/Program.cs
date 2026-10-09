using System;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

// Replay the measured geometry contract. Production preserves the game-owned
// renderer rather than writing a replacement transform/projection algorithm.
// This test does not execute targetTexture/Unity canvas/native callbacks.
var root = args.Length > 0 ? args[0] : "deployment/backups/flat-title-capture-20261006-025642-453/results/20261006-030030";
Require(File.ReadAllText(Path.Combine(root, "identity.txt")).Contains("SHA256=0AC77697A3F08589E20AF683DA2DE78E664DBE0726397280905608423E557CDC"), "exact shipping identity");
Require(File.ReadAllText(Path.Combine(root, "completion.txt")).Contains("titleSamples=2\nmainMenu=True"), "full control coverage");
var corners = 0;
foreach (var name in new[] { "title-0", "title-1", "main-menu" })
{
    var text = File.ReadAllText(Path.Combine(root, name + ".txt"));
    Require(text.Contains("XR.enabled=False XR.active=False"), "native flat control");
    Require(text.Contains("camera=UIcamera source=True") && text.Contains("camera=Main Camera source=False"), "both original passes present");
    Require(text.Contains("fov=75.2 aspect=1.5") && text.Contains("fov=60 aspect=1.5"), "native FOVs, not XR FOV");
    var camera = text.Substring(text.IndexOf("camera=UIcamera source=True", StringComparison.Ordinal));
    var view = Matrix(camera, " view=");
    var projection = Matrix(camera, " projection=");
    var cameraMatrix = Matrix(camera, "cameraMatrix=");
    var rootMatrix = Matrix(text, "rootMatrix=");
    var poseMatch = Regex.Match(camera, @"localPosition=\(([^)]+)\) localRotation=\(([^)]+)\)");
    var localPosition = Vector(poseMatch.Groups[1].Value);
    var rotationTerms = Terms(poseMatch.Groups[2].Value);
    var localRotation = new Quaternion(rotationTerms[0], rotationTerms[1], rotationTerms[2], rotationTerms[3]);
    Require(localRotation.x == 0 && localRotation.y == 0 && localRotation.z == 0 && localRotation.w == 1, "measured authored identity local rotation");
    var actualPosition = new Vector3(cameraMatrix.m03, cameraMatrix.m13, cameraMatrix.m23);
    Require((rootMatrix.MultiplyPoint3x4(localPosition) - actualPosition).magnitude < .00002f, "child camera belongs to unchanged root");
    foreach (Match match in Regex.Matches(text, @"corner=\(([^)]+)\) sourceViewport=\(([^)]+)\) introLocal=\(([^)]+)\)"))
    {
        var world = Vector(match.Groups[1].Value);
        var recorded = Vector(match.Groups[2].Value);
        var local = Vector(match.Groups[3].Value);
        var replay = Viewport(view, projection, world);
        Require((replay - recorded).magnitude < .0002f, name + " same-phase measured view/corners reproduce viewport");
        var reconstructed = rootMatrix.MultiplyPoint3x4(local);
        // Native InverseTransformPoint -> float receipt -> managed multiply
        // rounds each component independently. The first oversized fade corner
        // differs by 2 ULPs in X/Y and 5 in Z, NOT a camera/space discrepancy.
        Require(FloatRoundTrip(reconstructed.x, world.x) && FloatRoundTrip(reconstructed.y, world.y) && FloatRoundTrip(reconstructed.z, world.z),
            "native root-local geometry round trips within eight single-precision ULPs");
        // Uniform root scale cancels projected size/position when the original
        // camera remains its child. Never copy a child pose to a camera which
        // then becomes the parent's screen-space canvas layout owner.
        foreach (var scale in new[] { .05347941f, .09072687f, .2f, .031f })
        {
            var p = local * scale;
            var c = localPosition * scale;
            var distance = p.z - c.z;
            if (Math.Abs(distance) < .00001f) continue;
            var projectedX = .5f + projection.m00 * (p.x - c.x) / distance * .5f;
            var projectedY = .5f + projection.m11 * (p.y - c.y) / distance * .5f;
            Require(Math.Abs(projectedX - recorded.x) < .00003f && Math.Abs(projectedY - recorded.y) < .00003f,
                "original child/canvas ownership is scale independent");
            // Existing mod UI target is 16:9; preserve vertical framing, derive
            // horizontal framing from target aspect, not one monitor/headset.
            var targetM00 = projection.m11 / (2560f / 1440f);
            var targetX = .5f + targetM00 * (p.x - c.x) / distance * .5f;
            Require(Math.Abs(targetX - (.5f + (recorded.x - .5f) * (1.5f / (2560f / 1440f)))) < .00003f,
                "16:9 horizontal reframing exactly follows aspect");
        }
        corners++;
    }
    Require(text.Contains("graphic=art/layer1 enabled=True"), "background layer part of same native camera pass");
    if (name == "main-menu")
    {
        Require(text.Contains("introState=7") && text.Contains("graphic=logo enabled=True color=RGBA(1.000000, 1.000000, 1.000000, 0.000000)"), "native post-Enter fades");
    }
}
Require(corners >= 200, "all root/title/prompt/fade/art corners exercised");
Console.WriteLine($"PASS: three native states, exact shipping/XR identity, {corners} same-phase corners, preserved child/root ownership across four scales and 16:9 reframing, background and post-Enter state. Numerical control only; candidate native callbacks, composition/postprocessing and headset acceptance remain unverified.");

static Vector3 Viewport(Matrix4x4 view, Matrix4x4 projection, Vector3 point)
{
    var camera = view * new Vector4(point.x, point.y, point.z, 1);
    var clip = projection * camera;
    return new Vector3(.5f + clip.x / clip.w * .5f, .5f + clip.y / clip.w * .5f, -camera.z);
}
static Matrix4x4 Matrix(string text, string marker)
{
    var start = text.IndexOf(marker, StringComparison.Ordinal);
    Require(start >= 0, "matrix marker exists");
    var rows = text.Substring(start + marker.Length).TrimStart().Split('\n');
    var matrix = new Matrix4x4();
    for (var row = 0; row < 4; row++)
    {
        var values = rows[row].Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        for (var col = 0; col < 4; col++) matrix[row, col] = float.Parse(values[col], CultureInfo.InvariantCulture);
    }
    return matrix;
}
static float[] Terms(string text) => Array.ConvertAll(text.Split(','), value => float.Parse(value, CultureInfo.InvariantCulture));
static Vector3 Vector(string text) { var values = Terms(text); return new Vector3(values[0], values[1], values[2]); }
static bool FloatRoundTrip(float actual, float expected) =>
    Math.Abs(actual - expected) <= Math.Max(.000001f, 8 * Math.Abs(MathF.BitIncrement(expected) - expected));
static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
