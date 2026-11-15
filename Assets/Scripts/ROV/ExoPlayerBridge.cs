using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// C# bridge to the Android ExoPlayer plugin (ROVExoPlayerPlugin.java).
///
/// On Android:
///   - Calls into the Java plugin via AndroidJavaObject/AndroidJNI.
///   - Receives decoded ARGB frames as byte[] and uploads to a Texture2D.
///
/// In Unity Editor (Windows/Mac):
///   - Falls back to Unity's built-in VideoPlayer for testing.
///   - No RTSP support in Editor; use HTTP URLs or local files for testing.
///
/// Performance notes:
///   - Frame data transfer uses a pre-allocated byte[] — zero alloc per frame.
///   - Texture upload uses LoadRawTextureData — one GPU copy, no Color32[] intermediate.
///   - State polling is throttled to 4× per second, not every frame.
/// </summary>
public class ExoPlayerBridge : MonoBehaviour
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("Video Display")]
    [Tooltip("RawImage that shows the video feed.")]
    public RawImage videoDisplay;

    [Tooltip("Video width in pixels.")]
    public int videoWidth = 1280;

    [Tooltip("Video height in pixels.")]
    public int videoHeight = 720;

    [Tooltip("Target frame extraction rate.")]
    [Range(10, 60)]
    public int targetFps = 30;

    [Header("Stream")]
    [Tooltip("RTSP URL.")]
    public string rtspUrl = "";

    [Tooltip("Auto-connect on Start.")]
    public bool autoConnect = false;

    [Header("Reconnect")]
    public bool  autoReconnect       = true;
    [Range(1f, 30f)]
    public float reconnectDelay      = 3f;
    public int   maxReconnectAttempts = 10;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    // =========================================================================
    // Status (Read Only)
    // =========================================================================

    [Header("Status (Read Only)")]
    [SerializeField] private bool   _isInitialized;
    [SerializeField] private bool   _isPlaying;
    [SerializeField] private bool   _isBuffering;
    [SerializeField] private float  _fps;
    [SerializeField] private int    _reconnectCount;
    [SerializeField] private string _status = "Not initialized";

    // =========================================================================
    // Events
    // =========================================================================

    public event Action         OnConnected;
    public event Action         OnDisconnected;
    public event Action<string> OnError;
    public event Action<int>    OnReconnecting;

    // =========================================================================
    // Properties
    // =========================================================================

    public bool   IsInitialized  => _isInitialized;
    public bool   IsPlaying      => _isPlaying;
    public bool   IsBuffering    => _isBuffering;
    public float  CurrentFPS     => _fps;
    public string Status         => _status;
    public int    ReconnectCount => _reconnectCount;

    // =========================================================================
    // Private state
    // =========================================================================

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject _plugin;
    private AndroidJavaObject _activity;
#endif

    // Editor fallback
#if UNITY_EDITOR
    private UnityEngine.Video.VideoPlayer _editorPlayer;
    private RenderTexture _editorRT;
#endif

    private Texture2D _videoTexture;
    private IntPtr    _buffer0Ptr = IntPtr.Zero;
    private IntPtr    _buffer1Ptr = IntPtr.Zero;

    // State polling throttle
    private float _statePollTimer;
    private const float STATE_POLL_INTERVAL = 0.25f;

    // FPS tracking
    private int   _frameCounter;
    private float _fpsSampleTimer;
    private const float FPS_SAMPLE_INTERVAL = 1f;

    // Reconnect
    private bool  _reconnectScheduled;
    private float _reconnectTimer;
    private bool  _firstFrameReceived;
    private float _lastFrameTime;
    private float _driftCheckTimer;
    private float _currentSpeed = 1.0f;

    // NoSignal overlay
    private GameObject _noSignalOverlay;

    // =========================================================================
    // Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (videoDisplay == null)
            videoDisplay = GetComponent<RawImage>();
    }

    void Start()
    {
        if (Initialize() && autoConnect && !string.IsNullOrEmpty(rtspUrl))
            Connect(rtspUrl);
    }

    void Update()
    {
        UploadPendingFrame();
        PollPlayerState();
        TrackFPS();
        ProcessReconnect();
        CheckStreamStall();
        CheckLatencyDrift();
    }

    void OnDestroy()  => Cleanup();
    void OnApplicationQuit() => Cleanup();

    // =========================================================================
    // Public API
    // =========================================================================

    public bool Initialize()
    {
        if (_isInitialized) return true;

        try
        {
            int bufSize = videoWidth * videoHeight * 4;

            // Create texture — RGBA32 matches Android Bitmap.Config.ARGB_8888 memory layout (R, G, B, A)
            _videoTexture = new Texture2D(videoWidth, videoHeight, TextureFormat.RGBA32, false, false);
            _videoTexture.filterMode = FilterMode.Bilinear;
            _videoTexture.wrapMode   = TextureWrapMode.Clamp;

            // Black placeholder
            var black = new byte[bufSize];
            for (int i = 0; i < bufSize; i += 4)
            {
                black[i]     = 0;   // R
                black[i + 1] = 0;   // G
                black[i + 2] = 0;   // B
                black[i + 3] = 255; // A
            }
            _videoTexture.LoadRawTextureData(black);
            _videoTexture.Apply(false);

            if (videoDisplay != null)
            {
                videoDisplay.texture = _videoTexture;
                videoDisplay.uvRect  = new Rect(0f, 1f, 1f, -1f); // Flip Y (Android is top-down)
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            InitAndroid();
#elif UNITY_EDITOR
            InitEditor();
#endif

            _isInitialized = true;
            SetStatus("Ready");
            Log("Initialized.");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Initialize failed: {ex.Message}");
            SetStatus("Init Error");
            return false;
        }
    }

    public void Connect() => Connect(rtspUrl);

    public void Connect(string url)
    {
        if (!_isInitialized) { LogError("Not initialized."); return; }
        if (string.IsNullOrEmpty(url)) { LogError("URL is empty."); return; }

        rtspUrl             = url;
        _reconnectScheduled = false;
        _reconnectTimer     = 0f;
        _firstFrameReceived = false;
        _frameCounter       = 0;
        _fps                = 0f;
        _lastFrameTime      = Time.unscaledTime;

        Log($"Connecting → {url}");
        SetStatus("Connecting…");

#if UNITY_ANDROID && !UNITY_EDITOR
        ConnectAndroid(url);
#elif UNITY_EDITOR
        ConnectEditor(url);
#endif
    }

    public void Stop()
    {
        CancelReconnect();
        SetStatus("Disconnecting…");

#if UNITY_ANDROID && !UNITY_EDITOR
        StopAndroid();
#elif UNITY_EDITOR
        StopEditor();
#endif

        _isPlaying   = false;
        _isBuffering = false;
        OnDisconnected?.Invoke();
        SetStatus("Disconnected");
        ToggleNoSignal(true);
    }

    /// <summary>
    /// Capture the current frame as a new Texture2D.
    /// Caller owns the returned texture and must Destroy() it.
    /// </summary>
    public Texture2D TakeSnapshot()
    {
        if (!_isPlaying || _videoTexture == null) return null;

        var snap = new Texture2D(_videoTexture.width, _videoTexture.height,
                                 _videoTexture.format, false);
        Graphics.CopyTexture(_videoTexture, snap);
        return snap;
    }

    // =========================================================================
    // Android implementation
    // =========================================================================

#if UNITY_ANDROID && !UNITY_EDITOR
    private void InitAndroid()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }

            _plugin = new AndroidJavaObject("com.navyuginfo.rovnavyug.ROVExoPlayerPlugin");

            _plugin.Call("init", _activity, videoWidth, videoHeight, targetFps);
            Log("Android ExoPlayer plugin initialized.");

            CacheBufferPointers();
        }
        catch (Exception ex)
        {
            LogError($"Android plugin init failed: {ex.Message}");
            _plugin = null;
            throw; // rethrow so outer Initialize() catch sets Init Error status
        }
    }

    private void CacheBufferPointers()
    {
        if (_plugin == null) return;
        try
        {
            long addr0 = _plugin.Call<long>("getBuffer0Address");
            if (addr0 != 0)
            {
                _buffer0Ptr = new IntPtr(addr0);
                Log($"Buffer 0 direct address: {_buffer0Ptr}");
            }
            else
            {
                LogError("Buffer 0 is null!");
            }

            long addr1 = _plugin.Call<long>("getBuffer1Address");
            if (addr1 != 0)
            {
                _buffer1Ptr = new IntPtr(addr1);
                Log($"Buffer 1 direct address: {_buffer1Ptr}");
            }
            else
            {
                LogError("Buffer 1 is null!");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to cache direct buffer pointers: {ex.Message}");
        }
    }

    private void ConnectAndroid(string url)
    {
        if (_plugin == null)
        {
            LogError("ConnectAndroid: plugin is null — init failed.");
            OnError?.Invoke("ExoPlayer plugin not initialized");
            return;
        }
        // Check if ExoPlayer init failed silently (caught in Java try-catch)
        bool hasInitError = _plugin.Call<bool>("getHasInitError");
        if (hasInitError)
        {
            string msg = _plugin.Call<string>("getErrorMessage");
            LogError($"ExoPlayer init error: {msg}");
            OnError?.Invoke(msg);
            return;
        }
        Log($"[RTSP] Starting stream: {url}");
        _plugin.Call("play", url);
    }

    private void StopAndroid()
    {
        _plugin?.Call("stop");
    }

    private void CleanupAndroid()
    {
        _plugin?.Call("release");
        _plugin   = null;
        _activity = null;
    }
#endif

    // =========================================================================
    // Editor fallback (Unity VideoPlayer)
    // =========================================================================

#if UNITY_EDITOR
    private void InitEditor()
    {
        _editorPlayer = gameObject.AddComponent<UnityEngine.Video.VideoPlayer>();
        _editorPlayer.playOnAwake = false;
        _editorPlayer.renderMode  = UnityEngine.Video.VideoRenderMode.RenderTexture;

        _editorRT = new RenderTexture(videoWidth, videoHeight, 0);
        _editorPlayer.targetTexture = _editorRT;

        if (videoDisplay != null)
        {
            videoDisplay.texture = _editorRT;
            videoDisplay.uvRect  = new Rect(0f, 0f, 1f, 1f); // No flip needed in editor
        }

        _editorPlayer.errorReceived += (vp, msg) =>
        {
            LogError($"Editor VideoPlayer error: {msg}");
            _isPlaying = false;
            OnError?.Invoke(msg);
        };

        _editorPlayer.started += (vp) =>
        {
            _isPlaying = true;
            _firstFrameReceived = true;
            SetStatus("Streaming (Editor)");
            ToggleNoSignal(false);
            OnConnected?.Invoke();
        };

        Log("Editor fallback initialized (Unity VideoPlayer). RTSP won't work — use HTTP URLs.");
    }

    private void ConnectEditor(string url)
    {
        _editorPlayer.url = url;
        _editorPlayer.Play();
    }

    private void StopEditor()
    {
        _editorPlayer?.Stop();
    }

    private void CleanupEditor()
    {
        if (_editorPlayer != null)
        {
            _editorPlayer.Stop();
            Destroy(_editorPlayer);
        }
        if (_editorRT != null)
        {
            _editorRT.Release();
            Destroy(_editorRT);
        }
    }
#endif

    // =========================================================================
    // Frame upload (Android only — Editor uses RenderTexture directly)
    // =========================================================================

    private void UploadPendingFrame()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_plugin == null || _videoTexture == null) return;

        bool hasFrame = _plugin.Call<bool>("isNewFrameAvailable");
        if (!hasFrame) return;

        _lastFrameTime = Time.unscaledTime;

        int readIdx = _plugin.Call<int>("getReadBufferIndex");
        IntPtr activePtr = (readIdx == 0) ? _buffer0Ptr : _buffer1Ptr;

        if (activePtr == IntPtr.Zero) return;

        try
        {
            int bufSize = videoWidth * videoHeight * 4;
            _videoTexture.LoadRawTextureData(activePtr, bufSize);
            _videoTexture.Apply(false);
            Interlocked.Increment(ref _frameCounter);

            // Clear flag on Java side so it knows we consumed this frame
            _plugin.Call("clearNewFrameAvailable");
        }
        catch (Exception ex)
        {
            Log($"Texture upload error: {ex.Message}");
        }

        if (!_firstFrameReceived)
        {
            _firstFrameReceived = true;
            _reconnectCount     = 0;
            SetStatus("Streaming");
            ToggleNoSignal(false);
            OnConnected?.Invoke();
            Log("First frame received — stream live.");
        }
#endif
    }

    // =========================================================================
    // State polling
    // =========================================================================

    private void PollPlayerState()
    {
        _statePollTimer += Time.unscaledDeltaTime;
        if (_statePollTimer < STATE_POLL_INTERVAL) return;
        _statePollTimer = 0f;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (_plugin == null) return;

        bool playing   = _plugin.Call<bool>("getIsPlaying");
        bool buffering = _plugin.Call<bool>("getIsBuffering");
        bool hasError  = _plugin.Call<bool>("getHasError");

        _isPlaying   = playing;
        _isBuffering = buffering;

        if (hasError)
        {
            string errMsg = _plugin.Call<string>("getErrorMessage");
            _plugin.Call("clearError");
            LogError($"ExoPlayer error: {errMsg}");
            _isPlaying = false;
            SetStatus("Error");
            ToggleNoSignal(true);
            OnError?.Invoke(errMsg);
            OnDisconnected?.Invoke();
            ScheduleReconnect();
        }
        else if (buffering && _status != "Buffering…")
        {
            SetStatus("Buffering…");
        }
#endif
    }

    private void CheckStreamStall()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!_isPlaying || !_firstFrameReceived) return;

        // If more than 3 seconds pass without receiving a new frame, trigger reconnect
        if (Time.unscaledTime - _lastFrameTime > 3.0f)
        {
            LogError("Stream stalled: No new frames received for 3 seconds. Reconnecting...");
            _lastFrameTime = Time.unscaledTime; // Reset timer to prevent double trigger

            Stop(); // Disconnects player and resets status
            ScheduleReconnect(); // Schedules reconnect attempt
        }
#endif
    }

    /// <summary>
    /// Periodically checks the buffered drift from the Java plugin.
    /// Uses dynamic playback speed adjustments (1.08x - 1.25x) to catch up to the live edge smoothly
    /// instead of calling seekTo(), which corrupts the RTSP decoder state and causes stream freezes.
    /// If drift is too large (>2000ms), it performs a clean reconnect to snap to the live edge immediately.
    /// </summary>
    private void CheckLatencyDrift()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_plugin == null || !_isPlaying || !_firstFrameReceived) return;

        _driftCheckTimer += Time.unscaledDeltaTime;
        if (_driftCheckTimer < 0.5f) return;
        _driftCheckTimer = 0f;

        long driftMs = _plugin.Call<long>("getBufferedDriftMs");
        
        if (driftMs > 2000)
        {
            LogError($"Latency drift is too high ({driftMs}ms). Triggering a clean reconnect to snap to live edge...");
            Stop();
            ScheduleReconnect();
            return;
        }

        float targetSpeed = 1.0f;
        if (driftMs > 500)
        {
            targetSpeed = 1.25f; // Catch up quickly
        }
        else if (driftMs > 100)
        {
            targetSpeed = 1.08f; // Gentle catch up
        }
        else if (driftMs < 50)
        {
            targetSpeed = 1.0f; // Return to normal speed when aligned
        }

        if (Mathf.Abs(_currentSpeed - targetSpeed) > 0.01f)
        {
            _currentSpeed = targetSpeed;
            Log($"Latency drift is {driftMs}ms. Adjusting playback speed to {_currentSpeed}x to catch up.");
            _plugin.Call("setPlaybackSpeed", _currentSpeed);
        }
#endif
    }

    // =========================================================================
    // FPS tracking
    // =========================================================================

    private void TrackFPS()
    {
        _fpsSampleTimer += Time.unscaledDeltaTime;
        if (_fpsSampleTimer < FPS_SAMPLE_INTERVAL) return;

        int count = Interlocked.Exchange(ref _frameCounter, 0);
        _fps = count / _fpsSampleTimer;
        _fpsSampleTimer = 0f;
    }

    // =========================================================================
    // Reconnect
    // =========================================================================

    private void ScheduleReconnect()
    {
        if (!autoReconnect || string.IsNullOrEmpty(rtspUrl)) return;
        if (maxReconnectAttempts > 0 && _reconnectCount >= maxReconnectAttempts)
        {
            LogError($"Max reconnects ({maxReconnectAttempts}) reached.");
            SetStatus("Reconnect Failed");
            return;
        }
        _reconnectScheduled = true;
        _reconnectTimer     = reconnectDelay;
        Log($"Reconnecting in {reconnectDelay:F0}s (attempt {_reconnectCount + 1})…");
    }

    private void ProcessReconnect()
    {
        if (!_reconnectScheduled) return;
        _reconnectTimer -= Time.unscaledDeltaTime;
        if (_reconnectTimer <= 0f)
        {
            _reconnectScheduled = false;
            _reconnectCount++;
            SetStatus($"Reconnecting ({_reconnectCount})…");
            OnReconnecting?.Invoke(_reconnectCount);
            Connect(rtspUrl);
        }
    }

    private void CancelReconnect()
    {
        _reconnectScheduled = false;
        _reconnectTimer     = 0f;
    }

    // =========================================================================
    // Cleanup
    // =========================================================================

    private void Cleanup()
    {
        Log("Cleaning up…");

#if UNITY_ANDROID && !UNITY_EDITOR
        CleanupAndroid();
#elif UNITY_EDITOR
        CleanupEditor();
#endif

        if (_videoTexture != null)
        {
            Destroy(_videoTexture);
            _videoTexture = null;
        }

        _buffer0Ptr = IntPtr.Zero;
        _buffer1Ptr = IntPtr.Zero;

        _isInitialized = false;
        _isPlaying     = false;
        _isBuffering   = false;
    }

    // =========================================================================
    // Logging
    // =========================================================================

    private void SetStatus(string s)
    {
        if (_status == s) return;
        _status = s;
    }

    /// <summary>
    /// Show or hide the "NO SIGNAL" text overlay on this camera's panel.
    /// </summary>
    private void ToggleNoSignal(bool show)
    {
        if (_noSignalOverlay == null && videoDisplay != null)
        {
            var t = videoDisplay.transform.parent != null
                ? videoDisplay.transform.parent.Find("NoSignalText")
                : videoDisplay.transform.Find("NoSignalText");

            // If the RawImage IS the camera root, search its own children
            if (t == null)
                t = videoDisplay.transform.Find("NoSignalText");

            if (t != null)
                _noSignalOverlay = t.gameObject;
        }

        if (_noSignalOverlay != null)
            _noSignalOverlay.SetActive(show);
    }

    private void Log(string msg)
    {
        if (enableDebugLogs) Debug.Log($"[ExoPlayerBridge] {msg}");
    }

    private void LogError(string msg) => Debug.LogError($"[ExoPlayerBridge] {msg}");
}
