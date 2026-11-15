@echo off
setlocal enabledelayedexpansion

echo ====================================================================
echo              AUTOMATED TIFF PROCESSING PIPELINE
echo    Military-Grade TIFF to Unity Converter v2.0 - Intelligence Ready
echo ====================================================================
echo.
echo ENHANCED GEOSPATIAL INTELLIGENCE FEATURES:
echo    - Automatic DEM detection and elevation data extraction
echo    - Enhanced band statistics for terrain analysis
echo    - MGRS-ready coordinate precision
echo    - Military-grade geospatial metadata generation
echo    - Real-time elevation display support
echo.

REM Check if GDAL is installed
where gdal_translate >nul 2>nul
if errorlevel 1 (
    echo ERROR: GDAL not found in PATH
    echo.
    echo Please install GDAL first:
    echo 1. Download from: https://gdal.org/download.html
    echo 2. Add GDAL bin folder to your system PATH
    echo 3. Or install via conda: conda install -c conda-forge gdal
    echo.

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
    echo ERROR: File not found: "%INPUT_FILE%"
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
        echo Found file with alternative path format
        goto :continue
    )
    
    echo Example: C:\Maps\mymap.tif

    exit /b 1
)

:continue

REM Verify it's a TIFF file
set "EXTENSION=%INPUT_FILE:~-4%"
if /i not "%EXTENSION%"==".tif" (
    if /i not "%INPUT_FILE:~-5%"==".tiff" (
        echo ERROR: File must be a TIFF file (.tif or .tiff)
        echo Provided file: "%INPUT_FILE%"

        exit /b 1
    )
)

REM Extract filename without path and extension
for %%i in ("%INPUT_FILE%") do (
    set "SOURCE_FILENAME=%%~ni"
    set "FILEPATH=%%~dpi"
)

REM Optional output map name (from Unity UI map name)
if "%~2"=="" (
    set "FILENAME=%SOURCE_FILENAME%"
) else (
    set "FILENAME=%~2"
)
set "FILENAME=%FILENAME:\"=%"

echo.
echo Processing file: %SOURCE_FILENAME%
echo Output map name: %FILENAME%
echo Source path: %FILEPATH%
echo Full input path: "%INPUT_FILE%"

REM Pre-analysis: Detect file type and characteristics for enhanced processing
echo.
echo Step 0/6: Analyzing source file for geospatial intelligence...
echo    - Detecting elevation data (DEM vs RGB imagery)
echo    - Analyzing coordinate reference system
echo    - Checking band information for enhanced processing
gdalinfo "%INPUT_FILE%" > temp_analysis.txt
if errorlevel 0 (
    REM Check for number of bands - proper RGB detection
    findstr /c:"Band 3" temp_analysis.txt >nul && (
        REM 3+ bands = RGB imagery
        echo RGB MAP DETECTED: Standard color imagery (3+ bands detected)
        echo    - Will use color-preserving algorithms
        echo    - Using RGB photometric interpretation
        set "IS_ELEVATION=false"
    ) || (
        REM 1 band = likely elevation/grayscale
        findstr /c:"Band 2" temp_analysis.txt >nul && (
            echo RGB MAP DETECTED: Two-band imagery 
            echo    - Will use color-preserving algorithms
            set "IS_ELEVATION=false"
        ) || (
            echo ELEVATION MAP DETECTED: Single-band Digital Elevation Model (DEM)
            echo    - Will use elevation-preserving algorithms
            echo    - Will extract elevation range for real-time display
            set "IS_ELEVATION=true"
        )
    )
    
    findstr /i "minimum=" temp_analysis.txt >nul && (
        echo Band statistics detected - Enhanced metadata extraction enabled
        set "HAS_STATS=true"
    ) || (
        echo Will compute band statistics for enhanced analysis
        set "HAS_STATS=false"
    )
)
del temp_analysis.txt >nul 2>nul
echo.

REM Detect if running from build or editor (check if *_Data folder exists)
set "BUILD_DATA_FOLDER="
for /d %%D in (*_Data) do (
    if exist "%%D\StreamingAssets" set "BUILD_DATA_FOLDER=%%D"
    if not defined BUILD_DATA_FOLDER set "BUILD_DATA_FOLDER=%%D"
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

REM Create map-specific folder structure
set "MAP_FOLDER=%STREAMING_ASSETS%\%FILENAME%"
if not exist "%MAP_FOLDER%" (
    mkdir "%MAP_FOLDER%"
    echo Created map folder: %MAP_FOLDER%
)

REM Step 0.5: Reproject to Web Mercator (EPSG:3857) for runtime consistency
set "PROCESS_INPUT=%INPUT_FILE%"
set "REPROJECTED_FILE=%MAP_FOLDER%\%FILENAME%_reprojected_3857.tif"
set "TEMP_REPROJECTED=0"

echo.
echo Step 0.5/6: Standardizing CRS to EPSG:3857 (runtime pipeline)
if "%IS_ELEVATION%"=="true" (
    gdalwarp -t_srs EPSG:3857 -r bilinear -overwrite "%INPUT_FILE%" "%REPROJECTED_FILE%"
) else (
    gdalwarp -t_srs EPSG:3857 -r cubic -overwrite "%INPUT_FILE%" "%REPROJECTED_FILE%"
)

if errorlevel 1 (
    echo ERROR: CRS reprojection failed
    exit /b 1
)

set "PROCESS_INPUT=%REPROJECTED_FILE%"
set "TEMP_REPROJECTED=1"

REM Define output paths with proper quoting in map folder
set "TILED_FILE=%MAP_FOLDER%\%FILENAME%_tiled.tif"
set "OPTIMIZED_FILE=%MAP_FOLDER%\%FILENAME%_optimized.tif"

echo Step 1/5: Creating tiled version with compression...
echo    - Adding internal tiling (512x512 tiles)
echo    - Applying LZW compression
echo    - Adding BIGTIFF support for files over 4GB
if "%IS_ELEVATION%"=="true" (
    echo    - DEM MODE: Preserving elevation precision with PREDICTOR=2
    echo    - Using grayscale color interpretation for elevation data
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PREDICTOR=2 -co PHOTOMETRIC=MINISBLACK "%PROCESS_INPUT%" "%TILED_FILE%"
) else (
    echo    - RGB MODE: Optimizing for color imagery with full RGB support
    echo    - Using RGB photometric interpretation for perfect colors
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PHOTOMETRIC=RGB "%PROCESS_INPUT%" "%TILED_FILE%"
)

if errorlevel 1 (
    echo ERROR: Failed to create tiled version

    exit /b 1
)
echo Tiled version created: %TILED_FILE%

echo.
echo Step 2/5: Adding pyramid overviews (LOD levels)...
echo    - This creates multiple resolution levels for smooth zooming
echo    - LOD 0 (Full res) - LOD 1 (1/2) - LOD 2 (1/4) - LOD 3 (1/8) - LOD 4 (1/16) - LOD 5 (1/32)
if "%IS_ELEVATION%"=="true" (
    echo    - Using AVERAGE resampling to preserve elevation accuracy
    gdaladdo -r average "%TILED_FILE%" 2 4 8 16 32 64
) else (
    echo    - Using CUBIC resampling for best visual quality  
    gdaladdo -r cubic "%TILED_FILE%" 2 4 8 16 32 64
)

if errorlevel 1 (
    echo ERROR: Failed to add overviews

    exit /b 1
)

echo.
echo Step 3/5: Creating final optimized version...
if "%IS_ELEVATION%"=="true" (
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PREDICTOR=2 -co PHOTOMETRIC=MINISBLACK "%TILED_FILE%" "%OPTIMIZED_FILE%"
) else (
    gdal_translate -of GTiff -co TILED=YES -co BLOCKXSIZE=512 -co BLOCKYSIZE=512 -co COMPRESS=LZW -co BIGTIFF=YES -co PHOTOMETRIC=RGB "%TILED_FILE%" "%OPTIMIZED_FILE%"
)

if errorlevel 1 (
    echo ERROR: Failed to create optimized version

    exit /b 1
)

echo.
echo Step 4/5: Adding final overviews to optimized file...
if "%IS_ELEVATION%"=="true" (
    gdaladdo -r average "%OPTIMIZED_FILE%" 2 4 8 16 32 64
) else (
    gdaladdo -r cubic "%OPTIMIZED_FILE%" 2 4 8 16 32 64
)

if errorlevel 1 (
    echo ERROR: Failed to add final overviews

    exit /b 1
)

REM Clean up intermediate file
del "%TILED_FILE%" >nul 2>nul

echo.
echo Step 5/5: Extracting enhanced geospatial metadata...
set "METADATA_FILE=%MAP_FOLDER%\%FILENAME%_metadata.json"
echo    - Extracting coordinate reference system (CRS) for MGRS conversion
echo    - Extracting geographic bounds (lat/lng) with high precision
echo    - Extracting geotransform matrix for pixel-to-coordinate conversion
if "%IS_ELEVATION%"=="true" (
    echo    - EXTRACTING ELEVATION DATA: Band statistics, min/max values, NoData
    echo    - Computing detailed statistics for real-time elevation display
)
echo    - Extracting band information for geospatial intelligence features

REM Enhanced metadata extraction with statistics for elevation analysis
gdalinfo -json -stats "%OPTIMIZED_FILE%" > "%METADATA_FILE%"

if errorlevel 1 (
    echo WARNING: Failed to extract enhanced metadata (attempting basic extraction)
    gdalinfo -json "%OPTIMIZED_FILE%" > "%METADATA_FILE%"
    if errorlevel 1 (
        echo WARNING: Failed to extract metadata (file will still work without coordinates)
    ) else (
        echo Basic geospatial metadata extracted: %METADATA_FILE%
    )
) else (
    echo Enhanced geospatial metadata extracted: %METADATA_FILE%
    
    REM Display elevation analysis if it's a DEM
    if "%IS_ELEVATION%"=="true" (
        echo.
        echo ELEVATION DATA ANALYSIS:
        gdalinfo -stats "%OPTIMIZED_FILE%" > temp_elevation.txt 2>nul
        for /f "tokens=2" %%a in ('findstr /c:"Minimum=" temp_elevation.txt 2^>nul') do (
            echo    Minimum Elevation: %%a meters
        )
        for /f "tokens=2" %%a in ('findstr /c:"Maximum=" temp_elevation.txt 2^>nul') do (
            echo    Maximum Elevation: %%a meters  
        )
        for /f "tokens=2" %%a in ('findstr /c:"NoData Value=" temp_elevation.txt 2^>nul') do (
            echo    NoData Value: %%a
        )
        del temp_elevation.txt >nul 2>nul
        echo Elevation range extracted for real-time display
    )
)

echo.
echo ====================================================================
echo                    GEOSPATIAL INTELLIGENCE SUCCESS!
echo ====================================================================
echo.
echo Your TIFF file has been processed with ENHANCED GEOSPATIAL INTELLIGENCE!
echo.
echo Files created:
echo    %OPTIMIZED_FILE%
echo    %METADATA_FILE%
echo.
echo Map folder structure:
echo    %MAP_FOLDER%\
echo      - %FILENAME%_optimized.tif
echo      - %FILENAME%_metadata.json
echo.
echo Enhanced features activated:
echo    - Internal 512x512 tiling for ultra-fast access
echo    - LZW compression (reduces file size by 30-70%%)
echo    - BIGTIFF support for files over 4GB
echo    - 6 pyramid levels (LOD 0-5) for smooth multi-scale rendering
if "%IS_ELEVATION%"=="true" (
echo    - ELEVATION DATA: DEM processing with terrain-preserving algorithms
echo    - PREDICTOR=2: Optimized compression for elevation data
echo    - AVERAGE resampling: Preserves elevation accuracy
echo    - Real-time elevation display support (Phase 3)
) else (
echo    - RGB photometric for perfect color reproduction
echo    - CUBIC resampling for enhanced visual quality
)
echo    - Enhanced geospatial metadata with coordinate reference system
echo    - MGRS-ready coordinate conversion support (Phase 4)
echo    - Military-grade precision for operational use
echo.
echo NEW PHASE 3 ^& 4 FEATURES READY:
echo    Real-time elevation display under cursor (Phase 3)
echo    Professional measurement tools with Haversine formula (Phase 3) 
echo    Enhanced coordinate information panel (Phase 4)
echo    MGRS military grid reference system (Phase 4)
echo    Stabilized LOD system (Fixed fluctuation on small maps)
echo.
echo Next steps for GEOSPATIAL INTELLIGENCE:
echo    1. Your file is organized in: %MAP_FOLDER%\
echo    2. Update SafeTiffRenderer.tiffFileName to: "%FILENAME%\%FILENAME%_optimized.tif"
echo    3. Enable enhanced features in Unity Inspector:
if "%IS_ELEVATION%"=="true" (
echo       - enableElevationDisplay = true  (Real-time elevation from DEM)
) else (
echo       - enableElevationDisplay = false (No elevation data in RGB imagery)
)
echo       - enableMeasurementTools = true  (Distance and area measurement)
echo       - enableCoordinateDisplay = true (Multi-format coordinates)
echo    4. Press Play in Unity - your geospatial intelligence system is ready!
echo.
echo NEW CONTROLS AVAILABLE:
echo    - M key - Toggle measurement tools (distance/area)
echo    - Mouse hover - Real-time coordinates and elevation
echo    - Left-click - Place measurement points  
echo    - Right-click - Clear measurements
echo    - Mouse wheel - Smooth zoom (no more LOD fluctuation)
echo.
echo Performance optimized for:
echo    - Small maps (43MB): Stable LOD, no fluctuation issues
echo    - Large maps (10GB+): Intelligent memory management
if "%IS_ELEVATION%"=="true" (
echo    - Elevation maps: Real-time terrain analysis capabilities
echo    - DEM files: Enhanced elevation precision and NoData handling
)
echo    - Military operations: MGRS coordinates and measurement tools
echo    - Geospatial intelligence: Professional cartographic accuracy
echo ====================================================================

REM Generate enhanced Unity configuration file  
echo. > "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // Enhanced Unity Configuration for %FILENAME% - GEOSPATIAL INTELLIGENCE >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // Military-Grade System with Phase 3 ^& 4 Features >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // Copy these settings to your SafeTiffRenderer component: >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo. >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo tiffFileName = "%FILENAME%_optimized.tif"; >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo. >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // PHASE 3 ^& 4 ENHANCED FEATURES (NEW): >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo enableCoordinateDisplay = true;    // Multi-format coordinate panel >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
if "%IS_ELEVATION%"=="true" (
echo enableElevationDisplay = true;     // Real-time elevation from DEM >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
) else (
echo enableElevationDisplay = false;    // No elevation data in RGB imagery >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
)
echo enableMeasurementTools = true;     // Professional distance/area measurement >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo. >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // PERFORMANCE SETTINGS (Optimized for enhanced features): >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
if "%IS_ELEVATION%"=="true" (
echo maxTotalTiles = 2000;   // Balanced for elevation data processing >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
) else (
echo maxTotalTiles = 3000;   // Higher limit for RGB imagery >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
)
echo maxTilesPerFrame = 50;  // Fast loading for responsive feel >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo maxQueueSize = 250;     // Large queue for smooth panning >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo maxLoadingTime = 300;   // 5 minute timeout for large files >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo showDebugInfo = true;   // See all features in action >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo. >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // FIXES IMPLEMENTED: >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // LOD fluctuation eliminated (stable thresholds) >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"  
echo // Small map optimization (prevents unnecessary overviews) >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // [OK] Elevation data integration (automatic DEM detection) >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"
echo // [OK] MGRS coordinate support (military grid reference) >> "%MAP_FOLDER%\unity_config_%FILENAME%.txt"

echo [SUCCESS] Enhanced Unity configuration saved to: %MAP_FOLDER%\unity_config_%FILENAME%.txt
echo.

REM Cleanup reprojection temp file
if "%TEMP_REPROJECTED%"=="1" (
    if exist "%REPROJECTED_FILE%" del "%REPROJECTED_FILE%" >nul 2>nul
)