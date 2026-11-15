using System;
using UnityEngine;
using UnityEngine.UI;
using SocketIO;

/// <summary>
/// Receives ROV telemetry (battery, GPS, speed) via Socket.IO.
///
/// Expected server events:
///   "rov_telemetry" — JSON payload:
///   {
///     "serial_no": "ROV001",
///     "name": "ROV 1",
///     "lat": 28.6139,
///     "lon": 77.2090,
///     "depth": 12.5,
///     "heading": 180.0,
///     "speed": 1.2,
///     "battery": 85,
///     "status": "streaming",
///     "is_streaming": true
///   }
///
/// Uses the existing SocketIO plugin (Fabio Panettieri's implementation).
/// Attach this to a GameObject with a SocketIOComponent.
/// </summary>
public class ROVTelemetryReceiver : MonoBehaviour
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("Connection")]
    [Tooltip("Socket.IO endpoint URL. Set before Start() or via SetUrl().")]
    public string socketUrl = "ws://192.168.1.1:3000/socket.io/?EIO=4&transport=websocket";

    [Tooltip("Auto-connect on Start.")]
    public bool autoConnect = true;

    [Header("Configuration")]
    [Tooltip("Optional ScriptableObject config.")]
    public ROVAppConfig appConfig;

    [Header("Telemetry Event Names")]
    [Tooltip("Socket.IO event name for telemetry updates.")]
    public string telemetryEventName = "rov_telemetry";

    [Header("UI — Telemetry Display")]
    public Text batteryText;
    public Slider batterySlider;
    public Text gpsText;
    public Text speedText;
    public Text depthText;
    public Text headingText;
    public Text telemetryStatusText;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    // =========================================================================
    // Properties
    // =========================================================================

    [Header("Status (Read Only)")]
    [SerializeField] private bool _isConnected;
    [SerializeField] private int  _batteryLevel = 100;
    [SerializeField] private float _latitude;
    [SerializeField] private float _longitude;
    [SerializeField] private float _speed;
    [SerializeField] private float _depth;
    [SerializeField] private float _heading;

    public bool  IsConnected  => _isConnected;
    public int   BatteryLevel => _batteryLevel;
    public float Latitude     => _latitude;
    public float Longitude    => _longitude;
    public float Speed        => _speed;
    public float Depth        => _depth;
    public float Heading      => _heading;

    /// <summary>Full telemetry data record.</summary>
    public ROVData CurrentData => _rovData;

    // =========================================================================
    // Events
    // =========================================================================

    public event Action<ROVData> OnTelemetryUpdate;
    public event Action          OnTelemetryConnected;
    public event Action          OnTelemetryDisconnected;

    // =========================================================================
    // Internals
    // =========================================================================

    private SocketIOComponent _socket;
    private ROVData           _rovData;

    // UI throttle — avoid per-update string allocs
    private int   _lastDisplayedBattery = -1;
    private float _lastDisplayedLat     = float.NaN;
    private float _lastDisplayedLon     = float.NaN;
    private float _lastDisplayedSpeed   = -1f;
    private float _lastDisplayedDepth   = -1f;
    private float _lastDisplayedHeading = -1f;

    // Socket error throttle — prevent log flooding when server unreachable
    private float _lastSocketErrorTime = -999f;
    private int   _socketErrorCount    = 0;
    private const float SOCKET_ERROR_LOG_INTERVAL = 5f; // log at most once per 5s

    // =========================================================================
    // Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        _rovData = new ROVData();
        LoadTelemetryUrl();
    }

    private void LoadTelemetryUrl()
    {
        string defaultUrl = appConfig != null ? appConfig.defaultTelemetryUrl : "ws://192.168.1.1:3000/socket.io/?EIO=4&transport=websocket";
        socketUrl = PlayerPrefs.GetString("ROV_TelemetryUrl", defaultUrl);
    }

    void Start()
    {
        SetupSocket();

        if (autoConnect || (appConfig != null && appConfig.autoConnectTelemetry))
            ConnectTelemetry();
    }

    void OnDestroy()
    {
        DisconnectTelemetry();
    }

    // =========================================================================
    // Setup
    // =========================================================================

    private void SetupSocket()
    {
        _socket = GetComponent<SocketIOComponent>();
        if (_socket == null)
            _socket = gameObject.AddComponent<SocketIOComponent>();

        _socket.url         = socketUrl;
        _socket.autoConnect = false; // We control connection manually

        // Connection events
        _socket.On("connect", OnSocketConnected);
        _socket.On("disconnect", OnSocketDisconnected);
        _socket.On("error", OnSocketError);

        // Telemetry data event
        _socket.On(telemetryEventName, OnTelemetryReceived);

        Log($"Socket configured → {socketUrl}");
    }

    // =========================================================================
    // Public API
    // =========================================================================

    public void ConnectTelemetry()
    {
        if (_socket == null) return;
        if (_socket.IsConnected) return; // thread is already running
        Log("Connecting telemetry…");
        SetTelemetryStatus("Connecting…");
        _socket.Connect();
    }

    public void DisconnectTelemetry()
    {
        if (_socket == null) return;
        if (!_socket.IsConnected) return; // already closed
        Log("Disconnecting telemetry…");
        _socket.Close();
        _isConnected = false;
        SetTelemetryStatus("Disconnected");
    }

    /// <summary>Change the Socket.IO URL and reconnect.</summary>
    public void SetUrl(string newUrl)
    {
        socketUrl = newUrl;
        DisconnectTelemetry();

        // Recreate the socket component with the new URL
        if (_socket != null) Destroy(_socket);
        SetupSocket();
        ConnectTelemetry();
    }

    // =========================================================================
    // Socket event handlers
    // =========================================================================

    private void OnSocketConnected(SocketIOEvent e)
    {
        _isConnected = true;
        Log("Telemetry connected.");
        SetTelemetryStatus("Connected");
        OnTelemetryConnected?.Invoke();
    }

    private void OnSocketDisconnected(SocketIOEvent e)
    {
        _isConnected = false;
        Log("Telemetry disconnected.");
        SetTelemetryStatus("Disconnected");
        OnTelemetryDisconnected?.Invoke();
    }

    private void OnSocketError(SocketIOEvent e)
    {
        _socketErrorCount++;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now - _lastSocketErrorTime >= SOCKET_ERROR_LOG_INTERVAL)
        {
            _lastSocketErrorTime = now;
            Debug.LogWarning($"[ROV-Telemetry] Socket error ({_socketErrorCount} times): {e.data}");
            _socketErrorCount = 0;
        }
        SetTelemetryStatus("Error");
    }

    private void OnTelemetryReceived(SocketIOEvent e)
    {
        try
        {
            if (e.data == null) return;

            // Parse the JSON payload using the SocketIO plugin's JSONObject
            JSONObject json = e.data;

            // Update ROVData from JSON
            if (json.HasField("lat"))          _rovData.latitude     = json["lat"].f;
            if (json.HasField("lon"))          _rovData.longitude    = json["lon"].f;
            if (json.HasField("depth"))        _rovData.depth        = json["depth"].f;
            if (json.HasField("heading"))      _rovData.heading      = json["heading"].f;
            if (json.HasField("speed"))        _rovData.speed        = json["speed"].f;
            if (json.HasField("battery"))      _rovData.battery      = (int)json["battery"].f;
            if (json.HasField("status"))       _rovData.status       = json["status"].str;
            if (json.HasField("is_streaming")) _rovData.is_streaming = json["is_streaming"].b;
            if (json.HasField("serial_no"))    _rovData.serial_no    = json["serial_no"].str;
            if (json.HasField("name"))         _rovData.name         = json["name"].str;
            if (json.HasField("rtsp_url"))     _rovData.rtsp_url     = json["rtsp_url"].str;

            // Update cached inspector values
            _batteryLevel = _rovData.battery;
            _latitude     = _rovData.latitude;
            _longitude    = _rovData.longitude;
            _speed        = _rovData.speed;
            _depth        = _rovData.depth;
            _heading      = _rovData.heading;

            // Update UI
            UpdateTelemetryUI();

            // Notify listeners
            OnTelemetryUpdate?.Invoke(_rovData);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ROV-Telemetry] Parse error: {ex.Message}");
        }
    }

    // =========================================================================
    // UI Updates — all guard against redundant string assignments
    // =========================================================================

    private void UpdateTelemetryUI()
    {
        // Battery
        if (_lastDisplayedBattery != _batteryLevel)
        {
            _lastDisplayedBattery = _batteryLevel;

            if (batteryText != null)
                batteryText.text = _batteryLevel + "%";

            if (batterySlider != null)
            {
                float v = _batteryLevel / 100f;
                if (!Mathf.Approximately(batterySlider.value, v))
                    batterySlider.value = v;
            }
        }

        // GPS
        if (!Mathf.Approximately(_lastDisplayedLat, _latitude) ||
            !Mathf.Approximately(_lastDisplayedLon, _longitude))
        {
            _lastDisplayedLat = _latitude;
            _lastDisplayedLon = _longitude;

            if (gpsText != null)
                gpsText.text = $"{_latitude:F5}, {_longitude:F5}";
        }

        // Speed
        if (!Mathf.Approximately(_lastDisplayedSpeed, _speed))
        {
            _lastDisplayedSpeed = _speed;
            if (speedText != null)
                speedText.text = $"{_speed:F1} m/s";
        }

        // Depth
        if (!Mathf.Approximately(_lastDisplayedDepth, _depth))
        {
            _lastDisplayedDepth = _depth;
            if (depthText != null)
                depthText.text = $"{_depth:F1} m";
        }

        // Heading
        if (!Mathf.Approximately(_lastDisplayedHeading, _heading))
        {
            _lastDisplayedHeading = _heading;
            if (headingText != null)
                headingText.text = $"{_heading:F0}°";
        }
    }

    private void SetTelemetryStatus(string s)
    {
        if (telemetryStatusText != null && telemetryStatusText.text != s)
            telemetryStatusText.text = s;
    }

    // =========================================================================
    // Logging
    // =========================================================================

    private void Log(string msg)
    {
        if (enableDebugLogs) Debug.Log($"[ROV-Telemetry] {msg}");
    }
}
