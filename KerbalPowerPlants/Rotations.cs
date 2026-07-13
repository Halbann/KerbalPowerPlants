using UnityEngine;

namespace KerbalPowerPlants;

internal static class Rotations
{
    // Signed swing-twist angle (degrees) of quaternion `q` around unit `axis`.
    public static float TwistAngle(Quaternion q, Vector3 axis)
    {
        float projAlongAxis = q.x * axis.x + q.y * axis.y + q.z * axis.z;
        float deg = 2f * Mathf.Atan2(projAlongAxis, q.w) * Mathf.Rad2Deg;

        if (deg > 180f)
            deg -= 360f;
        else if (deg < -180f)
            deg += 360f;

        return deg;
    }
}
