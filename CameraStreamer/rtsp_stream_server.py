import os
import sys
import subprocess
import atexit
import re
import platform
import urllib.request
import zipfile
import tarfile
import shutil

# Configurations
MEDIAMTX_VERSION = "v1.9.0"
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MEDIAMTX_DIR = os.path.join(SCRIPT_DIR, "mediamtx")
os.makedirs(MEDIAMTX_DIR, exist_ok=True)

is_windows = os.name == 'nt'
processes = []
log_files = []

def find_ffmpeg():
    """Locate the FFmpeg binary on the system."""
    ffmpeg_in_path = shutil.which("ffmpeg")
    if ffmpeg_in_path:
        return ffmpeg_in_path
    
    if is_windows:
        user_home = os.path.expanduser("~")
        # Check standard WinGet path generic for any Windows user
        winget_path = os.path.join(user_home, "AppData", "Local", "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-8.1.1-full_build", "bin", "ffmpeg.exe")
        if os.path.exists(winget_path):
            return winget_path
            
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

def download_mediamtx():
    """Detect OS/Arch and download the corresponding MediaMTX release."""
    binary_name = "mediamtx.exe" if is_windows else "mediamtx"
    binary_path = os.path.join(MEDIAMTX_DIR, binary_name)
    
    if os.path.exists(binary_path):
        if not is_windows:
            try:
                os.chmod(binary_path, 0o755)
            except Exception as e:
                print(f"[Warn] Could not set execute permissions on {binary_path}: {e}")
        return binary_path

    # Detect platform/architecture
    os_name = platform.system().lower()
    arch = platform.machine().lower()

    print(f"[Init] MediaMTX binary not found. Detecting system: OS={os_name}, Arch={arch}")

    suffix = ""
    extension = ""
    if os_name == "windows":
        suffix = "windows_amd64"
        extension = "zip"
    elif os_name == "linux":
        if "armv7" in arch or "arm" in arch and "64" not in arch:
            suffix = "linux_armv7"
            extension = "tar.gz"
        elif "aarch64" in arch or "arm64" in arch:
            suffix = "linux_arm64v8"
            extension = "tar.gz"
        else:
            suffix = "linux_amd64"
            extension = "tar.gz"
    else:
        print(f"[Error] Unsupported operating system: {os_name}")
        return None

    filename = f"mediamtx_{MEDIAMTX_VERSION}_{suffix}.{extension}"
    url = f"https://github.com/bluenviron/mediamtx/releases/download/{MEDIAMTX_VERSION}/{filename}"
    download_path = os.path.join(MEDIAMTX_DIR, filename)

    print(f"[Init] Downloading MediaMTX from: {url}")
    try:
        urllib.request.urlretrieve(url, download_path)
        print("[Init] Download complete. Extracting files...")
        
        if extension == "zip":
            with zipfile.ZipFile(download_path, 'r') as zip_ref:
                zip_ref.extractall(MEDIAMTX_DIR)
        else:
            with tarfile.open(download_path, "r:gz") as tar_ref:
                tar_ref.extractall(MEDIAMTX_DIR)
                
        # Clean up downloaded archive
        os.remove(download_path)
        
        # Set execute permissions on Linux
        if not is_windows:
            os.chmod(binary_path, 0o755)
            
        print(f"[Init] MediaMTX successfully set up at: {binary_path}")
        return binary_path
    except Exception as e:
        print(f"[Error] Failed to automatically set up MediaMTX: {e}")
        print("[Error] Please manually download the binary and place it in the 'mediamtx' folder.")
        return None

def check_h264_hardware_encoder(ffmpeg_path):
    """Check if the Raspberry Pi hardware-accelerated H.264 encoder is physically working."""
    if is_windows:
        return False
    try:
        res = subprocess.run([ffmpeg_path, "-encoders"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if "h264_v4l2m2m" not in res.stdout:
            print("[Init] Hardware H.264 encoder (h264_v4l2m2m) is NOT compiled in FFmpeg.")
            return False
    except Exception:
        return False

    # Perform a 1-second trial run to see if hardware codec actually works (e.g. Pi 5 doesn't have it physically)
    try:
        test_cmd = [
            ffmpeg_path,
            "-f", "lavfi",
            "-i", "testsrc=duration=1:size=320x240:rate=10",
            "-codec:v", "h264_v4l2m2m",
            "-f", "null",
            "-"
        ]
        test_res = subprocess.run(test_cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if test_res.returncode == 0:
            print("[Init] Hardware H.264 encoder (h264_v4l2m2m) is physically working and verified!")
            return True
        else:
            print("[Init] Hardware H.264 encoder (h264_v4l2m2m) failed test run. Probably Raspberry Pi 5 or permission issue.")
    except Exception as e:
        print(f"[Init] Failed to test hardware encoder: {e}")
        
    print("[Init] Falling back to libx264 software encoding.")
    return False

# Find dependencies
ffmpeg_bin = find_ffmpeg()
print(f"[Init] Using FFmpeg path: {ffmpeg_bin}")

use_hardware_encoder = check_h264_hardware_encoder(ffmpeg_bin)

mediamtx_bin = download_mediamtx()
if not mediamtx_bin:
    sys.exit(1)

def start_mediamtx():
    """Start the MediaMTX server in the background."""
    print("[MediaMTX] Starting server...")
    
    # Hide window on Windows
    startupinfo = None
    if is_windows:
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW

    # Run from the mediamtx directory to pick up configuration if present
    proc = subprocess.Popen([mediamtx_bin], cwd=MEDIAMTX_DIR, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, startupinfo=startupinfo)
    processes.append(proc)
    print("[MediaMTX] Server started on port 8554 (RTSP)")

def start_ffmpeg_rtsp_push(video_device, stream_name):
    """Spawns background FFmpeg process to capture from a camera and stream RTSP to MediaMTX."""
    rtsp_url = f"rtsp://localhost:8554/{stream_name}"
    
    if is_windows:
        cmd = [
            ffmpeg_bin,
            "-y",
            "-fflags", "nobuffer",
            "-flags", "low_delay",
            "-f", "dshow",
            "-framerate", "30",
            "-i", f"video={video_device}",
            "-codec:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "zerolatency",
            "-threads", "1",
            "-b:v", "600k",
            "-maxrate", "600k",
            "-bufsize", "150k",
            "-pix_fmt", "yuv420p",
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
            "-g", "10",
            "-an",
            "-f", "rtsp",
            rtsp_url
        ]
    else:
        if use_hardware_encoder:
            cmd = [
                ffmpeg_bin,
                "-y",
                "-fflags", "nobuffer",
                "-flags", "low_delay",
                "-f", "v4l2",
                "-input_format", "mjpeg",
                "-framerate", "30",
                "-i", video_device,
                "-codec:v", "h264_v4l2m2m",
                "-b:v", "600k",
                "-maxrate", "600k",
                "-bufsize", "150k",
                "-pix_fmt", "yuv420p",
                "-s", "640x480",
                "-g", "10",
                "-an",
                "-f", "rtsp",
                rtsp_url
            ]
        else:
            cmd = [
                ffmpeg_bin,
                "-y",
                "-fflags", "nobuffer",
                "-flags", "low_delay",
                "-f", "v4l2",
                "-input_format", "mjpeg",
                "-framerate", "30",
                "-i", video_device,
                "-codec:v", "libx264",
                "-preset", "ultrafast",
                "-tune", "zerolatency",
                "-threads", "1",
                "-b:v", "600k",
                "-maxrate", "600k",
                "-bufsize", "150k",
                "-pix_fmt", "yuv420p",
                "-s", "640x480",
                "-g", "10",
                "-an",
                "-f", "rtsp",
                rtsp_url
            ]

    print(f"[RTSP] Pushing: {video_device} -> {rtsp_url}")
    
    startupinfo = None
    if is_windows:
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW

    # Open log file for FFmpeg to write errors
    try:
        log_path = os.path.join(MEDIAMTX_DIR, f"ffmpeg_{stream_name}.log")
        log_file = open(log_path, "w")
        log_files.append(log_file)
        stderr_target = log_file
        print(f"[RTSP] Logging errors to: {log_path}")
    except Exception as e:
        print(f"[Warn] Could not create FFmpeg log file: {e}")
        stderr_target = subprocess.DEVNULL

    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=stderr_target, startupinfo=startupinfo)
    processes.append(proc)

def cleanup():
    """Terminate all background processes on shutdown."""
    if processes:
        print("[Shutdown] Terminating all streaming and server processes...")
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

    # Close open log files
    global log_files
    for f in log_files:
        try:
            f.close()
        except Exception:
            pass
    log_files.clear()

atexit.register(cleanup)

def main():
    # Start MediaMTX
    start_mediamtx()
    
    # Wait for MediaMTX to bind ports
    import time
    time.sleep(1)

    # Initialize streams based on Operating System
    if is_windows:
        cams = detect_windows_cameras(ffmpeg_bin)
        if not cams:
            print("[Init] ERROR: No camera devices detected via DirectShow.")
        else:
            print(f"[Init] Detected Camera Devices: {cams}")
            for idx, cam_name in enumerate(cams[:3]):
                start_ffmpeg_rtsp_push(cam_name, f"cam{idx+1}")
    else:
        # Linux / Raspberry Pi defaults
        start_ffmpeg_rtsp_push("/dev/video0", "cam1")
        start_ffmpeg_rtsp_push("/dev/video2", "cam2")
        start_ffmpeg_rtsp_push("/dev/video4", "cam3")

    print("\n=======================================================")
    print("      RTSP STREAMS ACTIVE (<1s Latency)")
    print("      Press Ctrl+C to stop streaming")
    print("=======================================================")
    
    # Keep main thread alive
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nStopping...")
    finally:
        cleanup()

if __name__ == "__main__":
    main()
