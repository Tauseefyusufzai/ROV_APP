import os
import sys
import subprocess
import re
import socket
import threading
import time
import json
import http.server
import socketserver
import urllib.parse

# Configurations
PORT = 8080
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WEB_ROOT = os.path.join(SCRIPT_DIR, "web")
HLS_DIR = os.path.join(WEB_ROOT, "hls")

# Ensure directories exist
os.makedirs(WEB_ROOT, exist_ok=True)
os.makedirs(HLS_DIR, exist_ok=True)

# Global States
ffmpeg_exe = None
selected_video_device = None
selected_audio_device = None
video_devices_list = []
audio_devices_list = []
hls_process = None
ffmpeg_log_buffer = []
ffmpeg_log_max_size = 50
settings = {
    "resolution": "default",  # Can be "default", "1280x720", "640x480", etc.
    "fps": "30",
    "video_bitrate": "1500k",
    "audio_enabled": True
}

def add_to_ffmpeg_log(line):
    global ffmpeg_log_buffer
    ffmpeg_log_buffer.append(line.strip())
    if len(ffmpeg_log_buffer) > ffmpeg_log_max_size:
        ffmpeg_log_buffer.pop(0)

def find_ffmpeg():
    # 1. Check system path
    import shutil
    ffmpeg_in_path = shutil.which("ffmpeg")
    if ffmpeg_in_path:
        return ffmpeg_in_path
        
    # 2. Check the winget path we discovered generic for any Windows user
    user_home = os.path.expanduser("~")
    winget_path = os.path.join(user_home, "AppData", "Local", "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-8.1.1-full_build", "bin", "ffmpeg.exe")
    if os.path.exists(winget_path):
        return winget_path
        
    # 3. Check for any Gyan.FFmpeg folder in WinGet packages
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
            
    return None

def detect_devices(ffmpeg_path):
    cmd = [ffmpeg_path, '-list_devices', 'true', '-f', 'dshow', '-i', 'dummy']
    # Run command, capture stderr (which contains the devices)
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', errors='ignore')
    output = res.stderr
    
    video_devs = []
    audio_devs = []
    
    for line in output.splitlines():
        m = re.search(r'"([^"]+)"\s+\((video|audio)\)', line)
        if m:
            name, dev_type = m.group(1), m.group(2)
            if dev_type == 'video':
                if name not in video_devs:
                    video_devs.append(name)
            else:
                if name not in audio_devs:
                    audio_devs.append(name)
                    
    return video_devs, audio_devs

def get_local_ips():
    ips = []
    try:
        hostname = socket.gethostname()
        for item in socket.getaddrinfo(hostname, None):
            ip = item[4][0]
            if '.' in ip and not ip.startswith('127.'):
                if ip not in ips:
                    ips.append(ip)
    except Exception:
        pass
    # Fallback
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        if ip not in ips:
            ips.append(ip)
        s.close()
    except Exception:
        pass
    return ips

def start_hls_stream():
    global hls_process, ffmpeg_exe, selected_video_device, selected_audio_device, settings
    
    if hls_process and hls_process.poll() is None:
        return True
        
    if not ffmpeg_exe or not os.path.exists(ffmpeg_exe):
        print("[HLS] FFmpeg not found. Cannot start background stream.")
        return False
        
    if not selected_video_device:
        print("[HLS] No video device selected. Cannot start background stream.")
        return False
        
    # Clear old HLS files to prevent segment mismatch issues
    for f in os.listdir(HLS_DIR):
        if f.endswith(".m3u8") or f.endswith(".ts"):
            try:
                os.remove(os.path.join(HLS_DIR, f))
            except Exception:
                pass

    # Build HLS command
    cmd = [
        ffmpeg_exe,
        '-y',
        '-f', 'dshow',
    ]
    
    # Configure input parameters if defined
    if settings.get("resolution") and settings["resolution"] != "default":
        cmd += ['-video_size', settings["resolution"]]
    if settings.get("fps"):
        cmd += ['-framerate', settings["fps"]]
        
    cmd += ['-i', f'video={selected_video_device}']
    
    # If audio is enabled and found
    use_audio = settings["audio_enabled"] and selected_audio_device
    if use_audio:
        cmd += ['-f', 'dshow', '-i', f'audio={selected_audio_device}']
        
    # Output encoding parameters
    cmd += [
        '-c:v', 'libx264',
        '-preset', 'ultrafast',
        '-tune', 'zerolatency',
        '-b:v', settings["video_bitrate"],
        '-pix_fmt', 'yuv420p',
    ]
    
    # Add video filter to ensure dimensions are divisible by 2 (H.264 requirement)
    cmd += ['-vf', 'scale=trunc(iw/2)*2:trunc(ih/2)*2']
    
    if use_audio:
        cmd += ['-c:a', 'aac', '-b:a', '128k']
    else:
        cmd += ['-an']
        
    # HLS output parameters
    cmd += [
        '-f', 'hls',
        '-hls_time', '2',
        '-hls_list_size', '4',
        '-hls_flags', 'delete_segments',
        '-start_number', '0',
        os.path.join(HLS_DIR, 'stream.m3u8')
    ]
    
    print(f"[HLS] Starting background HLS encoder:")
    print(" ".join(cmd))
    
    # Hide console window on Windows
    startupinfo = None
    if os.name == 'nt':
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        
    hls_process = subprocess.Popen(
        cmd,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.PIPE,
        text=True,
        encoding='utf-8',
        errors='ignore',
        startupinfo=startupinfo
    )
    
    def monitor_ffmpeg():
        global hls_process
        for line in hls_process.stderr:
            add_to_ffmpeg_log(line)
        hls_process.wait()
        print("[HLS] Background HLS encoder terminated.")
        
    threading.Thread(target=monitor_ffmpeg, daemon=True).start()
    return True

def stop_hls_stream():
    global hls_process
    if hls_process and hls_process.poll() is None:
        print("[HLS] Stopping background HLS encoder process...")
        hls_process.terminate()
        try:
            hls_process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            hls_process.kill()
            hls_process.wait()
        hls_process = None

class ThreadingHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True

class StreamHandler(http.server.BaseHTTPRequestHandler):
    def end_headers(self):
        # Enable CORS for easy cross-origin video streaming
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        super().end_headers()
        
    def do_OPTIONS(self):
        self.send_response(200)
        self.end_headers()
        
    def do_GET(self):
        parsed_url = urllib.parse.urlparse(self.path)
        path = parsed_url.path
        
        if path == "/" or path == "/index.html":
            self.serve_static_file(os.path.join(WEB_ROOT, "index.html"), "text/html")
        elif path == "/favicon.ico":
            self.send_response(404)
            self.end_headers()
        elif path.startswith("/hls/"):
            filename = os.path.basename(path)
            file_path = os.path.join(HLS_DIR, filename)
            if filename.endswith(".m3u8"):
                self.serve_static_file(file_path, "application/vnd.apple.mpegurl")
            elif filename.endswith(".ts"):
                self.serve_static_file(file_path, "video/MP2T")
            else:
                self.send_response(404)
                self.end_headers()
        elif path == "/live.ts":
            self.stream_progressive("mpegts")
        elif path == "/live.mp4":
            self.stream_progressive("mp4")
        elif path == "/api/status":
            self.serve_api_status()
        elif path == "/api/settings":
            self.update_settings(parsed_url.query)
        else:
            local_path = os.path.join(WEB_ROOT, path.lstrip("/"))
            if os.path.exists(local_path) and os.path.isfile(local_path):
                ext = os.path.splitext(local_path)[1]
                mime_types = {
                    ".html": "text/html",
                    ".css": "text/css",
                    ".js": "application/javascript",
                    ".png": "image/png",
                    ".jpg": "image/jpeg",
                    ".svg": "image/svg+xml"
                }
                mime = mime_types.get(ext, "application/octet-stream")
                self.serve_static_file(local_path, mime)
            else:
                self.send_response(404)
                self.end_headers()
                
    def serve_static_file(self, file_path, content_type):
        if not os.path.exists(file_path):
            self.send_response(404)
            self.end_headers()
            return
        
        try:
            with open(file_path, "rb") as f:
                content = f.read()
            self.send_response(200)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
            self.end_headers()
            self.wfile.write(content)
        except Exception as e:
            try:
                self.send_error(500, f"Error reading file: {e}")
            except Exception:
                pass

    def serve_api_status(self):
        global selected_video_device, selected_audio_device, video_devices_list, audio_devices_list, hls_process, settings
        status = {
            "ffmpeg_found": ffmpeg_exe is not None,
            "video_device": selected_video_device,
            "audio_device": selected_audio_device,
            "video_devices": video_devices_list,
            "audio_devices": audio_devices_list,
            "hls_running": hls_process is not None and hls_process.poll() is None,
            "ips": get_local_ips(),
            "port": PORT,
            "settings": settings,
            "logs": ffmpeg_log_buffer[-10:] if ffmpeg_log_buffer else ["No logs available."]
        }
        res_data = json.dumps(status).encode('utf-8')
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(res_data)))
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        self.wfile.write(res_data)

    def update_settings(self, query):
        global selected_video_device, selected_audio_device, settings
        params = urllib.parse.parse_qs(query)
        
        restart_needed = False
        
        if "video_device" in params:
            new_dev = params["video_device"][0]
            if new_dev in video_devices_list and new_dev != selected_video_device:
                selected_video_device = new_dev
                restart_needed = True
                
        if "audio_device" in params:
            new_dev = params["audio_device"][0]
            if new_dev in audio_devices_list and new_dev != selected_audio_device:
                selected_audio_device = new_dev
                restart_needed = True
                
        if "resolution" in params:
            res = params["resolution"][0]
            if res != settings["resolution"]:
                settings["resolution"] = res
                restart_needed = True
                
        if "fps" in params:
            fps = params["fps"][0]
            if fps != settings["fps"]:
                settings["fps"] = fps
                restart_needed = True
                
        if "audio_enabled" in params:
            ae = params["audio_enabled"][0].lower() == "true"
            if ae != settings["audio_enabled"]:
                settings["audio_enabled"] = ae
                restart_needed = True

        if restart_needed:
            print("[Settings] Settings updated, restarting background HLS stream...")
            stop_hls_stream()
            start_hls_stream()
            
        self.serve_api_status()

    def stream_progressive(self, fmt):
        global ffmpeg_exe, settings
        
        if not ffmpeg_exe or not os.path.exists(ffmpeg_exe):
            self.send_response(500)
            self.end_headers()
            self.wfile.write(b"FFmpeg not found.")
            return

        hls_playlist = os.path.join(HLS_DIR, 'stream.m3u8')
        if not os.path.exists(hls_playlist):
            self.send_response(503)
            self.end_headers()
            self.wfile.write(b"HLS stream not initialized. Please wait a moment and try again.")
            return

        # Prepare progressive ffmpeg command reading from the live HLS playlist
        cmd = [
            ffmpeg_exe,
            '-y',
            '-f', 'hls',
            '-i', hls_playlist,
            '-c', 'copy',
        ]
        
        if fmt == "mp4":
            cmd += [
                '-bsf:a', 'aac_adtstoasc',
                '-f', 'mp4',
                '-movflags', 'frag_keyframe+empty_moov+default_base_moof',
                '-'
            ]
            content_type = "video/mp4"
        else: # mpegts
            cmd += [
                '-f', 'mpegts',
                '-'
            ]
            content_type = "video/mp2t"

        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Connection", "close")
        self.end_headers()

        print(f"[Stream] Starting progressive {fmt} stream for {self.client_address} (remuxed from HLS)")
        
        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            
        proc = subprocess.Popen(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            startupinfo=startupinfo
        )

        try:
            while True:
                data = proc.stdout.read(65536)
                if not data:
                    break
                self.wfile.write(data)
        except (ConnectionAbortedError, ConnectionResetError, BrokenPipeError):
            pass
        except Exception as e:
            print(f"[Stream] Error streaming to client: {e}")
        finally:
            try:
                proc.terminate()
                proc.wait(timeout=2)
            except Exception:
                try:
                    proc.kill()
                except Exception:
                    pass
            print(f"[Stream] Progressive {fmt} stream stopped for {self.client_address}")

def main():
    global ffmpeg_exe, selected_video_device, selected_audio_device, video_devices_list, audio_devices_list
    
    print("=" * 60)
    print("         WiFi HTTP CAMERA STREAMER STARTING")
    print("=" * 60)
    
    # 1. Find FFmpeg
    ffmpeg_exe = find_ffmpeg()
    if not ffmpeg_exe:
        print("ERROR: FFmpeg binary not found on this system!")
        print("Please ensure FFmpeg is installed and added to the PATH, or run via winget.")
        sys.exit(1)
        
    print(f"[Init] Found FFmpeg: {ffmpeg_exe}")
    
    # 2. Detect Devices
    print("[Init] Scanning for DirectShow video and audio input devices...")
    video_devices_list, audio_devices_list = detect_devices(ffmpeg_exe)
    
    if not video_devices_list:
        print("ERROR: No camera devices detected via DirectShow.")
        print("Please ensure your camera is connected and not in use by another application.")
        sys.exit(1)
        
    print(f"[Init] Detected Camera Devices: {video_devices_list}")
    print(f"[Init] Detected Audio Devices: {audio_devices_list}")
    
    # Prioritize "USB2.0 HD UVC WebCam" or default to the first webcam found
    for dev in video_devices_list:
        if "USB2.0" in dev or "UVC" in dev or "WebCam" in dev:
            selected_video_device = dev
            break
    if not selected_video_device:
        selected_video_device = video_devices_list[0]
        
    # Prioritize Microphone Array
    for dev in audio_devices_list:
        if "Microphone Array" in dev or "Realtek" in dev:
            selected_audio_device = dev
            break
    if not selected_audio_device and audio_devices_list:
        selected_audio_device = audio_devices_list[0]
        
    print(f"[Init] Selected Video Input: \"{selected_video_device}\"")
    if selected_audio_device:
        print(f"[Init] Selected Audio Input: \"{selected_audio_device}\"")
    else:
        print("[Init] No Audio input selected. Running video-only.")
        
    # 3. Start HLS stream in background
    hls_started = start_hls_stream()
    if not hls_started:
        print("ERROR: Failed to start background HLS encoding process.")
        
    # 4. Start HTTP Web Server
    ips = get_local_ips()
    server = ThreadingHTTPServer(('0.0.0.0', PORT), StreamHandler)
    
    print("\n" + "-" * 50)
    print(f"Server is running! Access the dashboard locally at:")
    print(f"   http://localhost:{PORT}")
    if ips:
        print("\nAccess the stream from other devices on your WiFi:")
        for ip in ips:
            print(f"   http://{ip}:{PORT}")
    print("-" * 50 + "\n")
    print("Press Ctrl+C to stop the server.")
    
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping server...")
    finally:
        stop_hls_stream()
        server.server_close()
        print("Server shutdown completed.")

if __name__ == "__main__":
    main()
