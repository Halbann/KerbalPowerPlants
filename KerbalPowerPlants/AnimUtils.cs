using UnityEngine;

namespace KerbalPowerPlants;

public static class AnimUtils
{
    public static (Animation, AnimationState) FindAnim(Part part, string name)
    {
        Animation[] animators = part.FindModelAnimators(name);
        if (animators.Length == 0)
        {
            Logger.Error($"animation '{name}' not found");
            return (null, null);
        }

        Animation animation = animators[0];
        AnimationState state = animation[name];

        if (state == null)
            Logger.Error($"clip '{name}' missing from Animation component");

        return (animation, state);
    }

    public static Sampler CreateSampler(Part part, string name, int layer)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Logger.Error($"Tried to create an animation sampler on {part.name} with an empty name");
            return null;
        }

        (var anim, var clipState) = FindAnim(part, name);
        if (anim == null || clipState == null)
        {
            Logger.Error($"Missing animation {name} on {part.name}");
            return null;
        }

        clipState.layer = layer;
        clipState.wrapMode = WrapMode.Once;

        return new Sampler(anim, clipState, name);
    }

    public class Sampler(Animation anim, AnimationState clip, string name)
    {
        public string name = name;
        public Animation anim = anim;
        public AnimationState clip = clip;
        public float normalizedTime;

        public void Sample(float normalizedTime)
        {
            if (clip == null)
                return;

            clip.enabled = true;
            clip.weight = 1f;
            clip.normalizedTime = normalizedTime;
            clip.speed = 0f;

            if (anim == null)
                return;

            anim.Play(name);
            anim.Sample();
            anim.Stop(name);
        }
    }

}
