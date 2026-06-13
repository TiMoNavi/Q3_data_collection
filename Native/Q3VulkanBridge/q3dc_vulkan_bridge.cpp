#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"

#include <android/log.h>
#include <android/native_window.h>
#include <android/native_window_jni.h>
#include <jni.h>
#include <cstdint>
#include <cstdio>
#include <cstring>

#define Q3DC_LOG_TAG "Q3VulkanBridge"
#define Q3DC_LOGI(...) __android_log_print(ANDROID_LOG_INFO, Q3DC_LOG_TAG, __VA_ARGS__)
#define Q3DC_LOGW(...) __android_log_print(ANDROID_LOG_WARN, Q3DC_LOG_TAG, __VA_ARGS__)

namespace
{
constexpr int kProbeTextureEventId = 41001;

IUnityInterfaces* g_unity = nullptr;
IUnityGraphics* g_graphics = nullptr;
IUnityGraphicsVulkan* g_vulkan = nullptr;
UnityGfxRenderer g_renderer = kUnityGfxRendererNull;

void* g_texture = nullptr;
int g_textureWidth = 0;
int g_textureHeight = 0;
uint64_t g_renderEventCount = 0;
bool g_pluginLoaded = false;
bool g_vulkanReady = false;
bool g_lastAccessTextureOk = false;
VkImage g_lastImage = VK_NULL_HANDLE;
VkFormat g_lastFormat = VK_FORMAT_UNDEFINED;
VkExtent3D g_lastExtent = {};
ANativeWindow* g_encoderWindow = nullptr;
int g_encoderSurfaceWidth = 0;
int g_encoderSurfaceHeight = 0;
int g_encoderWindowWidth = 0;
int g_encoderWindowHeight = 0;
uint64_t g_encoderSurfaceAttachCount = 0;
char g_lastStatus[512] = "Not loaded.";
char g_lastEncoderSurfaceStatus[512] = "Encoder surface not attached.";

const char* RendererName(UnityGfxRenderer renderer)
{
    switch (renderer)
    {
        case kUnityGfxRendererVulkan:
            return "Vulkan";
        case kUnityGfxRendererOpenGLES30:
            return "OpenGLES3";
        case kUnityGfxRendererD3D11:
            return "D3D11";
        case kUnityGfxRendererD3D12:
            return "D3D12";
        case kUnityGfxRendererNull:
            return "Null";
        default:
            return "Other";
    }
}

void SetStatus(const char* status)
{
    std::snprintf(g_lastStatus, sizeof(g_lastStatus), "%s", status != nullptr ? status : "");
}

void SetEncoderSurfaceStatus(const char* status)
{
    std::snprintf(
        g_lastEncoderSurfaceStatus,
        sizeof(g_lastEncoderSurfaceStatus),
        "%s",
        status != nullptr ? status : "");
}

void ReleaseEncoderWindow()
{
    if (g_encoderWindow != nullptr)
    {
        ANativeWindow_release(g_encoderWindow);
        g_encoderWindow = nullptr;
    }

    g_encoderSurfaceWidth = 0;
    g_encoderSurfaceHeight = 0;
    g_encoderWindowWidth = 0;
    g_encoderWindowHeight = 0;
    SetEncoderSurfaceStatus("Encoder surface not attached.");
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
{
    if (eventType == kUnityGfxDeviceEventInitialize)
    {
        g_renderer = g_graphics != nullptr ? g_graphics->GetRenderer() : kUnityGfxRendererNull;
        g_vulkan = g_unity != nullptr ? g_unity->Get<IUnityGraphicsVulkan>() : nullptr;
        g_vulkanReady = g_renderer == kUnityGfxRendererVulkan && g_vulkan != nullptr;
        SetStatus(g_vulkanReady ? "Vulkan graphics device initialized." : "Graphics device initialized but Vulkan is not ready.");

        if (g_vulkanReady)
        {
            UnityVulkanPluginEventConfig config = {};
            config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
            config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
            config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission;
            g_vulkan->ConfigureEvent(kProbeTextureEventId, &config);
        }

        Q3DC_LOGI("Initialize renderer=%s vulkanReady=%d", RendererName(g_renderer), g_vulkanReady ? 1 : 0);
    }
    else if (eventType == kUnityGfxDeviceEventShutdown)
    {
        ReleaseEncoderWindow();
        g_vulkanReady = false;
        g_vulkan = nullptr;
        g_renderer = kUnityGfxRendererNull;
        SetStatus("Graphics device shutdown.");
        Q3DC_LOGI("Shutdown");
    }
}

void UNITY_INTERFACE_API OnRenderEvent(int eventId)
{
    if (eventId != kProbeTextureEventId)
    {
        return;
    }

    g_renderEventCount++;
    g_lastAccessTextureOk = false;
    g_lastImage = VK_NULL_HANDLE;
    g_lastFormat = VK_FORMAT_UNDEFINED;
    g_lastExtent = {};

    if (!g_vulkanReady || g_vulkan == nullptr)
    {
        SetStatus("Render event received but Vulkan is not ready.");
        return;
    }

    if (g_texture == nullptr)
    {
        SetStatus("Render event received with no Unity texture pointer.");
        return;
    }

    UnityVulkanImage image = {};
    bool ok = g_vulkan->AccessTexture(
        g_texture,
        UnityVulkanWholeImage,
        VK_IMAGE_LAYOUT_UNDEFINED,
        0,
        0,
        kUnityVulkanResourceAccess_ObserveOnly,
        &image);

    g_lastAccessTextureOk = ok;
    if (!ok)
    {
        SetStatus("IUnityGraphicsVulkan.AccessTexture failed.");
        Q3DC_LOGW("AccessTexture failed texture=%p size=%dx%d", g_texture, g_textureWidth, g_textureHeight);
        return;
    }

    g_lastImage = image.image;
    g_lastFormat = image.format;
    g_lastExtent = image.extent;
    std::snprintf(
        g_lastStatus,
        sizeof(g_lastStatus),
        "AccessTexture ok image=0x%llx format=%u extent=%ux%ux%u source=%dx%d.",
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(image.image)),
        static_cast<unsigned int>(image.format),
        image.extent.width,
        image.extent.height,
        image.extent.depth,
        g_textureWidth,
        g_textureHeight);
}
}

extern "C"
{
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    g_unity = unityInterfaces;
    g_graphics = g_unity != nullptr ? g_unity->Get<IUnityGraphics>() : nullptr;
    g_pluginLoaded = true;
    SetStatus("UnityPluginLoad called.");

    if (g_graphics != nullptr)
    {
        g_graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
        OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
    }

    Q3DC_LOGI("UnityPluginLoad");
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload()
{
    ReleaseEncoderWindow();

    if (g_graphics != nullptr)
    {
        g_graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
    }

    g_pluginLoaded = false;
    g_graphics = nullptr;
    g_vulkan = nullptr;
    g_unity = nullptr;
    SetStatus("UnityPluginUnload called.");
    Q3DC_LOGI("UnityPluginUnload");
}

UNITY_INTERFACE_EXPORT UnityRenderingEvent UNITY_INTERFACE_API Q3DC_GetRenderEventFunc()
{
    return OnRenderEvent;
}

UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API Q3DC_GetProbeEventId()
{
    return kProbeTextureEventId;
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API Q3DC_SetUnityTexture(void* texture, int width, int height)
{
    g_texture = texture;
    g_textureWidth = width;
    g_textureHeight = height;
}

UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API Q3DC_GetRenderer()
{
    return static_cast<int>(g_renderer);
}

UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API Q3DC_IsVulkanReady()
{
    return g_vulkanReady ? 1 : 0;
}

UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API Q3DC_GetStatusJson(char* buffer, int bufferLength)
{
    if (buffer == nullptr || bufferLength <= 0)
    {
        return 0;
    }

    int written = std::snprintf(
        buffer,
        static_cast<size_t>(bufferLength),
        "{\"pluginLoaded\":%s,\"renderer\":\"%s\",\"rendererId\":%d,\"vulkanReady\":%s,"
        "\"renderEventCount\":%llu,\"texturePtr\":\"0x%llx\",\"textureWidth\":%d,\"textureHeight\":%d,"
        "\"lastAccessTextureOk\":%s,\"vkImage\":\"0x%llx\",\"vkFormat\":%u,"
        "\"vkExtentWidth\":%u,\"vkExtentHeight\":%u,\"vkExtentDepth\":%u,"
        "\"encoderSurfaceAttached\":%s,\"encoderSurfaceAttachCount\":%llu,"
        "\"encoderSurfaceWidth\":%d,\"encoderSurfaceHeight\":%d,"
        "\"encoderWindowWidth\":%d,\"encoderWindowHeight\":%d,"
        "\"lastStatus\":\"%s\",\"lastEncoderSurfaceStatus\":\"%s\"}",
        g_pluginLoaded ? "true" : "false",
        RendererName(g_renderer),
        static_cast<int>(g_renderer),
        g_vulkanReady ? "true" : "false",
        static_cast<unsigned long long>(g_renderEventCount),
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(g_texture)),
        g_textureWidth,
        g_textureHeight,
        g_lastAccessTextureOk ? "true" : "false",
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(g_lastImage)),
        static_cast<unsigned int>(g_lastFormat),
        g_lastExtent.width,
        g_lastExtent.height,
        g_lastExtent.depth,
        g_encoderWindow != nullptr ? "true" : "false",
        static_cast<unsigned long long>(g_encoderSurfaceAttachCount),
        g_encoderSurfaceWidth,
        g_encoderSurfaceHeight,
        g_encoderWindowWidth,
        g_encoderWindowHeight,
        g_lastStatus,
        g_lastEncoderSurfaceStatus);

    if (written < 0)
    {
        buffer[0] = '\0';
        return 0;
    }

    if (written >= bufferLength)
    {
        buffer[bufferLength - 1] = '\0';
        return bufferLength - 1;
    }

    return written;
}
}

extern "C" JNIEXPORT jint JNICALL
Java_com_q3datacapture_mediacodec_Q3SurfaceVideoEncoder_nativeAttachEncoderSurface(
    JNIEnv* env,
    jclass,
    jobject surface,
    jint width,
    jint height)
{
    if (env == nullptr || surface == nullptr)
    {
        SetEncoderSurfaceStatus("nativeAttachEncoderSurface failed: null surface.");
        Q3DC_LOGW("%s", g_lastEncoderSurfaceStatus);
        return 0;
    }

    ANativeWindow* window = ANativeWindow_fromSurface(env, surface);
    if (window == nullptr)
    {
        SetEncoderSurfaceStatus("nativeAttachEncoderSurface failed: ANativeWindow_fromSurface returned null.");
        Q3DC_LOGW("%s", g_lastEncoderSurfaceStatus);
        return 0;
    }

    ReleaseEncoderWindow();
    g_encoderWindow = window;
    g_encoderSurfaceWidth = static_cast<int>(width);
    g_encoderSurfaceHeight = static_cast<int>(height);
    g_encoderWindowWidth = ANativeWindow_getWidth(g_encoderWindow);
    g_encoderWindowHeight = ANativeWindow_getHeight(g_encoderWindow);
    g_encoderSurfaceAttachCount++;

    std::snprintf(
        g_lastEncoderSurfaceStatus,
        sizeof(g_lastEncoderSurfaceStatus),
        "Encoder Surface attached nativeWindow=%p requested=%dx%d window=%dx%d.",
        g_encoderWindow,
        g_encoderSurfaceWidth,
        g_encoderSurfaceHeight,
        g_encoderWindowWidth,
        g_encoderWindowHeight);
    Q3DC_LOGI("%s", g_lastEncoderSurfaceStatus);
    return 1;
}

extern "C" JNIEXPORT void JNICALL
Java_com_q3datacapture_mediacodec_Q3SurfaceVideoEncoder_nativeDetachEncoderSurface(JNIEnv*, jclass)
{
    ReleaseEncoderWindow();
    Q3DC_LOGI("Encoder Surface detached.");
}

extern "C" JNIEXPORT jint JNICALL
Java_com_q3datacapture_mediacodec_Q3SurfaceVideoEncoder_nativeHasEncoderSurface(JNIEnv*, jclass)
{
    return g_encoderWindow != nullptr ? 1 : 0;
}
