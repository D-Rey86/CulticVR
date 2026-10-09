using System;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using CulticVR.OpenXRControllers;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

var router = new OptionsStickRouter();
const int options = 7;
Step(0, 0, options, MenuDirection.None);
Step(.199987784f, -.5999939f, options, MenuDirection.Down); // Captured accidental arrow adjustment.
Step(.7f, -.55f, options, MenuDirection.Down); // Axis lock during horizontal drift.
Step(.8f, 0, options, MenuDirection.None); // Release vertical; no horizontal action until neutral.
Step(0, 0, options, MenuDirection.None);
Step(-.5999939f, -.466658533f, options, MenuDirection.Left); // Captured intentional horizontal attempt.
for (var i = 0; i < 200; i++) Step(-.65f, -.2f, options, MenuDirection.Left); // Hold, no pulses.
Step(0, 0, options, MenuDirection.None);
Step(.5999939f, 0, options, MenuDirection.Right);
Step(.7f, -.6f, options + 1, MenuDirection.None); // Changing menu while held.
Step(.7f, 0, options + 1, MenuDirection.None);
Step(0, 0, options + 1, MenuDirection.None);
Step(0, .75f, options + 1, MenuDirection.Up);
var analog = router.Route(new Vector2(0, .75f), 0, out var direction);
Require(Zero(analog) && direction == MenuDirection.None, "resume sends release, not movement from a menu hold");
Require(Zero(router.Route(new Vector2(.7f, .6f), 0, out direction)), "exit ownership stays blocked until neutral");
router.Route(Vector2.zero, 0, out direction);
analog = router.Route(new Vector2(.7123f, -.239f), 0, out direction);
Require(analog.x == .7123f && analog.y == -.239f && direction == MenuDirection.None, "exact gameplay analog returns after release");
Step(0, 0, options, MenuDirection.None);
Step(.75f, .75f, options, MenuDirection.Up); // Exact diagonal has one direction, never two.
Step(float.NaN, 0, options, MenuDirection.None);
Step(.8f, 0, options, MenuDirection.None);
Step(0, 0, options, MenuDirection.None);
Step(.8f, 0, options, MenuDirection.Right);
Step(float.PositiveInfinity, 0, options, MenuDirection.None);
Step(0, 0, options, MenuDirection.None);
Step(.499f, .2f, options, MenuDirection.None);
Step(.501f, .2f, options, MenuDirection.Right);
Step(.3f, .15f, options, MenuDirection.Right);
Step(.25f, .15f, options, MenuDirection.None);
Step(-.8f, 0, options, MenuDirection.Left);

var path = args.Length > 0 ? args[0] : "deployment/backups/menu-observations-20261005/20261006-014954/input.txt";
Require(File.Exists(path), "real input receipt exists");
router = default;
var rows = 0;
var matchedDown = 0;
foreach (var line in File.ReadLines(path))
{
    var match = Regex.Match(line, @"menu=(\S+).*raw=\(([^,]+),([^\)]+)\)");
    Require(match.Success, "receipt row parses");
    var raw = new Vector2(float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
        float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    var context = match.Groups[1].Value == "menuVideo" ? options :
        match.Groups[1].Value == "menuOptions" ? options + 1 : 0;
    analog = router.Route(raw, context, out direction);
    if (context != 0)
    {
        Require(Zero(analog), "options emit no duplicate analog input");
        if (direction == MenuDirection.Down)
        {
            matchedDown++;
            Require(direction != MenuDirection.Left && direction != MenuDirection.Right, "vertical navigation never adjusts");
        }
    }
    ValidateDigital(direction);
    rows++;
}
Require(rows == 160 && matchedDown > 0, "complete 160-row real replay includes vertical navigation");

router = default;
var random = new System.Random(526);
for (var i = 0; i < 10000; i++)
{
    var raw = new Vector2((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1));
    var pass = router.Route(raw, 0, out direction);
    Require(pass.x == raw.x && pass.y == raw.y && direction == MenuDirection.None, "unrelated analog path bit-identical");
}
Console.WriteLine("PASS: production router, captured diagonal/horizontal cases, 160 live raw input rows, axis/neutral/context ownership, sustained hold, finite guards, cardinal gamepad encoding, 10,000 exact non-options analog fixtures. Static replay only; native menu/headset acceptance still required.");

void Step(float x, float y, int context, MenuDirection expected)
{
    var result = router.Route(new Vector2(x, y), context, out var actual);
    Require(Zero(result) && actual == expected, $"({x},{y}) context={context}: expected {expected}, got {actual}");
    ValidateDigital(actual);
}
static bool Zero(Vector2 value) => value.x == 0 && value.y == 0;
static void ValidateDigital(MenuDirection direction)
{
    var state = new GamepadState();
    state = state.WithButton(GamepadButton.DpadLeft, direction == MenuDirection.Left);
    state = state.WithButton(GamepadButton.DpadRight, direction == MenuDirection.Right);
    state = state.WithButton(GamepadButton.DpadUp, direction == MenuDirection.Up);
    state = state.WithButton(GamepadButton.DpadDown, direction == MenuDirection.Down);
    var bits = state.buttons;
    Require(bits == 0 || (bits & (bits - 1)) == 0, "at most one gamepad direction bit");
}
static void Require(bool pass, string reason)
{
    if (!pass) throw new InvalidOperationException(reason);
}
