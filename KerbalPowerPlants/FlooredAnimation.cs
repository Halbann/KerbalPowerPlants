using UnityEngine;

namespace KerbalPowerPlants;

// An animation played open or closed, held above a variable floor.
// Use case: bay doors around a deployable engine that must not clip the nozzle as it stows.
public class FlooredAnimation : MonoBehaviour
{
    #region Config

    public Part part;
    public string animationName = string.Empty;
    public int animationLayer = 1;
    public bool open;

    #endregion

    public float minProgress;

    // Normalized position. 0 (closed) to 1 (open).
    public bool AtClosed => !open && progress <= 0f && minProgress <= 0f;
    public bool Settled => anim == null || damper.Settled;

    AnimUtils.Sampler anim;
    public float progress;
    private SymmetricSmoothDamp damper;
    private float sampled = -1f;

    public float Progress => damper.current;

    // Smoothing on the normalized clip time. accel <= 0 disables (snaps).
    public float smoothAccel = 0f;
    public float smoothMaxSpeed = Mathf.Infinity;

    #region Lifetime

    protected void Start()
    {
        if (!SceneValid())
            return;

        anim = AnimUtils.CreateSampler(part, animationName, animationLayer);
        if (anim == null)
        {
            this.ErrorAndDisable($"Failed to create animation sampler for {animationName} on {part}");
            return;
        }

        // Snap to the persisted end state.
        damper = new(Target(), smoothAccel, smoothMaxSpeed);
        anim.Sample(damper.current);
        sampled = damper.current;
    }

    protected void Update()
    {
        if (anim == null || !SceneValid())
            return;

        float t = Target();

        // Hard floor: stay clear even if it rose faster than we can play.
        if (t < minProgress)
            t = Mathf.Clamp01(minProgress);

        damper.Settings(smoothAccel, smoothMaxSpeed);
        progress = damper.UpdateTo(t, Time.deltaTime);

        if (progress != sampled)
        {
            anim.Sample(progress);
            sampled = progress;
        }
    }

    protected void OnDisable()
    {
        damper.Reset(Target());
        anim?.Sample(damper.current);
    }

    #endregion

    #region Control

    public void SetOpen(bool value, bool instant)
    {
        open = value;

        if (!instant)
            return;

        // Instant snap.
        progress = Target();
        anim.Sample(progress);
        sampled = progress;
    }

    #endregion

    #region Animation

    private float Target() => Mathf.Clamp01(Mathf.Max(open ? 1f : 0f, minProgress));

    #endregion

    private bool SceneValid() =>
        HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight;
}
