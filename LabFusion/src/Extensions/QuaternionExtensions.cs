using UnityEngine;

namespace LabFusion.Extensions
{
    public static class QuaternionExtensions
    {
        public static readonly Quaternion identity = Quaternion.identity;

        /// <summary>
        /// Returns only the rotation around world up (yaw), removing pitch and roll.
        /// </summary>
        public static Quaternion YawOnly(this Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            forward.y = 0f;

            // Looking straight up or down, fall back to the up axis tilted forward
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = rotation * Vector3.up;
                forward.y = 0f;

                if (forward.sqrMagnitude < 0.0001f)
                {
                    return Quaternion.identity;
                }
            }

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
