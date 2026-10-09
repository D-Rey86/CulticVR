using System;
using UnityEngine;
using CulticVR.AimPreview;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
var view=Vector3.zero;var tip=new Vector3(0,0,1);
Check(IgnitionContactMath.InContact(tip,tip,view,.008f,.12f),"same tip");
Check(IgnitionContactMath.InContact(tip,new Vector3(.005f,0,1),view,.008f,.12f),"visible contact");
Check(!IgnitionContactMath.InContact(tip,new Vector3(.02f,0,1),view,.008f,.12f),"outside tip radius");
Check(IgnitionContactMath.InContact(tip,new Vector3(0,0,1.11f),view,.008f,.12f),"accepted TNT depth allowance");
Check(!IgnitionContactMath.InContact(tip,new Vector3(0,0,1.13f),view,.008f,.12f),"remote overlap rejected");
Check(!IgnitionContactMath.InContact(view,tip,view,.008f,.12f),"zero depth rejected");
Check(!IgnitionContactMath.InContact(new Vector3(float.NaN,0,1),tip,view,.008f,.12f),"invalid rejected");
var random=new System.Random(917);
for(int i=0;i<10000;i++) {
    var f=new Vector3((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f,1);
    var w=f+new Vector3((float)random.NextDouble()*.02f,0,(float)random.NextDouble()*.2f);
    var ff=f-view;var ww=w-view;float fd=ff.magnitude,wd=ww.magnitude;
    bool old=Vector3.Distance(ff/fd,ww/wd)*Mathf.Min(fd,wd)<=.008f&&Mathf.Abs(fd-wd)<=.12f;
    bool current=IgnitionContactMath.InContact(f,w,view,.008f,.12f);
    Check(old==current,"exact accepted TNT formula replay");
    Check(current==IgnitionContactMath.InContact(new Vector3(-f.x,f.y,f.z),new Vector3(-w.x,w.y,w.z),view,.008f,.12f),"left/right reflection invariant");
}
Console.WriteLine("PASS 10,000 accepted-contact formula/reflection cases and contact/depth/invalid guards. Static math, not native runtime ignition.");
// Exact reviewed crop alpha spans for cloth rows13..55; separated glass is
// excluded above row20. Row53 has a transparent two-pixel notch.
int[] left={13,12,12,12,12,12,12,11,11,11,11,11,10,10,10,9,9,9,8,8,7,7,7,6,6,5,5,5,4,4,3,3,3,2,2,3,4,5,7,10,13,25,28};
int[] right={32,32,32,32,32,32,32,31,31,31,31,31,30,30,29,30,30,30,30,31,31,31,32,32,32,33,33,33,32,32,32,32,32,31,31,31,31,30,30,30,30,30,29};
var origin=new Vector3(-.03f,.04f,1);var axisX=new Vector3(.001f,0,0);var axisY=new Vector3(0,-.001f,0);
var rotation=new Quaternion(.1f,.2f,.3f,(float)Math.Sqrt(.86));var translatedViewer=new Vector3(2,-1,3);
int covered=0;
for(int row=0;row<70;row++)for(int column=0;column<70;column++) {
    var pixel=new Vector2(column+.5f,row+.5f);
    bool expected=row>=13 && row<=55 && column>=left[row-13] && column<right[row-13] && !(row==53 && column>=21 && column<23);
    Check(ClothContactMath.Contains(pixel,MolotovCloth.Outline)==expected,$"exact alpha coverage {column},{row}");
    var point=origin+axisX*pixel.x+axisY*pixel.y;
    bool actual=ClothContactMath.InContact(point,view,origin,axisX,axisY,MolotovCloth.Outline,.000001f,.12f);
    Check(actual==expected,$"filled cloth contact {column},{row}");
    Check(ClothContactMath.InContact(point,view,origin,-axisX,axisY,MolotovCloth.Outline,.000001f,.12f)==
        ClothContactMath.InContact(new Vector3(-point.x-.06f,point.y,point.z),view,origin,axisX,axisY,MolotovCloth.Outline,.000001f,.12f),"mirror geometry");
    if(expected) {
        covered++;
        var rotated=translatedViewer+rotation*point;
        Check(ClothContactMath.InContact(rotated,translatedViewer,translatedViewer+rotation*origin,rotation*axisX,rotation*axisY,MolotovCloth.Outline,.000001f,.12f),"rotated/scaled actual quad");
        Check(!ClothContactMath.InContact(point.normalized*(point.magnitude+.2f),view,origin,axisX,axisY,MolotovCloth.Outline,.000001f,.12f),"remote cloth overlap");
    }
}
Check(covered==960,"full960-pixel rag region");
var tail=origin+axisX*16+axisY*53;
Check(ClothContactMath.InContact(tail,view,origin,axisX,axisY,MolotovCloth.Outline,.008f,.12f),"original tail remains accepted");
Check(!ClothContactMath.InContact(new Vector3(float.NaN,0,1),view,origin,axisX,axisY,MolotovCloth.Outline,.008f,.12f),"cloth invalid flame");
Check(!ClothContactMath.InContact(tail,view,origin,axisX,axisX,MolotovCloth.Outline,.008f,.12f),"degenerate quad");
Check(!ClothContactMath.InContact(view,view,origin,axisX,axisY,MolotovCloth.Outline,.008f,.12f),"cloth zero viewer distance");
var edgePoint=origin+axisX*2+axisY*47;
Check(ClothContactMath.InContact(edgePoint-axisX*3,view,origin,axisX,axisY,MolotovCloth.Outline,.008f,.12f),"small edge tolerance retained");
Check(!ClothContactMath.InContact(edgePoint-axisX*30,view,origin,axisX,axisY,MolotovCloth.Outline,.008f,.12f),"outside cloth tolerance rejected");
var parallelFlame=new Vector3(1,0,0);
Check(!ClothContactMath.InContact(parallelFlame,view,origin,axisX,axisY,MolotovCloth.Outline,.008f,.12f),"parallel distant cloth rejected");
// Accepted original tip contacts remain possible throughout reflection and
// size changes; new area does not weaken the existing angular/depth gates.
for(int i=0;i<1000;i++) {
    float scale=.2f+(float)random.NextDouble()*4;
    var sx=axisX*scale*(i%2==0?1:-1);var sy=axisY*scale;
    var oldTip=origin+sx*(270f-MolotovCloth.CropX)+sy*53;
    var f=oldTip+new Vector3((float)random.NextDouble()*.006f,0,(float)random.NextDouble()*.1f);
    if(IgnitionContactMath.InContact(f,oldTip,view,.008f,.12f))
        Check(ClothContactMath.InContact(f,view,origin,sx,sy,MolotovCloth.Outline,.008f,.12f),"old tip contacts retained at scale/mirror");
}
Console.WriteLine("PASS exact960-pixel cloth/transparent-notch coverage, empty glass/hand/background, rotated and mirrored geometry, remote/invalid/degenerate guards. Headset test still pending.");
