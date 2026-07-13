using System;
using System.Collections.Generic;
using UnityEngine;

namespace KerbalPowerPlants;

// Continuously scrubs an Animation clip's normalized time to match the local
// rotation of a target transform on a chosen axis.
// todo: generic drivers, not just rotation.
public class FXModuleConstrainAnimation : PartModule
{
    public enum Axis { X, Y, Z }

    // Config.
    [KSPField] public string animationName = string.Empty;
    [KSPField] public string targetName = string.Empty;
    [KSPField] public Axis axis = Axis.X;
    [KSPField] public float angleMin = -90f;
    [KSPField] public float angleMax = 0f;
    [KSPField] public int animationLayer = 5;

    // Smoothing on the normalized clip time. accel <= 0 disables (snaps).
    [KSPField] public float smoothAccel = 0f;
    [KSPField] public float smoothMaxSpeed = Mathf.Infinity;

    private Transform target;
    private AnimUtils.Sampler anim;
    private Quaternion initRot;

    // Exposed so a caller can rewrite the smoothed state.
    public SymmetricSmoothDamp damper;

    // Modifiers.
    public List<Func<float, float>> modifiers = [];
    public bool modifiersPostSmoothing = false;

    public bool Settled => anim == null || damper.Settled;

    // Last sampled clip position (normalized progress, post-modifiers).
    private float sampled;
    public float Current => sampled;

    #region Lifetime

    public override void OnStart(StartState state)
    {
        if (!SceneValid())
            return;

        // Find target transform. In blender terms, this the bone that drives the constraint.
        target = part.FindModelTransform(targetName);
        if (target == null)
        {
            this.ErrorAndDisable($"target transform '{targetName}' not found");
            return;
        }

        // Cache the authored pose so we can extract per-axis twist relative to it.
        initRot = target.localRotation;

        anim = AnimUtils.CreateSampler(part, animationName, animationLayer);
        if (anim == null)
        {
            this.ErrorAndDisable($"failed to create animation sampler for '{animationName}'");
            return;
        }

        damper = new SymmetricSmoothDamp(0, smoothAccel, smoothMaxSpeed);
        Sample(snap: true);
    }

    protected void OnDisable()
    {
        if (!SceneValid())
            return;

        Sample(snap: true);
    }

    protected void LateUpdate()
    {
        if (!SceneValid())
            return;

        Sample();
    }

    #endregion

    #region Functions

    private bool SceneValid() =>
        HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight;

    // Ease the clip time toward the target angle.
    public void Sample(bool snap = false)
    {
        // OnEnable/OnDisable can fire before OnStart builds the sampler.
        if (anim == null)
            return;

        float raw = Mathf.InverseLerp(angleMin, angleMax, ReadAngle());

        float t = modifiersPostSmoothing
            ? ApplyModifiers(Step(raw, snap))
            : Step(ApplyModifiers(raw), snap);

        sampled = Mathf.Clamp01(t);
        anim.Sample(sampled);
    }

    private float Step(float value, bool snap)
    {
        if (snap)
        {
            damper.Reset(value);
            return value;
        }

        damper.Settings(smoothAccel, smoothMaxSpeed);
        return damper.UpdateTo(value, Time.deltaTime);
    }        

    private float ApplyModifiers(float t)
    {
        foreach (var mod in modifiers)
            if (mod != null)
                t = mod(t);

        return t;
    }

    private float ReadAngle()
    {
        Quaternion rel = Quaternion.Inverse(initRot) * target.localRotation;

        Vector3 a = axis switch
        {
            Axis.X => Vector3.right,
            Axis.Y => Vector3.up,
            Axis.Z => Vector3.forward,
            _ => Vector3.right,
        };

        return Rotations.TwistAngle(rel, a);
    }

    #endregion
}
