using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace CulticVR.OpenXRControllers
{
    [BepInPlugin("culticvr.openxrcontrollers", "CULTIC VR OpenXR Controller Bootstrap", "0.1.8")]
    [BepInDependency("raicuparta.uuvr-modern")]
    public sealed partial class OpenXRControllersPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // UUVR creates its loader in Start. Register before that, while feature
            // configuration can still be changed safely.
            var settings = OpenXRSettings.Instance;
            if (settings.GetFeatures<ControllerActionFeature>().Length != 0) return;
            var field = typeof(OpenXRSettings).GetField("features", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Logger.LogError("OpenXR feature field not found; controller actions were not registered.");
                return;
            }

            var feature = ScriptableObject.CreateInstance<ControllerActionFeature>();
            feature.enabled = true;
            var features = settings.GetFeatures().Concat(new OpenXRFeature[] { feature }).ToArray();
            field.SetValue(settings, features);
        }
    }

    // The UUVR 0.4.0 managed OpenXR fork omits OpenXRInput.AttachActionSets.
    // Its native UnityOpenXR.dll is version 1.4.2 and retains these input exports.
    // Signatures and lifecycle match Unity OpenXR package 1.4.2.
    public sealed class ControllerActionFeature : OpenXRFeature
    {
        private const string Left = "/user/hand/left";
        private const string Right = "/user/hand/right";
        private const string NativeLibrary = "UnityOpenXR";
        private bool _attached;

        protected override void OnSessionBegin(ulong xrSession)
        {
            if (_attached) return;
            try
            {
                _attached = RegisterActions();
                Debug.Log($"CULTIC VR controller action sets attached={_attached} session={xrSession:X}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"CULTIC VR controller action registration failed: {exception}");
            }
        }

        protected override void OnSessionDestroy(ulong xrSession) => _attached = false;

        private static bool RegisterActions()
        {
            foreach (var profile in Profiles)
            {
                foreach (var hand in new[] { Left, Right })
                {
                    var side = hand == Left ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right;
                    var characteristics = InputDeviceCharacteristics.HeldInHand |
                        InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Controller | side;
                    if (RegisterDeviceDefinition(hand, profile.Path, (uint)characteristics,
                            profile.DisplayName, profile.Manufacturer, "") == 0)
                    {
                        Debug.LogError($"CULTIC VR: OpenXR rejected {profile.Path} device definition.");
                        return false;
                    }
                }
            }

            foreach (var profile in Profiles)
            {
                var actionSet = CreateActionSet("culticvr_" + profile.Id, profile.DisplayName, default);
                if (actionSet == 0) return Fail(profile.Path, "create action set");
                var bindings = new List<SerializedBinding>();
                foreach (var action in profile.Actions)
                {
                    var hands = action.Hand == Hand.Both ? new[] { Left, Right } :
                        action.Hand == Hand.Left ? new[] { Left } : new[] { Right };
                    var id = CreateAction(actionSet, action.Name, action.Name, (uint)action.Type,
                        default, hands, (uint)hands.Length, new[] { action.Usage }, 1);
                    if (id == 0) return Fail(profile.Path, "create " + action.Name);
                    foreach (var hand in hands)
                        bindings.Add(new SerializedBinding { actionId = id, path = hand + action.Path });
                }

                if (!SuggestBindings(profile.Path, bindings.ToArray(), (uint)bindings.Count))
                    Debug.LogWarning($"CULTIC VR: OpenXR did not accept suggested bindings for {profile.Path}.");
            }

            if (!AttachActionSets()) return Fail("session", "attach action sets");
            return true;
        }

        private static bool Fail(string profile, string step)
        {
            Debug.LogError($"CULTIC VR: OpenXR failed to {step} for {profile}.");
            return false;
        }

        private enum ActionType : uint { Binary, Axis1D, Axis2D, Pose }
        private enum Hand { Both, Left, Right }

        private sealed class Action
        {
            public string Name = "";
            public string Path = "";
            public string Usage = "";
            public ActionType Type;
            public Hand Hand;
        }

        private sealed class Profile
        {
            public string Id = "";
            public string Path = "";
            public string DisplayName = "";
            public string Manufacturer = "";
            public Action[] Actions = Array.Empty<Action>();
        }

        private static Action A(string name, ActionType type, string usage, string path, Hand hand = Hand.Both) =>
            new Action { Name = name, Type = type, Usage = usage, Path = path, Hand = hand };

        private static Action[] Common(string grip, string trigger, string axis) => new[]
        {
            A("devicepose", ActionType.Pose, "Device", "/input/grip/pose"),
            A("pointer", ActionType.Pose, "Pointer", "/input/aim/pose"),
            A("trigger", ActionType.Axis1D, "Trigger", trigger),
            A("triggerpressed", ActionType.Binary, "TriggerButton", trigger),
            A("grip", ActionType.Axis1D, "Grip", grip),
            A("grippressed", ActionType.Binary, "GripButton", grip),
            A("primaryaxis", ActionType.Axis2D, "Primary2DAxis", axis),
        };

        private static Action[] With(Action[] common, params Action[] extra) => common.Concat(extra).ToArray();

        private static readonly Profile[] Profiles =
        {
            new Profile
            {
                Id = "touch", Path = "/interaction_profiles/oculus/touch_controller",
                DisplayName = "CULTIC Touch Controller", Manufacturer = "OpenXR",
                Actions = With(Common("/input/squeeze/value", "/input/trigger/value", "/input/thumbstick"),
                    A("primaryleft", ActionType.Binary, "PrimaryButton", "/input/x/click", Hand.Left),
                    A("primaryright", ActionType.Binary, "PrimaryButton", "/input/a/click", Hand.Right),
                    A("secondaryleft", ActionType.Binary, "SecondaryButton", "/input/y/click", Hand.Left),
                    A("secondaryright", ActionType.Binary, "SecondaryButton", "/input/b/click", Hand.Right),
                    A("menu", ActionType.Binary, "MenuButton", "/input/menu/click", Hand.Left),
                    A("axisclick", ActionType.Binary, "Primary2DAxisClick", "/input/thumbstick/click"))
            },
            new Profile
            {
                Id = "index", Path = "/interaction_profiles/valve/index_controller",
                DisplayName = "CULTIC Index Controller", Manufacturer = "OpenXR",
                Actions = With(Common("/input/squeeze/value", "/input/trigger/value", "/input/thumbstick"),
                    A("primary", ActionType.Binary, "PrimaryButton", "/input/a/click"),
                    A("secondary", ActionType.Binary, "SecondaryButton", "/input/b/click"),
                    A("axisclick", ActionType.Binary, "Primary2DAxisClick", "/input/thumbstick/click"))
            },
            new Profile
            {
                Id = "vive", Path = "/interaction_profiles/htc/vive_controller",
                DisplayName = "CULTIC Vive Controller", Manufacturer = "OpenXR",
                Actions = With(Common("/input/squeeze/click", "/input/trigger/value", "/input/trackpad"),
                    A("menu", ActionType.Binary, "MenuButton", "/input/menu/click"),
                    A("axisclick", ActionType.Binary, "Primary2DAxisClick", "/input/trackpad/click"))
            },
            new Profile
            {
                Id = "wmr", Path = "/interaction_profiles/microsoft/motion_controller",
                DisplayName = "CULTIC Motion Controller", Manufacturer = "OpenXR",
                Actions = With(Common("/input/squeeze/click", "/input/trigger/value", "/input/thumbstick"),
                    A("menu", ActionType.Binary, "MenuButton", "/input/menu/click"),
                    A("axisclick", ActionType.Binary, "Primary2DAxisClick", "/input/thumbstick/click"))
            },
            new Profile
            {
                Id = "simple", Path = "/interaction_profiles/khr/simple_controller",
                DisplayName = "CULTIC Simple Controller", Manufacturer = "OpenXR",
                Actions = new[]
                {
                    A("devicepose", ActionType.Pose, "Device", "/input/grip/pose"),
                    A("pointer", ActionType.Pose, "Pointer", "/input/aim/pose"),
                    A("select", ActionType.Binary, "PrimaryButton", "/input/select/click"),
                    A("menu", ActionType.Binary, "MenuButton", "/input/menu/click")
                }
            }
        };

        [StructLayout(LayoutKind.Explicit)]
        private struct SerializedGuid
        {
            [FieldOffset(0)] public Guid Guid;
            [FieldOffset(0)] public ulong Low;
            [FieldOffset(8)] public ulong High;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct SerializedBinding
        {
            public ulong actionId;
            public string path;
        }

        [DllImport(NativeLibrary, EntryPoint = "OpenXRInputProvider_RegisterDeviceDefinition", CharSet = CharSet.Ansi)]
        private static extern ulong RegisterDeviceDefinition(string userPath, string interactionProfile,
            uint characteristics, string name, string manufacturer, string serialNumber);

        [DllImport(NativeLibrary, EntryPoint = "OpenXRInputProvider_CreateActionSet", CharSet = CharSet.Ansi)]
        private static extern ulong CreateActionSet(string name, string localizedName, SerializedGuid guid);

        [DllImport(NativeLibrary, EntryPoint = "OpenXRInputProvider_CreateAction", CharSet = CharSet.Ansi)]
        private static extern ulong CreateAction(ulong actionSetId, string name, string localizedName,
            uint actionType, SerializedGuid guid, string[] userPaths, uint userPathCount,
            string[] usages, uint usageCount);

        [DllImport(NativeLibrary, EntryPoint = "OpenXRInputProvider_SuggestBindings", CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool SuggestBindings(string interactionProfile, SerializedBinding[] bindings, uint count);

        [DllImport(NativeLibrary, EntryPoint = "OpenXRInputProvider_AttachActionSets", CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool AttachActionSets();
    }
}
