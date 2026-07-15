namespace KerbalPowerPlants;

// Base: scales a subclass-defined penalty while the part's breakable
// objects are broken, restoring it on repair.
public abstract class ModuleBrokenPenalty : PartModule
{
    [KSPField] public float brokenMultiplier = 0.3f;

    public override void OnStart(StartState startState)
    {
        if (!HighLogic.LoadedSceneIsFlight)
            return;

        ModuleBreakableObjects breaker = part.FindModuleImplementing<ModuleBreakableObjects>();
        if (breaker == null || !Initialize(breaker))
        {
            this.ErrorAndDisable("missing breaker or penalty target");
            return;
        }

        breaker.OnBroke += ApplyPenalty;
        breaker.OnRepaired += RemovePenalty;

        if (breaker.broken)
            ApplyPenalty();
    }

    // Find the target module (and register any break gates), caching originals; false if unavailable.
    protected abstract bool Initialize(ModuleBreakableObjects breaker);
    // Set the penalized fields to their original value times factor (factor 1 restores them).
    protected abstract void Scale(float factor);

    private void ApplyPenalty() => Scale(brokenMultiplier);
    private void RemovePenalty() => Scale(1f);
}
