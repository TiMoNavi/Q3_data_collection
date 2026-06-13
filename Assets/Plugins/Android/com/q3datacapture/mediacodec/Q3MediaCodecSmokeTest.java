package com.q3datacapture.mediacodec;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLExt;
import android.opengl.EGLSurface;
import android.opengl.GLES20;
import android.view.Surface;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;

public final class Q3MediaCodecSmokeTest {
    private Q3MediaCodecSmokeTest() {
    }

    public static String runSolidColorH264Test(
            int width,
            int height,
            int bitrate,
            int frameRate,
            int frameCount) {
        MediaCodec codec = null;
        Surface inputSurface = null;
        EglSurfaceRenderer renderer = null;
        EncoderStats stats = new EncoderStats();
        stats.width = width;
        stats.height = height;
        stats.frameRate = frameRate;
        stats.targetFrameCount = frameCount;
        stats.codec = "video/avc";

        long startNs = System.nanoTime();
        try {
            MediaFormat format = MediaFormat.createVideoFormat("video/avc", width, height);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
            format.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
            format.setInteger(MediaFormat.KEY_FRAME_RATE, frameRate);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);

            codec = MediaCodec.createEncoderByType("video/avc");
            stats.encoderName = codec.getName();
            codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            inputSurface = codec.createInputSurface();
            codec.start();

            renderer = new EglSurfaceRenderer(inputSurface, width, height);
            MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();
            long frameDurationNs = 1000000000L / Math.max(1, frameRate);

            for (int i = 0; i < frameCount; i++) {
                renderer.drawFrame(i, frameCount);
                EGLExt.eglPresentationTimeANDROID(renderer.display, renderer.surface, i * frameDurationNs);
                if (!EGL14.eglSwapBuffers(renderer.display, renderer.surface)) {
                    throw new RuntimeException("eglSwapBuffers failed.");
                }
                drain(codec, bufferInfo, false, stats);
            }

            codec.signalEndOfInputStream();
            drain(codec, bufferInfo, true, stats);
            stats.success = stats.outputBytes > 0;
            stats.elapsedMs = (System.nanoTime() - startNs) / 1000000L;
        } catch (Throwable t) {
            stats.success = false;
            stats.error = t.getClass().getSimpleName() + ": " + t.getMessage();
            stats.elapsedMs = (System.nanoTime() - startNs) / 1000000L;
        } finally {
            if (renderer != null) {
                renderer.release();
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
            }
            if (inputSurface != null) {
                inputSurface.release();
            }
        }

        return stats.toJson();
    }

    private static void drain(
            MediaCodec codec,
            MediaCodec.BufferInfo bufferInfo,
            boolean waitForEndOfStream,
            EncoderStats stats) {
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
                stats.outputFormat = codec.getOutputFormat().toString();
                continue;
            }

            if (status < 0) {
                continue;
            }

            ByteBuffer encodedData = codec.getOutputBuffer(status);
            if (encodedData != null && bufferInfo.size > 0) {
                encodedData.position(bufferInfo.offset);
                encodedData.limit(bufferInfo.offset + bufferInfo.size);
                stats.outputBytes += bufferInfo.size;
                stats.outputBufferCount++;

                if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) {
                    stats.codecConfigCount++;
                } else {
                    stats.encodedFrameCount++;
                }

                if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0) {
                    stats.keyFrameCount++;
                }
            }

            boolean endOfStream = (bufferInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            codec.releaseOutputBuffer(status, false);
            if (endOfStream) {
                break;
            }
        }
    }

    private static final class EglSurfaceRenderer {
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

        EglSurfaceRenderer(Surface inputSurface, int width, int height) {
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

        void drawFrame(int frameIndex, int totalFrames) {
            float t = totalFrames <= 1 ? 0.0f : frameIndex / (float) (totalFrames - 1);
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

    private static final class EncoderStats {
        boolean success;
        String error = "";
        String encoderName = "";
        String codec = "";
        String outputFormat = "";
        int width;
        int height;
        int frameRate;
        int targetFrameCount;
        int encodedFrameCount;
        int outputBufferCount;
        int codecConfigCount;
        int keyFrameCount;
        long outputBytes;
        long elapsedMs;

        String toJson() {
            return "{"
                    + "\"success\":" + success
                    + ",\"error\":\"" + escape(error) + "\""
                    + ",\"encoderName\":\"" + escape(encoderName) + "\""
                    + ",\"codec\":\"" + escape(codec) + "\""
                    + ",\"outputFormat\":\"" + escape(outputFormat) + "\""
                    + ",\"width\":" + width
                    + ",\"height\":" + height
                    + ",\"frameRate\":" + frameRate
                    + ",\"targetFrameCount\":" + targetFrameCount
                    + ",\"encodedFrameCount\":" + encodedFrameCount
                    + ",\"outputBufferCount\":" + outputBufferCount
                    + ",\"codecConfigCount\":" + codecConfigCount
                    + ",\"keyFrameCount\":" + keyFrameCount
                    + ",\"outputBytes\":" + outputBytes
                    + ",\"elapsedMs\":" + elapsedMs
                    + "}";
        }

        private static String escape(String value) {
            if (value == null) {
                return "";
            }
            return value.replace("\\", "\\\\").replace("\"", "\\\"");
        }
    }
}
