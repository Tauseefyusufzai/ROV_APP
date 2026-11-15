using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Automatically captures all Unity log messages (Debug.Log, warnings, errors, exceptions)
/// and writes them to a persistent file on the device (rov_app_logs.txt).
/// </summary>
public static class ROVFileLogger
{
    private static string s_LogFilePath;
    private static readonly object s_Lock = new object();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        try
        {
            s_LogFilePath = Path.Combine(Application.persistentDataPath, "rov_app_logs.txt");

            // Overwrite log file on startup to prevent it from growing indefinitely,
            // while capturing useful system/build information.
            File.WriteAllText(s_LogFilePath, 
                $"=== ROV App Log Started: {DateTime.Now} ===\n" +
                $"OS: {SystemInfo.operatingSystem}\n" +
                $"Device: {SystemInfo.deviceModel} ({SystemInfo.deviceName})\n" +
                $"CPU: {SystemInfo.processorType} ({SystemInfo.processorCount} cores)\n" +
                $"System Memory: {SystemInfo.systemMemorySize} MB\n" +
                $"Graphics: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)\n" +
                $"Unity Version: {Application.unityVersion}\n" +
                $"App Version: {Application.version}\n" +
                $"Log Path: {s_LogFilePath}\n" +
                $"================================================\n\n");

            // Listen to logs from any thread (useful for background thread logs)
            Application.logMessageReceivedThreaded += OnLogReceived;

            Debug.Log($"[ROV-Logger] Initialized. Writing logs to: {s_LogFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ROV-Logger] Failed to initialize file logger: {e.Message}");
        }
    }

    private static void OnLogReceived(string logString, string stackTrace, LogType type)
    {
        try
        {
            lock (s_Lock)
            {
                string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
                string prefix = type == LogType.Error || type == LogType.Exception ? "ERROR" : type.ToString().ToUpper();
                string line = $"[{timeStr}] [{prefix}] {logString}\n";

                if (type == LogType.Exception || type == LogType.Error)
                {
                    line += $"{stackTrace}\n";
                }

                File.AppendAllText(s_LogFilePath, line);
            }
        }
        catch
        {
            // Fail silently to avoid infinite recursive logging loops on write failure
        }
    }
}
