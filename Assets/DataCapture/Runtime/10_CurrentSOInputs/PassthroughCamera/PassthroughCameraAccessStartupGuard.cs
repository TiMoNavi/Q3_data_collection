using System.Collections;
using Meta.XR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace DataCapture.CameraCapture.PassthroughCamera
{
    public sealed class PassthroughCameraAccessStartupGuard : MonoBehaviour
    {
        private const string GuardObjectName = "[Runtime] Passthrough Camera Access Startup Guard";
        private static bool installed;

        [SerializeField] private float sceneLoadDelaySeconds = 10f;
        [SerializeField] private float xrReadyTimeoutSeconds = 45f;
        [SerializeField] private float postXrReadyDelaySeconds = 3f;
        [SerializeField] private float retryIntervalSeconds = 2f;
        [SerializeField] private int maxEnableAttempts = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (installed)
            {
                return;
            }

            installed = true;
            var guardObject = new GameObject(GuardObjectName);
            DontDestroyOnLoad(guardObject);
            guardObject.AddComponent<PassthroughCameraAccessStartupGuard>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(EnableSceneCameraAccessAfterDelay(SceneManager.GetActiveScene()));
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(EnableSceneCameraAccessAfterDelay(scene));
        }

        private IEnumerator EnableSceneCameraAccessAfterDelay(Scene scene)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, sceneLoadDelaySeconds));

            float waitStartedAt = Time.realtimeSinceStartup;
            while (!XRSettings.isDeviceActive &&
                   Time.realtimeSinceStartup - waitStartedAt < Mathf.Max(0f, xrReadyTimeoutSeconds))
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, retryIntervalSeconds));
            }

            if (!XRSettings.isDeviceActive)
            {
                Debug.LogWarning(
                    "Leaving PassthroughCameraAccess disabled because the XR device did not become active before the startup timeout. scene=" +
                    scene.name,
                    this);
                yield break;
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, postXrReadyDelaySeconds));

            for (int attempt = 1; attempt <= Mathf.Max(1, maxEnableAttempts); attempt++)
            {
                if (!XRSettings.isDeviceActive)
                {
                    Debug.Log(
                        "Waiting to enable PassthroughCameraAccess because XR device is no longer active. attempt=" +
                        attempt + " scene=" + scene.name,
                        this);
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, retryIntervalSeconds));
                    continue;
                }

                var cameras = UnityEngine.Object.FindObjectsByType<PassthroughCameraAccess>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                bool foundCamera = false;
                bool anyPlaying = false;

                foreach (var cameraAccess in cameras)
                {
                    if (cameraAccess == null || !cameraAccess.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    foundCamera = true;
                    if (!cameraAccess.enabled)
                    {
                        cameraAccess.enabled = true;
                    }

                    anyPlaying |= cameraAccess.IsPlaying;
                }

                if (!foundCamera)
                {
                    yield break;
                }

                if (anyPlaying)
                {
                    Debug.Log(
                        "PassthroughCameraAccess started after scene load. attempt=" + attempt +
                        " scene=" + scene.name,
                        this);
                    yield break;
                }

                Debug.Log(
                    "Waiting for PassthroughCameraAccess to start after scene load. attempt=" + attempt +
                    " xrDeviceActive=" + XRSettings.isDeviceActive +
                    " scene=" + scene.name,
                    this);

                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, retryIntervalSeconds));
            }
        }
    }
}
