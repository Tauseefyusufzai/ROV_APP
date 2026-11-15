using UnityEngine;

/// <summary>
/// ScriptableObject for ROV app configuration.
/// Create via Assets → Create → ROV → App Config.
/// </summary>
[CreateAssetMenu(fileName = "ROVAppConfig", menuName = "ROV/App Config", order = 1)]
public class ROVAppConfig : ScriptableObject
{
    [Header("ROV Connection")]
    [Tooltip("Default RTSP stream URL of the ROV.")]
    public string defaultRtspUrl = "rtsp://192.168.0.145:1945/live";

    [Tooltip("Default Socket.IO telemetry endpoint.")]
    public string defaultTelemetryUrl = "ws://192.168.1.1:3000/socket.io/?EIO=4&transport=websocket";

    [Header("Video Settings")]
    [Tooltip("Video render width in pixels.")]
    public int videoWidth = 1280;

    [Tooltip("Video render height in pixels.")]
    public int videoHeight = 720;

    [Tooltip("Target frame extraction rate (fps). Lower = less CPU usage.")]
    [Range(10, 60)]
    public int targetFps = 30;

    [Header("Network")]
    [Tooltip("Network buffer in ms. Lower = less latency, higher = smoother.")]
    [Range(100, 2000)]
    public int networkCachingMs = 300;

    [Tooltip("Auto-connect to RTSP stream on app start.")]
    public bool autoConnectVideo = false;

    [Tooltip("Auto-connect to telemetry on app start.")]
    public bool autoConnectTelemetry = true;

    [Header("Reconnect")]
    [Tooltip("Automatically reconnect when the stream drops.")]
    public bool autoReconnect = true;

    [Tooltip("Seconds between reconnect attempts.")]
    [Range(1f, 30f)]
    public float reconnectDelaySec = 3f;

    [Tooltip("Maximum reconnect attempts (0 = unlimited).")]
    public int maxReconnectAttempts = 10;

    [Header("Snapshot")]
    [Tooltip("Save snapshots to this subfolder inside persistentDataPath.")]
    public string snapshotFolder = "ROV_Snapshots";

    [Header("Debug")]
    public bool enableDebugLogs = true;
}
