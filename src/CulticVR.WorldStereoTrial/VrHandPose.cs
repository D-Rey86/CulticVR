using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR;
using XRDevice=UnityEngine.XR.InputDevice;
using XRUsage=UnityEngine.XR.CommonUsages;

namespace CulticVR.WorldStereoTrial
{
    public static class VrHandPose
    {
        private static UnityEngine.InputSystem.InputDevice? _pointerDevice;
        private static Vector3Control? _position;
        private static QuaternionControl? _rotation;
        private static XRDevice _left,_right,_head;
        private static int _frame=-1;
        private static float _nextDiscovery;
        private static bool _pointerValid,_leftValid,_rightValid,_headValid;
        private static Vector3 _pointerPosition,_leftPosition,_rightPosition,_headPosition;
        private static Quaternion _pointerRotation,_leftRotation,_rightRotation,_headRotation;
        public static XRNode WeaponNode=>VrSettings.LeftWeapon?XRNode.LeftHand:XRNode.RightHand;
        public static XRNode OffhandNode=>VrSettings.LeftWeapon?XRNode.RightHand:XRNode.LeftHand;
        public static bool TryHead(out Vector3 position,out Quaternion rotation)
        { Sample(); position=_headPosition; rotation=_headRotation; return _headValid; }
        public static bool TryGrip(XRNode hand,out Vector3 position,out Quaternion rotation)
        {
            Sample(); bool left=hand==XRNode.LeftHand;
            position=left?_leftPosition:_rightPosition; rotation=left?_leftRotation:_rightRotation;
            return left?_leftValid:_rightValid;
        }
        public static bool TryPointer(XRNode hand,out Vector3 position,out Quaternion rotation)
        {
            Sample(); position=_pointerPosition; rotation=_pointerRotation;
            if(!_pointerValid) return false;
            if(hand==XRNode.RightHand) return true;
            if(!_leftValid||!_rightValid) return false;
            // Same measured aim-from-grip transform used by accepted offhand
            // artwork. The custom left pointer can alias right; retain the
            // standard left grip position/orientation instead.
            position=_leftPosition;
            rotation=_leftRotation*Quaternion.Inverse(_rightRotation)*_pointerRotation;
            return true;
        }
        private static bool Tracked(XRDevice device)=>device.isValid &&
            device.TryGetFeatureValue(XRUsage.isTracked,out bool tracked)&&tracked;
        private static void Sample()
        {
            if(_frame==Time.frameCount) return; _frame=Time.frameCount;
            if(!_left.isValid) _left=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if(!_right.isValid) _right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if(!_head.isValid) _head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            _leftValid=Tracked(_left)&&_left.TryGetFeatureValue(XRUsage.devicePosition,out _leftPosition)&&_left.TryGetFeatureValue(XRUsage.deviceRotation,out _leftRotation);
            _rightValid=Tracked(_right)&&_right.TryGetFeatureValue(XRUsage.devicePosition,out _rightPosition)&&_right.TryGetFeatureValue(XRUsage.deviceRotation,out _rightRotation);
            _headValid=_head.isValid&&(!_head.TryGetFeatureValue(XRUsage.isTracked,out bool ht)||ht)&&
                _head.TryGetFeatureValue(XRUsage.devicePosition,out _headPosition)&&_head.TryGetFeatureValue(XRUsage.deviceRotation,out _headRotation);
            _pointerValid=false;
            if(!_rightValid) return;
            if(_pointerDevice==null||!_pointerDevice.added||_position==null||_rotation==null) {
                if(Time.unscaledTime<_nextDiscovery) return; _nextDiscovery=Time.unscaledTime+.5f;
                _pointerDevice=null; _position=null; _rotation=null;
                foreach(var device in InputSystem.devices) {
                    if(device.description.interfaceName?.IndexOf("XR",StringComparison.OrdinalIgnoreCase)<0) continue;
                    bool right=false; foreach(var usage in device.usages) if(usage=="RightHand") { right=true; break; }
                    if(!right) continue;
                    foreach(var control in device.allControls) {
                        if(control is Vector3Control p && control.path.EndsWith("/pointer/position",StringComparison.OrdinalIgnoreCase)) _position=p;
                        else if(control is QuaternionControl r && control.path.EndsWith("/pointer/rotation",StringComparison.OrdinalIgnoreCase)) _rotation=r;
                    }
                    if(_position!=null&&_rotation!=null) { _pointerDevice=device; break; }
                    _position=null; _rotation=null;
                }
            }
            if(_position==null||_rotation==null) return;
            _pointerPosition=_position.ReadValue(); _pointerRotation=_rotation.ReadValue();
            _pointerValid=ScopeSightingMath.Finite(_pointerPosition)&&Quaternion.Dot(_pointerRotation,_pointerRotation)>.5f;
        }
    }
}
