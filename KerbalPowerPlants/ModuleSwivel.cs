using System;
using System.Linq;
using UnityEngine;

namespace KerbalPowerPlants;

public class ModuleSwivel : PartModule
{
    public enum Axis { X, Y, Z }
    public enum ElevationMap { Linear, TripleBearing }

    #region Config

    [KSPField] public bool instantInEditor = true;

    [KSPField] public string gimbalProxyTransform = "";
    [KSPField] public string swivelProxyTransform = "";
    [KSPField] public bool debugProxies = false;

    [KSPField] public Axis swivelAxis = Axis.X;
    [KSPField] public Axis rollAxis = Axis.Y;
    [KSPField] public float deployAngle = -90f;

    [KSPField] public ElevationMap elevationMap = ElevationMap.Linear;

    // Door sequencing, in normalized travel. Doors release the swivel at doorLead when
    // deploying; the door floor ramps 0..1 as swivel travel crosses [doorWait, doorClear].
    [KSPField] public float doorLead = 0.75f;
    [KSPField] public float doorWait = 0.15f;
    [KSPField] public float doorClear = 0.5f;

    // Door animation. Empty name means no doors.
    [KSPField] public string doorAnimationName = "";
    [KSPField] public int doorAnimationLayer = 1;
    [KSPField] public float doorSmoothAccel = 0f;
    [KSPField] public float doorSmoothMaxSpeed = Mathf.Infinity;

    // UI Text.
    [KSPField] public string deployText = "Engage Swivel";
    [KSPField] public string retractText = "Disengage Swivel";
    [KSPField] public string actionText = "Toggle Swivel";
    [KSPField] public string statusFieldName = "Swivel";
    [KSPField] public string statusDeploying = "Engaging...";
    [KSPField] public string statusDeployed = "Engaged";
    [KSPField] public string statusRetracting = "Disengaging...";
    [KSPField] public string statusRetracted = "Disengaged";

    #endregion

    #region PAW, Events and Actions

    [KSPField(guiActive = true, guiActiveEditor = false, guiName = "")]
    public string status = string.Empty;

    [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "")]
    public void Toggle() =>
        SetDeployed(!deploy);

    [KSPAction("")]
    public void ToggleAction(KSPActionParam param) =>
        Toggle();

    #endregion

    [KSPField(isPersistant = true)] public bool deploy = false;
    bool moving = false;
    bool settleArmed = false;
    bool modifierSpace = false;

    // Runtime.
    private Transform swivelProxy;
    private Transform gimbalProxy;
    private Quaternion swivelRestRot;
    private Quaternion swivelDeployedRot;
    private Quaternion gimbalRestRot;
    private bool abAllowed, abApplied;

    private ModuleGimbalTorque gimbal;
    private FXModuleConstrainAnimation constraint;
    private FXModuleCopyRotation copyRotation;
    private FlooredAnimation doors;
    private MultiModeEngine multimode;

    private bool Settled => 
        (constraint?.Settled ?? true) 
        && (copyRotation?.Settled ?? true)
        && (doors?.Settled ?? true);

    #region Lifetime

    public override void OnStart(StartState state)
    {
        // Set Text.
        Actions[nameof(ToggleAction)].guiName = actionText;
        Fields[nameof(status)].guiName = statusFieldName;
        UpdateText();

        // Find swivel proxy.
        swivelProxy = part.FindModelTransform(swivelProxyTransform);
        if (swivelProxy == null)
        {
            Logger.Error($"{part.name}: swivel proxy '{swivelProxyTransform}' not found");
            enabled = false;
            return;
        }

        swivelRestRot = swivelProxy.localRotation;
        swivelDeployedRot = swivelRestRot * Quaternion.AngleAxis(deployAngle, AxisVector(swivelAxis));

        // Find gimbal proxy.
        gimbalProxy = part.FindModelTransform(gimbalProxyTransform);
        if (gimbalProxy != null)
            gimbalRestRot = gimbalProxy.localRotation;

        // Find modules.
        gimbal = part.FindModulesImplementing<ModuleGimbalTorque>().FirstOrDefault();
        constraint = part.FindModulesImplementing<FXModuleConstrainAnimation>().FirstOrDefault();
        copyRotation = part.FindModulesImplementing<FXModuleCopyRotation>().FirstOrDefault();
        multimode = part.FindModulesImplementing<MultiModeEngine>().FirstOrDefault();

        // Door animation.
        if (!string.IsNullOrWhiteSpace(doorAnimationName))
        {
            doors = gameObject.AddComponent<FlooredAnimation>();
            doors.part = part;
            doors.animationName = doorAnimationName;
            doors.animationLayer = doorAnimationLayer;
            doors.smoothAccel = doorSmoothAccel;
            doors.smoothMaxSpeed = doorSmoothMaxSpeed;
            doors.open = deploy;
        }

        // Triple-bearing geometry needs the elevation remap for a linear gimbal response.
        if (elevationMap == ElevationMap.TripleBearing)
            constraint?.modifiers.Add(ElevationRemap);

        ApplySmoothingSpace();

        // moduleIsEnabled defaults true, so a stowed load must be disabled explicitly.
        EnableGimbal(deploy);

        SetAfterburnerAllowed(!deploy);

        if (!debugProxies)
        {
            gimbalProxy.gameObject.SetActive(false);
            swivelProxy.gameObject.SetActive(false);
        }
    }

    protected void Update()
    {
        if (swivelProxy == null)
            return;

        if (!deploy && !moving)
            return; // Nothing to do.

        // GimbalProxy never carries the deploy rotation, only deflection from its rest.
        Quaternion deflection = gimbalProxy != null
            ? Quaternion.Inverse(gimbalRestRot) * gimbalProxy.localRotation
            : Quaternion.identity;

        // Doors lead on deploy: the swivel is commanded down only once they're past doorLead.
        bool commandDeployed = deploy && (doors == null || doors.progress >= doorLead);
        swivelProxy.localRotation = commandDeployed ? swivelDeployedRot * deflection : swivelRestRot;

        DriveDoors();

        if (moving)
        {
            // One frame latch on checking Settled.
            if (!settleArmed)
            {
                settleArmed = true;
            }
            else if (Settled)
            {
                moving = false;
                UpdateText();
                ApplySmoothingSpace();

                if (!deploy)
                    SetAfterburnerAllowed(true);
            }
        }
    }

    protected void OnDestroy() =>
        constraint?.modifiers.Remove(ElevationRemap);

    #endregion

    #region Swivel

    public void SetDeployed(bool deploy)
    {
        if (this.deploy == deploy)
            return;

        this.deploy = deploy;
        moving = true;
        settleArmed = false;

        EnableGimbal(deploy);
        UpdateText();
        ApplySmoothingSpace();

        if (deploy)
            SetAfterburnerAllowed(false);
    }

    // Smooth in progress space during transitions, in modifier space once deployed and
    // settled. Reproject the damper across the change so its output has no seam.
    private void ApplySmoothingSpace()
    {
        if (constraint == null || elevationMap != ElevationMap.TripleBearing)
            return;

        bool want = deploy && !moving;
        if (want == modifierSpace)
            return;

        float value = constraint.damper.current;
        float slope = want ? ElevationUnmapSlope(value) : ElevationRemapSlope(value);
        float velocity = slope * constraint.damper.velocity;

        constraint.damper.current = want ? ElevationUnmap(value) : ElevationRemap(value);
        constraint.damper.velocity = float.IsNaN(velocity) || float.IsInfinity(velocity) ? 0f : velocity;
        constraint.modifiersPostSmoothing = want;
        modifierSpace = want;
    }

    // Doors follow the deploy command, held above a floor by the swivel's actual travel
    // so they can never close onto the nozzle; the ramp makes the close trail it down.
    private void DriveDoors()
    {
        if (doors == null)
            return;

        doors.open = deploy;
        doors.minProgress = constraint != null
            ? Mathf.InverseLerp(doorWait, doorClear, constraint.Current)
            : 0f;
    }

    private void EnableGimbal(bool on)
    {
        if (gimbal == null)
            return;

        gimbal.moduleIsEnabled = on;
        gimbal.UpdateToggles();

        if (!on && gimbalProxy != null)
            gimbalProxy.localRotation = gimbalRestRot;
    }

    // Lock the engine to its primary mode unless fully stowed, and hide the mode
    // switch, so the afterburner (secondary mode) is only available in level flight.
    private void SetAfterburnerAllowed(bool allow)
    {
        if (multimode == null || (abApplied && allow == abAllowed))
            return;

        abApplied = true;
        abAllowed = allow;

        if (!allow && !multimode.runningPrimary)
            multimode.SetPrimary(HighLogic.LoadedSceneIsFlight);

        multimode.Events[nameof(MultiModeEngine.ModeEvent)].guiActive = allow;
        multimode.Events[nameof(MultiModeEngine.ModeEvent)].guiActiveEditor = allow;
        multimode.Actions[nameof(MultiModeEngine.ModeAction)].active = allow;

        multimode.moduleIsEnabled = allow;

        // loadFailure prevents the afterburner being turned on by other mods and AG.
        if (multimode.primaryEngine != null && multimode.secondaryEngine != null)
            multimode.loadFailure = !allow;
    }

    private void UpdateText()
    {
        status = deploy ? (moving ? statusDeploying : statusDeployed)
            : (moving ? statusRetracting : statusRetracted);

        Events[nameof(Toggle)].guiName = deploy ? retractText : deployText;
    }

    #endregion

    #region Elevation remap

    // Triple-bearing chain: sin(elevation) = K sin^2(pi p) + cos(pi p)/2 + 1/2, solved
    // for progress p. Maps the linear sweep fraction to true bearing travel.
    private static readonly double K = (2.0 * Math.Sqrt(2.0) - 3.0) / 4.0;

    // Normalized modifier interface: elevation fraction [0, 1] <-> progress [0, 1].
    private float ElevationRemap(float t) =>
        (float)ProgressForElevation(Mathf.Lerp(90f, 0f, t));

    private float ElevationUnmap(float p) =>
        Mathf.InverseLerp(90f, 0f, (float)ElevationForProgress(p));

    private static double ProgressForElevation(double deg)
    {
        double s = Math.Sin(Clamp(deg, 0.0, 90.0) * (Math.PI / 180.0));
        double a = K, b = -0.5, c = -(K + 0.5) + s;
        double disc = b * b - 4.0 * a * c;
        double x = (-b - Math.Sqrt(disc)) / (2.0 * a);
        return Math.Acos(Clamp(x, -1.0, 1.0)) / Math.PI;
    }

    private static double ElevationForProgress(double p)
    {
        double x = Math.Cos(Clamp(p, 0.0, 1.0) * Math.PI);
        double s = K * (1.0 - x * x) + 0.5 * x + 0.5;
        return Math.Asin(Clamp(s, -1.0, 1.0)) * (180.0 / Math.PI);
    }

    // Slopes at the switch instant, so the reprojection preserves the damper velocity.
    private float ElevationRemapSlope(float t)
    {
        const float h = 1e-3f;
        float lo = Mathf.Max(0f, t - h), hi = Mathf.Min(1f, t + h);
        return (ElevationRemap(hi) - ElevationRemap(lo)) / (hi - lo);
    }

    private float ElevationUnmapSlope(float p) =>
        1f / ElevationRemapSlope(ElevationUnmap(p));

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;

    #endregion

    private static Vector3 AxisVector(Axis a) => a switch
    {
        Axis.X => Vector3.right,
        Axis.Y => Vector3.up,
        Axis.Z => Vector3.forward,
        _ => Vector3.right,
    };
}
