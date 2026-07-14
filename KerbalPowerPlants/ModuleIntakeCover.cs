namespace KerbalPowerPlants;

// Bridges the intake cover's break state to sibling modules:
// gates breaking on the cover being fully deployed, throttles the
// intake once the cover is gone, and restores it on repair.
public class ModuleIntakeCover : PartModule
{
    [KSPField] public string linkedAnimationName = "Cover";
    [KSPField] public float animationProgressThreshold = 0.3f;
    [KSPField] public float brokenIntakeMultiplier = 0.3f;

    private ModuleResourceIntake intake;
    private double originalArea;

    public override void OnStart(StartState startState)
    {
        if (!HighLogic.LoadedSceneIsFlight)
            return;

        ModuleBreakableObjects breaker = part.FindModuleImplementing<ModuleBreakableObjects>();
        intake = part.FindModuleImplementing<ModuleResourceIntake>();
        ModuleLinkedAnimation cover = FindCover();

        if (breaker == null || intake == null || cover == null)
        {
            this.ErrorAndDisable("missing breaker, intake, or cover");
            return;
        }

        originalArea = intake.area;

        breaker.AddBreakCondition(() => cover.Progress >= animationProgressThreshold);

        breaker.OnBroke += ApplyPenalty;
        breaker.OnRepaired += RemovePenalty;

        if (breaker.broken)
            ApplyPenalty();
    }

    private ModuleLinkedAnimation FindCover()
    {
        foreach (ModuleLinkedAnimation anim in part.FindModulesImplementing<ModuleLinkedAnimation>())
            if (anim.animationName == linkedAnimationName)
                return anim;

        return null;
    }

    private void ApplyPenalty() => intake.area = originalArea * brokenIntakeMultiplier;
    private void RemovePenalty() => intake.area = originalArea;
}
