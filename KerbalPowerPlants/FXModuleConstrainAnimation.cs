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

    // Private fields.
    private Transform target;
    private AnimUtils.Sampler anim;
    private Quaternion initRot;
    private SymmetricSmoothDamp damper;

    // Modifiers.
    public List<Func<float, float>> modifiers = [];

    public bool Settled => anim == null || damper.Settled;

    // Smoothed position (normalized, post-modifiers).
    public float Current => damper.current;

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

        float t = TargetT();

        if (snap)
            damper.Reset(t);
        else
        {
            damper.Settings(smoothAccel, smoothMaxSpeed);
            t = damper.UpdateTo(t, Time.deltaTime);
        }

        // todo: switch between modifier-space and progress-space.

        // Remap from elevation to progress after smoothing,
        // so smoothing happens in elevation space rather than progress-space.
        //foreach (var mod in modifiers)
        //    if (mod != null)
        //        t = mod(t);

        anim.Sample(Mathf.Clamp01(t));
    }

    private float TargetT()
    {
        float angle = ReadAngle();
        float t = Mathf.InverseLerp(angleMin, angleMax, angle);

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
