using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KSP.Localization;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KerbalPowerPlants;

public class ModuleBreakableObjects : PartModule
{
    [KSPField] public string objectPatterns = "";
    [KSPField] public float impactResistance = 10f;
    [KSPField] public float windResistance = 3f;
    [KSPField] public double gResistance = double.PositiveInfinity;
    [KSPField] public float subPartMass = 0.01f;
    [KSPField] public float panelDrag = 1f;
    [KSPField] public string breakMessage = "";
    [KSPField] public bool perObjectVelocities = false;
    [KSPField] public float perObjectMutiplier = 1f;

    [KSPField(isPersistant = true)] public bool broken;

    private const float MinAoAForQCheck = 0.1875f;

    private readonly List<GameObject> targets = [];
    private readonly List<Func<bool>> conditions = [];
    private Transform reference;
    private int repairKitsNecessary = 1;

    public event Action OnBroke;
    public event Action OnRepaired;

    // Siblings register a gate; breaking requires every gate true (none = always).
    public void AddBreakCondition(Func<bool> condition) => conditions.Add(condition);

    public override void OnStart(StartState startState)
    {
        if (!HighLogic.LoadedSceneIsFlight)
        {
            enabled = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(objectPatterns))
        {
            this.ErrorAndDisable("no objectPatterns set");
            return;
        }

        if (!FindTargets())
            return;

        // Stock EVA-repair kit count, scaled by part mass.
        repairKitsNecessary = Math.Min(Math.Max((int)(part.mass / GameSettings.PART_REPAIR_MASS_PER_KIT), 1), GameSettings.PART_REPAIR_MAX_KIT_AMOUNT);
        BaseEvent repair = Events[nameof(Repair)];
        repair.guiName = Localizer.Format("#autoLOC_6005092", repairKitsNecessary.ToString());
        repair.active = broken;

        // Apply persistent state.
        if (broken)
            foreach (GameObject go in targets)
                go.SetActive(false);
    }

    // Find the model objects to break; add colliders where missing.
    private bool FindTargets()
    {
        targets.Clear();

        List<Regex> patterns = [];
        foreach (string p in objectPatterns.Split(','))
        {
            string trimmed = p.Trim();
            if (trimmed.Length > 0)
                patterns.Add(new Regex(trimmed));
        }

        Transform model = part.FindModelTransform("model") ?? part.transform;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (!Matches(t.name, patterns))
                continue;

            if (!t.GetComponent<Collider>())
                t.gameObject.AddComponent<BoxCollider>(); // Box collider automatically sizes to mesh BB.

            targets.Add(t.gameObject);
        }

        if (targets.Count == 0)
        {
            this.ErrorAndDisable($"no objects matching '{objectPatterns}'");
            return false;
        }

        reference = targets[0].transform;
        return true;
    }

    private bool ConditionsMet()
    {
        foreach (Func<bool> condition in conditions)
            if (!condition())
                return false;

        // No conditions, or all return true.
        return true;
    }

    private static bool Matches(string name, List<Regex> patterns)
    {
        foreach (Regex pattern in patterns)
            if (pattern.IsMatch(name))
                return true;

        return false;
    }

    public void OnCollisionEnter(Collision collision)
    {
        if (broken || part.packed || CheatOptions.NoCrashDamage || !ConditionsMet())
            return;

        if (collision.relativeVelocity.magnitude > impactResistance)
            Break();
    }

    public void FixedUpdate()
    {
        if (broken || !HighLogic.LoadedSceneIsFlight || part.packed || vessel == null || vessel.HoldPhysics || !ConditionsMet())
            return;

        if (ShouldBreakFromPressure() || ShouldBreakFromG())
            Break();
    }

    private bool ShouldBreakFromPressure()
    {
        if (part.ShieldedFromAirstream || reference == null)
            return false;

        // Dynamic pressure, above and below water.
        float q = (float)(part.dynamicPressurekPa + part.submergedDynamicPressurekPa);
        if (q < windResistance)
            return false;

        // Weight by angle to the airstream with a min weight.
        float aoa = Mathf.Abs(Vector3.Dot(vessel.velocityD.normalized, reference.forward.normalized));
        if (aoa < MinAoAForQCheck)
            aoa = MinAoAForQCheck;

        return aoa * q > windResistance;
    }

    private bool ShouldBreakFromG() => vessel.geeForce > gResistance;

    private void Break() =>
        StartCoroutine(BreakCoroutine());

    private IEnumerator BreakCoroutine()
    {
        Dictionary<GameObject, Vector3> lastPos = null;
        if (perObjectVelocities)
        {
            // Store positions per object.
            lastPos = [];

            foreach (GameObject go in targets)
            {
                if (go == null)
                    continue;

                lastPos.Add(go, go.transform.position);
            }

            // Wait one physics update.
            float collisionTime = Time.fixedTime;
            while (Time.fixedTime == collisionTime)
                yield return new WaitForFixedUpdate();
        }

        broken = true;

        // Shed a throwaway copy of each target as free-flying debris, then hide
        // the real object so repair can bring it back with all references intact.
        foreach (GameObject go in targets)
        {
            if (go == null)
                continue;

            GameObject debris = Instantiate(go, go.transform.parent);
            go.SetActive(false);

            physicalObject obj = physicalObject.ConvertToPhysicalObject(part, debris);
            Rigidbody rb = obj.rb;
            rb.maxAngularVelocity = PhysicsGlobals.MaxAngularVelocity;

            // Inherit the part's motion plus a random shove and tumble.
            Vector3 linear = new(Random.Range(0, 2), Random.Range(0, 2), Random.Range(0, 2));
            Vector3 spin = new(Random.Range(-3, 3), Random.Range(-3, 3), Random.Range(-3, 3));
            rb.angularVelocity = part.Rigidbody.angularVelocity + spin;

            // Add per-object part-relative velocity.
            if (perObjectVelocities && lastPos.TryGetValue(go, out Vector3 lastObjectPos))
            {
                var worldDelta = go.transform.position - lastObjectPos;
                var pointVelocity = part.Rigidbody.GetPointVelocity(go.transform.position);
                linear += (worldDelta / Time.fixedDeltaTime - pointVelocity) * perObjectMutiplier;
            }

            // Tangential velocity so it flies off spinning about the vessel's CoM (arm x w = w x r).
            Vector3 arm = vessel.CurrentCoM - part.Rigidbody.worldCenterOfMass;
            rb.velocity = part.Rigidbody.velocity + linear + Vector3.Cross(arm, rb.angularVelocity);

            rb.mass = subPartMass;
            rb.useGravity = false;
            obj.origDrag = panelDrag;
        }

        // Screen message.
        if (!string.IsNullOrWhiteSpace(breakMessage) && vessel == FlightGlobals.ActiveVessel)
            ScreenMessages.PostScreenMessage($"<color=orange>[{part.partInfo.title}]: {breakMessage}</color>", 6f, ScreenMessageStyle.UPPER_CENTER);

        Events[nameof(Repair)].active = true;

        part.RefreshHighlighter();
        part.ResetCollisions();
        GameEvents.onVesselWasModified.Fire(vessel);

        OnBroke?.Invoke();
    }

    // EVA repair, mirroring ModuleDeployablePart.EventRepairExternal.
    [KSPEvent(guiActiveUnfocused = true, externalToEVAOnly = true, guiActive = false, unfocusedRange = 4f, active = false, guiName = "#autoLOC_8003453")]
    public void Repair()
    {
        Vessel eva = FlightGlobals.ActiveVessel;

        if (HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>().KerbalExperienceEnabled(HighLogic.CurrentGame.Mode)
            && eva.VesselValues.RepairSkill.value < 1)
        {
            ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_246904", 1.ToString()));
            return;
        }

        if (!eva.isEVA || eva.evaController.ModuleInventoryPartReference == null)
            return;

        if (eva.VesselValues.RepairSkill.value > 0)
        {
            ModuleInventoryPart inventory = eva.evaController.ModuleInventoryPartReference;
            if (inventory.TotalAmountOfPartStored("evaRepairKit") >= repairKitsNecessary)
            {
                inventory.RemoveNPartsFromInventory("evaRepairKit", repairKitsNecessary, playSound: true);
                if (broken)
                    DoRepair();
                return;
            }

            AvailablePart kit = PartLoader.getPartInfoByName("evaRepairKit");
            if (kit != null)
                ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_6006097", repairKitsNecessary.ToString(), kit.title));
        }
        else
        {
            ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_6006098"));
        }
    }

    private void DoRepair()
    {
        // Unhide originals.
        foreach (GameObject go in targets)
            if (go) go.SetActive(true);

        broken = false;
        Events[nameof(Repair)].active = false;

        part.RefreshHighlighter();
        part.ResetCollisions();
        GameEvents.onPartRepaired.Fire(part);
        GameEvents.onVesselWasModified.Fire(vessel);

        OnRepaired?.Invoke();
    }

    // todo: destruction should affect drag cubes
}
