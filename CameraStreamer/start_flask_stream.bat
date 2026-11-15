@echo off
title ROV Navyug Flask Camera Streamer
echo ======================================================
echo       Starting Flask HLS Camera Streamer (Port 5000)
echo ======================================================
echo.
python "%~dp0flask_stream_server.py"
if %errorlevel% neq 0 (
    echo.
    echo Server stopped with error code %errorlevel%.
    pause
)
