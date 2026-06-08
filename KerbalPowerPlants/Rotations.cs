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

    public static Quaternion SmoothDamp(Quaternion rot, Quaternion target, ref Vector4 deriv, float time, float maxSpeed, float deltaTime)
    {
        if (deltaTime < Mathf.Epsilon)
            return rot;

        // account for double-cover
        float Dot = Quaternion.Dot(rot, target);

        float Multi = Dot > 0f ? 1f : -1f;
        target.x *= Multi;
        target.y *= Multi;
        target.z *= Multi;
        target.w *= Multi;

        // smooth damp (nlerp approx)
        Vector4 Result = new(
            Mathf.SmoothDamp(rot.x, target.x, ref deriv.x, time, maxSpeed, deltaTime),
            Mathf.SmoothDamp(rot.y, target.y, ref deriv.y, time, maxSpeed, deltaTime),
            Mathf.SmoothDamp(rot.z, target.z, ref deriv.z, time, maxSpeed, deltaTime),
            Mathf.SmoothDamp(rot.w, target.w, ref deriv.w, time, maxSpeed, deltaTime)
        );

        Result.Normalize();

        // ensure deriv is tangent
        Vector4 derivError = Vector4.Project(deriv, Result);

        deriv.x -= derivError.x;
        deriv.y -= derivError.y;
        deriv.z -= derivError.z;
        deriv.w -= derivError.w;

        return new Quaternion(Result.x, Result.y, Result.z, Result.w);
    }

}
