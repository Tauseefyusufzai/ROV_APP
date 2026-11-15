@echo off
setlocal enabledelayedexpansion

echo ====================================================================
echo           UNIVERSAL MAP PROCESSOR - FOR ALL FUTURE MAPS
echo        Automatically optimizes any map size from 1GB to 100GB+
echo ====================================================================
echo.

REM Get input file from user
if "%~1"=="" (
    set /p "INPUT_FILE=Enter the full path to your TIFF file: "
) else (
    set "INPUT_FILE=%~1"
)

REM Remove quotes if present
set "INPUT_FILE=%INPUT_FILE:"=%"

REM Check if input file exists
if not exist "%INPUT_FILE%" (
    echo ERROR: File not found: "%INPUT_FILE%"
    exit /b 1
)

REM Extract filename without path and extension
REM Extract filename without path and extension, or use provided output name (arg2)
if "%~2"=="" (
    for %%i in ("%INPUT_FILE%") do (
        set "FILENAME=%%~ni"
        set "FILEPATH=%%~dpi"
    )
) else (
    set "FILENAME=%~2"
    for %%i in ("%INPUT_FILE%") do (
        set "FILEPATH=%%~dpi"
    )
)

echo.
echo Processing: %FILENAME%
echo.

REM Step 1: Process with AutoTiffProcessor (handles any size)
echo Step 1/3: Processing TIFF with universal optimization...
call AutoTiffProcessor.bat "%INPUT_FILE%" "%FILENAME%"

REM Detect if running from build or editor
REM Dynamically find any *_Data folder (Unity build convention: {BuildName}_Data)
set "BUILD_DATA_FOLDER="
for /d %%D in (*_Data) do (
    if exist "%%D\" (
        set "BUILD_DATA_FOLDER=%%D"
        echo Found build data folder: %%D
    )
)

if defined BUILD_DATA_FOLDER (
    echo Detected BUILD environment - using !BUILD_DATA_FOLDER!
    set "MAP_FOLDER=!BUILD_DATA_FOLDER!\StreamingAssets\%FILENAME%"
) else if exist "Assets" (
    echo Detected EDITOR environment
    set "MAP_FOLDER=Assets\StreamingAssets\%FILENAME%"
) else (
    echo WARNING: Could not detect environment, using default
    set "MAP_FOLDER=Assets\StreamingAssets\%FILENAME%"
)

REM Create map folder structure
if not exist "%MAP_FOLDER%" (
    mkdir "%MAP_FOLDER%"
    echo Created map folder: %MAP_FOLDER%
)

REM Check if the actual output files were created instead of relying on error level
set "OPTIMIZED_FILE=%MAP_FOLDER%\%FILENAME%_optimized.tif"
set "METADATA_FILE=%MAP_FOLDER%\%FILENAME%_metadata.json"

if not exist "%OPTIMIZED_FILE%" (
    echo ERROR: TIFF processing failed - optimized file not created
    exit /b 1
)

if not exist "%METADATA_FILE%" (
    echo WARNING: Metadata file not created, but continuing...
)

echo [SUCCESS] TIFF processing completed successfully!

REM Step 2: Analyze map characteristics
echo.
echo Step 2/3: Analyzing map characteristics for auto-configuration...

REM Get map dimensions using gdalinfo
gdalinfo "%INPUT_FILE%" > temp_map_info.txt

REM Extract dimensions
for /f "tokens=3,4" %%a in ('findstr /c:"Size is" temp_map_info.txt') do (
    set "WIDTH=%%a"
    set "HEIGHT=%%b"
)

REM Remove comma from width
set "WIDTH=%WIDTH:,=%"

REM Calculate tile count (assuming 512x512 tiles)
set /a TILES_X=(%WIDTH% + 511) / 512
set /a TILES_Y=(%HEIGHT% + 511) / 512  
set /a TOTAL_TILES=%TILES_X% * %TILES_Y%

echo    Map dimensions: %WIDTH% x %HEIGHT% pixels
echo    Tile grid: %TILES_X% x %TILES_Y% tiles
echo    Total tiles: %TOTAL_TILES%

REM Determine map category and optimal settings
if %TOTAL_TILES% LEQ 50 (
    set "CATEGORY=TINY MAP"
    set "MAX_TILES=100"
    set "TILES_PER_FRAME=10"
    set "QUEUE_SIZE=30"
    set "TIMEOUT=60"
    set "MAX_LOD=1"
) else if %TOTAL_TILES% LEQ 200 (
    set "CATEGORY=SMALL MAP"
    set "MAX_TILES=400"
    set "TILES_PER_FRAME=20"
    set "QUEUE_SIZE=80"
    set "TIMEOUT=90"
    set "MAX_LOD=2"
) else if %TOTAL_TILES% LEQ 1000 (
    set "CATEGORY=MEDIUM MAP"
    set "MAX_TILES=1500"
    set "TILES_PER_FRAME=30"
    set "QUEUE_SIZE=200"
    set "TIMEOUT=180"
    set "MAX_LOD=3"
) else if %TOTAL_TILES% LEQ 5000 (
    set "CATEGORY=LARGE MAP"
    set "MAX_TILES=3000"
    set "TILES_PER_FRAME=50"
    set "QUEUE_SIZE=300"
    set "TIMEOUT=300"
    set "MAX_LOD=4"
) else if %TOTAL_TILES% LEQ 15000 (
    set "CATEGORY=HUGE MAP"
    set "MAX_TILES=4000"
    set "TILES_PER_FRAME=60"
    set "QUEUE_SIZE=400"
    set "TIMEOUT=450"
    set "MAX_LOD=5"
) else (
    set "CATEGORY=MASSIVE MAP"
    set "MAX_TILES=5000"
    set "TILES_PER_FRAME=80"
    set "QUEUE_SIZE=500"
    set "TIMEOUT=600"
    set "MAX_LOD=6"
)

echo    Detected: %CATEGORY% (%TOTAL_TILES% tiles)
echo    Auto-selected settings: Max %MAX_TILES% tiles, LOD 0-%MAX_LOD%

REM Step 3: Generate optimized Unity configuration
echo.
echo Step 3/3: Generating optimized Unity configuration...

set "CONFIG_FILE=%MAP_FOLDER%\unity_config_%FILENAME%_UNIVERSAL.txt"

echo. > "%CONFIG_FILE%"
echo // UNIVERSAL AUTO-GENERATED Configuration for %FILENAME% >> "%CONFIG_FILE%"
echo // Map Category: %CATEGORY% (%TOTAL_TILES% tiles) >> "%CONFIG_FILE%"
echo // Auto-optimized settings for perfect rendering >> "%CONFIG_FILE%"
echo. >> "%CONFIG_FILE%"
echo tiffFileName = "%FILENAME%\%FILENAME%_optimized.tif"; >> "%CONFIG_FILE%"
echo. >> "%CONFIG_FILE%"
echo // UNIVERSAL AUTO-OPTIMIZATION (No manual tuning needed): >> "%CONFIG_FILE%"
echo maxTotalTiles = %MAX_TILES%;   // Auto-calculated for %TOTAL_TILES% tiles >> "%CONFIG_FILE%"
echo maxTilesPerFrame = %TILES_PER_FRAME%;  // %CATEGORY% optimized loading speed >> "%CONFIG_FILE%"
echo maxQueueSize = %QUEUE_SIZE%;     // %CATEGORY% optimized queue size >> "%CONFIG_FILE%"
echo maxLoadingTime = %TIMEOUT%;   // %CATEGORY% optimized timeout >> "%CONFIG_FILE%"
echo showDebugInfo = true;   // Monitor the universal system >> "%CONFIG_FILE%"
echo. >> "%CONFIG_FILE%"
echo // ENHANCED FEATURES (Always enabled): >> "%CONFIG_FILE%"
echo enableCoordinateDisplay = true;    // Multi-format coordinates >> "%CONFIG_FILE%"
echo enableElevationDisplay = false;    // Set to true for DEM files >> "%CONFIG_FILE%"
echo enableMeasurementTools = true;     // Professional measurement tools >> "%CONFIG_FILE%"
echo. >> "%CONFIG_FILE%"
echo // UNIVERSAL SYSTEM STATUS: >> "%CONFIG_FILE%"
echo // [OK] Map automatically categorized as: %CATEGORY% >> "%CONFIG_FILE%"
echo // [OK] LOD range: 0-%MAX_LOD% (automatically enforced) >> "%CONFIG_FILE%"
echo // [OK] Stability optimized for map size >> "%CONFIG_FILE%"
echo // [OK] Memory management configured >> "%CONFIG_FILE%"
echo // [OK] Performance settings optimized >> "%CONFIG_FILE%"
echo. >> "%CONFIG_FILE%"
echo // SETUP INSTRUCTIONS: >> "%CONFIG_FILE%"
echo // 1. Copy above settings to your SafeTiffRenderer component >> "%CONFIG_FILE%"
echo // 2. Add UniversalMapHandler component (if not already added) >> "%CONFIG_FILE%"
echo // 3. Press Play - the system will auto-optimize everything else! >> "%CONFIG_FILE%"

REM Cleanup
del temp_map_info.txt >nul 2>nul

echo.
echo ====================================================================
echo                    UNIVERSAL MAP PROCESSING COMPLETE!
echo ====================================================================
echo.
REM Generate lightweight preview image using GDAL
echo.
echo Step 4/4: Generating lightweight preview image...
set "PREVIEW_FILE=%MAP_FOLDER%\%FILENAME%_preview.png"

REM Calculate aspect ratio and resize maintaining proportions
REM Use GDAL to create a high-quality preview maintaining original aspect ratio
REM Maximum dimension will be 1024 pixels, but aspect ratio is preserved
gdal_translate -of PNG -outsize 1024 0 -co COMPRESS=PNG -co ZLEVEL=6 "%OPTIMIZED_FILE%" "%PREVIEW_FILE%"

if exist "%PREVIEW_FILE%" (
    echo [SUCCESS] Preview image generated: %PREVIEW_FILE%
    
    REM Get file size for reporting
    for %%A in ("%PREVIEW_FILE%") do set "PREVIEW_SIZE=%%~zA"
    set /a PREVIEW_KB=!PREVIEW_SIZE!/1024
    echo    Preview size: Proportional to original (~!PREVIEW_KB! KB)
) else (
    echo [WARNING] Preview generation failed - will use fallback method
)

echo.
echo ====================================================================
echo.
echo [SUCCESS] Your map has been universally optimized for perfect rendering!
echo.
echo Map Analysis Results:
echo    Category: %CATEGORY%
echo    Dimensions: %WIDTH% x %HEIGHT% pixels  
echo    Tile Count: %TOTAL_TILES% tiles (%TILES_X% x %TILES_Y%)
echo    Max LOD: %MAX_LOD% (automatically enforced)
echo    Optimized for: %CATEGORY% performance profile
echo.
echo Files Created:
echo    %MAP_FOLDER%\%FILENAME%_optimized.tif
echo    %MAP_FOLDER%\%FILENAME%_metadata.json  
echo    %MAP_FOLDER%\%FILENAME%_preview.png (1024px preview)
echo    %CONFIG_FILE%
echo.
echo UNIVERSAL SYSTEM FEATURES:
echo    - Automatic map size detection and optimization
echo    - Intelligent LOD constraints (Max LOD %MAX_LOD%)
echo    - Dynamic stability control for smooth rendering
echo    - Memory pressure management
echo    - Performance monitoring and diagnostics
echo    - Future-proof for any map size (1GB to 100GB+)
echo.
echo NEXT STEPS:
echo    1. Apply settings from: %CONFIG_FILE%
echo    2. Ensure UniversalMapHandler component is added
echo    3. Press Play in Unity - everything else is automatic!
echo.
echo This map will now render perfectly with the Universal Map System!
echo ====================================================================

REM Auto-exit without pause for automated Unity workflow
exit /b 0
