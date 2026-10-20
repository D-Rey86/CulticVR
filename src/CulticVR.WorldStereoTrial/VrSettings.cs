using BepInEx.Configuration;
using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    // Shared settings are bound once. Consumers read typed values without any
    // reflection, config lookup or disk work in simulation/render callbacks.
    public static class VrSettings
    {
        public static ConfigEntry<float> Resolution = null!;
        public static ConfigEntry<float> Separation = null!;
        public static ConfigEntry<bool> Teleport = null!;
        public static ConfigEntry<bool> SnapTurn = null!;
        public static ConfigEntry<int> SnapDegrees = null!;
        public static ConfigEntry<bool> ControllerMode = null!;
        public static ConfigEntry<bool> LeftHand = null!;
        public static ConfigEntry<bool> SwapSticks = null!;
        public static ConfigEntry<float> SpriteWidth = null!;
        public static ConfigEntry<float> SpriteHeight = null!;
        public static ConfigEntry<bool> DeferredTrial = null!;
        public static ConfigEntry<bool> PalmMap = null!;
        public static ConfigEntry<bool> ControllerHaptics = null!;
        public static ConfigEntry<float> ControllerHapticStrength = null!;
        public static ConfigEntry<bool> VestHaptics = null!;
        public static ConfigEntry<float> VestHapticStrength = null!;
        private static ConfigFile? _file;
        public static bool MotionControls => ControllerMode == null || !ControllerMode.Value;
        public static bool LeftWeapon => LeftHand != null && LeftHand.Value;
        public static bool SticksSwapped => SwapSticks != null && SwapSticks.Value;
        public static float WeaponWidth => SpriteWidth == null ? 1f : SpriteWidth.Value;
        public static float WeaponHeight => SpriteHeight == null ? 1f : SpriteHeight.Value;
        public static int Revision { get; private set; }
        public static bool Dirty { get; private set; }
        public const int MenuId = 1000;

        internal static void Bind(ConfigFile file)
        {
            _file = file;
            Resolution = Float(file,"Picture","ResolutionScale",1f,.5f,2f,"Per-eye resolution relative to the runtime recommendation. Applied on the next game launch; supersampling increases GPU and memory cost.");
            Separation = Float(file,"Picture","StereoSeparation",1f,.5f,1.5f,"Stereo eye baseline multiplier. 1 uses the headset's measured eye separation.");
            Teleport = file.Bind("Controls","Teleport",false,"Hold the movement stick forward to aim a teleport arc; release to travel to a valid standing surface.");
            SnapTurn = file.Bind("Controls","SnapTurn",false,"Use a discrete turn per horizontal turn-stick deflection. Return stick to neutral before the next turn.");
            SnapDegrees = file.Bind("Controls","SnapTurnDegrees",45,new ConfigDescription("Snap turn amount in degrees.",new AcceptableValueList<int>(15,30,45,60,90)));
            // New positive key deliberately does not inherit the failed menu's
            // persisted MotionControls=false lockout. Existing profiles recover
            // to motion controls once; subsequent gamepad choices persist.
            ControllerMode = file.Bind("Controls","GamepadMode",false,"Use a regular gamepad with HMD aim. Disabled by default for tracked motion controls. Motion controllers can still navigate menus in this mode.");
            LeftHand = file.Bind("Controls","LeftHandWeapon",false,"Hold and aim weapons with the left hand; independent of stick assignments.");
            SwapSticks = file.Bind("Controls","SwapMovementTurnSticks",false,"Swap movement and turning stick assignments; button assignments stay unchanged.");
            PalmMap = file.Bind("Controls","PalmUpMap",true,"Look at the palm of your weapon hand (right by default) to open/close the native map. Look away before repeating. No raising, lowering, steady hold or neutral-stick gesture is required. Uses a larger stationary VR UI panel.");
            ControllerHaptics = file.Bind("Haptics","MotionControllers",true,"Custom firearm recoil in the weapon hand and health/armor damage in both hands. OpenXR motion controls only; does not alter regular gamepad rumble.");
            ControllerHapticStrength = Float(file,"Haptics","ControllerStrength",.6f,0f,1f,"Motion-controller haptic strength.");
            VestHaptics = file.Bind("Haptics","TactSuit",false,"Optional X40 recoil and traced incoming attack patterns. Front/back uses VR facing yaw, not measured torso orientation. Requires Player and vest connected. Local only; never launches Player. Defaults OFF.");
            VestHapticStrength = Float(file,"Haptics","VestStrength",.4f,0f,1f,"bHaptics vest haptic strength, independent of controller strength.");
            SpriteWidth = Float(file,"Weapons","SpriteWidth",1f,.5f,2f,"Weapon artwork width multiplier; muzzle and scope landmarks use the same dimensions.");
            SpriteHeight = Float(file,"Weapons","SpriteHeight",1f,.5f,2f,"Weapon artwork height multiplier; muzzle and scope landmarks use the same dimensions.");
            DeferredTrial = file.Bind("Rendering Trials","Deferred",false,"Live deferred rendering on the existing stereo world camera. Compare fog, transparency, scopes and lighting; OFF restores the original camera path.");
        }
        private static ConfigEntry<float> Float(ConfigFile file,string section,string key,float value,float min,float max,string description) =>
            file.Bind(section,key,value,new ConfigDescription(description,new AcceptableValueRange<float>(min,max)));
        public static void Set<T>(ConfigEntry<T> entry,T value)
        {
            if (Equals(entry.Value,value)) return;
            var save = entry.ConfigFile.SaveOnConfigSet;
            try { entry.ConfigFile.SaveOnConfigSet=false; entry.Value=value; }
            finally { entry.ConfigFile.SaveOnConfigSet=save; }
            Revision++; Dirty=true;
        }
        public static void Save()
        {
            if (!Dirty) return;
            _file?.Save(); Dirty=false;
        }
        public static void Reset()
        {
            Set(Resolution,1f); Set(Separation,1f); Set(Teleport,false); Set(SnapTurn,false);
            Set(SnapDegrees,45); Set(ControllerMode,false); Set(LeftHand,false); Set(SwapSticks,false);
            Set(SpriteWidth,1f); Set(SpriteHeight,1f);
            Set(DeferredTrial,false);
            Set(PalmMap,true);
            Set(ControllerHaptics,true); Set(ControllerHapticStrength,.6f);
            Set(VestHaptics,false); Set(VestHapticStrength,.4f);
        }
    }
}
