using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    // The same target and gaze calculation is used by presentation and input capture.
    // Head/controller poses must share a coordinate space. No raycast or eye-specific optics.
    public static class PalmAttention
    {
        public const float EnterGazeDot=.94f, ExitGazeDot=.90f;
        public static Vector3 WristCenter(bool left,Vector3 hand,Quaternion rotation) =>
            hand-rotation*Vector3.forward*.115f+rotation*(left?Vector3.right:Vector3.left)*.035f;
        public static bool ValidRotation(Quaternion q) {
            float n=Quaternion.Dot(q,q);return !float.IsNaN(n) && !float.IsInfinity(n) && Mathf.Abs(n-1f)<.01f;
        }
        public static bool LookingAt(Vector3 head,Quaternion headRotation,Vector3 target,bool retaining)
        {
            if(!ScopeSightingMath.Finite(head) || !ScopeSightingMath.Finite(target) || !ValidRotation(headRotation))return false;
            var direction=target-head;float squared=direction.sqrMagnitude;
            return squared>.0001f && !float.IsInfinity(squared) &&
                Vector3.Dot(headRotation*Vector3.forward,direction)>=(retaining?ExitGazeDot:EnterGazeDot)*Mathf.Sqrt(squared);
        }
        public static bool LookingAtPalm(bool left,Vector3 head,Quaternion headRotation,
            Vector3 hand,Quaternion handRotation,bool retaining)
        {
            if(!ValidRotation(handRotation) || !LookingAt(head,headRotation,hand,retaining))return false;
            var towardHead=head-hand;
            return Vector3.Dot(handRotation*(left?Vector3.right:Vector3.left),towardHead)>=
                (retaining?.65f:.75f)*Mathf.Sqrt(towardHead.sqrMagnitude);
        }
    }
}
