using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace KerbalPowerPlants;

// Plays an animation once forward/reverse to follow a bool on a sibling module.
public class ModuleLinkedAnimation : PartModule, IMultipleDragCube
{
    [KSPField] public string animationName = "";
    [KSPField] public int layer = 1;

    [KSPField] public string linkedModule = "";
    [KSPField] public string linkedState = "";

    [KSPField] public bool invert = false;

    [KSPField] public bool instantInEditor = true;

    // Render Deployed/Retracted drag cubes at the clip ends and blend them
    // with its progress.
    [KSPField] public bool dragCubes = false;

    private Animation anim;
    private AnimationState clip;
    private AnimUtils.Sampler sampler;
    private Func<bool> read;
    private bool last;

    public float Progress => clip == null ? 0f : (clip.enabled ? Mathf.Clamp01(clip.normalizedTime) : (last ? 1f : 0f));

    public override void OnStart(StartState startState)
    {
        if (!SceneValid())
        {
            enabled = false;
            return;
        }

        // Get anim and clip.
        (anim, clip) = AnimUtils.FindAnim(part, animationName);

        if (clip == null)
        {
            this.ErrorAndDisable($"missing animation '{animationName}'");
            return;
        }

        // Get module and field.
        PartModule watched = part.Modules[linkedModule];
        FieldInfo state = watched?.GetType().GetField(linkedState);

        if (state == null || state.FieldType != typeof(bool))
        {
            this.ErrorAndDisable($"no bool '{linkedModule}.{linkedState}' to link '{animationName}' to");
            return;
        }

        // Set up clip.
        clip.layer = layer;
        clip.wrapMode = WrapMode.Once;

        // Compiled so the per-frame poll is allocation free.
        read = Expression.Lambda<Func<bool>>(Expression.Field(Expression.Constant(watched), state)).Compile();
        sampler = new AnimUtils.Sampler(anim, clip, animationName);

        // Snap initial.
        last = Read();
        Snap(last);
    }

    protected void Update()
    {
        if (read == null)
            return;

        bool value = Read();

        // Lazy play.
        if (value != last)
        {
            if (instantInEditor && HighLogic.LoadedSceneIsEditor)
            {
                last = value;
                Snap(value);
                return;
            }

            clip.speed = value ? 1f : -1f;
            clip.normalizedTime = Progress;
            last = value;
            anim.Play(animationName);
        }
        // Interpolate drag while playing.
        else if (clip.enabled)
        {
            SetDragState(Mathf.Clamp01(clip.normalizedTime));
        }
    }

    private bool Read() => read() != invert;

    // Jump straight to the settled pose.
    private void Snap(bool value)
    {
        sampler.Sample(value ? 1f : 0f);
        SetDragState(value ? 1f : 0f);
    }

    private static bool SceneValid() =>
        HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight;

    #region Drag cubes

    public bool IsMultipleCubesActive => dragCubes;

    public bool UsesProceduralDragCubes() => false;

    public string[] GetDragCubeNames() => ["Retracted", "Deployed"];

    // Called on a render copy before OnStart; find the clip locally.
    public void AssumeDragCubePosition(string name)
    {
        (_, var pose) = AnimUtils.FindAnim(part, animationName);
        if (pose == null)
            return;

        pose.speed = 0f;
        pose.enabled = true;
        pose.weight = 1f;
        pose.normalizedTime = name == "Deployed" ? 1f : 0f;
    }

    // t is the clip's progress toward Deployed.
    private void SetDragState(float t)
    {
        if (!dragCubes)
            return;

        part.DragCubes.SetCubeWeight("Deployed", t);
        part.DragCubes.SetCubeWeight("Retracted", 1f - t);
    }

    #endregion
}
