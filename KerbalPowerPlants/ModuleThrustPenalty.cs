namespace KerbalPowerPlants;

// Scales the engine's thrust down while its blades are broken off.
public class ModuleThrustPenalty : ModuleBrokenPenalty
{
    private ModuleEngines engine;

    protected override bool Initialize(ModuleBreakableObjects breaker)
        => (engine = part.FindModuleImplementing<ModuleEngines>()) != null;

    protected override double Value
    {
        get => engine.maxThrust;
        set => engine.maxThrust = (float)value;
    }
}
