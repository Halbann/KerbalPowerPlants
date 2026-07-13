using UnityEngine;

namespace KerbalPowerPlants;

// Acceleration-limited easing toward a target.
// Symmetric speed up/down, no overshoot.
public struct SymmetricSmoothDamp(float initialValue, float acceleration, float maxSpeed)
{
    public float current = initialValue;
    public float target = initialValue;
    public float velocity = 0f;
    public float acceleration = acceleration;
    public float maxSpeed = maxSpeed;

    public SymmetricSmoothDamp(float initialValue, float acceleration)
        : this(initialValue, acceleration, Mathf.Infinity) { }

    public readonly bool Settled =>
        Mathf.Abs(current - target) < 1e-4f;

    public void Reset(float value)
    {
        current = target = value;
        velocity = 0f;
    }

    public void Settings(float acc, float maxSpeed)
    {
        acceleration = acc;
        this.maxSpeed = maxSpeed;
    }

    public float Update(float dt) => Update(current, target, dt);

    public float UpdateTo(float target, float dt) => Update(current, target, dt);

    public float UpdateFrom(float current, float dt) => Update(current, target, dt);

    public float Update(float current, float target, float dt)
    {
        this.target = target;

        // No easing configured: snap.
        if (acceleration <= 0f)
        {
            velocity = 0f;
            return this.current = target;
        }

        if (dt <= 0f)
            return this.current = current;

        // Fold any external displacement of current into velocity.
        if (current != this.current)
            velocity += (current - this.current) / dt;
        this.current = current;

        float toTarget = target - this.current;

        // Fastest speed that can still stop exactly on the target.
        float p = 0.5f * acceleration * dt;
        float maxApproach = Mathf.Sqrt(p * p + 2f * acceleration * Mathf.Abs(toTarget)) - p;
        float desiredVelocity = Mathf.Sign(toTarget) * Mathf.Min(Mathf.Max(maxApproach, 0f), maxSpeed);

        // Move velocity toward the desired value within this step's acceleration limit.
        float maxDelta = acceleration * dt;
        velocity += Mathf.Clamp(desiredVelocity - velocity, -maxDelta, maxDelta);

        // Semi-implicit Euler.
        return this.current += velocity * dt;
    }
}
