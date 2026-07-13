using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KerbalPowerPlants;

public class ModuleBreakableObjects : PartModule
{
    [KSPField] public string objectNames = "";
    [KSPField] public float impactResistance = 10f;
    [KSPField] public float windResistance = 3f;
    [KSPField] public double gResistance = double.PositiveInfinity;
    [KSPField] public float subPartMass = 0.01f;
    [KSPField] public float panelDrag = 1f;
    [KSPField] public string breakMessage = "";

    [KSPField(isPersistant = true)] public bool broken;

    private const float MinAoAForQCheck = 0.1875f;

    private readonly List<GameObject> targets = [];
    private readonly List<Func<bool>> conditions = [];
    private Transform reference;

    public event Action OnBroke;

    // Siblings register a gate; breaking requires every gate true (none = always).
    public void AddBreakCondition(Func<bool> condition) => conditions.Add(condition);

    public override void OnStart(StartState startState)
    {
        if (!HighLogic.LoadedSceneIsFlight)
        {
            enabled = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(objectNames))
        {
            this.ErrorAndDisable("no objectNames set");
            return;
        }

        // Get name prefixes.
        string[] prefixes = objectNames.Split(',');
        for (int i = 0; i < prefixes.Length; i++)
            prefixes[i] = prefixes[i].Trim();

        // Search game objects for ones that match a prefix.
        Transform model = part.FindModelTransform("model") ?? part.transform;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (!Matches(t.name, prefixes))
                continue;

            if (!t.GetComponent<Collider>())
                t.gameObject.AddComponent<BoxCollider>();

            targets.Add(t.gameObject);
        }

        if (targets.Count == 0)
        {
            this.ErrorAndDisable($"no objects matching '{objectNames}'");
            return;
        }

        // Apply persistent state.
        if (broken)
        {
            foreach (GameObject go in targets)
                go.SetActive(false);

            enabled = false; // Only breaks once (for now).
            return;
        }

        reference = targets[0].transform;
    }

    private bool ConditionsMet()
    {
        foreach (Func<bool> condition in conditions)
            if (!condition())
                return false;

        // No conditions, or all return true.
        return true;
    }

    private static bool Matches(string name, string[] prefixes)
    {
        foreach (string prefix in prefixes)
            if (prefix.Length > 0 && name.StartsWith(prefix))
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

    private void Break()
    {
        broken = true;

        // Detach each target into free-flying debris.
        foreach (GameObject go in targets)
        {
            if (go == null)
                continue;

            physicalObject obj = physicalObject.ConvertToPhysicalObject(part, go);
            Rigidbody rb = obj.rb;
            rb.maxAngularVelocity = PhysicsGlobals.MaxAngularVelocity;

            // Inherit the part's motion plus a random shove and tumble.
            Vector3 linear = new(Random.Range(0, 2), Random.Range(0, 2), Random.Range(0, 2));
            Vector3 spin = new(Random.Range(-3, 3), Random.Range(-3, 3), Random.Range(-3, 3));
            rb.angularVelocity = part.Rigidbody.angularVelocity + spin;

            // Tangential velocity so it flies off spinning about the vessel's CoM (arm x w = w x r).
            Vector3 arm = vessel.CurrentCoM - part.Rigidbody.worldCenterOfMass;
            rb.velocity = part.Rigidbody.velocity + linear + Vector3.Cross(arm, rb.angularVelocity);

            rb.mass = subPartMass;
            rb.useGravity = false;
            go.transform.parent = null;
            obj.origDrag = panelDrag;   
        }

        targets.Clear();

        // Screen message.
        if (!string.IsNullOrWhiteSpace(breakMessage) && vessel == FlightGlobals.ActiveVessel)
            ScreenMessages.PostScreenMessage($"<color=orange>[{part.partInfo.title}]: {breakMessage}</color>", 6f, ScreenMessageStyle.UPPER_CENTER);

        part.RefreshHighlighter();
        part.ResetCollisions();
        GameEvents.onVesselWasModified.Fire(vessel);

        OnBroke?.Invoke();
    }
}
