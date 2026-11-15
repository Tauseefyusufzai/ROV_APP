# Setup Guide: Ultra-Low Latency RTSP Streaming on Raspberry Pi

This guide details how to set up an **RTSP (Real-Time Streaming Protocol)** server on the Raspberry Pi to achieve ultra-low latency (<1s delay) inside the ROV Android App.

---

## Why this change is required
* **HLS (HTTP Live Streaming)** splits the video into small file segments, causing a mandatory **4–5 second delay** due to buffer queuing.
* **RTSP** sends real-time packets directly over the network, bringing display delay down to **under 1 second**!

---

## System Prerequisites

Run this command on the Raspberry Pi to ensure Python 3 and FFmpeg are installed:
```bash
sudo apt update
sudo apt install ffmpeg python3 -y
```

---

## Deployment Steps

1. Copy the **[rtsp_stream_server.py](file:///d:/.Navyug/New%20folder/ROV%20Navyug/CameraStreamer/rtsp_stream_server.py)** script from the `CameraStreamer` folder onto your Raspberry Pi.
2. Run the script on the Raspberry Pi:
   ```bash
   python3 rtsp_stream_server.py
   ```

### What this script does automatically:
1. **Detects the System architecture** (ARMv7, ARM64, Windows, Linux) and automatically downloads the corresponding compiled binary of **MediaMTX** (formerly *rtsp-simple-server*).
2. **Launches the MediaMTX RTSP Server** in the background (listening on default port 8554).
3. **Pushes the Camera feeds** (`/dev/video0`, `/dev/video2`, `/dev/video4`) to the RTSP paths using FFmpeg.
4. **Optimizes the streaming parameters** for zero-latency H.264 encoding and formats them for the Android hardware decoder:
   - `-pix_fmt yuv420p` (compatible pixel layout)
   - `-tune zerolatency` (removes encoding buffering delay)
   - `-preset ultrafast` (fastest possible frame compression)
   - `-an` (disables audio encoding to save bandwidth)

---

## App Configuration URLs

Once the script displays `RTSP STREAMS ACTIVE`, configure the camera URLs in the **ROV Android App Settings** overlay:

*   **Camera 1:** `rtsp://<raspberry-pi-ip>:8554/cam1`
*   **Camera 2:** `rtsp://<raspberry-pi-ip>:8554/cam2`
*   **Camera 3:** `rtsp://<raspberry-pi-ip>:8554/cam3`

*(Replace `<raspberry-pi-ip>` with the actual IP address of the Raspberry Pi on the local network, e.g. `192.168.1.5`)*
