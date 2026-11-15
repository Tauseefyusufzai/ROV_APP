#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor utility — creates a professional 2-screen ROV scene.
/// Menu: ROV → Create ROV Scene
///
/// Layout (1920×1080 landscape reference):
///
/// ╔══════════════ HOME SCREEN ═══════════════╗
/// ║              [BG Image]                   ║
/// ║           ┌─────────────┐                 ║
/// ║           │    LOGO     │                 ║
/// ║           └─────────────┘                 ║
/// ║          ROV NAVYUG                       ║
/// ║   Remotely Operated Vehicle               ║
/// ║                                           ║
/// ║        ┌─────────────────┐                ║
/// ║        │     START       │                ║
/// ║        └─────────────────┘                ║
/// ║     ┌─────────┐  ┌─────────┐             ║
/// ║     │  CONFIG  │  │  EXIT   │             ║
/// ║     └─────────┘  └─────────┘             ║
/// ║                                  v1.0.0   ║
/// ╚═══════════════════════════════════════════╝
///
/// ╔══════════════ MAIN SCREEN ═══════════════╗
/// ║ [←] ROV NAVYUG        [⚙] [● Connected] ║ ← Top Bar (48px)
/// ╠═════════════════════╤════════════════════╣
/// ║                     │    Camera 2        ║
/// ║   Main Camera       ├────────────────────║
/// ║                     │    Camera 3        ║
/// ╠═════════════════════╧════════════════════╣
/// ║ BAT:85% │ SPD:1.2 │ DEP:5m │ HDG:180°   ║ ← Status Bar (52px)
/// ║ Connection: Connected                    ║
/// ╚══════════════════════════════════════════╝
/// </summary>
public class ROVSceneSetup : Editor
{
    // =========================================================================
    // Color palette — professional dark underwater theme
    // =========================================================================
    static readonly Color COL_BG_DARK      = Hex("#0A0E1A");       // Deep navy
    static readonly Color COL_BG_PANEL     = Hex("#141B2D");       // Dark panel
    static readonly Color COL_BG_CARD      = Hex("#1A2332");       // Card background
    static readonly Color COL_BG_INPUT     = Hex("#0D1520");       // Input field bg
    static readonly Color COL_ACCENT       = Hex("#00BCD4");       // Teal accent
    static readonly Color COL_ACCENT_DIM   = Hex("#00838F");       // Darker teal
    static readonly Color COL_BTN_START    = Hex("#00897B");       // Green-teal
    static readonly Color COL_BTN_CONFIG   = Hex("#37474F");       // Blue-grey
    static readonly Color COL_BTN_EXIT     = Hex("#B71C1C");       // Deep red
    static readonly Color COL_BTN_BACK     = Hex("#455A64");       // Slate
    static readonly Color COL_TEXT_PRIMARY = Color.white;
    static readonly Color COL_TEXT_LABEL   = Hex("#8892A0");       // Muted label
    static readonly Color COL_TEXT_DIM     = Hex("#546E7A");       // Dim text
    static readonly Color COL_STATUS_BAR   = Hex("#0D1117");       // Almost black
    static readonly Color COL_TOP_BAR      = Hex("#101828");       // Top bar bg
    static readonly Color COL_DIVIDER      = Hex("#1E2A3A");       // Divider line
    static readonly Color COL_VIDEO_BG     = Hex("#050810");       // Video placeholder

    static Font FONT => Resources.GetBuiltinResource<Font>("Arial.ttf");

    // =========================================================================
    // Main entry point
    // =========================================================================

    [MenuItem("ROV/Create ROV Scene", false, 1)]
    public static void CreateROVScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Camera
        var cam = Camera.main;
        if (cam != null) { cam.backgroundColor = COL_BG_DARK; cam.clearFlags = CameraClearFlags.SolidColor; }

        // Canvas
        var canvasGO = new GameObject("ROVCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // =====================================================================
        // HOME SCREEN
        // =====================================================================
        var homeScreen = BuildHomeScreen(canvasGO);

        // =====================================================================
        // MAIN SCREEN (starts hidden)
        // =====================================================================
        var mainScreen = BuildMainScreen(canvasGO);
        mainScreen.SetActive(false);

        // =====================================================================
        // CONFIG PANEL (overlay, starts hidden)
        // =====================================================================
        var configPanel = BuildConfigPanel(canvasGO);
        configPanel.SetActive(false);

        // =====================================================================
        // Manager — wire up controllers
        // =====================================================================
        var managerGO = new GameObject("ROVManager");
        WireUpControllers(managerGO, homeScreen, mainScreen, configPanel);

        // Event System
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("ROV Scene created! Save: File → Save Scene As → Assets/Scenes/ROVMain.unity");
        EditorUtility.DisplayDialog("ROV Scene Created",
            "Professional ROV scene created with HomeScreen and MainScreen.\n\n" +
            "Save: File → Save Scene As → Assets/Scenes/ROVMain.unity\n" +
            "Then: ROV → Add ROV Scene to Build Settings", "OK");
    }

    // =========================================================================
    // HOME SCREEN
    // =========================================================================
    static GameObject BuildHomeScreen(GameObject canvas)
    {
        var home = Panel(canvas, "HomeScreen", COL_BG_DARK);
        StretchFull(home);

        // --- Background gradient overlay ---
        var bgOverlay = Panel(home, "BgGradient", new Color(0.04f, 0.06f, 0.12f, 0.9f));
        StretchFull(bgOverlay);

        // --- Decorative top accent line ---
        var accentLine = Panel(home, "AccentLine", COL_ACCENT);
        SetAnchored(accentLine, 0f, 0.97f, 1f, 0.975f); // Thin line at top

        // --- Logo placeholder (160x160 centered image area) ---
        var logoGO = Child(home, "LogoImage");
        var logoImg = logoGO.AddComponent<Image>();
        logoImg.color = COL_BG_CARD;
        SetCentered(logoGO, 140, 140, 0, 180);

        // Logo text inside
        var logoText = Label(logoGO, "LogoText", "ROV", 64, COL_ACCENT, TextAnchor.MiddleCenter);
        StretchFull(logoText);
        logoText.GetComponent<Text>().fontStyle = FontStyle.Bold;

        // --- Title ---
        var title = Label(home, "Title", "ROV NAVYUG", 52, COL_TEXT_PRIMARY, TextAnchor.MiddleCenter);
        title.GetComponent<Text>().fontStyle = FontStyle.Bold;
        SetCentered(title, 600, 60, 0, 60);

        // --- Subtitle ---
        var subtitle = Label(home, "Subtitle", "Remotely Operated Vehicle — Camera System", 22, COL_TEXT_LABEL, TextAnchor.MiddleCenter);
        SetCentered(subtitle, 600, 30, 0, 15);

        // --- Start Button (wide, prominent) ---
        var startBtn = RoundedButton(home, "StartButton", "START", COL_BTN_START, 30);
        SetCentered(startBtn, 340, 60, 0, -60);

        // --- Config + Exit buttons (side by side) ---
        var configBtn = RoundedButton(home, "HomeConfigButton", "CONFIGURE", COL_BTN_CONFIG, 22);
        SetCentered(configBtn, 155, 48, -90, -140);

        var exitBtn = RoundedButton(home, "ExitButton", "EXIT", COL_BTN_EXIT, 22);
        SetCentered(exitBtn, 155, 48, 90, -140);

        // --- Version text ---
        var verText = Label(home, "VersionText", "v1.0.0", 16, COL_TEXT_DIM, TextAnchor.LowerRight);
        SetAnchored(verText, 0.85f, 0.01f, 0.99f, 0.05f);

        // --- Bottom accent line ---
        var bottomLine = Panel(home, "BottomAccent", COL_ACCENT_DIM);
        SetAnchored(bottomLine, 0f, 0.025f, 1f, 0.03f);

        // --- Decorative corner markers ---
        var cornerTL = Panel(home, "CornerTL", COL_ACCENT);
        SetRect_Px(cornerTL, 20, -20, 22, -50); // Left-top corner marker
        var cornerBR = Panel(home, "CornerBR", COL_ACCENT);
        var cornerBR_rt = cornerBR.GetComponent<RectTransform>();
        cornerBR_rt.anchorMin = new Vector2(1, 0);
        cornerBR_rt.anchorMax = new Vector2(1, 0);
        cornerBR_rt.pivot = new Vector2(1, 0);
        cornerBR_rt.anchoredPosition = new Vector2(-20, 20);
        cornerBR_rt.sizeDelta = new Vector2(2, 30);

        return home;
    }

    // =========================================================================
    // MAIN SCREEN
    // =========================================================================
    static GameObject BuildMainScreen(GameObject canvas)
    {
        var main = Panel(canvas, "MainScreen", COL_BG_DARK);
        StretchFull(main);

        // ── Top Bar (48px from top) ──────────────────────────────────────
        var topBar = Panel(main, "TopBar", COL_TOP_BAR);
        SetAnchored(topBar, 0f, 1f, 1f, 1f);
        SetOffset(topBar, 0, 0, 0, -48); // 48px tall anchored to top

        // Top bar layout: [Back] [Title] ... [Config] [Status indicator]
        // Back button
        var backBtn = RoundedButton(topBar, "BackButton", "< BACK", COL_BTN_BACK, 18);
        SetAnchored_Px(backBtn, 8, 6, 120, -6);

        // Title in top bar
        var topTitle = Label(topBar, "TopTitle", "ROV NAVYUG", 24, COL_TEXT_PRIMARY, TextAnchor.MiddleLeft);
        topTitle.GetComponent<Text>().fontStyle = FontStyle.Bold;
        SetAnchored_Px(topTitle, 136, 0, 400, 0);

        // Config button (right side)
        var mainConfigBtn = RoundedButton(topBar, "MainConfigButton", "CONFIG", COL_BTN_CONFIG, 16);
        var cfgRt = mainConfigBtn.GetComponent<RectTransform>();
        cfgRt.anchorMin = new Vector2(1, 0);
        cfgRt.anchorMax = new Vector2(1, 1);
        cfgRt.offsetMin = new Vector2(-250, 6);
        cfgRt.offsetMax = new Vector2(-140, -6);

        // Connection status indicator
        var connStatus = Panel(topBar, "ConnectionIndicator", Hex("#1B5E20"));
        var connRt = connStatus.GetComponent<RectTransform>();
        connRt.anchorMin = new Vector2(1, 0);
        connRt.anchorMax = new Vector2(1, 1);
        connRt.offsetMin = new Vector2(-130, 8);
        connRt.offsetMax = new Vector2(-8, -8);
        var connLabel = Label(connStatus, "ConnectionText", "DISCONNECTED", 14, COL_TEXT_PRIMARY, TextAnchor.MiddleCenter);
        StretchFull(connLabel);

        // ── Camera Area (between top bar and status bar) ─────────────────
        // Layout: Main camera left (2/3 width), two small cameras stacked right (1/3 width)
        var cameraArea = Child(main, "CameraArea");
        var caRT = cameraArea.GetComponent<RectTransform>();
        caRT.anchorMin = Vector2.zero;
        caRT.anchorMax = Vector2.one;
        caRT.offsetMin = new Vector2(0, 52);    // Above status bar
        caRT.offsetMax = new Vector2(0, -48);   // Below top bar

        // Divider gap = 2px between cameras
        int gap = 2;

        // Camera 1 — Main (left 2/3)
        var cam1GO = Child(cameraArea, "Camera1_Main");
        var cam1Img = cam1GO.AddComponent<RawImage>();
        cam1Img.color = COL_VIDEO_BG;
        var cam1RT = cam1GO.GetComponent<RectTransform>();
        cam1RT.anchorMin = new Vector2(0, 0);
        cam1RT.anchorMax = new Vector2(0.6667f, 1);
        cam1RT.offsetMin = Vector2.zero;
        cam1RT.offsetMax = new Vector2(-gap / 2f, 0);

        // Camera 1 label overlay
        var cam1Label = CameraLabel(cam1GO, "CAM 1 — MAIN");

        // Camera 1 ExoPlayerBridge
        var bridge1 = cam1GO.AddComponent<ExoPlayerBridge>();
        bridge1.videoDisplay = cam1Img;

        // Camera 2 — Top-right (1/3 width, top half)
        var cam2GO = Child(cameraArea, "Camera2_Secondary");
        var cam2Img = cam2GO.AddComponent<RawImage>();
        cam2Img.color = COL_VIDEO_BG;
        var cam2RT = cam2GO.GetComponent<RectTransform>();
        cam2RT.anchorMin = new Vector2(0.6667f, 0.5f);
        cam2RT.anchorMax = new Vector2(1, 1);
        cam2RT.offsetMin = new Vector2(gap / 2f, gap / 2f);
        cam2RT.offsetMax = Vector2.zero;

        var cam2Label = CameraLabel(cam2GO, "CAM 2");

        var bridge2 = cam2GO.AddComponent<ExoPlayerBridge>();
        bridge2.videoDisplay = cam2Img;

        // Camera 3 — Bottom-right (1/3 width, bottom half)
        var cam3GO = Child(cameraArea, "Camera3_Secondary");
        var cam3Img = cam3GO.AddComponent<RawImage>();
        cam3Img.color = COL_VIDEO_BG;
        var cam3RT = cam3GO.GetComponent<RectTransform>();
        cam3RT.anchorMin = new Vector2(0.6667f, 0);
        cam3RT.anchorMax = new Vector2(1, 0.5f);
        cam3RT.offsetMin = new Vector2(gap / 2f, 0);
        cam3RT.offsetMax = new Vector2(0, -gap / 2f);

        var cam3Label = CameraLabel(cam3GO, "CAM 3");

        var bridge3 = cam3GO.AddComponent<ExoPlayerBridge>();
        bridge3.videoDisplay = cam3Img;

        // ── Status Bar (52px from bottom) ────────────────────────────────
        var statusBar = Panel(main, "StatusBar", COL_STATUS_BAR);
        SetAnchored(statusBar, 0f, 0f, 1f, 0f);
        SetOffset(statusBar, 0, 52, 0, 0); // 52px tall at bottom

        // Top divider line
        var statusDiv = Panel(statusBar, "Divider", COL_ACCENT);
        var sdRT = statusDiv.GetComponent<RectTransform>();
        sdRT.anchorMin = new Vector2(0, 1);
        sdRT.anchorMax = new Vector2(1, 1);
        sdRT.offsetMin = new Vector2(0, -2);
        sdRT.offsetMax = Vector2.zero;

        // Status items — evenly spaced
        // Layout: | BAT | SPD | DEP | HDG | GPS | Connection msg |
        float colW = 1f / 6f;
        int col = 0;

        // Battery
        var batPanel = StatusItem(statusBar, "Battery", "BATTERY", "100%", col, colW);
        col++;
        // Speed
        var spdPanel = StatusItem(statusBar, "Speed", "SPEED", "0.0 m/s", col, colW);
        col++;
        // Depth
        var depPanel = StatusItem(statusBar, "Depth", "DEPTH", "0.0 m", col, colW);
        col++;
        // Heading
        var hdgPanel = StatusItem(statusBar, "Heading", "HEADING", "0°", col, colW);
        col++;
        // GPS
        var gpsPanel = StatusItem(statusBar, "GPS", "GPS", "—, —", col, colW);
        col++;
        // Connection message
        var connMsg = StatusItem(statusBar, "ConnMsg", "CONNECTION", "Waiting...", col, colW);
        // Make connection text accent
        connMsg.transform.Find("Value").GetComponent<Text>().color = COL_ACCENT;

        return main;
    }

    // =========================================================================
    // CONFIG PANEL (overlay on any screen)
    // =========================================================================
    static GameObject BuildConfigPanel(GameObject canvas)
    {
        // Dark overlay
        var overlay = Panel(canvas, "ConfigPanel", new Color(0, 0, 0, 0.7f));
        StretchFull(overlay);

        // Panel card (centered, 700x600)
        var card = Panel(overlay, "ConfigCard", COL_BG_PANEL);
        SetCentered(card, 700, 560, 0, 0);

        // Title bar
        var titleBar = Panel(card, "TitleBar", COL_BG_CARD);
        var tbRT = titleBar.GetComponent<RectTransform>();
        tbRT.anchorMin = new Vector2(0, 1);
        tbRT.anchorMax = new Vector2(1, 1);
        tbRT.pivot = new Vector2(0.5f, 1);
        tbRT.sizeDelta = new Vector2(0, 50);
        tbRT.anchoredPosition = Vector2.zero;

        var titleLabel = Label(titleBar, "TitleLabel", "CONFIGURATION", 22, COL_TEXT_PRIMARY, TextAnchor.MiddleCenter);
        titleLabel.GetComponent<Text>().fontStyle = FontStyle.Bold;
        StretchFull(titleLabel);

        // Close button in title bar
        var closeBtn = RoundedButton(titleBar, "CloseConfigButton", "X", COL_BTN_EXIT, 20);
        var closeRT = closeBtn.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1, 0);
        closeRT.anchorMax = new Vector2(1, 1);
        closeRT.offsetMin = new Vector2(-46, 4);
        closeRT.offsetMax = new Vector2(-4, -4);

        // Form area (inside card, below title bar)
        var formArea = Child(card, "FormArea");
        var faRT = formArea.GetComponent<RectTransform>();
        faRT.anchorMin = Vector2.zero;
        faRT.anchorMax = Vector2.one;
        faRT.offsetMin = new Vector2(30, 60);  // padding + save button area
        faRT.offsetMax = new Vector2(-30, -60); // below title bar

        float y = 1f;
        float rowH = 0.14f;

        // --- Camera 1 RTSP URL ---
        y -= rowH;
        ConfigField(formArea, "Cam1Url", "Camera 1 RTSP URL", "rtsp://192.168.1.1:554/cam1", y, rowH);

        // --- Camera 2 RTSP URL ---
        y -= rowH;
        ConfigField(formArea, "Cam2Url", "Camera 2 RTSP URL", "rtsp://192.168.1.1:554/cam2", y, rowH);

        // --- Camera 3 RTSP URL ---
        y -= rowH;
        ConfigField(formArea, "Cam3Url", "Camera 3 RTSP URL", "rtsp://192.168.1.1:554/cam3", y, rowH);

        // --- Telemetry URL ---
        y -= rowH;
        ConfigField(formArea, "TelemetryUrl", "Telemetry URL (Socket.IO)", "ws://192.168.1.1:3000/socket.io/?EIO=4&transport=websocket", y, rowH);

        // --- Auto-reconnect toggle ---
        y -= rowH * 0.7f;
        var autoReconnLabel = Label(formArea, "AutoReconnectLabel", "Auto Reconnect", 18, COL_TEXT_LABEL, TextAnchor.MiddleLeft);
        SetAnchored(autoReconnLabel, 0, y, 0.5f, y + rowH * 0.6f);
        var toggleGO = Child(formArea, "AutoReconnectToggle");
        var toggle = toggleGO.AddComponent<Toggle>();
        toggle.isOn = true;
        var toggleBg = toggleGO.AddComponent<Image>();
        toggleBg.color = COL_BG_INPUT;
        toggle.targetGraphic = toggleBg;
        // Checkmark
        var checkGO = Child(toggleGO, "Checkmark");
        var checkImg = checkGO.AddComponent<Image>();
        checkImg.color = COL_ACCENT;
        toggle.graphic = checkImg;
        SetAnchored(checkGO, 0.2f, 0.2f, 0.8f, 0.8f);
        SetAnchored(toggleGO, 0.52f, y + 0.01f, 0.58f, y + rowH * 0.55f);

        // --- Save button ---
        var saveBtn = RoundedButton(card, "SaveConfigButton", "SAVE & CLOSE", COL_BTN_START, 22);
        var saveRT = saveBtn.GetComponent<RectTransform>();
        saveRT.anchorMin = new Vector2(0.25f, 0);
        saveRT.anchorMax = new Vector2(0.75f, 0);
        saveRT.pivot = new Vector2(0.5f, 0);
        saveRT.sizeDelta = new Vector2(0, 44);
        saveRT.anchoredPosition = new Vector2(0, 10);

        return overlay;
    }

    // =========================================================================
    // Wire up controllers
    // =========================================================================
    static void WireUpControllers(GameObject managerGO, GameObject homeScreen, GameObject mainScreen, GameObject configPanel)
    {
        // Find key UI elements by path
        var homeCfgBtn  = homeScreen.transform.Find("HomeConfigButton");
        var startBtn    = homeScreen.transform.Find("StartButton");
        var exitBtn     = homeScreen.transform.Find("ExitButton");

        var topBar      = mainScreen.transform.Find("TopBar");
        var backBtn     = topBar.Find("BackButton");
        var mainCfgBtn  = topBar.Find("MainConfigButton");
        var connText    = topBar.Find("ConnectionIndicator/ConnectionText");

        var statusBar   = mainScreen.transform.Find("StatusBar");
        var batValue    = statusBar.Find("Battery/Value");
        var spdValue    = statusBar.Find("Speed/Value");
        var depValue    = statusBar.Find("Depth/Value");
        var hdgValue    = statusBar.Find("Heading/Value");
        var gpsValue    = statusBar.Find("GPS/Value");
        var connMsgVal  = statusBar.Find("ConnMsg/Value");

        var cam1        = mainScreen.transform.Find("CameraArea/Camera1_Main");
        var cam2        = mainScreen.transform.Find("CameraArea/Camera2_Secondary");
        var cam3        = mainScreen.transform.Find("CameraArea/Camera3_Secondary");

        var closeCfg    = configPanel.transform.Find("ConfigCard/CloseConfigButton");
        var saveCfg     = configPanel.transform.Find("ConfigCard/SaveConfigButton");

        var cam1Input   = configPanel.transform.Find("ConfigCard/FormArea/Cam1Url_Input");
        var cam2Input   = configPanel.transform.Find("ConfigCard/FormArea/Cam2Url_Input");
        var cam3Input   = configPanel.transform.Find("ConfigCard/FormArea/Cam3Url_Input");
        var telInput    = configPanel.transform.Find("ConfigCard/FormArea/TelemetryUrl_Input");

        // --- ROVStreamController (on cam1 manager) ---
        var streamCtrl = managerGO.AddComponent<ROVStreamController>();
        streamCtrl.videoDisplay     = cam1?.GetComponent<RawImage>();
        streamCtrl.player1          = cam1?.GetComponent<ExoPlayerBridge>();
        streamCtrl.player2          = cam2?.GetComponent<ExoPlayerBridge>();
        streamCtrl.player3          = cam3?.GetComponent<ExoPlayerBridge>();
        streamCtrl.statusText       = connMsgVal?.GetComponent<Text>();
        streamCtrl.connectButton    = startBtn?.GetComponent<Button>();
        streamCtrl.disconnectButton = backBtn?.GetComponent<Button>();
        streamCtrl.connectionMessageText = connText?.GetComponent<Text>();

        // --- ROVTelemetryReceiver ---
        var telemetry = managerGO.AddComponent<ROVTelemetryReceiver>();
        telemetry.batteryText   = batValue?.GetComponent<Text>();
        telemetry.speedText     = spdValue?.GetComponent<Text>();
        telemetry.depthText     = depValue?.GetComponent<Text>();
        telemetry.headingText   = hdgValue?.GetComponent<Text>();
        telemetry.gpsText       = gpsValue?.GetComponent<Text>();
        telemetry.telemetryStatusText = connMsgVal?.GetComponent<Text>();

        // --- ROVUIController ---
        var uiCtrl = managerGO.AddComponent<ROVUIController>();
        uiCtrl.streamController  = streamCtrl;
        uiCtrl.telemetryReceiver = telemetry;
        uiCtrl.connectPanel      = homeScreen;
        uiCtrl.streamingPanel    = mainScreen;
        uiCtrl.settingsPanel     = configPanel;
        uiCtrl.connectButton     = startBtn?.GetComponent<Button>();
        uiCtrl.disconnectButton  = backBtn?.GetComponent<Button>();
        uiCtrl.settingsButton    = mainCfgBtn?.GetComponent<Button>();
        uiCtrl.closeSettingsButton = closeCfg?.GetComponent<Button>();
        uiCtrl.saveSettingsButton  = saveCfg?.GetComponent<Button>();
        uiCtrl.connectStatusText   = connMsgVal?.GetComponent<Text>();

        if (homeCfgBtn != null)
        {
            // Home config button opens config panel too
            var homeConfigButton = homeCfgBtn.GetComponent<Button>();
            // Wire up in the UIController
        }
    }

    // =========================================================================
    // Composite UI builders
    // =========================================================================

    /// <summary>Creates a camera label overlay (top-left corner).</summary>
    static GameObject CameraLabel(GameObject camParent, string labelText)
    {
        var bg = Panel(camParent, "CamLabel", new Color(0, 0, 0, 0.6f));
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 1);
        bgRT.anchorMax = new Vector2(0, 1);
        bgRT.pivot = new Vector2(0, 1);
        bgRT.sizeDelta = new Vector2(130, 28);
        bgRT.anchoredPosition = new Vector2(6, -6);

        var lbl = Label(bg, "Label", labelText, 14, COL_ACCENT, TextAnchor.MiddleCenter);
        lbl.GetComponent<Text>().fontStyle = FontStyle.Bold;
        StretchFull(lbl);

        // "No Signal" placeholder text centered in camera
        var noSigGO = Label(camParent, "NoSignalText", "NO SIGNAL", 20, COL_TEXT_DIM, TextAnchor.MiddleCenter);
        SetAnchored(noSigGO, 0.2f, 0.3f, 0.8f, 0.7f);

        return bg;
    }

    /// <summary>Creates a status bar item: [LABEL / VALUE] in a column.</summary>
    static GameObject StatusItem(GameObject parent, string name, string labelText, string valueText, int colIndex, float colWidth)
    {
        var item = Child(parent, name);
        var itemRT = item.GetComponent<RectTransform>();
        float xMin = colIndex * colWidth;
        float xMax = xMin + colWidth;
        itemRT.anchorMin = new Vector2(xMin, 0);
        itemRT.anchorMax = new Vector2(xMax, 1);
        itemRT.offsetMin = new Vector2(8, 0);
        itemRT.offsetMax = new Vector2(-8, 0);

        // Label (top half, small)
        var lbl = Label(item, "Label", labelText, 11, COL_TEXT_DIM, TextAnchor.LowerLeft);
        lbl.GetComponent<Text>().fontStyle = FontStyle.Bold;
        SetAnchored(lbl, 0, 0.55f, 1, 1);

        // Value (bottom half, larger)
        var val = Label(item, "Value", valueText, 18, COL_TEXT_PRIMARY, TextAnchor.UpperLeft);
        val.GetComponent<Text>().fontStyle = FontStyle.Bold;
        SetAnchored(val, 0, 0, 1, 0.55f);

        // Right divider (except last column)
        if (colIndex < 5)
        {
            var div = Panel(item, "Divider", COL_DIVIDER);
            var divRT = div.GetComponent<RectTransform>();
            divRT.anchorMin = new Vector2(1, 0.15f);
            divRT.anchorMax = new Vector2(1, 0.85f);
            divRT.pivot = new Vector2(1, 0.5f);
            divRT.sizeDelta = new Vector2(1, 0);
            divRT.anchoredPosition = new Vector2(8, 0);
        }

        return item;
    }

    /// <summary>Creates a config form field: label + input field.</summary>
    static void ConfigField(GameObject form, string name, string labelText, string defaultValue, float yStart, float height)
    {
        // Label
        var lbl = Label(form, name + "_Label", labelText, 16, COL_TEXT_LABEL, TextAnchor.MiddleLeft);
        SetAnchored(lbl, 0, yStart + height * 0.55f, 1, yStart + height);

        // Input field
        var inputGO = Child(form, name + "_Input");
        var inputImg = inputGO.AddComponent<Image>();
        inputImg.color = COL_BG_INPUT;
        var input = inputGO.AddComponent<InputField>();

        var inputText = Child(inputGO, "Text").AddComponent<Text>();
        inputText.font = FONT;
        inputText.fontSize = 18;
        inputText.color = COL_TEXT_PRIMARY;
        inputText.supportRichText = false;
        input.textComponent = inputText;
        input.text = defaultValue;
        StretchPadded(inputText.gameObject, 8);

        var placeholder = Child(inputGO, "Placeholder").AddComponent<Text>();
        placeholder.font = FONT;
        placeholder.fontSize = 18;
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.color = COL_TEXT_DIM;
        placeholder.text = "Enter " + labelText.ToLower() + "...";
        input.placeholder = placeholder;
        StretchPadded(placeholder.gameObject, 8);

        SetAnchored(inputGO, 0, yStart, 1, yStart + height * 0.52f);
    }

    // =========================================================================
    // Primitive builders
    // =========================================================================

    static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static GameObject Panel(GameObject parent, string name, Color color)
    {
        var go = Child(parent, name);
        go.AddComponent<Image>().color = color;
        return go;
    }

    static GameObject Label(GameObject parent, string name, string text, int size, Color color, TextAnchor align)
    {
        var go = Child(parent, name);
        var t  = go.AddComponent<Text>();
        t.text      = text;
        t.font      = FONT;
        t.fontSize  = size;
        t.color     = color;
        t.alignment = align;
        return go;
    }

    static GameObject RoundedButton(GameObject parent, string name, string label, Color bgColor, int fontSize)
    {
        var go  = Child(parent, name);
        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn    = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = bgColor;
        colors.highlightedColor = new Color(
            Mathf.Min(bgColor.r * 1.3f, 1),
            Mathf.Min(bgColor.g * 1.3f, 1),
            Mathf.Min(bgColor.b * 1.3f, 1), 1);
        colors.pressedColor     = bgColor * 0.7f;
        colors.pressedColor     = new Color(colors.pressedColor.r, colors.pressedColor.g, colors.pressedColor.b, 1);
        btn.colors = colors;

        var textGO = Child(go, "Text");
        var text   = textGO.AddComponent<Text>();
        text.text      = label;
        text.font      = FONT;
        text.fontSize  = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.color     = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        StretchFull(textGO);

        return go;
    }

    // =========================================================================
    // RectTransform utilities
    // =========================================================================

    static void StretchFull(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void StretchPadded(GameObject go, float pad)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, 0);
        rt.offsetMax = new Vector2(-pad, 0);
    }

    static void SetAnchored(GameObject go, float xMin, float yMin, float xMax, float yMax)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Set anchored with pixel offsets from anchor edges.</summary>
    static void SetAnchored_Px(GameObject go, float left, float bottom, float right, float top)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(0, 1);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    /// <summary>Set offset from anchor edges. Use after setting anchors.</summary>
    static void SetOffset(GameObject go, float left, float bottom, float right, float top)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(left, bottom < 0 ? 0 : bottom);
        rt.offsetMax = new Vector2(right, top > 0 ? 0 : top);
        // Fix: for top-anchored bars
        if (top < 0) // Means "grow downward from top anchor"
        {
            rt.offsetMin = new Vector2(left, 0);
            rt.offsetMax = new Vector2(right, 0);
            rt.anchorMin = new Vector2(rt.anchorMin.x, 1f + top / 1080f);
        }
        if (bottom > 0) // Means "grow upward from bottom anchor"
        {
            rt.offsetMin = new Vector2(left, 0);
            rt.offsetMax = new Vector2(right, 0);
            rt.anchorMax = new Vector2(rt.anchorMax.x, bottom / 1080f);
        }
    }

    /// <summary>Center a fixed-size element relative to parent center.</summary>
    static void SetCentered(GameObject go, float width, float height, float offsetX, float offsetY)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(offsetX, offsetY);
    }

    static void SetRect_Px(GameObject go, float x, float y, float w, float h)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot     = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, Mathf.Abs(h));
    }

    // =========================================================================
    // Color utility
    // =========================================================================
    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    // =========================================================================
    // Menu helpers
    // =========================================================================

    [MenuItem("ROV/Add ROV Scene to Build Settings", false, 20)]
    public static void AddSceneToBuild()
    {
        string scenePath = "Assets/Scenes/ROVMain.unity";
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes)
        {
            if (s.path == scenePath)
            {
                EditorUtility.DisplayDialog("Already Added", "ROVMain.unity is already in Build Settings.", "OK");
                return;
            }
        }
        var newScenes = new EditorBuildSettingsScene[scenes.Length + 1];
        newScenes[0] = new EditorBuildSettingsScene(scenePath, true);
        System.Array.Copy(scenes, 0, newScenes, 1, scenes.Length);
        EditorBuildSettings.scenes = newScenes;
        Debug.Log($"Added {scenePath} to Build Settings at index 0.");
    }
}
#endif
