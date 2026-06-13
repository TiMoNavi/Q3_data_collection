package com.q3datacapture.mediacodec;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLExt;
import android.opengl.EGLSurface;
import android.opengl.GLES20;
import android.os.Bundle;
import android.util.Log;
import android.view.Surface;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;

public final class Q3SurfaceVideoEncoder {
    private static final String TAG = "Q3SurfaceVideoEncoder";
    private static final boolean NATIVE_BRIDGE_AVAILABLE = loadNativeBridge();

    private MediaCodec codec;
    private MediaMuxer muxer;
    private Surface inputSurface;
    private EglPatternRenderer renderer;
    private MediaCodec.BufferInfo bufferInfo;
    private byte[] codecConfig = new byte[0];
    private String mime = "";
    private String codecName = "";
    private String muxerOutputPath = "";
    private String lastStatus = "Not started.";
    private int width;
    private int height;
    private int frameRate;
    private int muxerTrackIndex = -1;
    private long frameIndex;
    private long muxedSampleCount;
    private long muxedBytes;
    private boolean muxerStarted;
    private boolean started;

    private static boolean loadNativeBridge() {
        try {
            System.loadLibrary("q3dc_vulkan_bridge");
            Log.i(TAG, "Loaded native Vulkan bridge.");
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "Native Vulkan bridge is not available: " +
                    t.getClass().getSimpleName() + ": " + t.getMessage());
            return false;
        }
    }

    private static native int nativeAttachEncoderSurface(Surface surface, int width, int height);
    private static native void nativeDetachEncoderSurface();
    private static native int nativeHasEncoderSurface();

    public synchronized boolean start(
            String requestedCodec,
            int requestedWidth,
            int requestedHeight,
            int requestedFrameRate,
            int bitrateKbps,
            float keyFrameIntervalSeconds) {
        return startInternal(
                requestedCodec,
                requestedWidth,
                requestedHeight,
                requestedFrameRate,
                bitrateKbps,
                keyFrameIntervalSeconds,
                null);
    }

    public synchronized boolean startWithMp4(
            String requestedCodec,
            int requestedWidth,
            int requestedHeight,
            int requestedFrameRate,
            int bitrateKbps,
            float keyFrameIntervalSeconds,
            String outputPath) {
        return startInternal(
                requestedCodec,
                requestedWidth,
                requestedHeight,
                requestedFrameRate,
                bitrateKbps,
                keyFrameIntervalSeconds,
                outputPath);
    }

    private synchronized boolean startInternal(
            String requestedCodec,
            int requestedWidth,
            int requestedHeight,
            int requestedFrameRate,
            int bitrateKbps,
            float keyFrameIntervalSeconds,
            String outputPath) {
        stop();

        mime = resolveMime(requestedCodec);
        width = makeEven(Math.max(16, requestedWidth));
        height = makeEven(Math.max(16, requestedHeight));
        frameRate = Math.max(1, requestedFrameRate);
        int bitrate = Math.max(128, bitrateKbps) * 1000;
        int keyFrameInterval = Math.max(1, Math.round(Math.max(0.1f, keyFrameIntervalSeconds)));

        try {
            if (outputPath != null && outputPath.trim().length() > 0) {
                File outputFile = new File(outputPath);
                File parent = outputFile.getParentFile();
                if (parent != null && !parent.exists() && !parent.mkdirs()) {
                    throw new RuntimeException("Failed to create muxer output directory: " + parent);
                }

                muxer = new MediaMuxer(
                        outputFile.getAbsolutePath(),
                        MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
                muxerOutputPath = outputFile.getAbsolutePath();
            }

            MediaFormat format = MediaFormat.createVideoFormat(mime, width, height);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
            format.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
            format.setInteger(MediaFormat.KEY_FRAME_RATE, frameRate);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, keyFrameInterval);

            codec = MediaCodec.createEncoderByType(mime);
            codecName = codec.getName();
            codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            inputSurface = codec.createInputSurface();
            int nativeSurfaceAttached = 0;
            if (NATIVE_BRIDGE_AVAILABLE) {
                nativeSurfaceAttached = nativeAttachEncoderSurface(inputSurface, width, height);
            }
            codec.start();

            renderer = new EglPatternRenderer(inputSurface, width, height);
            bufferInfo = new MediaCodec.BufferInfo();
            frameIndex = 0;
            codecConfig = new byte[0];
            muxerTrackIndex = -1;
            muxedSampleCount = 0;
            muxedBytes = 0;
            muxerStarted = false;
            started = true;
            lastStatus = "Started " + codecName + " " + mime + " " + width + "x" + height + "@" + frameRate +
                    (muxer != null ? " muxer=" + muxerOutputPath : "") +
                    " nativeSurfaceAttached=" + nativeSurfaceAttached + ".";
            Log.i(TAG, lastStatus);
            return true;
        } catch (Throwable t) {
            lastStatus = "Start failed: " + t.getClass().getSimpleName() + ": " + t.getMessage();
            Log.e(TAG, lastStatus, t);
            stop();
            return false;
        }
    }

    public synchronized byte[] encodePatternFrame(long presentationTimeUs, boolean forceKeyFrame) {
        if (!started || codec == null || renderer == null) {
            lastStatus = "Encoder is not started.";
            return new byte[0];
        }

        try {
            if (forceKeyFrame) {
                Bundle params = new Bundle();
                params.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0);
                codec.setParameters(params);
            }

            renderer.drawFrame(frameIndex);
            EGLExt.eglPresentationTimeANDROID(
                    renderer.display,
                    renderer.surface,
                    Math.max(0L, presentationTimeUs) * 1000L);
            if (!EGL14.eglSwapBuffers(renderer.display, renderer.surface)) {
                lastStatus = "eglSwapBuffers failed.";
                return new byte[0];
            }

            frameIndex++;
            byte[] output = drain(false);
            lastStatus = "Encoded pattern frame bytes=" + output.length + ".";
            if (output.length > 0 || frameIndex <= 3 || frameIndex % 30 == 0) {
                Log.i(TAG, lastStatus + " frameIndex=" + frameIndex +
                        " muxedSamples=" + muxedSampleCount +
                        " muxedBytes=" + muxedBytes + ".");
            }
            return output;
        } catch (Throwable t) {
            lastStatus = "Encode failed: " + t.getClass().getSimpleName() + ": " + t.getMessage();
            Log.e(TAG, lastStatus, t);
            return new byte[0];
        }
    }

    public synchronized byte[] encodeUnityTextureFrame(
            long unityNativeTexturePtr,
            int textureWidth,
            int textureHeight,
            long presentationTimeUs,
            boolean forceKeyFrame) {
        if (!started || codec == null || renderer == null) {
            lastStatus = "Encoder is not started.";
            return new byte[0];
        }

        if (unityNativeTexturePtr == 0L) {
            lastStatus = "Unity texture bridge blocked: native texture pointer is zero.";
            return new byte[0];
        }

        lastStatus = "Unity texture bridge is not implemented yet. graphics texture ptr=0x" +
                Long.toHexString(unityNativeTexturePtr) +
                " size=" + textureWidth + "x" + textureHeight +
                " ptsUs=" + presentationTimeUs +
                " nativeSurfaceAttached=" + (NATIVE_BRIDGE_AVAILABLE ? nativeHasEncoderSurface() : 0) + ".";
        Log.w(TAG, lastStatus);
        return new byte[0];
    }

    public synchronized void stop() {
        if (codec != null) {
            try {
                if (started) {
                    codec.signalEndOfInputStream();
                    byte[] eosOutput = drain(true);
                    lastStatus = "Stopped. eosBytes=" + eosOutput.length +
                            " muxedSamples=" + muxedSampleCount +
                            " muxedBytes=" + muxedBytes + ".";
                    Log.i(TAG, lastStatus);
                }
            } catch (Throwable t) {
                lastStatus = "Stop drain failed: " + t.getClass().getSimpleName() + ": " + t.getMessage();
                Log.e(TAG, lastStatus, t);
            }
        }

        if (renderer != null) {
            try {
                renderer.release();
            } catch (Throwable ignored) {
            }
            renderer = null;
        }

        if (NATIVE_BRIDGE_AVAILABLE) {
            try {
                nativeDetachEncoderSurface();
            } catch (Throwable ignored) {
            }
        }

        if (codec != null) {
            try {
                codec.stop();
            } catch (Throwable ignored) {
            }
            try {
                codec.release();
            } catch (Throwable ignored) {
            }
            codec = null;
        }

        releaseMuxer();

        if (inputSurface != null) {
            try {
                inputSurface.release();
            } catch (Throwable ignored) {
            }
            inputSurface = null;
        }

        started = false;
    }

    public synchronized boolean isStarted() {
        return started;
    }

    public synchronized String getLastStatus() {
        return lastStatus;
    }

    public synchronized String getCodecName() {
        return codecName;
    }

    public synchronized String getMuxerOutputPath() {
        return muxerOutputPath;
    }

    public synchronized long getMuxedSampleCount() {
        return muxedSampleCount;
    }

    public synchronized long getMuxedBytes() {
        return muxedBytes;
    }

    private byte[] drain(boolean waitForEndOfStream) {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        int idleCount = 0;

        while (true) {
            int status = codec.dequeueOutputBuffer(bufferInfo, 10000);
            if (status == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!waitForEndOfStream || idleCount++ > 200) {
                    break;
                }
                continue;
            }

            if (status == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                startMuxerIfNeeded(codec.getOutputFormat());
                continue;
            }

            if (status < 0) {
                continue;
            }

            ByteBuffer encodedData = codec.getOutputBuffer(status);
            boolean keyFrame = (bufferInfo.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0;
            boolean config = (bufferInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0;

            if (encodedData != null && bufferInfo.size > 0) {
                writeMuxerSampleIfNeeded(encodedData, config);

                byte[] bytes = new byte[bufferInfo.size];
                encodedData.position(bufferInfo.offset);
                encodedData.limit(bufferInfo.offset + bufferInfo.size);
                encodedData.get(bytes);

                if (config) {
                    codecConfig = bytes;
                } else {
                    if (keyFrame && codecConfig.length > 0) {
                        output.write(codecConfig, 0, codecConfig.length);
                    }
                    output.write(bytes, 0, bytes.length);
                }
            }

            boolean eos = (bufferInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            codec.releaseOutputBuffer(status, false);
            if (eos) {
                break;
            }
        }

        return output.toByteArray();
    }

    private void startMuxerIfNeeded(MediaFormat outputFormat) {
        if (muxer == null || muxerStarted) {
            return;
        }

        muxerTrackIndex = muxer.addTrack(outputFormat);
        muxer.start();
        muxerStarted = true;
        Log.i(TAG, "Muxer started track=" + muxerTrackIndex + " format=" + outputFormat + ".");
    }

    private void writeMuxerSampleIfNeeded(ByteBuffer encodedData, boolean config) {
        if (muxer == null || !muxerStarted || muxerTrackIndex < 0 || config) {
            return;
        }

        ByteBuffer sampleData = encodedData.duplicate();
        sampleData.position(bufferInfo.offset);
        sampleData.limit(bufferInfo.offset + bufferInfo.size);
        MediaCodec.BufferInfo sampleInfo = new MediaCodec.BufferInfo();
        sampleInfo.set(
                0,
                bufferInfo.size,
                bufferInfo.presentationTimeUs,
                bufferInfo.flags);
        muxer.writeSampleData(muxerTrackIndex, sampleData.slice(), sampleInfo);
        muxedSampleCount++;
        muxedBytes += bufferInfo.size;
        if (muxedSampleCount <= 3 || muxedSampleCount % 30 == 0) {
            Log.i(TAG, "Muxed sample count=" + muxedSampleCount +
                    " size=" + bufferInfo.size +
                    " ptsUs=" + bufferInfo.presentationTimeUs +
                    " flags=" + bufferInfo.flags + ".");
        }
    }

    private void releaseMuxer() {
        if (muxer == null) {
            muxerOutputPath = "";
            return;
        }

        try {
            if (muxerStarted) {
                muxer.stop();
            }
        } catch (Throwable ignored) {
        }

        try {
            muxer.release();
        } catch (Throwable ignored) {
        }

        muxer = null;
        muxerStarted = false;
        muxerTrackIndex = -1;
    }

    private static String resolveMime(String requestedCodec) {
        String codec = requestedCodec == null ? "" : requestedCodec.trim().toUpperCase();
        if ("H265".equals(codec) || "HEVC".equals(codec) || "ANDROIDMEDIACODECH265".equals(codec)) {
            return "video/hevc";
        }
        return "video/avc";
    }

    private static int makeEven(int value) {
        return value % 2 == 0 ? value : value - 1;
    }

    private static final class EglPatternRenderer {
        private static final int EGL_RECORDABLE_ANDROID = 0x3142;
        private static final float[] FULLSCREEN_QUAD = new float[]{
                -1.0f, -1.0f,
                1.0f, -1.0f,
                -1.0f, 1.0f,
                1.0f, 1.0f
        };

        private final EGLDisplay display;
        private final EGLContext context;
        private final EGLSurface surface;
        private final int program;
        private final int colorLocation;
        private final FloatBuffer vertexBuffer;
        private final int width;
        private final int height;

        EglPatternRenderer(Surface inputSurface, int width, int height) {
            this.width = width;
            this.height = height;
            display = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);
            if (display == EGL14.EGL_NO_DISPLAY) {
                throw new RuntimeException("eglGetDisplay failed.");
            }

            int[] version = new int[2];
            if (!EGL14.eglInitialize(display, version, 0, version, 1)) {
                throw new RuntimeException("eglInitialize failed.");
            }

            int[] configAttribs = {
                    EGL14.EGL_RED_SIZE, 8,
                    EGL14.EGL_GREEN_SIZE, 8,
                    EGL14.EGL_BLUE_SIZE, 8,
                    EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
                    EGL14.EGL_SURFACE_TYPE, EGL14.EGL_WINDOW_BIT,
                    EGL_RECORDABLE_ANDROID, 1,
                    EGL14.EGL_NONE
            };
            EGLConfig[] configs = new EGLConfig[1];
            int[] numConfigs = new int[1];
            if (!EGL14.eglChooseConfig(display, configAttribs, 0, configs, 0, configs.length, numConfigs, 0)) {
                throw new RuntimeException("eglChooseConfig failed.");
            }

            int[] contextAttribs = {
                    EGL14.EGL_CONTEXT_CLIENT_VERSION, 2,
                    EGL14.EGL_NONE
            };
            context = EGL14.eglCreateContext(display, configs[0], EGL14.EGL_NO_CONTEXT, contextAttribs, 0);
            if (context == EGL14.EGL_NO_CONTEXT) {
                throw new RuntimeException("eglCreateContext failed.");
            }

            int[] surfaceAttribs = {EGL14.EGL_NONE};
            surface = EGL14.eglCreateWindowSurface(display, configs[0], inputSurface, surfaceAttribs, 0);
            if (surface == EGL14.EGL_NO_SURFACE) {
                throw new RuntimeException("eglCreateWindowSurface failed.");
            }

            if (!EGL14.eglMakeCurrent(display, surface, surface, context)) {
                throw new RuntimeException("eglMakeCurrent failed.");
            }

            vertexBuffer = ByteBuffer
                    .allocateDirect(FULLSCREEN_QUAD.length * 4)
                    .order(ByteOrder.nativeOrder())
                    .asFloatBuffer();
            vertexBuffer.put(FULLSCREEN_QUAD).position(0);

            program = createProgram(
                    "attribute vec2 aPosition;\n" +
                            "void main() {\n" +
                            "  gl_Position = vec4(aPosition, 0.0, 1.0);\n" +
                            "}\n",
                    "precision mediump float;\n" +
                            "uniform vec4 uColor;\n" +
                            "void main() {\n" +
                            "  gl_FragColor = uColor;\n" +
                            "}\n");
            colorLocation = GLES20.glGetUniformLocation(program, "uColor");
        }

        void drawFrame(long frameIndex) {
            if (!EGL14.eglMakeCurrent(display, surface, surface, context)) {
                throw new RuntimeException("eglMakeCurrent failed before draw.");
            }

            float t = (frameIndex % 120) / 119.0f;
            GLES20.glViewport(0, 0, width, height);
            GLES20.glUseProgram(program);
            GLES20.glUniform4f(colorLocation, t, 0.25f, 1.0f - t, 1.0f);

            int positionLocation = GLES20.glGetAttribLocation(program, "aPosition");
            GLES20.glEnableVertexAttribArray(positionLocation);
            GLES20.glVertexAttribPointer(positionLocation, 2, GLES20.GL_FLOAT, false, 0, vertexBuffer);
            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4);
            GLES20.glDisableVertexAttribArray(positionLocation);
            GLES20.glFinish();
        }

        void release() {
            GLES20.glDeleteProgram(program);
            EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT);
            EGL14.eglDestroySurface(display, surface);
            EGL14.eglDestroyContext(display, context);
            EGL14.eglReleaseThread();
            EGL14.eglTerminate(display);
        }

        private static int createProgram(String vertexSource, String fragmentSource) {
            int vertexShader = compileShader(GLES20.GL_VERTEX_SHADER, vertexSource);
            int fragmentShader = compileShader(GLES20.GL_FRAGMENT_SHADER, fragmentSource);
            int program = GLES20.glCreateProgram();
            GLES20.glAttachShader(program, vertexShader);
            GLES20.glAttachShader(program, fragmentShader);
            GLES20.glLinkProgram(program);

            int[] linkStatus = new int[1];
            GLES20.glGetProgramiv(program, GLES20.GL_LINK_STATUS, linkStatus, 0);
            if (linkStatus[0] == 0) {
                String log = GLES20.glGetProgramInfoLog(program);
                GLES20.glDeleteProgram(program);
                throw new RuntimeException("Program link failed: " + log);
            }

            GLES20.glDeleteShader(vertexShader);
            GLES20.glDeleteShader(fragmentShader);
            return program;
        }

        private static int compileShader(int type, String source) {
            int shader = GLES20.glCreateShader(type);
            GLES20.glShaderSource(shader, source);
            GLES20.glCompileShader(shader);

            int[] compileStatus = new int[1];
            GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, compileStatus, 0);
            if (compileStatus[0] == 0) {
                String log = GLES20.glGetShaderInfoLog(shader);
                GLES20.glDeleteShader(shader);
                throw new RuntimeException("Shader compile failed: " + log);
            }

            return shader;
        }
    }
}
