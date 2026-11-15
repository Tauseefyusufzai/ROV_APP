using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the ROV app's two-screen UI + config overlay.
///
/// Screens:
///   HomeScreen  — Logo, Start, Exit, Config
///   MainScreen  — 3 cameras, status bar, back, config
///   ConfigPanel — Overlay for connection settings
///
/// Handles:
///   - Screen transitions (Home ↔ Main)
///   - Config panel show/hide (from both screens)
///   - Button interactivity based on state
///   - Android back button navigation
///   - Connection/disconnection orchestration
/// </summary>
public class ROVUIController : MonoBehaviour
{
    // =========================================================================
    // Inspector — Screens
    // =========================================================================

    [Header("Screens")]
    [Tooltip("Home screen (start, exit, config).")]
    public GameObject connectPanel;      // HomeScreen

    [Tooltip("Main streaming screen (cameras, status).")]
    public GameObject streamingPanel;    // MainScreen

    [Tooltip("Config overlay panel.")]
    public GameObject settingsPanel;     // ConfigPanel

    // =========================================================================
    // Inspector — Controllers
    // =========================================================================

    [Header("Controllers")]
    public ROVStreamController  streamController;
    public ROVTelemetryReceiver telemetryReceiver;

    // =========================================================================
    // Inspector — Home Screen Buttons
    // =========================================================================

    [Header("Home Screen")]
    public Button connectButton;          // Start button
    public Button homeConfigButton;       // Config button on home
    public Button exitButton;             // Exit button
    public InputField rtspUrlInput;       // (in config panel)
    public InputField telemetryUrlInput;  // (in config panel)
    public Text   connectStatusText;

    // =========================================================================
    // Inspector — Main Screen Buttons
    // =========================================================================

    [Header("Main Screen")]
    public Button disconnectButton;       // Back button
    public Button settingsButton;         // Config button on main
    public Button snapshotButton;
    public Button toggleTelemetryButton;
    public Button reconnectButton;        // Reconnect/Refresh button

    // =========================================================================
    // Inspector — Config Panel
    // =========================================================================

    [Header("Config Panel")]
    public Button closeSettingsButton;
    public Button saveSettingsButton;
    public InputField videoWidthInput;
    public InputField videoHeightInput;
    public InputField fpsInput;
    public Toggle     autoReconnectToggle;

    // =========================================================================
    // Inspector — General
    // =========================================================================

    [Header("General")]
    public Text   appVersionText;
    public GameObject telemetryHUD;

    // =========================================================================
    // State
    // =========================================================================

    private enum Screen { Home, Main }
    private Screen _currentScreen = Screen.Home;
    private bool   _configVisible;
    private bool   _telemetryVisible = true;

    // =========================================================================
    // Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        ShowHomeScreen();
    }

    void Start()
    {
        SetupButtons();

        if (appVersionText != null)
            appVersionText.text = $"v{Application.version}";

        // Poll UI state every 500ms
        InvokeRepeating(nameof(SyncUIState), 0.5f, 0.5f);
    }

    void Update()
    {
        HandleBackButton();
    }

    // =========================================================================
    // Setup
    // =========================================================================

    private void SetupButtons()
    {
        // Home screen
        if (connectButton    != null) connectButton   .onClick.AddListener(OnStartClicked);
        if (homeConfigButton != null) homeConfigButton.onClick.AddListener(ShowConfig);
        if (exitButton       != null) exitButton      .onClick.AddListener(OnExitClicked);

        // Main screen
        if (disconnectButton != null) disconnectButton.onClick.AddListener(OnBackClicked);
        if (settingsButton   != null) settingsButton  .onClick.AddListener(ShowConfig);
        if (snapshotButton   != null) snapshotButton  .onClick.AddListener(OnSnapshotClicked);
        if (toggleTelemetryButton != null) toggleTelemetryButton.onClick.AddListener(ToggleTelemetryHUD);
        if (reconnectButton  != null) reconnectButton .onClick.AddListener(OnReconnectClicked);

        // Config panel
        if (closeSettingsButton != null) closeSettingsButton.onClick.AddListener(HideConfig);
        if (saveSettingsButton  != null) saveSettingsButton .onClick.AddListener(SaveConfigAndClose);

        // Auto-find home buttons if not wired
        if (connectPanel != null)
        {
            if (connectButton == null)
            {
                var t = connectPanel.transform.Find("StartButton");
                if (t != null) { connectButton = t.GetComponent<Button>(); connectButton.onClick.AddListener(OnStartClicked); }
            }
            if (homeConfigButton == null)
            {
                var t = connectPanel.transform.Find("HomeConfigButton");
                if (t != null) { homeConfigButton = t.GetComponent<Button>(); homeConfigButton.onClick.AddListener(ShowConfig); }
            }
            if (exitButton == null)
            {
                var t = connectPanel.transform.Find("ExitButton");
                if (t != null) { exitButton = t.GetComponent<Button>(); exitButton.onClick.AddListener(OnExitClicked); }
            }
        }

        // Auto-find main screen buttons if not wired
        if (streamingPanel != null)
        {
            if (disconnectButton == null)
            {
                var t = streamingPanel.transform.Find("TopBar/BackButton");
                if (t != null) { disconnectButton = t.GetComponent<Button>(); disconnectButton.onClick.AddListener(OnBackClicked); }
            }
            if (settingsButton == null)
            {
                var t = streamingPanel.transform.Find("TopBar/MainConfigButton");
                if (t != null) { settingsButton = t.GetComponent<Button>(); settingsButton.onClick.AddListener(ShowConfig); }
            }
            if (reconnectButton == null)
            {
                var t = streamingPanel.transform.Find("TopBar/ReconnectButton");
                if (t == null) t = streamingPanel.transform.Find("ReconnectButton");
                if (t != null) { reconnectButton = t.GetComponent<Button>(); reconnectButton.onClick.AddListener(OnReconnectClicked); }
            }
        }

        // Auto-find config panel buttons
        if (settingsPanel != null)
        {
            if (closeSettingsButton == null)
            {
                var t = settingsPanel.transform.Find("ConfigCard/CloseConfigButton");
                if (t != null) { closeSettingsButton = t.GetComponent<Button>(); closeSettingsButton.onClick.AddListener(HideConfig); }
            }
            if (saveSettingsButton == null)
            {
                var t = settingsPanel.transform.Find("ConfigCard/SaveConfigButton");
                if (t != null) { saveSettingsButton = t.GetComponent<Button>(); saveSettingsButton.onClick.AddListener(SaveConfigAndClose); }
            }
        }
    }

    // =========================================================================
    // Screen transitions
    // =========================================================================

    public void ShowHomeScreen()
    {
        _currentScreen = Screen.Home;
        if (connectPanel   != null) connectPanel  .SetActive(true);
        if (streamingPanel != null) streamingPanel.SetActive(false);
        HideConfig();
    }

    public void ShowMainScreen()
    {
        _currentScreen = Screen.Main;
        if (connectPanel   != null) connectPanel  .SetActive(false);
        if (streamingPanel != null) streamingPanel.SetActive(true);
        HideConfig();
    }

    // =========================================================================
    // Config panel
    // =========================================================================

    public void ShowConfig()
    {
        _configVisible = true;
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            PopulateConfigInputs();
        }
    }

    private void PopulateConfigInputs()
    {
        if (settingsPanel == null) return;
        var formArea = settingsPanel.transform.Find("ConfigCard/FormArea");
        if (formArea == null) return;

        if (streamController != null)
        {
            var cam1Input = formArea.Find("Cam1Url_Input")?.GetComponent<InputField>();
            if (cam1Input != null) cam1Input.text = streamController.rtspUrl1;

            var cam2Input = formArea.Find("Cam2Url_Input")?.GetComponent<InputField>();
            if (cam2Input != null) cam2Input.text = streamController.rtspUrl2;

            var cam3Input = formArea.Find("Cam3Url_Input")?.GetComponent<InputField>();
            if (cam3Input != null) cam3Input.text = streamController.rtspUrl3;
        }

        if (telemetryReceiver != null)
        {
            var telInput = formArea.Find("TelemetryUrl_Input")?.GetComponent<InputField>();
            if (telInput != null) telInput.text = telemetryReceiver.socketUrl;
        }
    }

    public void HideConfig()
    {
        _configVisible = false;
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void ToggleTelemetryHUD()
    {
        _telemetryVisible = !_telemetryVisible;
        if (telemetryHUD != null) telemetryHUD.SetActive(_telemetryVisible);
    }

    // =========================================================================
    // Button handlers
    // =========================================================================

    private void OnStartClicked()
    {
        ShowMainScreen();

        // Connect video streams
        if (streamController != null)
            streamController.Connect();

        // Connect telemetry
        if (telemetryReceiver != null)
            telemetryReceiver.ConnectTelemetry();

        UpdateConnectionStatus("Connecting...");
    }

    private void OnBackClicked()
    {
        // Disconnect everything
        if (streamController != null)
            streamController.Disconnect();

        if (telemetryReceiver != null)
            telemetryReceiver.DisconnectTelemetry();

        UpdateConnectionStatus("Disconnected");
        ShowHomeScreen();
    }

    private void OnSnapshotClicked()
    {
        if (streamController != null)
            streamController.TakeSnapshot();
    }

    private void OnReconnectClicked()
    {
        Debug.Log("[ROV-UI] Reconnect/Refresh requested.");

        if (streamController != null)
            streamController.Reconnect();

        if (telemetryReceiver != null)
        {
            telemetryReceiver.DisconnectTelemetry();
            telemetryReceiver.ConnectTelemetry();
        }

        UpdateConnectionStatus("Reconnecting...");
    }

    private void OnExitClicked()
    {
        Debug.Log("[ROV-UI] Exit requested.");

        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    // =========================================================================
    // Config save
    // =========================================================================

    private void SaveConfigAndClose()
    {
        bool rtspChanged = false;
        bool telemetryChanged = false;

        if (settingsPanel != null)
        {
            var formArea = settingsPanel.transform.Find("ConfigCard/FormArea");
            if (formArea != null)
            {
                // Camera 1 URL
                var cam1Input = formArea.Find("Cam1Url_Input")?.GetComponent<InputField>();
                if (cam1Input != null && streamController != null)
                {
                    string val = cam1Input.text.Trim();
                    if (!string.IsNullOrEmpty(val) && val != streamController.rtspUrl1)
                    {
                        streamController.rtspUrl1 = val;
                        streamController.rtspUrl = val;
                        PlayerPrefs.SetString("ROV_Cam1Url", val);
                        rtspChanged = true;
                    }
                }

                // Camera 2 URL
                var cam2Input = formArea.Find("Cam2Url_Input")?.GetComponent<InputField>();
                if (cam2Input != null && streamController != null)
                {
                    string val = cam2Input.text.Trim();
                    if (!string.IsNullOrEmpty(val) && val != streamController.rtspUrl2)
                    {
                        streamController.rtspUrl2 = val;
                        PlayerPrefs.SetString("ROV_Cam2Url", val);
                        rtspChanged = true;
                    }
                }

                // Camera 3 URL
                var cam3Input = formArea.Find("Cam3Url_Input")?.GetComponent<InputField>();
                if (cam3Input != null && streamController != null)
                {
                    string val = cam3Input.text.Trim();
                    if (!string.IsNullOrEmpty(val) && val != streamController.rtspUrl3)
                    {
                        streamController.rtspUrl3 = val;
                        PlayerPrefs.SetString("ROV_Cam3Url", val);
                        rtspChanged = true;
                    }
                }

                // Telemetry URL
                var telInput = formArea.Find("TelemetryUrl_Input")?.GetComponent<InputField>();
                if (telInput != null && telemetryReceiver != null)
                {
                    string val = telInput.text.Trim();
                    if (!string.IsNullOrEmpty(val) && val != telemetryReceiver.socketUrl)
                    {
                        telemetryReceiver.socketUrl = val;
                        PlayerPrefs.SetString("ROV_TelemetryUrl", val);
                        telemetryChanged = true;
                    }
                }
            }
        }

        PlayerPrefs.Save();
        Debug.Log("[ROV-UI] Configuration saved.");
        HideConfig();

        // If changes were made while active, apply them immediately
        if (_currentScreen == Screen.Main)
        {
            if (rtspChanged && streamController != null)
            {
                streamController.Reconnect();
            }
            if (telemetryChanged && telemetryReceiver != null)
            {
                telemetryReceiver.SetUrl(telemetryReceiver.socketUrl);
            }
        }
    }

    // =========================================================================
    // UI state sync
    // =========================================================================

    private void SyncUIState()
    {
        if (_currentScreen != Screen.Main) return;

        // Update connection indicator
        if (streamController != null)
        {
            bool streaming = streamController.IsStreaming;
            if (snapshotButton != null)
                snapshotButton.interactable = streaming;

            // Update connection indicator color
            var connIndicator = streamingPanel?.transform.Find("TopBar/ConnectionIndicator");
            if (connIndicator != null)
            {
                var img = connIndicator.GetComponent<Image>();
                if (img != null)
                {
                    img.color = streaming
                        ? new Color(0.106f, 0.369f, 0.125f, 1f)   // Green
                        : new Color(0.545f, 0.106f, 0.106f, 1f);  // Red
                }

                var text = connIndicator.Find("ConnectionText")?.GetComponent<Text>();
                if (text != null)
                {
                    string status = streaming ? "CONNECTED" : "DISCONNECTED";
                    if (text.text != status) text.text = status;
                }
            }
        }
    }

    private void UpdateConnectionStatus(string status)
    {
        if (connectStatusText != null && connectStatusText.text != status)
            connectStatusText.text = status;
    }

    // =========================================================================
    // Android back button
    // =========================================================================

    private void HandleBackButton()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (_configVisible)
        {
            HideConfig();
        }
        else if (_currentScreen == Screen.Main)
        {
            OnBackClicked();
        }
        else
        {
            OnExitClicked();
        }
    }
}
