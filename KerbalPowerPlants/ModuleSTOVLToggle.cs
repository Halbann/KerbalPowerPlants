using System;
using System.Collections.Generic;
using UnityEngine;

namespace KerbalPowerPlants;

// Orchestrates STOVL deploy/retract on a multi-mode jet engine.
//
// Managed modules (matched out of the part's MODULE list at OnStart):
//   - ModuleGimbal entries selected by gimbalTransformName. Enabled only
//     when fully deployed. Their initRots are re-captured from the current
//     pose at deploy-end so the pitch gimbal pivots around the deployed
//     orientation, not the closed one ModuleGimbal cached at its OnStart.
//
//   - FXModuleConstrainAnimation entries selected by animationName. Active
//     during deploy/retract motion and while fully deployed, so bone-driven
//     follower animations track the door swivel throughout.
public class ModuleSTOVLToggle : PartModule
{
    // Config fields.
    [KSPField] public string animationName = "OpenClose";
    [KSPField] public int animationLayer = 1;
    [KSPField] public bool instantInEditor = true;

    [KSPField] public string deployText = "Engage Hover Mode";
    [KSPField] public string retractText = "Disengage Hover Mode";
    [KSPField] public string actionText = "Toggle Hover Mode";
    [KSPField] public string statusFieldName = "Hover";

    [KSPField] public string managedGimbalTransforms = string.Empty;
    [KSPField] public string managedConstraintAnimations = string.Empty;

    [KSPField(isPersistant = true)] public bool isDeployed = false;

    [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Hover")]
    public string status = StowedText;

    private const string StowedText = "Stowed";
    private const string DeployingText = "Deploying";
    private const string DeployedText = "Deployed";
    private const string RetractingText = "Retracting";

    private Animation anim;
    private AnimationState clipState;
    private readonly List<ModuleGimbalTorque> managedGimbals = [];
    private readonly List<FXModuleConstrainAnimation> managedConstraints = [];
    private MultiModeEngine multimode;
    private bool isMoving;

    [KSPEvent(guiActive = true, guiActiveEditor = true, guiActiveUnfocused = false, guiName = "Engage Hover")]
    public void Toggle()
    {
        SetDeployed(!isDeployed, instant: HighLogic.LoadedSceneIsEditor && instantInEditor);
    }

    [KSPAction("Toggle Hover")]
    public void ToggleAction(KSPActionParam param) { Toggle(); }

    #region Lifetime

    public override void OnStart(StartState state)
    {
        Actions[nameof(ToggleAction)].guiName = actionText;
        Fields[nameof(status)].guiName = statusFieldName;

        if (!FindAnimation())
            return;

        FindManagedModules();

        foreach (var animConstraint in managedConstraints)
            animConstraint?.modifiers.Add(ConstraintModifier);

        ApplyState(isDeployed, instant: true);
    }

    private void Update()
    {
        if (isMoving && !anim.IsPlaying(animationName))
            FinishMove();
    }

    private void OnDestroy()
    {
        foreach (var animConstraint in managedConstraints)
            animConstraint?.modifiers.Remove(ConstraintModifier);
    }

    #endregion

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
            if (instant)
            {
                SnapPose(1f);
                SetConstraintsEnabled(true);
                CaptureGimbalRest();
                EnableGimbals(true);
                isMoving = false;
                status = DeployedText;
            }
            else
            {
                EnableGimbals(false);
                SetConstraintsEnabled(true);
                PlayClip(forward: true);
                isMoving = true;
                status = DeployingText;
            }

            AllowAfterburner(false);
        }
        else
        {
            SnapGimbalsToRest();
            EnableGimbals(false);

            if (instant)
            {
                SnapPose(0f);
                SetConstraintsEnabled(false);
                isMoving = false;
                status = StowedText;
                AllowAfterburner(true);
            }
            else
            {
                SetConstraintsEnabled(true);
                PlayClip(forward: false);
                isMoving = true;
                status = RetractingText;
            }
        }
    }

    private void FinishMove()
    {
        isMoving = false;
        if (isDeployed)
        {
            CaptureGimbalRest();
            EnableGimbals(true);
            status = DeployedText;
        }
        else
        {
            SetConstraintsEnabled(false);
            status = StowedText;
            AllowAfterburner(true);
        }
    }

    // Apply a single pose without leaving the clip in the playing list, so
    // nothing keeps overwriting the transforms after this returns.
    private void SnapPose(float normalizedT)
    {
        clipState.enabled = true;
        clipState.weight = 1f;
        clipState.normalizedTime = normalizedT;
        clipState.speed = 0f;
        anim.Play(animationName);
        anim.Sample();
        anim.Stop(animationName);
    }

    // Start (or resume) playback in the chosen direction. WrapMode.Once stops
    // the clip on its own at each end; Update polls IsPlaying to react.
    private void PlayClip(bool forward)
    {
        clipState.enabled = true;
        clipState.weight = 1f;
        clipState.speed = forward ? 1f : -1f;
        // If we're sitting at the wrong end, rewind to the start of the move.
        if (forward && clipState.normalizedTime >= 1f) clipState.normalizedTime = 0f;
        else if (!forward && clipState.normalizedTime <= 0f) clipState.normalizedTime = 1f;
        anim.Play(animationName);
    }

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
            // If we just re-enabled, force an immediate sample so the bones
            // pick up any pose change we made this same frame (e.g. SnapPose).
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

    private void SnapGimbalsToRest()
    {
        for (int i = 0; i < managedGimbals.Count; i++)
        {
            var g = managedGimbals[i];
            for (int j = 0; j < g.gimbalTransforms.Count; j++)
                g.gimbalTransforms[j].localRotation = g.initRots[j];
        }
    }

    // Re-cache each managed gimbal's rest pose from the current target pose.
    // Required after deploy because ModuleGimbal.OnStart cached the closed
    // (pre-animation) pose, so without this the pitch gimbal would pivot
    // around the wrong rotation.
    private void CaptureGimbalRest()
    {
        for (int i = 0; i < managedGimbals.Count; i++)
        {
            var g = managedGimbals[i];
            for (int j = 0; j < g.gimbalTransforms.Count; j++)
            {
                g.initRots[j] = g.gimbalTransforms[j].localRotation;
                g.currentAngles[j] = Vector3.zero;
            }
        }
    }

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
}
