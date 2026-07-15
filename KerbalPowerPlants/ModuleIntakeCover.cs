namespace KerbalPowerPlants;

// Gates breaking on the cover being deployed, then throttles the
// intake once the cover is gone; restored on repair.
public class ModuleIntakeCover : ModuleBrokenPenalty
{
    [KSPField] public string linkedAnimationName = "Cover";
    [KSPField] public float animationProgressThreshold = 0.3f;

    private ModuleResourceIntake intake;
    private double originalArea;

    protected override bool Initialize(ModuleBreakableObjects breaker)
    {
        intake = part.FindModuleImplementing<ModuleResourceIntake>();
        ModuleLinkedAnimation cover = FindCover();

        if (intake == null || cover == null)
            return false;

        originalArea = intake.area;
        breaker.AddBreakCondition(() => cover.Progress >= animationProgressThreshold);
        return true;
    }

    protected override void Scale(float factor) => intake.area = originalArea * factor;

    private ModuleLinkedAnimation FindCover()
    {
        foreach (ModuleLinkedAnimation anim in part.FindModulesImplementing<ModuleLinkedAnimation>())
            if (anim.animationName == linkedAnimationName)
                return anim;

        return null;
    }
}
