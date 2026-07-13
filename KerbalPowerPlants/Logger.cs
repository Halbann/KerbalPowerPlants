using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

internal static class Logger
{
    public const string modName = "KerbalPowerPlants";

    private static string Format(string message, string sourceFilePath)
    {
        string className = Path.GetFileNameWithoutExtension(sourceFilePath);
        return $"[{modName}]: {className}: {message}.";
    }

    // The compiler automatically fills in callerFilePath at compile time
    public static void Log(string message, [CallerFilePath] string sourceFilePath = "") =>
        Debug.Log(Format(message, sourceFilePath));

    public static void LogWarning(string message, [CallerFilePath] string sourceFilePath = "") =>
        Debug.LogWarning(Format(message, sourceFilePath));

    public static void Error(string message, [CallerFilePath] string sourceFilePath = "") =>
        Debug.LogError(Format(message, sourceFilePath));
}
