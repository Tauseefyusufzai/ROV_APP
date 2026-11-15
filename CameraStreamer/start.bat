@echo off
title ROV Navyug Camera Streamer
echo ======================================================
echo          Starting ROV Navyug Camera Streamer
echo ======================================================
echo.
python "%~dp0stream_server.py"
if %errorlevel% neq 0 (
    echo.
    echo Server stopped with error code %errorlevel%.
    pause
)
