using System;

namespace CulticVR.OpenXRControllers
{
    internal enum IncomingHapticKind { Unknown, SingleShot, Automatic, Shotgun, Rifle, Melee, Explosion, Arrow, Shrapnel, HeavyImpact, Energy, GroundShock, Burn, Acid, Grip, Irritation, HelicopterAutomatic }
    internal enum HapticDirection { Travel, TowardSource, None }
    internal enum ImpactSide { Unknown, Front, Back }
    internal readonly struct VestMotorFrame
    {
        internal const uint All = (1u << 20) - 1;
        internal readonly uint Front, Back;
        internal readonly float Amplitude, Seconds;
        internal VestMotorFrame(uint front, uint back, float amplitude, float seconds)
        { Front = front & All; Back = back & All; Amplitude = amplitude; Seconds = seconds; }
    }
    // State belongs to the actual native attack invocation. A shotgun's shared
    // projectile metadata points to this same group; no frame/time heuristics.
    internal sealed class HapticAttackGroup
    {
        internal readonly ulong Identity;
        internal readonly IncomingHapticKind Kind;
        internal readonly HapticDirection Direction;
        private byte _shotgunAccepted;
        internal HapticAttackGroup(ulong identity, IncomingHapticKind kind) { Identity = identity; Kind = kind; }
        internal HapticAttackGroup(ulong identity, IncomingHapticKind kind,HapticDirection direction)
        { Identity=identity;Kind=kind;Direction=direction; }
        internal bool Accept(ImpactSide side, bool actualLoss)
        {
            if (!actualLoss) return false;
            if (Kind != IncomingHapticKind.Shotgun) return true;
            if (side == ImpactSide.Unknown) return false;
            byte bit = side == ImpactSide.Front ? (byte)1 : side == ImpactSide.Back ? (byte)2 : (byte)3;
            if ((_shotgunAccepted & bit) == bit) return false;
            _shotgunAccepted |= bit; return true;
        }
    }
    // Authored candidate patterns. Native event classification and torso pose
    // are not inferred here; callers supply traced kind and VR-facing side.
    internal static class HapticPatterns
    {
        private static readonly byte[] SingleSpots = {5,6,9,10,13,14};
        internal static float Strength(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0,Math.Min(1,value));
        internal static ImpactSide Side(float facingX, float facingZ, float incomingX, float incomingZ)
        {
            // Incoming points from impact toward the source. The facing basis
            // must come from the same world transform as the rendered VR view.
            if (!Finite(facingX) || !Finite(facingZ) || !Finite(incomingX) || !Finite(incomingZ) ||
                (double)facingX*facingX+(double)facingZ*facingZ < 1e-8 ||
                (double)incomingX*incomingX+(double)incomingZ*incomingZ < 1e-8) return ImpactSide.Unknown;
            return (double)facingX*incomingX+(double)facingZ*incomingZ >= 0 ? ImpactSide.Front : ImpactSide.Back;
        }
        internal static VestMotorFrame Incoming(IncomingHapticKind kind, ImpactSide side, uint acceptedSequence, float strength)
        {
            uint spot = 1u << SingleSpots[acceptedSequence % (uint)SingleSpots.Length];
            uint mask; float amplitude, seconds;
            switch (kind) {
                case IncomingHapticKind.SingleShot: mask=spot;amplitude=.42f;seconds=.075f;break;
                case IncomingHapticKind.Automatic: mask=spot;amplitude=.42f;seconds=.055f;break;
                // Keep the physically verified duration; helicopter impacts now
                // use the higher gain felt in the standalone X40 comparison.
                case IncomingHapticKind.HelicopterAutomatic: mask=spot;amplitude=.9f;seconds=.14f;break;
                case IncomingHapticKind.Rifle: mask=spot;amplitude=.78f;seconds=.13f;break;
                case IncomingHapticKind.Shotgun: mask=Bits(5,6,9,10,14);amplitude=.82f;seconds=.14f;break;
                case IncomingHapticKind.Melee: mask=Bits(5,6,9,10);amplitude=.65f;seconds=.12f;break;
                case IncomingHapticKind.Explosion: return new VestMotorFrame(VestMotorFrame.All,VestMotorFrame.All,.95f*Strength(strength),.2f);
                case IncomingHapticKind.Arrow: mask=spot;amplitude=.58f;seconds=.11f;break;
                case IncomingHapticKind.Shrapnel: mask=spot;amplitude=.34f;seconds=.055f;break;
                case IncomingHapticKind.HeavyImpact: mask=Bits(5,6,9,10)|Bits(8,11,13,14);amplitude=.85f;seconds=.16f;break;
                case IncomingHapticKind.Energy: mask=Bits(5,6,9,10);amplitude=.62f;seconds=.09f;break;
                case IncomingHapticKind.Acid: mask=Bits(9,10,13,14);amplitude=.4f;seconds=.09f;break;
                case IncomingHapticKind.Grip: mask=Bits(5,6,9,10);amplitude=.38f;seconds=.08f;break;
                case IncomingHapticKind.GroundShock:
                    mask=Bits(9,10,13,14);return new VestMotorFrame(mask,mask,.7f*Strength(strength),.12f);
                case IncomingHapticKind.Irritation:
                    mask=Bits(9,10,13,14);return new VestMotorFrame(mask,mask,.28f*Strength(strength),.07f);
                case IncomingHapticKind.Burn:
                    // Native ongoing burn ticks have no source direction. Sparse,
                    // alternating pairs on both panels convey burning, not gunfire.
                    mask=(acceptedSequence&1)==0?Bits(5,10,13,18):Bits(6,9,14,17);
                    return new VestMotorFrame(mask,mask,.24f*Strength(strength),.045f);
                default: return default; // Unknown producer does not become a fabricated gun hit.
            }
            if (side == ImpactSide.Unknown) return default; // No guessed front/back injury.
            amplitude *= Strength(strength);
            return new VestMotorFrame(side==ImpactSide.Front?mask:0,side==ImpactSide.Back?mask:0,amplitude,seconds);
        }
        internal static VestMotorFrame HelicopterNearMiss(ImpactSide side,uint sequence,float strength)
        {
            if(side==ImpactSide.Unknown)return default;
            uint spot=1u<<SingleSpots[sequence%(uint)SingleSpots.Length];
            return new VestMotorFrame(side==ImpactSide.Front?spot:0,side==ImpactSide.Back?spot:0,.36f*Strength(strength),.14f);
        }
        internal static float Recoil(int weapon, bool alternate, out float seconds)
        {
            seconds=.07f;
            if(alternate && weapon!=2 && weapon!=3 && weapon!=5 && weapon!=10 && weapon!=11 && weapon!=17)return 0;
            switch(weapon) {
                case 1:case 11:return .4f;
                case 4:case 15:seconds=.045f;return .3f;
                case 17:seconds=.09f;return .5f; // Match the disposable LP42 on both outputs/modes.
                case 2:case 9:case 14:seconds=.11f;return .75f;
                case 3:case 12:seconds=.14f;return .95f;
                case 5:seconds=.16f;return .85f;
                case 10:seconds=alternate?.15f:.055f;return alternate?.7f:.22f;
                default:return 0;
            }
        }
        private static uint Bits(int a,int b,int c,int d,int e=-1) => (1u<<a)|(1u<<b)|(1u<<c)|(1u<<d)|(e<0?0:1u<<e);
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
