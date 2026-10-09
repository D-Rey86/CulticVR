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
            SpriteWidth = Float(file,"Weapons","SpriteWidth",1f,.5f,2f,"Weapon artwork width multiplier; muzzle and scope landmarks use the same dimensions.");
            SpriteHeight = Float(file,"Weapons","SpriteHeight",1f,.5f,2f,"Weapon artwork height multiplier; muzzle and scope landmarks use the same dimensions.");
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
        }
    }
}
