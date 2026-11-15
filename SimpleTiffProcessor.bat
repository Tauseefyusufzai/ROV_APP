@echo off
setlocal enabledelayedexpansion

echo ====================================================================
echo              SIMPLE TIFF PROCESSOR
echo          For files with spaces/special characters
echo ====================================================================
echo.

REM Check if GDAL is installed
where gdal_translate >nul 2>nul
if errorlevel 1 (
    echo ❌ ERROR: GDAL not found in PATH
    echo Please install GDAL first and add to PATH
    pause
    exit /b 1
)

echo Please drag and drop your TIFF file onto this batch file,
echo or place your TIFF file in the same folder as this script.
echo.

REM If file was dragged onto the script
if not "%~1"=="" (
    set "INPUT_FILE=%~1"
    goto :process
)

REM Look for TIFF files in current directory
for %%f in (*.tif *.tiff) do (
    set "INPUT_FILE=%%f"
    echo Found TIFF file: "%%f"
    set /p "CONFIRM=Process this file? (y/n): "
    if /i "!CONFIRM!"=="y" goto :process
)

echo No TIFF files found in current directory.
set /p "INPUT_FILE=Enter full path to TIFF file: "

:process
echo.
echo Processing: "%INPUT_FILE%"

REM Check if file exists
if not exist "%INPUT_FILE%" (
    echo ❌ ERROR: File not found
    pause
    exit /b 1
)

REM Extract clean filename
for %%i in ("%INPUT_FILE%") do (
    set "FILENAME=%%~ni"
)

echo File: %FILENAME%

REM Create StreamingAssets if needed
if not exist "Assets\StreamingAssets\" mkdir "Assets\StreamingAssets"

REM Simple processing with robust paths
echo.
echo Step 1: Creating tiled version...
gdal_translate -of GTiff -co TILED=YES -co COMPRESS=LZW "%INPUT_FILE%" "Assets\StreamingAssets\%FILENAME%_optimized.tif"

if errorlevel 1 (
    echo ❌ GDAL processing failed
    pause
    exit /b 1
)

echo.
echo Step 2: Adding overviews...
gdaladdo -r average "Assets\StreamingAssets\%FILENAME%_optimized.tif" 2 4 8 16 32

if errorlevel 1 (
    echo ❌ Overview creation failed
    pause
    exit /b 1
)

echo.
echo ✅ SUCCESS!
echo File ready: Assets\StreamingAssets\%FILENAME%_optimized.tif
echo.
echo Update SafeTiffRenderer.tiffFileName to: %FILENAME%_optimized.tif
echo.
pause
