using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;

// ModuleGimbalTorque
// ------------------
// A drop-in replacement for stock ModuleGimbal that solves the gimbal deflection from the
// DESIRED CONTROL TORQUE using the real lever arm (thrust transform -> CoM) and the real
// thrust direction. This makes it correct for arbitrary engine position and orientation,
// rather than assuming a tail-mounted, roughly-axial engine the way stock does.
//
// Control reference frame (vessel.ReferenceTransform), matching stock control conventions:
//   X = right   -> pitch axis
//   Y = up      -> roll axis (the direction the craft points)
//   Z = forward -> yaw axis
//
// Thrust-transform convention (stock KSP): thrust force on the vessel is applied along
// +thrustTransform.forward (the transform's local +Z), so the gimbal pivots about local X
// and local Y to vector that thrust direction.
//
// Per-axis input sign is handled by commandSign (default 1,1,1). Flip a component to -1 if
// that axis drives the gimbal backwards in flight (unusual artist conventions).

public class ModuleGimbalTorque : PartModule, ITorqueProvider
{
    // ---- Config fields (names kept identical to stock ModuleGimbal for cfg compatibility) ----

    [KSPField]
    public string gimbalTransformName = "thrustTransform";

    [UI_Toggle(disabledText = "#autoLOC_7000036", scene = UI_Scene.All, enabledText = "#autoLOC_7000035", affectSymCounterparts = UI_Scene.Editor)]
    [KSPField(isPersistant = true, guiActive = true, guiName = "#autoLoc_6003043")]
    public bool gimbalLock;

    [UI_FloatRange(minValue = 0f, stepIncrement = 1f, maxValue = 100f, affectSymCounterparts = UI_Scene.All)]
    [KSPField(isPersistant = true, guiActive = true, guiName = "#autoLOC_6001383")]
    public float gimbalLimiter = 100f;

    [KSPField]
    public float gimbalRange = 10f;

    [KSPField]
    public float gimbalRangeXP = -1f;   // +X travel (deg); < 0 => falls back to gimbalRange

    [KSPField]
    public float gimbalRangeXN = -1f;   // -X travel (deg); < 0 => falls back to gimbalRangeXP

    [KSPField]
    public float gimbalRangeYP = -1f;   // +Y travel (deg); < 0 => falls back to gimbalRange

    [KSPField]
    public float gimbalRangeYN = -1f;   // -Y travel (deg); < 0 => falls back to gimbalRangeYP

    [KSPField]
    public float minRollOffset = 0.1f;  // min lateral offset (m) before roll authority is counted

    [KSPField]
    public bool useGimbalResponseSpeed;

    // Reinterpreted vs stock: now drives SmoothDamp, smoothTime = 1 / gimbalResponseSpeed.
    // Default 10 => ~0.1 s settle. Higher = snappier, same as before.
    [KSPField]
    public float gimbalResponseSpeed = 10f;

    // Per-axis sign mapping control-input -> torque (pitch, roll, yaw). Default (1,1,1).
    // Flip a component to -1 if that axis assists the wrong way in flight.
    [KSPField]
    public Vector3 commandSign = Vector3.one;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "#autoLOC_6001331")]
    [UI_Toggle(disabledText = "#autoLOC_6001073", enabledText = "#autoLOC_6001074", affectSymCounterparts = UI_Scene.Editor)]
    public bool enableYaw = true;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "#autoLOC_6001330")]
    [UI_Toggle(disabledText = "#autoLOC_6001073", enabledText = "#autoLOC_6001074", affectSymCounterparts = UI_Scene.Editor)]
    public bool enablePitch = true;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "#autoLOC_6001332")]
    [UI_Toggle(disabledText = "#autoLOC_6001073", enabledText = "#autoLOC_6001074", affectSymCounterparts = UI_Scene.Editor)]
    public bool enableRoll = true;

    // Whether this part exposes the per-axis enable toggles at all. If true, the PAW shows a
    // (advanced-tweakable) "Show Axis Toggles" button that reveals/hides them.
    [KSPField]
    public bool showToggles = true;

    [KSPField(isPersistant = true)]
    public bool currentShowToggles;

    [KSPField(isPersistant = true)]
    public bool gimbalActive;

    // ---- Runtime state ----

    public List<Transform> gimbalTransforms;
    public List<Quaternion> initRots;

    // Smoothed gimbal angles per transform: x = deg about local X, y = deg about local Y.
    public Vector3[] currentAngles;
    private Vector3[] angleVelocities;     // SmoothDamp velocity refs

    // Engines (and their thrust share) feeding each gimbal transform; for GetPotentialTorque.
    private List<List<KeyValuePair<ModuleEngines, float>>> engineMultsList;

    private const float Probe = 1f;        // small probe angle (deg) to measure gimbal sensitivity
    private const float Eps = 1e-6f;

    // ---------------------------------------------------------------- Actions / events ----

    [KSPAction("#autoLOC_6001385")]
    public void ToggleAction(KSPActionParam param) => gimbalLock = !gimbalLock;

    [KSPAction("#autoLOC_6001386")]
    public void LockAction(KSPActionParam param) => gimbalLock = true;

    [KSPAction("#autoLOC_6001387")]
    public void FreeAction(KSPActionParam param) => gimbalLock = false;

    [KSPAction("#autoLOC_6001388", KSPActionGroup.None, true)]
    public void TogglePitchAction(KSPActionParam param) => enablePitch = !enablePitch;

    [KSPAction("#autoLOC_6001389", KSPActionGroup.None, true)]
    public void ToggleYawAction(KSPActionParam param) => enableYaw = !enableYaw;

    [KSPAction("#autoLOC_6001390", KSPActionGroup.None, true)]
    public void ToggleRollAction(KSPActionParam param) => enableRoll = !enableRoll;

    [KSPEvent(advancedTweakable = true, guiActive = false, guiActiveEditor = false, guiName = "#autoLOC_6001384")]
    public void ToggleToggles()
    {
        Debug.Log($"ModuleGimbalTorque ToggleToggles.");

        currentShowToggles = !currentShowToggles;
        UpdateToggles();
    }

    // ---------------------------------------------------------------- Lifecycle ----

    public override void OnLoad(ConfigNode node) => EnsureRanges();

    public override void OnStart(StartState state)
    {
        Debug.Log($"ModuleGimbalTorque OnStart.");

        EnsureRanges();

        gimbalTransforms = new List<Transform>(part.FindModelTransforms(gimbalTransformName));
        initRots = new List<Quaternion>(gimbalTransforms.Count);
        currentAngles = new Vector3[gimbalTransforms.Count];
        angleVelocities = new Vector3[gimbalTransforms.Count];

        for (int i = 0; i < gimbalTransforms.Count; i++)
        {
            initRots.Add(gimbalTransforms[i].localRotation);
            currentAngles[i] = Vector3.zero;
            angleVelocities[i] = Vector3.zero;
        }

        engineMultsList = null;   // rebuilt lazily on first GetPotentialTorque

        UpdateToggles();
    }

    public override void OnActive()
    {
        Debug.Log($"ModuleGimbalTorque OnActive. gimbalActive is {gimbalActive}");
        gimbalActive = true;
    }

    private void EnsureRanges()
    {
        if (gimbalRangeXP < 0f) gimbalRangeXP = gimbalRange;
        if (gimbalRangeYP < 0f) gimbalRangeYP = gimbalRange;
        if (gimbalRangeXN < 0f) gimbalRangeXN = gimbalRangeXP;
        if (gimbalRangeYN < 0f) gimbalRangeYN = gimbalRangeYP;
    }

    // Sync the per-axis enable-toggle visibility (and the "Show Axis Toggles" button's label)
    // with current state. Call after anything that flips showToggles, currentShowToggles, or
    // moduleIsEnabled.
    public void UpdateToggles()
    {
        bool show = showToggles && currentShowToggles && moduleIsEnabled;

        Fields[nameof(enableYaw)].guiActive = show;
        Fields[nameof(enableYaw)].guiActiveEditor = show;
        Fields[nameof(enablePitch)].guiActive = show;
        Fields[nameof(enablePitch)].guiActiveEditor = show;
        Fields[nameof(enableRoll)].guiActive = show;
        Fields[nameof(enableRoll)].guiActiveEditor = show;

        BaseEvent ev = Events[nameof(ToggleToggles)];
        ev.guiActive = showToggles && moduleIsEnabled;
        ev.guiActiveEditor = showToggles && moduleIsEnabled;
        ev.guiName = Localizer.Format(currentShowToggles ? "#autoLOC_221352" : "#autoLOC_7000023");
    }

    // ---------------------------------------------------------------- Actuation ----

    public void FixedUpdate()
    {
        if (!HighLogic.LoadedSceneIsFlight || !gimbalActive || !moduleIsEnabled
            || vessel == null || vessel.ReferenceTransform == null)
            return;

        if (gimbalTransforms == null)
            OnStart(StartState.Flying);

        Transform rt = vessel.ReferenceTransform;
        Vector3 com = vessel.CurrentCoM;

        // Desired control torque (world). Control axes: pitch about rt.right, roll about rt.up,
        // yaw about rt.forward. commandSign maps input sign to the correct torque sense per axis.
        float pitch = (enablePitch && !gimbalLock) ? vessel.ctrlState.pitch : 0f;
        float roll = (enableRoll && !gimbalLock) ? vessel.ctrlState.roll : 0f;
        float yaw = (enableYaw && !gimbalLock) ? vessel.ctrlState.yaw : 0f;

        Vector3 desiredTorque = commandSign.x * pitch * rt.right
                              + commandSign.y * roll * rt.up
                              + commandSign.z * yaw * rt.forward;

        float commandMag = Mathf.Clamp01(desiredTorque.magnitude);
        Vector3 desiredDir = commandMag > Eps ? desiredTorque / desiredTorque.magnitude : Vector3.zero;

        float limiter = gimbalLimiter * 0.01f;
        float smoothTime = 1f / Mathf.Max(gimbalResponseSpeed, 1e-4f);
        float dt = TimeWarp.fixedDeltaTime;

        for (int i = 0; i < gimbalTransforms.Count; i++)
        {
            Transform t = gimbalTransforms[i];

            // Reset to neutral first so the transform axes we read in SolveGimbal match the frame
            // in which the deflection rotations (post-multiplied onto initRot) actually act.
            t.localRotation = initRots[i];

            Vector2 target = Vector2.zero;   // x = deg about local X, y = deg about local Y
            if (!gimbalLock && commandMag > Eps)
                target = SolveGimbal(t, com, desiredDir, commandMag, limiter);

            Vector3 angles;
            if (useGimbalResponseSpeed)
            {
                angles = Vector3.SmoothDamp(currentAngles[i], new Vector3(target.x, target.y, 0f),
                                            ref angleVelocities[i], smoothTime, Mathf.Infinity, dt);
            }
            else
            {
                angles = new Vector3(target.x, target.y, 0f);
                angleVelocities[i] = Vector3.zero;
            }
            currentAngles[i] = angles;

            t.localRotation = initRots[i]
                * Quaternion.AngleAxis(angles.x, Vector3.right)
                * Quaternion.AngleAxis(angles.y, Vector3.up);
        }
    }

    // Returns gimbal angles (deg) about local X and local Y that best assist desiredDir,
    // scaled by command intensity and clamped to the per-direction limits.
    private Vector2 SolveGimbal(Transform t, Vector3 com, Vector3 desiredDir, float commandMag, float limiter)
    {
        Vector3 r = t.position - com;        // lever arm (world); invariant under localRotation
        Vector3 thrustDir = t.forward;       // thrust force direction (world)

        // Change in thrust direction for a small +deflection about each gimbal axis, measured with
        // the same quaternion convention we apply below, so the signs are guaranteed consistent.
        Vector3 dThrustX = (Quaternion.AngleAxis(Probe, t.right) * thrustDir) - thrustDir;
        Vector3 dThrustY = (Quaternion.AngleAxis(Probe, t.up) * thrustDir) - thrustDir;

        // Torque change per +deflection (tau = r x F). Thrust magnitude is irrelevant to the
        // direction we pick, so unit thrust is used here.
        Vector3 torquePerX = Vector3.Cross(r, dThrustX);
        Vector3 torquePerY = Vector3.Cross(r, dThrustY);

        float magX = torquePerX.magnitude;
        float magY = torquePerY.magnitude;

        // Per-axis alignment with the commanded torque, expressed as a fraction of that axis's
        // own maximum achievable torque magnitude. relX/relY are dimensionless [-1, 1] and degrade
        // smoothly to 0 when the geometry can't help -- no cross-axis normalization, so float
        // noise on a near-zero projection stays near zero instead of getting amplified to +/-1.
        // Full-stick sweep traces a circle (ellipse for asymmetric ranges/axes) of deflections.
        float relX = magX > 0f ? Vector3.Dot(torquePerX, desiredDir) / magX : 0f;
        float relY = magY > 0f ? Vector3.Dot(torquePerY, desiredDir) / magY : 0f;

        float angleX = relX * commandMag * (relX >= 0f ? gimbalRangeXP : gimbalRangeXN) * limiter;
        float angleY = relY * commandMag * (relY >= 0f ? gimbalRangeYP : gimbalRangeYN) * limiter;

        return new Vector2(angleX, angleY);
    }

    // ---------------------------------------------------------------- ITorqueProvider ----

    // Reports the control torque (kN*m) the gimbal can produce in the +/- direction about each
    // control axis, at the current thrust. Used by SAS for authority estimation. This computes
    // real torque via r x dF at the gimbal limits, so it is correct for off-axis / canted mounts.
    public void GetPotentialTorque(out Vector3 pos, out Vector3 neg)
    {
        pos = Vector3.zero;
        neg = Vector3.zero;

        if (gimbalLock || !moduleIsEnabled || gimbalTransforms == null
            || vessel == null || vessel.ReferenceTransform == null)
            return;

        if (engineMultsList == null || engineMultsList.Count != gimbalTransforms.Count)
            CreateEngineList();

        Transform rt = vessel.ReferenceTransform;
        Vector3 com = vessel.CurrentCoM;
        float limiter = gimbalLimiter * 0.01f;

        Vector3 axPitch = commandSign.x * rt.right;
        Vector3 axRoll = commandSign.y * rt.up;
        Vector3 axYaw = commandSign.z * rt.forward;

        for (int i = 0; i < gimbalTransforms.Count; i++)
        {
            Transform t = gimbalTransforms[i];

            float thrust = 0f;
            List<KeyValuePair<ModuleEngines, float>> feeders = engineMultsList[i];
            for (int e = 0; e < feeders.Count; e++)
            {
                float ft = feeders[e].Key.finalThrust;
                if (ft > 0f) thrust += feeders[e].Value * ft;
            }
            if (thrust <= 0f) continue;

            Vector3 r = t.position - com;
            Vector3 thrustDir = t.forward;

            // Torque produced (relative to neutral) at each single-axis limit, finite deflection.
            Vector3 tXP = Vector3.Cross(r, thrust * ((Quaternion.AngleAxis(gimbalRangeXP * limiter, t.right) * thrustDir) - thrustDir));
            Vector3 tXN = Vector3.Cross(r, thrust * ((Quaternion.AngleAxis(-gimbalRangeXN * limiter, t.right) * thrustDir) - thrustDir));
            Vector3 tYP = Vector3.Cross(r, thrust * ((Quaternion.AngleAxis(gimbalRangeYP * limiter, t.up) * thrustDir) - thrustDir));
            Vector3 tYN = Vector3.Cross(r, thrust * ((Quaternion.AngleAxis(-gimbalRangeYN * limiter, t.up) * thrustDir) - thrustDir));

            AxisAuthority(tXP, tXN, tYP, tYN, axPitch, out float p, out float n); pos.x += p; neg.x += n;
            AxisAuthority(tXP, tXN, tYP, tYN, axYaw, out p, out n); pos.z += p; neg.z += n;

            // Roll authority only exists with a lateral offset; gate it to avoid numerical noise.
            float lateral = Vector3.ProjectOnPlane(r, rt.up).magnitude;
            if (lateral > minRollOffset)
            {
                AxisAuthority(tXP, tXN, tYP, tYN, axRoll, out p, out n); pos.y += p; neg.y += n;
            }
        }
    }

    // Best torque achievable along +axis and -axis by independently choosing each gimbal axis's
    // deflection (positive limit, negative limit, or neutral).
    private static void AxisAuthority(Vector3 tXP, Vector3 tXN, Vector3 tYP, Vector3 tYN,
                                      Vector3 axis, out float posMag, out float negMag)
    {
        float xp = Vector3.Dot(tXP, axis);
        float xn = Vector3.Dot(tXN, axis);
        float yp = Vector3.Dot(tYP, axis);
        float yn = Vector3.Dot(tYN, axis);

        posMag = Mathf.Max(0f, xp, xn) + Mathf.Max(0f, yp, yn);
        negMag = Mathf.Max(0f, -xp, -xn) + Mathf.Max(0f, -yp, -yn);
    }

    private void CreateEngineList()
    {
        engineMultsList = new List<List<KeyValuePair<ModuleEngines, float>>>();
        if (gimbalTransforms == null) return;

        List<ModuleEngines> engines = part.FindModulesImplementing<ModuleEngines>();

        for (int i = 0; i < gimbalTransforms.Count; i++)
        {
            Transform gt = gimbalTransforms[i];
            var feeders = new List<KeyValuePair<ModuleEngines, float>>();

            for (int e = 0; e < engines.Count; e++)
            {
                ModuleEngines me = engines[e];
                if (me.thrustTransforms == null) continue;

                int idx = me.thrustTransforms.IndexOf(gt);
                if (idx < 0) continue;

                float mult;
                if (me.thrustTransformMultipliers != null && idx < me.thrustTransformMultipliers.Count)
                    mult = me.thrustTransformMultipliers[idx];
                else
                    mult = 1f / Mathf.Max(1, me.thrustTransforms.Count);

                feeders.Add(new KeyValuePair<ModuleEngines, float>(me, mult));
            }

            engineMultsList.Add(feeders);
        }
    }
}
