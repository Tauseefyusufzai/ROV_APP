package com.navyuginfo.rovnavyug;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.SurfaceTexture;
import android.net.Uri;
import android.os.Handler;
import android.os.HandlerThread;
import android.view.TextureView;
import android.view.ViewGroup;
import android.widget.FrameLayout;

import com.google.android.exoplayer2.ExoPlayer;
import com.google.android.exoplayer2.SimpleExoPlayer;
import com.google.android.exoplayer2.MediaItem;
import com.google.android.exoplayer2.Player;
import com.google.android.exoplayer2.source.MediaSource;
import com.google.android.exoplayer2.source.rtsp.RtspMediaSource;
import com.google.android.exoplayer2.source.DefaultMediaSourceFactory;
import com.google.android.exoplayer2.upstream.DefaultDataSource;
import com.google.android.exoplayer2.upstream.DefaultHttpDataSource;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/**
 * ExoPlayer RTSP bridge for Unity.
 *
 * Architecture:
 *   ExoPlayer → TextureView (invisible, full-resolution)
 *   Background thread → getBitmap() → ByteBuffer → Unity Texture2D
 *
 * Thread model:
 *   - ExoPlayer: runs on UI thread (Android requirement)
 *   - Frame extraction: dedicated HandlerThread ("ROVFrameExtract")
 *   - Unity reads: from any thread via getFrameData()
 *
 * Buffer strategy:
 *   Double-buffered DirectByteBuffer. The extract thread writes to the
 *   "write" buffer and atomically swaps it into the "read" slot.
 *   Unity reads from the "read" buffer without blocking extraction.
 */
public class ROVExoPlayerPlugin implements TextureView.SurfaceTextureListener {

    // =========================================================================
    // Singleton
    // =========================================================================

    private static ROVExoPlayerPlugin instance;

    public static ROVExoPlayerPlugin getInstance() {
        if (instance == null) {
            instance = new ROVExoPlayerPlugin();
        }
        return instance;
    }

    // =========================================================================
    // State
    // =========================================================================

    private Activity activity;
    private ExoPlayer player;
    private TextureView textureView;
    private TextureView.SurfaceTextureListener exoListener;

    private int videoWidth  = 1280;
    private int videoHeight = 720;

    // Double-buffered frame data
    private ByteBuffer[] buffers = new ByteBuffer[2];
    private volatile int readBufferIndex = 0;
    private volatile int writeBufferIndex = 1;
    private final Object bufferLock = new Object();
    private volatile boolean newFrameAvailable = false;
    private Bitmap reusedBitmap;

    // Player state
    private volatile boolean isPlaying    = false;
    private volatile boolean isBuffering  = false;
    private volatile boolean hasError     = false;
    private volatile boolean hasInitError = false;
    private volatile int     playerState  = Player.STATE_IDLE;
    private volatile String  errorMessage = "";
    private volatile boolean surfaceReady = false;
    private final java.util.concurrent.atomic.AtomicBoolean isExtracting = new java.util.concurrent.atomic.AtomicBoolean(false);
    private volatile long cachedDriftMs = 0;

    // Frame extraction thread
    private HandlerThread extractThread;
    private Handler extractHandler;
    private volatile boolean extracting = false;

    // =========================================================================
    // Initialise
    // =========================================================================

    /**
     * Must be called from Unity on the main thread (or via runOnUiThread).
     *
     * @param act    Unity's current Activity
     * @param width  Video frame width for extraction
     * @param height Video frame height for extraction
     * @param fps    Target frame extraction rate
     */
    public void init(final Activity act, final int width, final int height, final int fps) {
        if (player != null) {
            android.util.Log.i("ROVExoPlayer", "init: Already initialized, ignoring subsequent call.");
            return;
        }
        this.activity    = act;
        this.videoWidth  = width;
        this.videoHeight = height;

        int bufSize = width * height * 4; // ARGB_8888
        buffers[0] = ByteBuffer.allocateDirect(bufSize).order(ByteOrder.nativeOrder());
        buffers[1] = ByteBuffer.allocateDirect(bufSize).order(ByteOrder.nativeOrder());
        reusedBitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    // Create invisible TextureView at full video resolution
                    textureView = new TextureView(activity);
                    textureView.setAlpha(0f);              // Invisible
                    textureView.setClickable(false);
                    textureView.setFocusable(false);
                    textureView.setFocusableInTouchMode(false);

                    FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(
                            videoWidth, videoHeight);
                    activity.addContentView(textureView, params);

                    // Configure HTTP/HTTPS DataSource with custom redirect support & user-agent
                    DefaultHttpDataSource.Factory httpDataSourceFactory = new DefaultHttpDataSource.Factory()
                            .setUserAgent("ROVExoPlayerPlugin")
                            .setAllowCrossProtocolRedirects(true);

                    // Wrap with DefaultDataSource.Factory
                    DefaultDataSource.Factory dataSourceFactory = new DefaultDataSource.Factory(
                            activity.getApplicationContext(), httpDataSourceFactory);

                    com.google.android.exoplayer2.DefaultLoadControl loadControl = 
                            new com.google.android.exoplayer2.DefaultLoadControl.Builder()
                            .setBufferDurationsMs(
                                    200,   // Min buffer duration (default 15000)
                                    400,   // Max buffer duration (default 50000)
                                    50,    // Buffer for playback (default 2500)
                                    100    // Buffer for playback after rebuffer (default 5000)
                            )
                            .setPrioritizeTimeOverSizeThresholds(true)
                            .build();

                    // Build SimpleExoPlayer with custom MediaSourceFactory and LoadControl
                    player = new SimpleExoPlayer.Builder(activity.getApplicationContext())
                            .setMediaSourceFactory(new DefaultMediaSourceFactory(dataSourceFactory))
                            .setLoadControl(loadControl)
                            .build();

                    player.setVideoTextureView(textureView);

                    // Capture ExoPlayer's internal listener and delegate to it
                    exoListener = textureView.getSurfaceTextureListener();
                    textureView.setSurfaceTextureListener(ROVExoPlayerPlugin.this);

                    player.addListener(new Player.Listener() {
                        @Override
                        public void onPlaybackStateChanged(int state) {
                            playerState = state;
                            isBuffering = (state == Player.STATE_BUFFERING);
                            isPlaying   = (state == Player.STATE_READY && player.getPlayWhenReady());
                            android.util.Log.i("ROVExoPlayer", "onPlaybackStateChanged: state=" + state 
                                    + ", playWhenReady=" + player.getPlayWhenReady() + ", isPlaying=" + isPlaying);
                        }

                        @Override
                        public void onIsPlayingChanged(boolean playing) {
                            isPlaying = playing;
                            android.util.Log.i("ROVExoPlayer", "onIsPlayingChanged: playing=" + playing);
                        }

                        @Override
                        public void onPlayerError(com.google.android.exoplayer2.PlaybackException error) {
                            hasError     = true;
                            errorMessage = error.getMessage() != null ? error.getMessage() : "Unknown playback error";
                            isPlaying    = false;
                            android.util.Log.e("ROVExoPlayer", "ExoPlayer error occurred: " + errorMessage, error);
                            
                            Throwable cause = error.getCause();
                            if (cause != null) {
                                android.util.Log.e("ROVExoPlayer", "ExoPlayer error cause: " + cause.getMessage(), cause);
                            }
                        }

                        @Override
                        public void onTimelineChanged(com.google.android.exoplayer2.Timeline timeline, int reason) {}

                        @Override
                        public void onMediaItemTransition(com.google.android.exoplayer2.MediaItem mediaItem, int reason) {}

                        @Override
                        public void onTracksChanged(com.google.android.exoplayer2.Tracks tracks) {}

                        @Override
                        public void onMediaMetadataChanged(com.google.android.exoplayer2.MediaMetadata mediaMetadata) {}

                        @Override
                        public void onPlaylistMetadataChanged(com.google.android.exoplayer2.MediaMetadata mediaMetadata) {}

                        @Override
                        public void onIsLoadingChanged(boolean isLoading) {}

                        @Override
                        public void onLoadingChanged(boolean isLoading) {}

                        @Override
                        public void onAvailableCommandsChanged(com.google.android.exoplayer2.Player.Commands availableCommands) {}

                        @Override
                        public void onTrackSelectionParametersChanged(com.google.android.exoplayer2.trackselection.TrackSelectionParameters parameters) {}

                        @Override
                        public void onPlayerStateChanged(boolean playWhenReady, int playbackState) {}

                        @Override
                        public void onPlayWhenReadyChanged(boolean playWhenReady, int reason) {}

                        @Override
                        public void onPlaybackSuppressionReasonChanged(int playbackSuppressionReason) {}

                        @Override
                        public void onRepeatModeChanged(int repeatMode) {}

                        @Override
                        public void onShuffleModeEnabledChanged(boolean shuffleModeEnabled) {}

                        @Override
                        public void onPlayerErrorChanged(com.google.android.exoplayer2.PlaybackException error) {}

                        @Override
                        public void onPositionDiscontinuity(int reason) {}

                        @Override
                        public void onPositionDiscontinuity(com.google.android.exoplayer2.Player.PositionInfo oldPosition, com.google.android.exoplayer2.Player.PositionInfo newPosition, int reason) {}

                        @Override
                        public void onPlaybackParametersChanged(com.google.android.exoplayer2.PlaybackParameters playbackParameters) {}

                        @Override
                        public void onSeekBackIncrementChanged(long seekBackIncrementMs) {}

                        @Override
                        public void onSeekForwardIncrementChanged(long seekForwardIncrementMs) {}

                        @Override
                        public void onMaxSeekToPreviousPositionChanged(long maxSeekToPreviousPositionMs) {}

                        @Override
                        public void onEvents(com.google.android.exoplayer2.Player player, com.google.android.exoplayer2.Player.Events events) {}

                        @Override
                        public void onAudioSessionIdChanged(int audioSessionId) {}

                        @Override
                        public void onAudioAttributesChanged(com.google.android.exoplayer2.audio.AudioAttributes audioAttributes) {}

                        @Override
                        public void onVolumeChanged(float volume) {}

                        @Override
                        public void onSkipSilenceEnabledChanged(boolean skipSilenceEnabled) {}

                        @Override
                        public void onDeviceInfoChanged(com.google.android.exoplayer2.DeviceInfo deviceInfo) {}

                        @Override
                        public void onDeviceVolumeChanged(int volume, boolean muted) {}

                        @Override
                        public void onVideoSizeChanged(com.google.android.exoplayer2.video.VideoSize videoSize) {}

                        @Override
                        public void onSurfaceSizeChanged(int width, int height) {}

                        @Override
                        public void onRenderedFirstFrame() {
                            android.util.Log.i("ROVExoPlayer", "onRenderedFirstFrame: First video frame rendered on surface!");
                        }

                        @Override
                        public void onCues(java.util.List<com.google.android.exoplayer2.text.Cue> cues) {}

                        @Override
                        public void onCues(com.google.android.exoplayer2.text.CueGroup cueGroup) {}

                        @Override
                        public void onMetadata(com.google.android.exoplayer2.metadata.Metadata metadata) {}
                    });
                } catch (Exception e) {
                    hasInitError = true;
                    errorMessage = "ExoPlayer init failed: " + e.getMessage();
                    android.util.Log.e("ROVExoPlayer", "ExoPlayer init failed", e);
                }
            }
        });

        // Frame extraction background thread
        extractThread = new HandlerThread("ROVFrameExtract");
        extractThread.start();
        extractHandler = new Handler(extractThread.getLooper());
    }

    // =========================================================================
    // Playback control
    // =========================================================================

    /** Start playing a stream URL (supports RTSP and HTTP/HTTPS progressive). */
    public void play(final String streamUrl) {
        hasError     = false;
        errorMessage = "";
        android.util.Log.i("ROVExoPlayer", "play: requested URL = " + streamUrl);

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (player == null) return;

                MediaItem item = MediaItem.fromUri(Uri.parse(streamUrl));
                String urlLower = streamUrl.toLowerCase();

                if (urlLower.startsWith("rtsp://")) {
                    RtspMediaSource source = new RtspMediaSource.Factory()
                            .setForceUseRtpTcp(false)        // UDP first for low latency (no queue buildup), fallback to TCP if blocked
                            .createMediaSource(item);
                    player.setMediaSource(source);
                } else {
                    // Progressive streams (like HTTP/HTTPS MP4 or direct video feeds)
                    player.setMediaItem(item);
                }

                player.prepare();
                player.setPlayWhenReady(true);
                // Slight speed-up to naturally catch up to live edge
                player.setPlaybackParameters(new com.google.android.exoplayer2.PlaybackParameters(1.02f));
            }
        });

        startFrameExtraction();
    }

    /** Stop playback and cancel frame extraction. */
    public void stop() {
        stopFrameExtraction();

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (player != null) {
                    player.stop();
                    player.clearMediaItems();
                }
                isPlaying   = false;
                isBuffering = false;
            }
        });
    }

    /** Release all resources. Call before the plugin is destroyed. */
    public void release() {
        stopFrameExtraction();

        if (extractThread != null) {
            extractThread.quitSafely();
            extractThread = null;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (player != null) {
                    player.release();
                    player = null;
                }
                if (textureView != null) {
                    ViewGroup parent = (ViewGroup) textureView.getParent();
                    if (parent != null) parent.removeView(textureView);
                    textureView = null;
                }
            }
        });

        if (reusedBitmap != null) {
            reusedBitmap.recycle();
            reusedBitmap = null;
        }

        instance = null;
    }

    // =========================================================================
    // Frame extraction
    // =========================================================================

    private void startFrameExtraction() {
        extracting = true;
        isExtracting.set(false);
    }

    private void stopFrameExtraction() {
        extracting = false;
        isExtracting.set(false);
        if (extractHandler != null) {
            extractHandler.removeCallbacksAndMessages(null);
        }
    }

    private void doExtractFrame() {
        if (textureView == null || !isPlaying || !textureView.isAvailable() || reusedBitmap == null) return;

        try {
            textureView.getBitmap(reusedBitmap);

            ByteBuffer activeWriteBuffer = buffers[writeBufferIndex];
            activeWriteBuffer.rewind();
            reusedBitmap.copyPixelsToBuffer(activeWriteBuffer);

            // Atomically swap write → read index
            synchronized (bufferLock) {
                int tmp = readBufferIndex;
                readBufferIndex = writeBufferIndex;
                writeBufferIndex = tmp;
                newFrameAvailable = true;
            }
        } catch (Exception e) {
            android.util.Log.e("ROVExoPlayer", "Error extracting frame: " + e.getMessage(), e);
        }
    }

    // =========================================================================
    // Unity-callable getters
    // =========================================================================

    /** True if a new frame is ready for Unity to consume. */
    public boolean isNewFrameAvailable() {
        return newFrameAvailable;
    }

    /**
     * Copy the latest frame into the provided byte array.
     * Returns true if a new frame was copied, false otherwise.
     *
     * @param dest Byte array of size width * height * 4
     */
    public boolean copyFrameToArray(byte[] dest) {
        if (!newFrameAvailable || dest == null) return false;

        synchronized (bufferLock) {
            if (!newFrameAvailable) return false;
            ByteBuffer activeReadBuffer = buffers[readBufferIndex];
            activeReadBuffer.rewind();
            activeReadBuffer.get(dest, 0, Math.min(dest.length, activeReadBuffer.capacity()));
            newFrameAvailable = false;
        }
        return true;
    }

    /** Width of extracted frames. */
    public int getVideoWidth()  { return videoWidth; }

    /** Height of extracted frames. */
    public int getVideoHeight() { return videoHeight; }

    /** True if ExoPlayer is actively playing. */
    public boolean getIsPlaying() { return isPlaying; }

    /** True if ExoPlayer is buffering. */
    public boolean getIsBuffering() { return isBuffering; }

    /** True if a playback error occurred. */
    public boolean getHasError() { return hasError; }

    /** True if an initialization error occurred. */
    public boolean getHasInitError() { return hasInitError; }

    /** Error message from the last playback error, or "". */
    public String getErrorMessage() { return errorMessage; }

    /** ExoPlayer state: STATE_IDLE=1, BUFFERING=2, READY=3, ENDED=4. */
    public int getPlayerState() { return playerState; }

    /** Clear the error flag (call before retrying). */
    public void clearError() {
        hasError     = false;
        errorMessage = "";
    }

    /**
     * Returns the cached drift value (ms) between buffered and current positions.
     * Safe to call from any thread (including Unity's render thread).
     * The value is updated periodically on the UI thread.
     */
    public long getBufferedDriftMs() {
        return cachedDriftMs;
    }

    /**
     * Updates the cached drift value. Must be called on the UI thread.
     */
    private void updateDriftOnUiThread() {
        if (player == null || !isPlaying) {
            cachedDriftMs = 0;
            return;
        }
        long buffered = player.getBufferedPosition();
        long current  = player.getCurrentPosition();
        if (buffered <= 0 || current <= 0) {
            cachedDriftMs = 0;
        } else {
            cachedDriftMs = buffered - current;
        }
    }

    /**
     * Seeks the player forward to discard accumulated buffer and jump to live edge.
     * Call this when getBufferedDriftMs() exceeds your latency threshold.
     */
    public void seekToLiveEdge() {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (player == null || !isPlaying) return;
                long buffered = player.getBufferedPosition();
                if (buffered > 0) {
                    player.seekTo(buffered);
                    cachedDriftMs = 0;
                    android.util.Log.i("ROVExoPlayer", "seekToLiveEdge: jumped to " + buffered + "ms");
                }
            }
        });
    }

    /**
     * Sets the playback speed dynamically.
     * Use speed > 1.0 (e.g. 1.05, 1.1) to catch up to the live edge,
     * and 1.0 to resume normal playback speed.
     */
    public void setPlaybackSpeed(final float speed) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (player == null) return;
                player.setPlaybackParameters(new com.google.android.exoplayer2.PlaybackParameters(speed));
            }
        });
    }

    public ByteBuffer getBuffer0() {
        return buffers[0];
    }

    public ByteBuffer getBuffer1() {
        return buffers[1];
    }

    public long getBuffer0Address() {
        return getBufferAddress(buffers[0]);
    }

    public long getBuffer1Address() {
        return getBufferAddress(buffers[1]);
    }

    private long getBufferAddress(ByteBuffer buffer) {
        if (buffer == null) return 0L;
        try {
            java.lang.reflect.Field field = java.nio.Buffer.class.getDeclaredField("address");
            field.setAccessible(true);
            return field.getLong(buffer);
        } catch (Exception e) {
            android.util.Log.e("ROVExoPlayer", "Failed to get buffer address via reflection: " + e.getMessage());
            return 0L;
        }
    }

    public int getReadBufferIndex() {
        return readBufferIndex;
    }

    public void clearNewFrameAvailable() {
        synchronized (bufferLock) {
            newFrameAvailable = false;
        }
    }

    // =========================================================================
    // TextureView.SurfaceTextureListener
    // =========================================================================

    @Override
    public void onSurfaceTextureAvailable(SurfaceTexture surface, int w, int h) {
        if (exoListener != null) {
            exoListener.onSurfaceTextureAvailable(surface, w, h);
        }
        android.util.Log.i("ROVExoPlayer", "onSurfaceTextureAvailable: surface ready, size=" + w + "x" + h);
        surfaceReady = true;
    }

    @Override
    public void onSurfaceTextureSizeChanged(SurfaceTexture surface, int w, int h) {
        if (exoListener != null) {
            exoListener.onSurfaceTextureSizeChanged(surface, w, h);
        }
    }

    @Override
    public boolean onSurfaceTextureDestroyed(SurfaceTexture surface) {
        boolean result = true;
        if (exoListener != null) {
            result = exoListener.onSurfaceTextureDestroyed(surface);
        }
        android.util.Log.i("ROVExoPlayer", "onSurfaceTextureDestroyed: surface destroyed");
        surfaceReady = false;
        return result;
    }

    @Override
    public void onSurfaceTextureUpdated(SurfaceTexture surface) {
        if (exoListener != null) {
            exoListener.onSurfaceTextureUpdated(surface);
        }
        
        updateDriftOnUiThread();
        
        if (extracting && isExtracting.compareAndSet(false, true)) {
            extractHandler.post(new Runnable() {
                @Override
                public void run() {
                    try {
                        doExtractFrame();
                    } finally {
                        isExtracting.set(false);
                    }
                }
            });
        }
    }
}
