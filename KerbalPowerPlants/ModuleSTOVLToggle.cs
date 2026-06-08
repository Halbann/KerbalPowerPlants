using System;
using System.Collections.Generic;
using UnityEngine;

namespace KerbalPowerPlants;

public class ModuleSTOVLToggle : PartModule
{
    public enum Axis { X, Y, Z }

    [KSPField] public string animationName = string.Empty;
    [KSPField] public int animationLayer = 1;
    [KSPField] public bool instantInEditor = true;

    [KSPField] public string deployText = "Engage Swivel";
    [KSPField] public string retractText = "Disengage Swivel";
    [KSPField] public string actionText = "Toggle Swivel";
    [KSPField] public string statusFieldName = "Swivel";

    [KSPField] public string managedGimbalTransforms = string.Empty;
    [KSPField] public string managedConstraintAnimations = string.Empty;

    [KSPField] public string swivelTransformName = string.Empty;
    [KSPField] public Axis swivelAxis = Axis.X;
    [KSPField] public float swivelDeployAngle = -90f;

    [KSPField] public float swivelTime = 3f;
    [KSPField] public float clearance = 0.8f;

    [KSPField(isPersistant = true)] public bool isDeployed = false;

    [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Swivel")]
    public string status = StowedText;

    private const string StowedText = "Disengaged";
    private const string DeployingText = "Engaging";
    private const string DeployedText = "Engaged";
    private const string RetractingText = "Disengaging";

    private enum Phase { Stowed, Deploying, Deployed, Retracting }

    private Animation anim;
    private AnimationState clipState;
    private readonly List<ModuleGimbalTorque> managedGimbals = [];
    private readonly List<FXModuleConstrainAnimation> managedConstraints = [];
    private MultiModeEngine multimode;

    private Transform swivelTransform;
    private Quaternion swivelStowedRot;
    private Quaternion swivelDeployedRot;

    private Phase phase;
    private float phaseTime;
    private float swivelProgress;
    private bool swivelMoving;
    private Quaternion swivelFrom;
    private Quaternion swivelTo;
    private bool doorsReversing;

    [KSPEvent(guiActive = true, guiActiveEditor = true, guiActiveUnfocused = false, guiName = "Engage Swivel")]
    public void Toggle()
    {
        SetDeployed(!isDeployed, instant: HighLogic.LoadedSceneIsEditor && instantInEditor);
    }

    [KSPAction("Toggle Swivel")]
    public void ToggleAction(KSPActionParam param) { Toggle(); }

    #region Lifetime

    public override void OnStart(StartState state)
    {
        Actions[nameof(ToggleAction)].guiName = actionText;
        Fields[nameof(status)].guiName = statusFieldName;

        if (!FindAnimation())
            return;

        swivelTransform = part.FindModelTransform(swivelTransformName);
        if (swivelTransform == null)
        {
            Debug.LogError($"[KerbalPowerPlants]: ModuleSTOVLToggle on '{part.name}': swivel transform '{swivelTransformName}' not found");
            enabled = false;
            return;
        }

        swivelStowedRot = swivelTransform.localRotation;
        swivelDeployedRot = swivelStowedRot * Quaternion.AngleAxis(swivelDeployAngle, AxisVector(swivelAxis));

        FindManagedModules();

        foreach (var animConstraint in managedConstraints)
            animConstraint?.modifiers.Add(ConstraintModifier);

        ApplyState(isDeployed, instant: true);
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.Deploying:
                UpdateDeploying();
                break;
            case Phase.Retracting:
                UpdateRetracting();
                break;
            default:
                break;
        }
    }

    private void OnDestroy()
    {
        foreach (var animConstraint in managedConstraints)
            animConstraint?.modifiers.Remove(ConstraintModifier);
    }

    #endregion

    #region Transition

    public void SetDeployed(bool deployed, bool instant)
    {
        isDeployed = deployed;
        ApplyState(deployed, instant);
    }

    private void ApplyState(bool deployed, bool instant)
    {
        Events[nameof(Toggle)].guiName = deployed ? retractText : deployText;

        if (deployed)
        {
            AllowAfterburner(false);
            SetConstraintsEnabled(true);

            if (instant)
            {
                SampleDoors(1f);
                swivelMoving = false;
                swivelTransform.localRotation = swivelDeployedRot;
                CaptureGimbalRest();
                EnableGimbals(true);
                phase = Phase.Deployed;
                status = DeployedText;
            }
            else
            {
                EnableGimbals(false);
                PlayClip(forward: true);
                phaseTime = 0f;
                swivelMoving = false;
                phase = Phase.Deploying;
                status = DeployingText;
            }
        }
        else
        {
            EnableGimbals(false);
            SetConstraintsEnabled(true);

            if (instant)
            {
                SampleDoors(0f);
                swivelMoving = false;
                swivelTransform.localRotation = swivelStowedRot;
                SetConstraintsEnabled(false);
                phase = Phase.Stowed;
                status = StowedText;
                AllowAfterburner(true);
            }
            else
            {
                clipState.speed = 0f;
                phaseTime = 0f;
                doorsReversing = false;
                BeginSwivel(swivelStowedRot);
                phase = Phase.Retracting;
                status = RetractingText;
            }
        }
    }

    private void UpdateDeploying()
    {
        phaseTime += Time.deltaTime;

        if (!swivelMoving && phaseTime >= clearance)
            BeginSwivel(swivelDeployedRot);

        if (swivelMoving)
        {
            swivelProgress = Mathf.MoveTowards(swivelProgress, 1f, Time.deltaTime / Mathf.Max(swivelTime, 1e-4f));
            ApplySwivel();
        }

        bool doorsDone = !anim.IsPlaying(animationName);
        bool swivelDone = swivelMoving && swivelProgress >= 1f;
        if (doorsDone && swivelDone)
            EnterDeployed();
    }

    private void UpdateRetracting()
    {
        phaseTime += Time.deltaTime;

        swivelProgress = Mathf.MoveTowards(swivelProgress, 1f, Time.deltaTime / Mathf.Max(swivelTime, 1e-4f));
        ApplySwivel();

        if (!doorsReversing && phaseTime >= clearance)
        {
            PlayClip(forward: false);
            doorsReversing = true;
        }

        bool doorsDone = doorsReversing && !anim.IsPlaying(animationName);
        bool swivelDone = swivelProgress >= 1f;
        if (doorsDone && swivelDone)
            EnterStowed();
    }

    private void EnterDeployed()
    {
        swivelMoving = false;
        swivelTransform.localRotation = swivelDeployedRot;
        CaptureGimbalRest();
        EnableGimbals(true);
        phase = Phase.Deployed;
        status = DeployedText;
    }

    private void EnterStowed()
    {
        swivelMoving = false;
        swivelTransform.localRotation = swivelStowedRot;
        SetConstraintsEnabled(false);
        phase = Phase.Stowed;
        status = StowedText;
        AllowAfterburner(true);
    }

    private void BeginSwivel(Quaternion to)
    {
        swivelFrom = swivelTransform.localRotation;
        swivelTo = to;
        swivelProgress = 0f;
        swivelMoving = true;
    }

    private void ApplySwivel() =>
        swivelTransform.localRotation = Quaternion.Slerp(
            swivelFrom, swivelTo, Mathf.SmoothStep(0f, 1f, swivelProgress));

    #endregion

    #region Doors

    private bool FindAnimation()
    {
        var animators = part.FindModelAnimators(animationName);
        if (animators.Length == 0)
        {
            Debug.LogError($"[KerbalPowerPlants]: ModuleSTOVLToggle on '{part.name}': animation '{animationName}' not found");
            enabled = false;
            return false;
        }
        anim = animators[0];
        clipState = anim[animationName];
        if (clipState == null)
        {
            Debug.LogError($"[KerbalPowerPlants]: ModuleSTOVLToggle on '{part.name}': clip '{animationName}' missing from Animation component");
            enabled = false;
            return false;
        }
        clipState.layer = animationLayer;
        clipState.wrapMode = WrapMode.Once;
        return true;
    }

    private void SampleDoors(float normalizedT)
    {
        clipState.enabled = true;
        clipState.weight = 1f;
        clipState.normalizedTime = normalizedT;
        clipState.speed = 0f;
        anim.Play(animationName);
        anim.Sample();
        anim.Stop(animationName);
    }

    private void PlayClip(bool forward)
    {
        clipState.enabled = true;
        clipState.weight = 1f;
        clipState.speed = forward ? 1f : -1f;
        if (forward && clipState.normalizedTime >= 1f) clipState.normalizedTime = 0f;
        else if (!forward && clipState.normalizedTime <= 0f) clipState.normalizedTime = 1f;
        anim.Play(animationName);
    }

    #endregion

    #region Managed modules

    private void FindManagedModules()
    {
        var gimbalNames = ParseCsv(managedGimbalTransforms);
        var constraintNames = ParseCsv(managedConstraintAnimations);

        for (int i = 0; i < part.Modules.Count; i++)
        {
            var m = part.Modules[i];
            if (m is ModuleGimbalTorque g && gimbalNames.Contains(g.gimbalTransformName))
                managedGimbals.Add(g);
            else if (m is FXModuleConstrainAnimation c && constraintNames.Contains(c.animationName))
                managedConstraints.Add(c);
            else if (m is MultiModeEngine mm)
                multimode = mm;
        }
    }

    private static HashSet<string> ParseCsv(string csv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(csv))
            return set;

        foreach (var tok in csv.Split(','))
        {
            var s = tok.Trim();
            if (s.Length > 0) set.Add(s);
        }

        return set;
    }

    private void SetConstraintsEnabled(bool en)
    {
        for (int i = 0; i < managedConstraints.Count; i++)
        {
            var c = managedConstraints[i];
            c.enabled = en;
            if (en) c.SampleNow();
        }
    }

    private void EnableGimbals(bool en)
    {
        for (int i = 0; i < managedGimbals.Count; i++)
        {
            managedGimbals[i].moduleIsEnabled = en;
            managedGimbals[i].UpdateToggles();
        }
    }

    private void CaptureGimbalRest()
    {
        for (int i = 0; i < managedGimbals.Count; i++)
            managedGimbals[i].CaptureRest();
    }

    #endregion

    #region Elevation remap

    static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;

    readonly double K = (2.0 * Math.Sqrt(2.0) - 3.0) / 4.0;

    float ConstraintModifier(float t) =>
        (float)ProgressForElevation(Mathf.Lerp(90f, 0, t));

    double ElevationForProgress(double t)
    {
        t = Clamp(t, 0.0, 1.0);
        double s = Math.Sin(Math.PI * t);
        double v = K * s * s + 0.5 * Math.Cos(Math.PI * t) + 0.5;
        return Math.Asin(Clamp(v, -1.0, 1.0)) * (180.0 / Math.PI);
    }

    double ProgressForElevation(double deg)
    {
        double s = Math.Sin(Clamp(deg, 0.0, 90.0) * (Math.PI / 180.0));
        double a = K, b = -0.5, cc = -(K + 0.5) + s;
        double disc = b * b - 4.0 * a * cc;
        double x = (-b - Math.Sqrt(disc)) / (2.0 * a);
        return Math.Acos(Clamp(x, -1.0, 1.0)) / Math.PI;
    }

    #endregion

    #region Afterburner interlock

    void AllowAfterburner(bool allow)
    {
        if (multimode == null)
            return;

        if (!allow && !multimode.runningPrimary)
            multimode.SetPrimary(HighLogic.LoadedSceneIsFlight);

        multimode.Events[nameof(MultiModeEngine.ModeEvent)].guiActive = allow;
        multimode.Events[nameof(MultiModeEngine.ModeEvent)].guiActiveEditor = allow;
        multimode.Actions[nameof(MultiModeEngine.ModeAction)].active = allow;

        multimode.moduleIsEnabled = allow;

        if (multimode.primaryEngine != null && multimode.secondaryEngine != null)
            multimode.loadFailure = !allow;
    }

    #endregion

    private static Vector3 AxisVector(Axis a) => a switch
    {
        Axis.X => Vector3.right,
        Axis.Y => Vector3.up,
        Axis.Z => Vector3.forward,
        _ => Vector3.right,
    };
}
