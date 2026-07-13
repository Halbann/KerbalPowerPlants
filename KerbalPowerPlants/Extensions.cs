using UnityEngine;

namespace KerbalPowerPlants;

public static class Extensions
{
    public static void ErrorAndDisable(this PartModule module, string message)
    {
        Logger.Error($"{module.part.name}: {message}");
        module.enabled = false;
    }

    public static void ErrorAndDisable(this MonoBehaviour module, string message)
    {
        Logger.Error($"{module.name}: {message}");
        module.enabled = false;
    }
}
