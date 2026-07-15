namespace KerbalPowerPlants;

// Base: applies a configurable multiplier to some numeric module value
// while the part's breakable objects are broken, restoring it on repair.
public abstract class ModuleBrokenPenalty : PartModule
{
    [KSPField] public float brokenMultiplier = 0.3f;

    private double original;

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

        original = Value;

        breaker.OnBroke += ApplyPenalty;
        breaker.OnRepaired += RemovePenalty;

        if (breaker.broken)
            ApplyPenalty();
    }

    // Find the target module (and register any break gates); false if unavailable.
    protected abstract bool Initialize(ModuleBreakableObjects breaker);
    protected abstract double Value { get; set; }

    private void ApplyPenalty() => Value = original * brokenMultiplier;
    private void RemovePenalty() => Value = original;
}
