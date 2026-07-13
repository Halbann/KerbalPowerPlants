using UnityEngine;

namespace KerbalPowerPlants;

// Continuously rotates a target transform around a configured local axis by an angle
// proportional to the rotation of a source transform around its own configured axis.
public class FXModuleCopyRotation : PartModule
{
    public enum Axis { X, Y, Z }

    // Config.
    [KSPField] public string sourceName = string.Empty;
    [KSPField] public Axis sourceAxis = Axis.Y;
    [KSPField] public string targetName = string.Empty;
    [KSPField] public Axis targetAxis = Axis.Z;
    [KSPField] public float gain = 1f;

    // Smoothing on the copied angle (degrees).
    [KSPField] public float smoothAccel = 0f;
    [KSPField] public float smoothMaxSpeed = Mathf.Infinity;

    // Private fields.
    private Transform source;
    private Transform target;
    private Quaternion sourceInitRot;
    private Quaternion targetInitRot;
    private SymmetricSmoothDamp damper;

    public bool Settled => source == null || target == null || damper.Settled;

    #region Lifetime

    public override void OnStart(StartState state)
    {
        if (!SceneValid())
            return;

        source = part.FindModelTransform(sourceName);
        if (source == null)
        {
            this.ErrorAndDisable($"source transform '{sourceName}' not found");
            return;
        }

        target = part.FindModelTransform(targetName);
        if (target == null)
        {
            this.ErrorAndDisable($"target transform '{targetName}' not found");
            return;
        }

        // Authored poses cached before anyone mutates them; nothing writes these
        // transforms during OnStart.
        sourceInitRot = source.localRotation;
        targetInitRot = target.localRotation;

        damper = new(0, smoothAccel, smoothMaxSpeed);
        Sample(snap: true);
    }

    protected void OnDisable()
    {
        if (!SceneValid() || target == null)
            return;

        target.localRotation = targetInitRot;
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

    // Ease the target angle toward the copied source angle.
    public void Sample(bool snap = false)
    {
        if (source == null || target == null)
            return;

        float a = TargetAngle();
        if (!snap)
        {
            damper.Settings(smoothAccel, smoothMaxSpeed);
            a = damper.UpdateTo(a, Time.deltaTime);
        }

        target.localRotation = targetInitRot * Quaternion.AngleAxis(a, AxisVector(targetAxis));
    }        

    private float TargetAngle()
    {
        Quaternion rel = Quaternion.Inverse(sourceInitRot) * source.localRotation;
        return gain * Rotations.TwistAngle(rel, AxisVector(sourceAxis));
    }

    private static Vector3 AxisVector(Axis a) => a switch
    {
        Axis.X => Vector3.right,
        Axis.Y => Vector3.up,
        Axis.Z => Vector3.forward,
        _ => Vector3.right,
    };

    #endregion
}
