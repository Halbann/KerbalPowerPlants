namespace KerbalPowerPlants;

public class ModuleThrustPenalty : ModuleBrokenPenalty
{
    private ModuleEngines engine;
    private float originalMultIsp;
    private float originalMaxThrust;

    protected override bool Initialize(ModuleBreakableObjects breaker)
    {
        engine = part.FindModuleImplementing<ModuleEngines>();
        if (engine == null)
            return false;

        originalMultIsp = engine.multIsp;
        originalMaxThrust = engine.maxThrust;
        return true;
    }

    protected override void Scale(float factor)
    {
        engine.multIsp = originalMultIsp * factor;
        engine.maxThrust = originalMaxThrust * factor;
    }
}
