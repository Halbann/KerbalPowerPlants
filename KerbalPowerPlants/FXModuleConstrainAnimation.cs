using UnityEngine;
using System;
using System.Collections.Generic;

namespace KerbalPowerPlants;

// Continuously scrubs an Animation clip's normalized time to match the local
// rotation of a target transform on a chosen axis. Modelled on Blender's
// Action Constraint with target = a bone, source = rotation, output = action
// Only rotation for now.
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

    // Private fields.
    private Transform target;
    private Animation animator;
    private AnimationState clipState;
    private Quaternion initRot;

    // Public fields.
    public List<Func<float, float>> modifiers = [];

    #region Lifetime

    public override void OnStart(StartState state)
    {
        if (!SceneValid())
            return;

        // Find target transform. In blender terms, this the bone that drives the constraint.
        target = part.FindModelTransform(targetName);
        if (target == null)
        {
            ErrorAndDisable($"target transform '{targetName}' not found");
            return;
        }

        // Cache the authored pose so we can extract per-axis twist relative to it. Relies on
        // this module's OnStart running before anything else mutates target.localRotation; the
        // orchestrator is listed after us in the cfg to keep that invariant.
        initRot = target.localRotation;

        // Find the animator component.
        var animators = part.FindModelAnimators(animationName);
        if (animators.Length == 0)
        {
            ErrorAndDisable($"animation '{animationName}' not found");
            return;
        }

        animator = animators[0];

        // Find the animation clip.
        clipState = animator[animationName];
        if (clipState == null)
        {
            ErrorAndDisable($"clip '{animationName}' missing from Animation component");
            return;
        }

        clipState.layer = animationLayer;
        clipState.wrapMode = WrapMode.Once;

        // OnEnable fires before OnStart on first activation and no-ops because
        // anim is still null. Now that the refs are ready, start the clip.
        if (enabled)
            StartSampling();
    }

    protected void OnEnable()
    {
        if (!SceneValid())
            return;

        StartSampling();
    }

    protected void OnDisable()
    {
        if (!SceneValid())
            return;

        // Final sample so bones land on the current target angle, then stop.
        SampleNow();
    }

    protected void LateUpdate()
    {
        if (!SceneValid())
            return;

        SampleNow();
    }

    #endregion

    #region Functions

    private bool SceneValid() =>
        HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight;

    private void ErrorAndDisable(string message)
    {
        Debug.LogError($"[KerbalPowerPlants]: FXModuleConstrainAnimation on '{part.name}': {message}");
        enabled = false;
        return;
    }

    private void StartSampling()
    {
        if (clipState == null || animator == null)
            return;

        clipState.enabled = true;
        clipState.weight = 1f;
        clipState.speed = 0f;

        animator.Play(animationName);

        SampleNow();
    }

    public void SampleNow()
    {
        float angle = ReadAngle();
        float t = Mathf.InverseLerp(angleMin, angleMax, angle);

        foreach (var modifier in modifiers)
            t = modifier(t);

        clipState.normalizedTime = Mathf.Clamp01(t);
        animator.Sample();
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
