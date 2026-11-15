@echo off
echo ====================================
echo GDAL TIFF Processing for Unity
echo Creating tiled TIFF with overviews
echo ====================================

set INPUT_FILE=Assets\StreamingAssets\testingzoom_17_tiled.tif
set OUTPUT_FILE=Assets\StreamingAssets\testingzoom_17_optimized.tif

echo.
echo Step 1: Creating tiled version with compression...
gdal_translate -of GTiff -co TILED=YES -co COMPRESS=LZW -co BIGTIFF=YES "%INPUT_FILE%" "%OUTPUT_FILE%"

if errorlevel 1 (
    echo ERROR: Failed to create tiled version
    pause
    exit /b 1
)

echo.
echo Step 2: Adding overview pyramids (this may take a while for large files)...
gdaladdo -r average "%OUTPUT_FILE%" 2 4 8 16 32

if errorlevel 1 (
    echo ERROR: Failed to add overviews
    pause
    exit /b 1
)

echo.
echo ====================================
echo SUCCESS! 
echo Your optimized TIFF is ready: %OUTPUT_FILE%
echo.
echo Features added:
echo - Internal tiling for fast access
echo - LZW compression to reduce file size  
echo - BIGTIFF support for files over 4GB
echo - 5 overview levels (1/2, 1/4, 1/8, 1/16, 1/32 resolution)
echo.
echo Copy this file to: Assets/StreamingAssets/
echo Then update your SafeTiffRenderer tiffFileName to: %OUTPUT_FILE%
echo ====================================
pause
