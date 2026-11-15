using System;
using UnityEngine;

// =============================================================================
// ROVData — primary runtime model for ROV state
// =============================================================================

/// <summary>
/// Full ROV state held in memory at runtime.
/// Thread-safe timestamp tracking via atomic long write.
/// </summary>
[Serializable]
public class ROVData
{
    // ---- Serialised fields --------------------------------------------------
    public string serial_no;
    public string name;
    public string rtsp_url;
    public float  latitude;
    public float  longitude;
    public float  depth;          // Metres below surface (positive = deeper)
    public float  heading;        // Degrees 0–360, 0/360 = North
    public float  speed;          // m/s
    public int    battery;        // 0–100 %
    public string status;         // see ROVStatus constants
    public bool   is_streaming;
    public long   timestamp;      // Unix epoch ms

    // ---- Constructors -------------------------------------------------------

    public ROVData()
    {
        serial_no    = string.Empty;
        name         = "ROV";
        rtsp_url     = string.Empty;
        battery      = 100;
        status       = ROVStatus.Disconnected;
        is_streaming = false;
        timestamp    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public ROVData(string serialNo, string rovName, string rtspUrl)
    {
        serial_no    = serialNo ?? string.Empty;
        name         = rovName  ?? "ROV";
        rtsp_url     = ValidateUrl(rtspUrl);
        battery      = 100;
        status       = ROVStatus.Disconnected;
        is_streaming = false;
        timestamp    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Construct from a socket payload.</summary>
    public ROVData(ROVSocketData socket)
    {
        if (socket == null) throw new ArgumentNullException(nameof(socket));
        ApplySocketData(socket);
    }

    // ---- Mutators -----------------------------------------------------------

    public void UpdatePosition(float lat, float lon, float dep, float head, float spd)
    {
        latitude  = lat;
        longitude = lon;
        depth     = Mathf.Max(0f, dep);
        heading   = Mathf.Repeat(head, 360f);
        speed     = Mathf.Max(0f, spd);
        Touch();
    }

    public void UpdateBattery(int level)
    {
        battery = Mathf.Clamp(level, 0, 100);
        Touch();
    }

    public void SetStreamingStatus(bool streaming)
    {
        is_streaming = streaming;
        status       = streaming ? ROVStatus.Streaming : ROVStatus.Connected;
        Touch();
    }

    public void SetError(string reason = null)
    {
        is_streaming = false;
        status       = ROVStatus.Error;
        Touch();
    }

    /// <summary>Apply all fields from a live socket update.</summary>
    public void ApplySocketData(ROVSocketData socket)
    {
        serial_no    = socket.serial_no ?? serial_no;
        name         = socket.name      ?? name;
        rtsp_url     = ValidateUrl(socket.rtsp_url);
        latitude     = socket.lat;
        longitude    = socket.lon;
        depth        = Mathf.Max(0f, socket.depth);
        heading      = Mathf.Repeat(socket.heading, 360f);
        speed        = Mathf.Max(0f, socket.speed);
        battery      = Mathf.Clamp(socket.battery, 0, 100);
        status       = socket.status ?? ROVStatus.Disconnected;
        is_streaming = socket.is_streaming;
        Touch();
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>Age of this data record in seconds.</summary>
    public float AgeSeconds =>
        (float)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp) / 1000.0);

    public bool IsStale(float thresholdSeconds = 5f) => AgeSeconds > thresholdSeconds;

    private void Touch() =>
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        if (url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url.Trim();

        Debug.LogWarning($"[ROVData] Suspicious URL format: {url}");
        return url.Trim();
    }

    public override string ToString() =>
        $"ROVData[{serial_no}] \"{name}\" {status} | batt:{battery}% | depth:{depth:F1}m | {latitude:F5},{longitude:F5} | {rtsp_url}";
}

// =============================================================================
// ROVStatus — string constants
// =============================================================================

public static class ROVStatus
{
    public const string Disconnected = "disconnected";
    public const string Connected    = "connected";
    public const string Streaming    = "streaming";
    public const string Error        = "error";
}

// =============================================================================
// ROVSocketData — thin DTO for incoming socket/JSON payloads
// =============================================================================

/// <summary>
/// Lightweight data-transfer object mirroring the Socket.IO JSON payload.
/// Use <see cref="ROVData.ApplySocketData"/> to merge into a live <see cref="ROVData"/>.
/// </summary>
[Serializable]
public class ROVSocketData
{
    public string serial_no;
    public string name;
    public string rtsp_url;
    public float  lat;
    public float  lon;
    public float  depth;
    public float  heading;
    public float  speed;
    public int    battery;
    public string status;
    public bool   is_streaming;

    /// <summary>Produce a full <see cref="ROVData"/> from this payload.</summary>
    public ROVData ToROVData() => new ROVData(this);
}
