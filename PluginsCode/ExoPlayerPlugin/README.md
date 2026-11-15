# ROV ExoPlayer Plugin — Build Instructions

## Prerequisites

1. **Java JDK 8+** — Unity typically installs OpenJDK at:
   `C:\Program Files\Unity\Hub\Editor\2020.3.13f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK`

2. **Android SDK** — Unity installs at:
   `C:\Program Files\Unity\Hub\Editor\2020.3.13f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK`

## Build Steps

### Option A: Using Gradle Wrapper (Recommended)

```bash
cd PluginsCode/ExoPlayerPlugin

# Set JAVA_HOME and ANDROID_HOME if not already set
set JAVA_HOME=C:\Program Files\Unity\Hub\Editor\2020.3.13f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK
set ANDROID_HOME=C:\Program Files\Unity\Hub\Editor\2020.3.13f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK

# Generate wrapper (first time only)
gradle wrapper --gradle-version 7.6.1

# Build the AAR
gradlew assembleRelease
```

### Option B: Using Android Studio

1. Open `PluginsCode/ExoPlayerPlugin` in Android Studio
2. Build → Make Project
3. The AAR will be at `build/outputs/aar/ExoPlayerPlugin-release.aar`

## Output

The build produces:
```
build/outputs/aar/ExoPlayerPlugin-release.aar
```

This is automatically copied to `Assets/Plugins/Android/ExoPlayerPlugin.aar` by the Gradle build script.

If auto-copy fails, manually copy the AAR:
```
copy build\outputs\aar\ExoPlayerPlugin-release.aar ..\..\Assets\Plugins\Android\ExoPlayerPlugin.aar
```

## Unity Configuration

After placing the AAR in `Assets/Plugins/Android/`:

1. Select the AAR in Unity Project panel
2. In Inspector, ensure "Android" platform is checked
3. Click Apply

## Troubleshooting

- **"SDK not found"**: Set `ANDROID_HOME` or create `local.properties` with:
  ```
  sdk.dir=C:\\Program Files\\Unity\\Hub\\Editor\\2020.3.13f1\\Editor\\Data\\PlaybackEngines\\AndroidPlayer\\SDK
  ```

- **Duplicate class errors**: Ensure only ONE copy of ExoPlayer is in the project. Remove any other ExoPlayer AARs from `Assets/Plugins/Android/`.
