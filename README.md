# ROV Navyug (`ROV_APP`)

A Unity-based Android application for **Remotely Operated Vehicle (ROV)** multi-camera video streaming, real-time telemetry monitoring, and geospatial/DSM terrain visualization.

---

## Features

- **Ultra-Low Latency Multi-Camera Streaming (<1s Delay)**
  - Supports simultaneous playback of up to **3 live camera feeds** (`cam1`, `cam2`, `cam3`).
  - Uses a custom native Android **ExoPlayer JNI bridge** (`ExoPlayerPlugin.aar`) for hardware-accelerated **RTSP** and **HLS** decoding directly onto Unity `RawImage` surfaces with zero per-frame memory allocations.
  - Automatic stream reconnection, FPS monitoring, and one-tap frame snapshot capture (`ROV_Snapshots`).
- **Real-Time Socket.IO Telemetry HUD**
  - Live telemetry reception via WebSocket (`Socket.IO`) on the `rov_telemetry` channel.
  - Displays **Battery (%)**, **GPS Coordinates (Lat/Lon)**, **Depth (m)**, **Heading (°)**, **Speed (m/s)**, and connection status.
- **Raspberry Pi Camera Streamer Suite (`CameraStreamer/`)**
  - Includes cross-platform Python streaming servers (`rtsp_stream_server.py`, `stream_server.py`, `flask_stream_server.py`) that auto-detect architecture, launch **MediaMTX**, and push low-latency H.264 streams via **FFmpeg**.
- **Two-Screen UI & Runtime Configuration**
  - **Home Screen:** Quick-launch stream session, open configuration, or exit.
  - **Main Screen:** 3-camera viewport layout, live telemetry bar, snapshot capture, and stream refresh.
  - **Settings Overlay:** Runtime configuration of Camera 1–3 RTSP URLs, Telemetry WebSocket URL, target resolution, FPS, and auto-reconnect preferences (persisted via `PlayerPrefs`).
- **Geospatial & DSM Map Processing Tools**
  - Batch utilities (`AutoTiffProcessor.bat`, `EnhancedTiffProcessor.bat`, `UniversalMapProcessor.bat`) and hybrid GeoTIFF loader package for terrain/map preparation in Unity.

---

## Project Structure

```text
ROV Navyug/
├── Assets/
│   ├── Scenes/
│   │   ├── ROVMain.unity              # Primary ROV multi-camera & telemetry scene
│   │   └── Main.unity                 # Map / tactical view scene
│   ├── Scripts/ROV/
│   │   ├── ExoPlayerBridge.cs         # C# <-> Android ExoPlayer JNI bridge (RTSP/HLS)
│   │   ├── ROVStreamController.cs     # Multi-camera stream manager & snapshot capture
│   │   ├── ROVTelemetryReceiver.cs    # Socket.IO client for real-time ROV telemetry
│   │   ├── ROVUIController.cs         # Screen transitions (Home / Main / Config overlay)
│   │   ├── ROVData.cs                 # Runtime state model & Socket DTOs
│   │   ├── ROVAppConfig.cs            # ScriptableObject configuration asset
│   │   ├── ROVFileLogger.cs           # Persistent on-device diagnostic logging
│   │   └── Editor/                    # Unity Editor scene setup & hardware accel scripts
│   ├── Plugins/
│   │   ├── Android/                   # ExoPlayerPlugin.aar & custom Gradle templates
│   │   └── SocketIO/                  # WebSocket / Socket.IO Unity library
│   └── DSM_Prefabs/                   # Terrain & GeoTIFF rendering prefabs
├── CameraStreamer/
│   ├── rtsp_stream_server.py          # Zero-latency RTSP server using MediaMTX + FFmpeg
│   ├── stream_server.py               # HLS multi-camera streaming server
│   └── flask_stream_server.py         # Flask-based HTTP/HLS camera streamer
├── PluginsCode/
│   └── ExoPlayerPlugin/               # Android Java source & Gradle project for ExoPlayer AAR
├── Packages/                          # Unity Package Manager manifest & custom packages
├── ProjectSettings/                   # Unity project settings
├── rtsp_setup_guide.md                # Raspberry Pi RTSP deployment guide
└── score_explanation.md               # Tactical exercise scoring specification
```

---

## Prerequisites

### Unity Development Environment
- **Unity Editor:** `2020.3.13f1` (LTS)
- **Target Platform:** Android (API Level 24+ recommended) with Android SDK & OpenJDK installed via Unity Hub

### Raspberry Pi / Camera Host (for Live RTSP Streaming)
- **OS:** Raspberry Pi OS (ARMv7 / ARM64) or Linux/Windows host
- **Dependencies:** `python3` and `ffmpeg`
  ```bash
  sudo apt update
  sudo apt install ffmpeg python3 -y
  ```

---

## Getting Started

### 1. Clone the Repository
```bash
git clone https://github.com/Tauseefyusufzai/ROV_APP.git
cd ROV_APP
```

### 2. Open in Unity
1. Open **Unity Hub** and click **Add project from disk**, selecting the cloned repository folder.
2. Open the project using **Unity 2020.3.13f1**.
3. Open the main scene at `Assets/Scenes/ROVMain.unity`.
4. To rebuild or auto-wire the scene hierarchy at any time, use the Unity Editor menu: **`ROV → Setup ROV Scene`**.

### 3. Start the Raspberry Pi RTSP Streamer
1. Copy `CameraStreamer/rtsp_stream_server.py` to your Raspberry Pi.
2. Run the streamer:
   ```bash
   python3 rtsp_stream_server.py
   ```
   The script automatically downloads the appropriate **MediaMTX** binary for your architecture, launches the RTSP server on port `8554`, and encodes `/dev/video0`, `/dev/video2`, and `/dev/video4` using ultra-low-latency H.264 settings (`-tune zerolatency -preset ultrafast`).
3. See [rtsp_setup_guide.md](rtsp_setup_guide.md) for detailed setup instructions.

### 4. Configure Stream & Telemetry URLs in the App
Open the **Config / Settings** overlay inside the app and set:
- **Camera 1 URL:** `rtsp://<raspberry-pi-ip>:8554/cam1`
- **Camera 2 URL:** `rtsp://<raspberry-pi-ip>:8554/cam2`
- **Camera 3 URL:** `rtsp://<raspberry-pi-ip>:8554/cam3`
- **Telemetry URL:** `ws://<telemetry-server-ip>:3000/socket.io/?EIO=4&transport=websocket`

---

## Telemetry Payload Format

`ROVTelemetryReceiver` listens for the `rov_telemetry` Socket.IO event with the following JSON structure:

```json
{
  "serial_no": "ROV001",
  "name": "ROV 1",
  "lat": 28.6139,
  "lon": 77.2090,
  "depth": 12.5,
  "heading": 180.0,
  "speed": 1.2,
  "battery": 85,
  "status": "streaming",
  "is_streaming": true
}
```

---

## Building the Native Android ExoPlayer Plugin (Optional)

A prebuilt `ExoPlayerPlugin.aar` is already included under `Assets/Plugins/Android/`. If you modify the Java plugin source in `PluginsCode/ExoPlayerPlugin`:

```bash
cd PluginsCode/ExoPlayerPlugin
gradlew assembleRelease
```

The release AAR (`build/outputs/aar/ExoPlayerPlugin-release.aar`) will be copied to `Assets/Plugins/Android/ExoPlayerPlugin.aar`. Full build instructions are available in [PluginsCode/ExoPlayerPlugin/README.md](PluginsCode/ExoPlayerPlugin/README.md).
