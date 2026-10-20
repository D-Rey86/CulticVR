using System.Numerics;
using CulticVR.OpenXRControllers;

internal static class PatternTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool c,string why){checks++;if(!c)throw new Exception(why);}
        foreach(var side in new[]{ImpactSide.Front,ImpactSide.Back}){
            var shotgun=HapticPatterns.Incoming(IncomingHapticKind.Shotgun,side,0,1);
            Check(BitOperations.PopCount(shotgun.Front|shotgun.Back)==5,"Shotgun cluster simultaneous five spots");
            Check((shotgun.Front!=0)==(side==ImpactSide.Front)&&(shotgun.Back!=0)==(side==ImpactSide.Back),"Shotgun struck side only");
            var single=HapticPatterns.Incoming(IncomingHapticKind.SingleShot,side,0,1);
            var rifle=HapticPatterns.Incoming(IncomingHapticKind.Rifle,side,0,1);
            Check(BitOperations.PopCount(single.Front|single.Back)==1&&BitOperations.PopCount(rifle.Front|rifle.Back)==1,"Singles/rifle stay localized");
            Check(rifle.Amplitude>single.Amplitude,"Rifle stronger than small shot");
            var melee=HapticPatterns.Incoming(IncomingHapticKind.Melee,side,0,1);
            Check(BitOperations.PopCount(melee.Front|melee.Back)==4&&melee.Seconds<=.15,"Short broader melee thump");
        }
        var explosion=HapticPatterns.Incoming(IncomingHapticKind.Explosion,ImpactSide.Unknown,0,1);
        Check(explosion.Front==VestMotorFrame.All&&explosion.Back==VestMotorFrame.All,"Explosion all 40 motors in one frame");
        uint previous=0;
        for(uint i=0;i<20;i++){
            var auto=HapticPatterns.Incoming(IncomingHapticKind.Automatic,ImpactSide.Front,i,1);
            Check(BitOperations.PopCount(auto.Front)==1&&auto.Front!=previous&&auto.Back==0,"Automatic actual hits vary successive spots");previous=auto.Front;
        }
        var group=new HapticAttackGroup(1,IncomingHapticKind.Shotgun);
        Check(!group.Accept(ImpactSide.Front,false),"Miss/block does not consume shotgun group");
        Check(!group.Accept(ImpactSide.Unknown,true),"Unknown direction does not consume a directional pattern");
        Check(group.Accept(ImpactSide.Front,true),"First accepted pellet creates one blast");
        for(int i=0;i<31;i++)Check(!group.Accept(ImpactSide.Front,true),"Remaining pellets/difficulty iterations never replay blast");
        Check(group.Accept(ImpactSide.Back,true),"Separate struck panel preserved");
        Check(new HapticAttackGroup(2,IncomingHapticKind.Shotgun).Accept(ImpactSide.Front,true),"Concurrent attacker has independent group");
        Check(HapticPatterns.Side(0,1,0,1)==ImpactSide.Front&&HapticPatterns.Side(0,1,0,-1)==ImpactSide.Back,"Front/back direction");
        Check(HapticPatterns.Side(1,0,-1,0)==ImpactSide.Back&&HapticPatterns.Side(1,0,1,0)==ImpactSide.Front,"Rotated facing direction");
        Check(HapticPatterns.Side(0,0,1,0)==ImpactSide.Unknown&&HapticPatterns.Side(float.NaN,1,0,1)==ImpactSide.Unknown,"Invalid/vertical origin stays unknown");
        Check(HapticPatterns.Side(float.MaxValue,float.MaxValue,float.MaxValue,-float.MaxValue)==ImpactSide.Front,"Finite extreme components do not overflow the directional dot product");
        var unknown=HapticPatterns.Incoming(IncomingHapticKind.Unknown,ImpactSide.Front,0,1);
        Check(unknown.Front==0&&unknown.Back==0&&unknown.Amplitude==0,"Unclassified producer does not fabricate an impact");
        foreach(float strength in new[]{0f,.01f,.4f,1f}){
            float shot=HapticPatterns.Recoil(3,false,out _)*strength;
            float rifle=HapticPatterns.Recoil(14,false,out _)*strength;
            float small=HapticPatterns.Recoil(11,false,out _)*strength;
            Check(strength==0?shot==0:shot>rifle&&rifle>small,"Recoil hierarchy survives scaling without clipping");
        }
        Check(HapticPatterns.Recoil(10,true,out _)>HapticPatterns.Recoil(10,false,out _),"Charged incinerator pulse stronger than fuel flutter");
        Check(HapticPatterns.Strength(float.PositiveInfinity)==0&&HapticPatterns.Strength(float.NaN)==0,"Nonfinite strength stays silent");
        Console.WriteLine($"PASS {checks} accepted-pattern policy checks. Authored motor layout/strengths remain tactile-unverified; no native hooks or real Player connection.");
    }
}
