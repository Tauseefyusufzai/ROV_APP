using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main ROV stream controller — orchestrates video playback, UI, and snapshots.
///
/// Attach to a GameObject with an ExoPlayerBridge and configure in Inspector.
/// Call Connect() / Disconnect() / TakeSnapshot() from UI buttons.
/// </summary>
public class ROVStreamController : MonoBehaviour
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("ROV Settings")]
    public string rovName = "ROV 1";
    public string rtspUrl = "rtsp://192.168.0.145:1945/cam1";
    public string rtspUrl1 = "rtsp://192.168.0.145:1945/cam1";
    public string rtspUrl2 = "rtsp://192.168.0.145:1945/cam2";
    public string rtspUrl3 = "rtsp://192.168.0.145:1945/cam3";
    public bool   autoConnectOnStart = false;

    [Header("Video Display")]
    public RawImage videoDisplay;
    public int      videoWidth  = 1280;
    public int      videoHeight = 720;

    [Header("Configuration")]
    [Tooltip("Optional ScriptableObject config. Overrides Inspector values if assigned.")]
    public ROVAppConfig appConfig;

    [Header("UI — Status")]
    public Text statusText;
    public Text rovNameText;
    public Text fpsText;
    public Text connectionMessageText;

    [Header("UI — Buttons")]
    public Button connectButton;
    public Button disconnectButton;
    public Button snapshotButton;
    public Button reconnectButton;

    [Header("UI — Input")]
    public InputField urlInputField;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    // =========================================================================
    // Status (Read Only)
    // =========================================================================

    [Header("Status (Read Only)")]
    [SerializeField] private bool   _isConnected;
    [SerializeField] private bool   _isStreaming;
    [SerializeField] private string _status = "Ready";

    // =========================================================================
    // Properties
    // =========================================================================

    public bool   IsConnected => _isConnected;
    public bool   IsStreaming  => _isStreaming;
    public string Status       => _status;

    // =========================================================================
    // Internals
    // =========================================================================

    public ExoPlayerBridge player1;
    public ExoPlayerBridge player2;
    public ExoPlayerBridge player3;
    private ExoPlayerBridge _player => player1;
    private Coroutine       _delayedConnectRoutine;

    // FPS UI throttle
    private float  _fpsUiTimer;
    private float  _lastDisplayedFps = -1f;

    private const float FPS_UI_INTERVAL    = 0.5f;
    private const float AUTO_CONNECT_DELAY = 1.5f;

    // =========================================================================
    // Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (videoDisplay == null)
            videoDisplay = GetComponent<RawImage>();

        ApplyConfig();
        LoadUrls();
    }

    void Start()
    {
        SetupVideoPlayer();
        SetupUI();
        ForceRefreshUI();

        if (autoConnectOnStart && !string.IsNullOrEmpty(rtspUrl1))
        {
            Log($"Auto-connect in {AUTO_CONNECT_DELAY}s…");
            _delayedConnectRoutine = StartCoroutine(DelayedConnect());
        }
    }

    void Update()
    {
        UpdateFpsUI();
    }

    void OnDestroy()
    {
        if (_delayedConnectRoutine != null)
            StopCoroutine(_delayedConnectRoutine);

        UnsubscribeEvents();
    }

    // =========================================================================
    // Config
    // =========================================================================

    private void ApplyConfig()
    {
        if (appConfig == null) return;

        rtspUrl           = appConfig.defaultRtspUrl;
        videoWidth        = appConfig.videoWidth;
        videoHeight       = appConfig.videoHeight;
        autoConnectOnStart = appConfig.autoConnectVideo;
        enableDebugLogs   = appConfig.enableDebugLogs;
    }

    private void LoadUrls()
    {
        string default1 = appConfig != null ? appConfig.defaultRtspUrl : "rtsp://192.168.0.145:1945/cam1";
        string default2 = default1.Replace("cam1", "cam2").Replace("live", "cam2");
        string default3 = default1.Replace("cam1", "cam3").Replace("live", "cam3");

        rtspUrl1 = PlayerPrefs.GetString("ROV_Cam1Url", default1);
        rtspUrl2 = PlayerPrefs.GetString("ROV_Cam2Url", default2);
        rtspUrl3 = PlayerPrefs.GetString("ROV_Cam3Url", default3);

        rtspUrl = rtspUrl1;
    }

    // =========================================================================
    // Setup
    // =========================================================================

    private bool InitializePlayer(ExoPlayerBridge p, string url)
    {
        if (p == null) return false;
        p.videoDisplay    = p.GetComponent<RawImage>();
        p.videoWidth      = videoWidth;
        p.videoHeight     = videoHeight;
        p.rtspUrl         = url;
        p.enableDebugLogs = enableDebugLogs;
        p.autoConnect     = false;

        if (appConfig != null)
        {
            p.targetFps          = appConfig.targetFps;
            p.autoReconnect      = appConfig.autoReconnect;
            p.reconnectDelay     = appConfig.reconnectDelaySec;
            p.maxReconnectAttempts = appConfig.maxReconnectAttempts;
        }

        return p.Initialize();
    }

    private void SetupVideoPlayer()
    {
        if (player1 == null || player2 == null || player3 == null)
        {
            var bridges = Resources.FindObjectsOfTypeAll<ExoPlayerBridge>();
            foreach (var b in bridges)
            {
                if (b.gameObject.scene.name == null) continue; // Skip assets/prefabs in Project folder
                
                if (b.gameObject.name == "Camera1_Main") player1 = b;
                else if (b.gameObject.name == "Camera2_Secondary") player2 = b;
                else if (b.gameObject.name == "Camera3_Secondary") player3 = b;
            }
        }

        // Fallback for player1 to videoDisplay component if still null
        if (player1 == null && videoDisplay != null)
        {
            player1 = videoDisplay.GetComponent<ExoPlayerBridge>();
        }

        bool p1Ok = InitializePlayer(player1, rtspUrl1);
        InitializePlayer(player2, rtspUrl2);
        InitializePlayer(player3, rtspUrl3);

        SubscribeEvents();

        if (p1Ok)
            SetStatus("Ready");
        else
        {
            SetStatus("Init Failed");
            Debug.LogError("[ROV] Main player init failed.");
        }
    }

    private void SetupUI()
    {
        if (rovNameText != null) rovNameText.text = rovName;

        if (urlInputField != null)
        {
            urlInputField.text = rtspUrl1;
            urlInputField.onEndEdit.AddListener(HandleUrlChanged);
        }
    }

    private void SubscribeEvents()
    {
        if (player1 != null)
        {
            player1.OnConnected    += HandleConnected;
            player1.OnDisconnected += HandleDisconnected;
            player1.OnError        += HandleError;
            player1.OnReconnecting += HandleReconnecting;
        }
    }

    private void UnsubscribeEvents()
    {
        if (player1 != null)
        {
            player1.OnConnected    -= HandleConnected;
            player1.OnDisconnected -= HandleDisconnected;
            player1.OnError        -= HandleError;
            player1.OnReconnecting -= HandleReconnecting;
        }
    }

    // =========================================================================
    // Public API
    // =========================================================================

    public void Connect()
    {
        if (player1 == null) { Debug.LogError("[ROV] Not initialized."); return; }
        if (string.IsNullOrEmpty(rtspUrl1)) { SetStatus("Error: No URL"); return; }
        SetStatus("Connecting…");

        if (player1 != null) player1.Connect(rtspUrl1);
        if (player2 != null) player2.Connect(rtspUrl2);
        if (player3 != null) player3.Connect(rtspUrl3);
    }

    public void Disconnect()
    {
        if (player1 != null) { player1.autoReconnect = false; player1.Stop(); }
        if (player2 != null) { player2.autoReconnect = false; player2.Stop(); }
        if (player3 != null) { player3.autoReconnect = false; player3.Stop(); }
        _isConnected = false;
        _isStreaming  = false;
        SetStatus("Disconnected");
    }

    public void Reconnect()
    {
        Log("Manual reconnect triggered.");
        Disconnect();
        bool autoReconn = appConfig != null ? appConfig.autoReconnect : true;
        if (player1 != null) player1.autoReconnect = autoReconn;
        if (player2 != null) player2.autoReconnect = autoReconn;
        if (player3 != null) player3.autoReconnect = autoReconn;
        Connect();
    }

    /// <summary>
    /// Capture current frame as PNG. Encoding is main-thread; disk I/O is background.
    /// </summary>
    public void TakeSnapshot()
    {
        if (_player == null || !_isStreaming)
        {
            Debug.LogWarning("[ROV] Not streaming — snapshot skipped.");
            return;
        }

        Texture2D snap = _player.TakeSnapshot();
        if (snap == null) return;

        byte[] png      = snap.EncodeToPNG();
        Destroy(snap);

        string folder = appConfig != null ? appConfig.snapshotFolder : "ROV_Snapshots";
        string dir    = System.IO.Path.Combine(Application.persistentDataPath, folder);
        string file   = $"ROV_{rovName}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string path   = System.IO.Path.Combine(dir, file);

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(path, png);
                Debug.Log($"[ROV] Snapshot saved → {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ROV] Snapshot write failed: {ex.Message}");
            }
        });
    }

    // =========================================================================
    // Event handlers
    // =========================================================================

    private void HandleConnected()
    {
        _isConnected = true;
        _isStreaming  = true;
        SetStatus("Streaming");
        Log("Stream connected.");
    }

    private void HandleDisconnected()
    {
        _isConnected = false;
        _isStreaming  = false;
        if (_status != "Disconnected") SetStatus("Stream Lost");
        Log("Stream disconnected.");
    }

    private void HandleError(string error)
    {
        _isConnected = false;
        _isStreaming  = false;
        SetStatus($"Error: {error}");
        Debug.LogError($"[ROV] {rovName}: {error}");
    }

    private void HandleReconnecting(int attempt)
    {
        SetStatus($"Reconnecting ({attempt})…");
        Log($"Reconnect attempt {attempt}");
    }

    // =========================================================================
    // UI helpers
    // =========================================================================

    private void HandleUrlChanged(string newUrl)
    {
        rtspUrl1 = newUrl.Trim();
        rtspUrl = rtspUrl1;
        if (player1 != null) player1.rtspUrl = rtspUrl1;
        Log($"URL → {rtspUrl1}");
    }

    private void SetStatus(string s)
    {
        if (_status == s) return;
        _status = s;

        if (statusText            != null && statusText.text            != s) statusText.text            = s;
        if (connectionMessageText != null && connectionMessageText.text != s) connectionMessageText.text = s;
    }

    private void UpdateFpsUI()
    {
        if (fpsText == null || _player == null) return;

        _fpsUiTimer += Time.unscaledDeltaTime;
        if (_fpsUiTimer < FPS_UI_INTERVAL) return;
        _fpsUiTimer = 0f;

        if (!_isStreaming)
        {
            if (_lastDisplayedFps >= 0f)
            {
                fpsText.text      = "— fps";
                _lastDisplayedFps = -1f;
            }
            return;
        }

        float fps        = _player.CurrentFPS;
        int   fpsRounded = Mathf.RoundToInt(fps);
        int   lastRounded = Mathf.RoundToInt(_lastDisplayedFps);
        if (fpsRounded == lastRounded) return;

        _lastDisplayedFps = fps;
        fpsText.text = fpsRounded + " fps";
    }

    private void ForceRefreshUI()
    {
        _lastDisplayedFps    = -2f;
        SetStatus(_status);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private IEnumerator DelayedConnect()
    {
        yield return new WaitForSeconds(AUTO_CONNECT_DELAY);
        Connect();
    }

    private void Log(string msg)
    {
        if (enableDebugLogs) Debug.Log($"[ROV] {msg}");
    }
}
