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

    // Private fields.
    private Transform source;
    private Transform target;
    private Quaternion sourceInitRot;
    private Quaternion targetInitRot;

    #region Lifetime

    public override void OnStart(StartState state)
    {
        if (!SceneValid())
            return;

        source = part.FindModelTransform(sourceName);
        if (source == null)
        {
            ErrorAndDisable($"source transform '{sourceName}' not found");
            return;
        }

        target = part.FindModelTransform(targetName);
        if (target == null)
        {
            ErrorAndDisable($"target transform '{targetName}' not found");
            return;
        }

        // Authored poses cached before anyone mutates them. Orchestrator runs after us per cfg.
        sourceInitRot = source.localRotation;
        targetInitRot = target.localRotation;
    }

    protected void OnEnable()
    {
        if (!SceneValid() || source == null || target == null)
            return;

        Apply();
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

        Apply();
    }

    #endregion

    #region Functions

    private bool SceneValid() =>
        HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight;

    private void ErrorAndDisable(string message)
    {
        Debug.LogError($"[KerbalPowerPlants]: FXModuleCopyRotation on '{part.name}': {message}");
        enabled = false;
    }

    public void Apply()
    {
        if (source == null || target == null)
            return;

        Quaternion rel = Quaternion.Inverse(sourceInitRot) * source.localRotation;
        float sourceAngle = Rotations.TwistAngle(rel, AxisVector(sourceAxis));

        target.localRotation = targetInitRot * Quaternion.AngleAxis(gain * sourceAngle, AxisVector(targetAxis));
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
