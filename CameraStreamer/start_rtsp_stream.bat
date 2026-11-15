@echo off
title ROV Navyug RTSP Camera Streamer
echo ======================================================
echo          Starting ROV Navyug RTSP Streamer
echo ======================================================
echo.
python "%~dp0rtsp_stream_server.py"
if %errorlevel% neq 0 (
    echo.
    echo Server stopped with error code %errorlevel%.
    pause
)
