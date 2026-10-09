using CulticVR.AimPreview;

void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var halfDot = WristPointerBounds.PointerSize / 2.0;
var halfPanel = WristPointerBounds.BackgroundSize / 2.0;
var samples = 0;
// Cover inside/outside, every boundary direction, far and near-parallel rays.
foreach (var distance in new[] { 0.0, 1.0, 92.0, 140.0, 155.0, 160.0, 1000.0, (double)float.MaxValue })
for (var degrees = 0; degrees < 360; degrees++)
{
    var angle = degrees * Math.PI / 180;
    var x = (float)(distance * Math.Cos(angle));
    var y = (float)(distance * Math.Sin(angle));
    Require(WristPointerBounds.TryClamp(x, y, out var bx, out var by), "Finite ray rejected");
    foreach (var dx in new[] { -halfDot, halfDot })
    foreach (var dy in new[] { -halfDot, halfDot })
        Require(Math.Sqrt((bx+dx)*(bx+dx) + (by+dy)*(by+dy)) <= halfPanel + 0.0001, "Dot corner escaped visible panel");
    if (distance <= WristPointerBounds.Radius) Require(bx == x && by == y, "Interior pointer changed");
    Require(WristPointerBounds.TryClamp(bx, by, out var twiceX, out var twiceY) &&
        Math.Abs(twiceX-bx) < 0.0001 && Math.Abs(twiceY-by) < 0.0001, "Boundary not stable");
    samples++;
}
foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
{
    Require(!WristPointerBounds.TryClamp(invalid, 0, out _, out _), "Invalid X accepted");
    Require(!WristPointerBounds.TryClamp(0, invalid, out _, out _), "Invalid Y accepted");
}
Console.WriteLine($"PASS: {samples} bounded positions, full pointer corner containment, unchanged interior/button centers, stable boundary, invalid input rejection. Headset behavior remains unverified.");
