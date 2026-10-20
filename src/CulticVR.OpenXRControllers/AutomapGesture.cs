using UnityEngine;

namespace CulticVR.OpenXRControllers
{
    // Look at the palm to toggle; looking away rearms. A short debounce rejects a passing glance.
    public struct AutomapGesture
    {
        private bool _latched,_looking;
        private float _started;
        public void Reset() { _latched=false;_looking=false; }
        public void CancelPending() => _looking=false;
        public bool Step(bool eligible,bool left,float now,Vector3 head,Quaternion headRotation,
            Vector3 hand,Quaternion handRotation)
        {
            if(!CulticVR.WorldStereoTrial.ScopeSightingMath.Finite(head) ||
                !CulticVR.WorldStereoTrial.ScopeSightingMath.Finite(hand) ||
                !CulticVR.WorldStereoTrial.PalmAttention.ValidRotation(headRotation) ||
                !CulticVR.WorldStereoTrial.PalmAttention.ValidRotation(handRotation) ||
                float.IsNaN(now) || float.IsInfinity(now)) { CancelPending();return false; }
            bool looking=CulticVR.WorldStereoTrial.PalmAttention.LookingAtPalm(left,head,headRotation,hand,handRotation,_latched);
            if(!looking) { Reset();return false; }
            if(_latched)return false;
            if(!eligible) { CancelPending();return false; }
            if(!_looking || now<_started) { _looking=true;_started=now;return false; }
            if(now-_started<.12f)return false;
            _latched=true;_looking=false;return true;
        }
    }
}
