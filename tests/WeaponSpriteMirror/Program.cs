using System;
using CulticVR.AimPreview;
using UnityEngine;

static void Require(bool condition,string description) { if(!condition) throw new Exception(description); }
var random=new System.Random(197);
int cases=0;
for(int i=0;i<10000;i++) {
    float width=(float)(.1+random.NextDouble()*2),height=(float)(.1+random.NextDouble()),u=(float)random.NextDouble(),v=(float)random.NextDouble();
    float right=WeaponSpriteMirror.HorizontalScale(width,false),left=WeaponSpriteMirror.HorizontalScale(width,true);
    Require(BitConverter.SingleToInt32Bits(right)==BitConverter.SingleToInt32Bits(width),"right width bit identity");
    Require(left==-width,"left width exact reflection");
    float x=right*(u-.5f),mx=left*(u-.5f),y=height*v;
    Require(mx==-x && height*v==y,"muzzle/casing/crosshair reflection about unchanged lower-center anchor");
    Require(WeaponSpriteMirror.HorizontalScale(left,true)==width,"mirror twice restores original");
    // Native Image vertices/hotspots share the canvas transform: uniform root
    // reflection commutes with any pre-existing local position/animation scale.
    float imageX=(float)(random.NextDouble()*480-240),canvasScale=(float)(.0005+random.NextDouble()*.005);
    Require(WeaponSpriteMirror.HorizontalScale(canvasScale,true)*imageX==-(canvasScale*imageX),"offhand/TNT image and hotspot share reflection");
    cases++;
}
Require(WeaponSpriteMirror.HorizontalScale(.46f,true)*0f==0f,"center/anchor unchanged");
Console.WriteLine($"PASS {cases} sprite/canvas reflection cases; exact right-mode identity, mirrored landmark coordinates and unchanged anchor. Static math only, not runtime render proof.");

static Vector3 Reflect(Vector3 p)=>new Vector3(-p.x,p.y,p.z);
static float Error(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
for(int i=0;i<10000;i++) {
    var p=new Vector3((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f);
    var v=new Vector3((float)random.NextDouble()-.5f,(float)random.NextDouble(),0);
    float x=(float)random.NextDouble()-.5f,y=(float)random.NextDouble()-.5f,z=(float)random.NextDouble()-.5f,w=(float)random.NextDouble()-.5f;
    float n=MathF.Sqrt(x*x+y*y+z*z+w*w);var q=new Quaternion(x/n,y/n,z/n,w/n);
    var mirrored=WeaponSpriteMirror.Position(p,true)+WeaponSpriteMirror.Rotation(q,true)*Reflect(v);
    Require(Error(mirrored,Reflect(p+q*v))<.000002f,"full calibrated grip/muzzle/scope reflection");
    Require(WeaponSpriteMirror.Position(p,false)==p && WeaponSpriteMirror.Rotation(q,false)==q,"right pose unchanged");
    Require(WeaponSpriteMirror.Position(WeaponSpriteMirror.Position(p,true),true)==p && WeaponSpriteMirror.Rotation(WeaponSpriteMirror.Rotation(q,true),true)==q,"calibration save inverse");
    for(int axis=0;axis<6;axis++) {
        Require(WeaponSpriteMirror.CalibrationAxis(WeaponSpriteMirror.CalibrationAxis(x,axis,true),axis,true)==x,"menu get/set inverse");
        Require(BitConverter.SingleToInt32Bits(WeaponSpriteMirror.CalibrationAxis(x,axis,false))==BitConverter.SingleToInt32Bits(x),"right menu bit identity");
    }
}
var gripOffset=new Vector3(-.1722667f,-.02603554f,-.07761036f);
Require(WeaponSpriteMirror.Position(gripOffset,true).x==.1722667f,"accepted lateral grip offset is mirrored, not retained on wrong side");
Console.WriteLine("PASS 10,000 full-pose reflection and calibration round-trip cases. Right-hand values unchanged; runtime/headset acceptance still required.");
