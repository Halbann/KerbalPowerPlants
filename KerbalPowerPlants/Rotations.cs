using UnityEngine;

namespace KerbalPowerPlants;

internal static class Rotations
{
    // Signed swing-twist angle (degrees) of quaternion `q` around unit `axis`. Stable even when
    // the rotation also has components along orthogonal axes, which is what makes it the right
    // tool when several actuators stack rotations on the same transform.
    //
    // For q = AngleAxis(a, X) * AngleAxis(b, Y), TwistAngle(q, X) returns exactly a and
    // TwistAngle(q, Y) returns exactly b -- the cross-axis bleed that breaks Euler decomposition
    // doesn't appear here because the projection cleanly separates the two components.
    public static float TwistAngle(Quaternion q, Vector3 axis)
    {
        float projAlongAxis = q.x * axis.x + q.y * axis.y + q.z * axis.z;
        float deg = 2f * Mathf.Atan2(projAlongAxis, q.w) * Mathf.Rad2Deg;
        if (deg > 180f) deg -= 360f;
        else if (deg < -180f) deg += 360f;
        return deg;
    }
}
