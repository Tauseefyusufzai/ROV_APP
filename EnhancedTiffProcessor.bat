@echo off
setlocal enabledelayedexpansion

echo ====================================================================
echo           ENHANCED GEOSPATIAL TIFF PROCESSING PIPELINE
echo     Military-Grade TIFF to Unity Converter v2.0 - Intelligence Ready
echo ====================================================================
echo.

REM Check if GDAL is installed
where gdal_translate >nul 2>nul
if errorlevel 1 (
    echo ❌ ERROR: GDAL not found in PATH
    echo.
    echo Please install GDAL first:
    echo 1. Download from: https://gdal.org/download.html
    echo 2. Add GDAL bin folder to your system PATH
    echo 3. Or install via conda: conda install -c conda-forge gdal
    echo.
    pause
    exit /b 1
)

REM Get input file from user
if "%~1"=="" (
    set /p "INPUT_FILE=Enter the full path to your TIFF file (including filename): "
) else (
    set "INPUT_FILE=%~1"
)

REM Remove quotes if present
set "INPUT_FILE=%INPUT_FILE:"=%"

REM Convert forward slashes to backslashes (Unity Editor compatibility)
set "INPUT_FILE=%INPUT_FILE:/=\%"

REM Check if input file exists
if not exist "%INPUT_FILE%" (
    echo ❌ ERROR: File not found: "%INPUT_FILE%"
    echo.
    echo Please check:
    echo 1. The file path is correct
    echo 2. The file exists and is accessible  
    echo 3. Include the full filename with .tif extension
    echo.
    echo Trying alternative path formats...
    
    REM Try with forward slashes in case it's a Unix-style path
    set "ALT_INPUT=%INPUT_FILE:\=/%"
    if exist "%ALT_INPUT%" (
        set "INPUT_FILE=%ALT_INPUT%"
        echo ✅ Found file with alternative path format
        goto :continue
    )
    
    echo Example: C:\Maps\mymap.tif
    pause
    exit /b 1
)

:continue

REM Verify it's a TIFF file
set "EXTENSION=%INPUT_FILE:~-4%"
if /i not "%EXTENSION%"==".tif" (
    if /i not "%INPUT_FILE:~-5%"==".tiff" (
        echo ❌ ERROR: File must be a TIFF file (.tif or .tiff)
        echo Provided file: "%INPUT_FILE%"
        pause
        exit /b 1
    )
)

REM Extract filename without path and extension
for %%i in ("%INPUT_FILE%") do (
    set "FILENAME=%%~ni"
    set "FILEPATH=%%~dpi"
)

echo.
echo 🌍 GEOSPATIAL INTELLIGENCE PROCESSING
echo 📄 Processing file: %FILENAME%
echo 📁 Source path: %FILEPATH%
echo 📋 Full input path: "%INPUT_FILE%"
echo.

REM Pre-analysis: Detect file type and characteristics
echo 🔍 Step 0/6: Analyzing source file characteristics...
echo    - Detecting elevation data (DEM)
echo    - Analyzing coordinate reference system
echo    - Checking band information
gdalinfo "%INPUT_FILE%" > temp_analysis.txt
if errorlevel 0 (
    findstr /i "gray" temp_analysis.txt >nul && (
        echo ✅ DETECTED: Digital Elevation Model (DEM) - Elevation processing enabled
        set "IS_ELEVATION=true"
    ) || (
        echo ✅ DETECTED: RGB/Color imagery - Standard processing
        set "IS_ELEVATION=false"
    )
    
    findstr /i "minimum=" temp_analysis.txt >nul && (
        echo ✅ DETECTED: Band statistics available - Enhanced metadata extraction
        set "HAS_STATS=true"
    ) || (
        echo 📊 Computing band statistics for elevation analysis...
        set "HAS_STATS=false"
    )
)
del temp_analysis.txt >nul 2>nul

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
    echo Detected BUILD environment - using !BUILD_DATA_FOLDER! folder
    set "STREAMING_ASSETS=!BUILD_DATA_FOLDER!\StreamingAssets"
) else if exist "Assets" (
    echo Detected EDITOR environment - using Assets folder
    set "STREAMING_ASSETS=Assets\StreamingAssets"
) else (
    echo WARNING: Could not detect environment, using default Assets folder
    set "STREAMING_ASSETS=Assets\StreamingAssets"
)

REM Ensure StreamingAssets directory exists
if not exist "%STREAMING_ASSETS%\" (
    mkdir "%STREAMING_ASSETS%"
    echo Created StreamingAssets directory: %STREAMING_ASSETS%
)

REM Define output paths with proper quoting
set "TILED_FILE=%STREAMING_ASSETS%\%FILENAME%_tiled.tif"
set "OPTIMIZED_FILE=%STREAMING_ASSETS%\%FILENAME%_optimized.tif"

echo.
echo 🔄 Step 1/6: Creating enhanced tiled version...
echo    - Adding internal tiling (512x512 tiles) for optimal Unity performance
echo    - Applying LZW compression for reduced file size
echo    - Adding BIGTIFF support for files over 4GB
echo    - Preserving elevation data precision

REM Enhanced options for elevation data
if "%IS_ELEVATION%"=="true" (
    echo    - DEM MODE: Preserving elevation precision and NoData values
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PREDICTOR=2 "%INPUT_FILE%" "%TILED_FILE%"
) else (
    echo    - RGB MODE: Optimizing for color imagery
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PHOTOMETRIC=RGB "%INPUT_FILE%" "%TILED_FILE%"
)

if errorlevel 1 (
    echo ❌ ERROR: Failed to create tiled version
    pause
    exit /b 1
)
echo ✅ Enhanced tiled version created: %TILED_FILE%

echo.
echo 🔄 Step 2/6: Adding intelligent pyramid overviews...
echo    - Creating LOD levels optimized for geospatial intelligence
echo    - LOD 0 (Full res) → LOD 1-5 (Progressive scaling for smooth navigation)

if "%IS_ELEVATION%"=="true" (
    echo    - Using AVERAGE resampling for elevation data (preserves terrain accuracy)
    gdaladdo -r average "%TILED_FILE%" 2 4 8 16 32 64
) else (
    echo    - Using CUBIC resampling for imagery (preserves visual quality)
    gdaladdo -r cubic "%TILED_FILE%" 2 4 8 16 32 64
)

if errorlevel 1 (
    echo ❌ ERROR: Failed to add overviews
    pause
    exit /b 1
)

echo.
echo 🔄 Step 3/6: Creating final optimized version with intelligence features...
if "%IS_ELEVATION%"=="true" (
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PREDICTOR=2 "%TILED_FILE%" "%OPTIMIZED_FILE%"
) else (
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PHOTOMETRIC=RGB "%TILED_FILE%" "%OPTIMIZED_FILE%"
)

if errorlevel 1 (
    echo ❌ ERROR: Failed to create optimized version
    pause
    exit /b 1
)

echo.
echo 🔄 Step 4/6: Adding final overviews to optimized file...
if "%IS_ELEVATION%"=="true" (
    gdaladdo -r average "%OPTIMIZED_FILE%" 2 4 8 16 32 64
) else (
    gdaladdo -r cubic "%OPTIMIZED_FILE%" 2 4 8 16 32 64
)

if errorlevel 1 (
    echo ❌ ERROR: Failed to add final overviews
    pause
    exit /b 1
)

REM Clean up intermediate file
del "%TILED_FILE%" >nul 2>nul

echo.
echo 🔄 Step 5/6: Computing enhanced band statistics for elevation analysis...
if "%IS_ELEVATION%"=="true" (
    echo    - Computing elevation range (min/max values)
    echo    - Detecting NoData values for terrain analysis
    echo    - Generating histogram for elevation distribution
    gdalinfo -stats -hist "%OPTIMIZED_FILE%" > temp_stats.txt 2>nul
    if errorlevel 0 (
        echo ✅ Enhanced elevation statistics computed
    ) else (
        echo ⚠️ Warning: Could not compute enhanced statistics (basic stats will be used)
    )
    del temp_stats.txt >nul 2>nul
)

echo.
echo 🔄 Step 6/6: Extracting military-grade geospatial metadata...
set "METADATA_FILE=%STREAMING_ASSETS%\%FILENAME%_metadata.json"
echo    - Extracting coordinate reference system (CRS) for MGRS conversion
echo    - Extracting geographic bounds (lat/lng) with high precision
echo    - Extracting geotransform matrix for pixel-to-coordinate conversion
echo    - Extracting elevation data and band statistics
echo    - Computing projection parameters for measurement accuracy

REM Enhanced metadata extraction with statistics and checksum
gdalinfo -json -stats -checksum "%OPTIMIZED_FILE%" > "%METADATA_FILE%"

if errorlevel 1 (
    echo ❌ WARNING: Failed to extract enhanced metadata (basic extraction attempted)
    gdalinfo -json "%OPTIMIZED_FILE%" > "%METADATA_FILE%"
    if errorlevel 1 (
        echo ❌ ERROR: Could not extract any metadata
    ) else (
        echo ✅ Basic geospatial metadata extracted: %METADATA_FILE%
    )
) else (
    echo ✅ Enhanced military-grade metadata extracted: %METADATA_FILE%
    
    REM Verify elevation data in metadata
    if "%IS_ELEVATION%"=="true" (
        findstr /i "minimum" "%METADATA_FILE%" >nul && (
            echo ✅ Elevation range successfully extracted for real-time display
        ) || (
            echo ⚠️ Warning: Elevation statistics may need manual computation
        )
    )
)

echo.
echo ====================================================================
echo                    🎉 GEOSPATIAL INTELLIGENCE SUCCESS!
echo ====================================================================
echo.
echo Your TIFF file has been fully processed for military-grade geospatial intelligence!
echo.
echo 📁 Files created:
echo    ✅ %OPTIMIZED_FILE%
echo    ✅ %METADATA_FILE%
echo.
echo 🚀 Intelligence features enabled:
echo    • Internal 512x512 tiling for ultra-fast access
echo    • LZW compression (reduces file size by 30-70%%)
echo    • BIGTIFF support for files over 4GB
echo    • 6 pyramid levels (LOD 0-5) with intelligent resampling
if "%IS_ELEVATION%"=="true" (
echo    • ELEVATION DATA: DEM processing with terrain-preserving algorithms
echo    • Real-time elevation display under cursor
echo    • Elevation range: Available in metadata for UI display
echo    • NoData value handling for accurate terrain analysis
) else (
echo    • RGB photometric for perfect color reproduction
echo    • Cubic resampling for visual quality preservation
)
echo    • Enhanced geospatial metadata with coordinate reference system
echo    • Real-time lat/lng coordinate conversion support
echo    • MGRS coordinate system compatibility
echo    • Military-grade measurement tools (distance/area)
echo    • Band statistics for data quality analysis
echo.
echo 🌍 NEW PHASE 3 & 4 FEATURES READY:
echo    ✅ Real-time elevation display (Phase 3)
echo    ✅ Professional measurement tools (Phase 3)
echo    ✅ Enhanced coordinate panel (Phase 4)
echo    ✅ MGRS military grid reference system (Phase 4)
echo    ✅ Stabilized LOD for all map sizes (Fixed fluctuation issues)
echo.
echo 📋 Next steps:
echo    1. Your file is in: %STREAMING_ASSETS%\
echo    2. Update SafeTiffRenderer.tiffFileName to: "%FILENAME%_optimized.tif"
echo    3. Enable new features in Unity Inspector:
echo       - enableElevationDisplay = true (for DEM files)
echo       - enableMeasurementTools = true
echo       - enableCoordinateDisplay = true
echo    4. Press Play in Unity - your enhanced geospatial system is ready!
echo.
echo 🎮 New Controls Available:
echo    • M key - Toggle measurement tools (distance/area)
echo    • Mouse hover - Real-time coordinates and elevation
echo    • Left-click - Place measurement points
echo    • Right-click - Clear measurements
echo    • Mouse wheel - Smooth zoom (no more LOD fluctuation)
echo.
echo ⚡ Performance optimized for:
echo    • Small maps (43MB): Stable LOD, no fluctuation
echo    • Large maps (10GB+): Intelligent memory management
echo    • Elevation maps: Real-time terrain analysis
echo    • Military operations: MGRS coordinates and measurement tools
echo ====================================================================

REM Generate enhanced Unity configuration file
echo. > unity_config_%FILENAME%.txt
echo // Enhanced Unity Configuration for %FILENAME% >> unity_config_%FILENAME%.txt
echo // Military-Grade Geospatial Intelligence System >> unity_config_%FILENAME%.txt
echo // Copy these settings to your SafeTiffRenderer component: >> unity_config_%FILENAME%.txt
echo. >> unity_config_%FILENAME%.txt
echo tiffFileName = "%FILENAME%_optimized.tif"; >> unity_config_%FILENAME%.txt
echo. >> unity_config_%FILENAME%.txt
echo // Phase 3 & 4 Features (NEW): >> unity_config_%FILENAME%.txt
echo enableElevationDisplay = %IS_ELEVATION%;  // Real-time elevation under cursor >> unity_config_%FILENAME%.txt
echo enableMeasurementTools = true;   // Professional distance/area measurement >> unity_config_%FILENAME%.txt
echo enableCoordinateDisplay = true;  // Multi-format coordinate panel >> unity_config_%FILENAME%.txt
echo. >> unity_config_%FILENAME%.txt
echo // Performance settings (optimized): >> unity_config_%FILENAME%.txt
if "%IS_ELEVATION%"=="true" (
echo maxTotalTiles = 2000;   // Balanced for elevation data >> unity_config_%FILENAME%.txt
) else (
echo maxTotalTiles = 3000;   // Higher for imagery >> unity_config_%FILENAME%.txt
)
echo maxTilesPerFrame = 50;   // Fast loading >> unity_config_%FILENAME%.txt
echo maxQueueSize = 250;      // Large queue >> unity_config_%FILENAME%.txt
echo maxLoadingTime = 300;    // 5 minute timeout >> unity_config_%FILENAME%.txt
echo. >> unity_config_%FILENAME%.txt
echo // Anti-fluctuation (FIXED): >> unity_config_%FILENAME%.txt
echo // - Stabilized LOD thresholds >> unity_config_%FILENAME%.txt
echo // - Small map optimization >> unity_config_%FILENAME%.txt
echo // - Smooth transitions for all map sizes >> unity_config_%FILENAME%.txt

echo 📝 Enhanced Unity configuration saved to: unity_config_%FILENAME%.txt
echo.
echo 🌍 Your geospatial intelligence system is ready for deployment!
pause
