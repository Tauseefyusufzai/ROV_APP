import os
import sys
import subprocess
import atexit
import re

# Auto-install Flask if not present
try:
    from flask import Flask, send_from_directory, jsonify
except ImportError:
    print("[Init] Flask not found. Installing Flask library...")
    subprocess.check_call([sys.executable, "-m", "pip", "install", "flask"])
    from flask import Flask, send_from_directory, jsonify

import mimetypes
mimetypes.add_type('application/x-mpegURL', '.m3u8')
mimetypes.add_type('video/MP2T', '.ts')

app = Flask(__name__)

# Directory where HLS segments and playlists will be stored
HLS_DIR = os.path.join(app.root_path, "static", "hls")
os.makedirs(HLS_DIR, exist_ok=True)

# List to keep track of background FFmpeg processes
processes = []
is_windows = os.name == 'nt'

def find_ffmpeg():
    """Locate the FFmpeg binary on the system."""
    import shutil
    ffmpeg_in_path = shutil.which("ffmpeg")
    if ffmpeg_in_path:
        return ffmpeg_in_path
    
    if is_windows:
        user_home = os.path.expanduser("~")
        # Check standard WinGet path generic for any Windows user
        winget_path = os.path.join(user_home, "AppData", "Local", "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-8.1.1-full_build", "bin", "ffmpeg.exe")
        if os.path.exists(winget_path):
            return winget_path
            
        # Fallback to search inside winget packages folder
        winget_root = os.path.join(user_home, "AppData", "Local", "Microsoft", "WinGet", "Packages")
        if os.path.exists(winget_root):
            try:
                for item in os.listdir(winget_root):
                    if "Gyan.FFmpeg" in item:
                        full_path = os.path.join(winget_root, item)
                        for root, dirs, files in os.walk(full_path):
                            if "ffmpeg.exe" in files:
                                return os.path.join(root, "ffmpeg.exe")
            except Exception:
                pass
    return "ffmpeg"

def detect_windows_cameras(ffmpeg_path):
    """Scan and list DirectShow video devices on Windows."""
    if not is_windows:
        return []
    cmd = [ffmpeg_path, '-list_devices', 'true', '-f', 'dshow', '-i', 'dummy']
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', errors='ignore')
    cameras = []
    for line in res.stderr.splitlines():
        m = re.search(r'"([^"]+)"\s+\(video\)', line)
        if m:
            name = m.group(1)
            if name not in cameras:
                cameras.append(name)
    return cameras

# Find ffmpeg
ffmpeg_bin = find_ffmpeg()
print(f"[Init] Using FFmpeg path: {ffmpeg_bin}")

def start_hls_stream(video_device, stream_name):
    """
    Spawns a background FFmpeg process to capture from a camera device
    and output HLS (.m3u8 + .ts segments) to the static folder.
    """
    output_dir = os.path.join(HLS_DIR, stream_name)
    
    # Clean output dir
    if os.path.exists(output_dir):
        for f in os.listdir(output_dir):
            try:
                os.remove(os.path.join(output_dir, f))
            except Exception:
                pass
    else:
        os.makedirs(output_dir, exist_ok=True)
        
    playlist_path = os.path.join(output_dir, "stream.m3u8")

    # Command optimized for Windows or Linux/Raspberry Pi
    if is_windows:
        cmd = [
            ffmpeg_bin,
            "-y",
            "-f", "dshow",
            "-i", f"video={video_device}",
            "-codec:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "zerolatency",
            "-pix_fmt", "yuv420p",
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",  # ensures dimensions divisible by 2
            "-r", "30",
            "-g", "30",
            "-an",  # disable audio for video-only feeds (similar to v4l2 command)
            "-f", "hls",
            "-hls_time", "1",
            "-hls_list_size", "3",
            "-hls_flags", "delete_segments",
            playlist_path
        ]
    else:
        cmd = [
            ffmpeg_bin,
            "-y",
            "-f", "v4l2",
            "-input_format", "mjpeg",
            "-i", video_device,
            "-codec:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "zerolatency",
            "-pix_fmt", "yuv420p",
            "-s", "640x480",
            "-r", "30",
            "-g", "30",
            "-an",
            "-f", "hls",
            "-hls_time", "1",
            "-hls_list_size", "3",
            "-hls_flags", "delete_segments",
            playlist_path
        ]

    print(f"[HLS] Starting: {video_device} -> {playlist_path}")
    
    # Hide window on Windows
    startupinfo = None
    if is_windows:
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW

    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, startupinfo=startupinfo)
    processes.append(proc)

# Initialize streams based on Operating System
if is_windows:
    cams = detect_windows_cameras(ffmpeg_bin)
    if not cams:
        print("[Init] ERROR: No camera devices detected via DirectShow.")
    else:
        print(f"[Init] Detected Camera Devices: {cams}")
        # Map detected cameras to cam1, cam2, cam3 dynamically
        for idx, cam_name in enumerate(cams[:3]):
            start_hls_stream(cam_name, f"cam{idx+1}")
else:
    # Linux defaults
    start_hls_stream("/dev/video0", "cam1")
    start_hls_stream("/dev/video2", "cam2")
    start_hls_stream("/dev/video4", "cam3")

def cleanup():
    """Terminate all background FFmpeg processes on shutdown."""
    if processes:
        print("[Shutdown] Terminating background FFmpeg streaming processes...")
        for proc in processes:
            try:
                proc.terminate()
                proc.wait(timeout=2)
            except Exception:
                try:
                    proc.kill()
                except Exception:
                    pass
        processes.clear()

atexit.register(cleanup)

@app.route("/")
def index():
    # Helper index page listing active cameras and stream URLs
    available_streams = []
    for idx in range(1, len(processes) + 1):
        available_streams.append({
            "name": f"cam{idx}",
            "stream_url": f"/static/hls/cam{idx}/stream.m3u8"
        })
    return jsonify({
        "status": "ROV Camera HLS Streaming Server Running",
        "platform": "Windows" if is_windows else "Linux",
        "streams": available_streams
    })

# Route to serve HLS directory files
@app.route("/static/hls/<path:filename>")
def serve_hls(filename):
    import time
    filepath = os.path.join(HLS_DIR, filename)
    # If the playlist does not exist yet, wait up to 3 seconds for FFmpeg to create it
    if filename.endswith(".m3u8") and not os.path.exists(filepath):
        print(f"[HLS] {filename} not ready. Waiting for FFmpeg to write it...")
        for _ in range(30):
            time.sleep(0.1)
            if os.path.exists(filepath):
                break
    return send_from_directory(HLS_DIR, filename)

if __name__ == "__main__":
    try:
        app.run(host="0.0.0.0", port=5000, threaded=True)
    finally:
        cleanup()
