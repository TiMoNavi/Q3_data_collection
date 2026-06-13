using UnityEngine;

namespace DataCapture.Synchronization
{
    [DefaultExecutionOrder(70)]
    public class VirtualLayerFrameWriter : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField] private TimeStampVariable timestampVariable;

        [Header("Source")]
        [SerializeField] private Camera sourceCamera;
        [SerializeField] private RenderTexture sourceTexture;
        [SerializeField] private bool writeOnUpdate = true;
        [SerializeField] private bool renderSourceCamera = true;
        [SerializeField] private Vector2Int fallbackResolution = new Vector2Int(1280, 960);
        [SerializeField] private RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
        [SerializeField] private int texturePoolSize = 8;

        [Header("Outputs")]
        [SerializeField] private CurrentVirtualLayerFrameSO currentFrame;

        private long nextFrameId = 1;
        private RenderTexture[] ownedTextures;
        private int nextOwnedTextureIndex;

        private void Update()
        {
            if (writeOnUpdate)
            {
                WriteFrame();
            }
        }

        private void OnDestroy()
        {
            ReleaseOwnedTexture();
        }

        public void WriteFrame(long timestampUnixMs = 0)
        {
            if (currentFrame == null || sourceCamera == null)
            {
                return;
            }

            RenderTexture texture = ResolveSourceTexture();
            if (texture == null)
            {
                currentFrame.SetFrame(null, default);
                return;
            }

            if (renderSourceCamera)
            {
                sourceCamera.enabled = false;
                sourceCamera.targetTexture = texture;
                sourceCamera.Render();
            }

            Vector2Int resolution = texture != null
                ? new Vector2Int(texture.width, texture.height)
                : new Vector2Int(Mathf.Max(1, sourceCamera.pixelWidth), Mathf.Max(1, sourceCamera.pixelHeight));

            if (timestampUnixMs <= 0)
            {
                timestampUnixMs = SynchronizationClock.GetUnixMilliseconds(timestampVariable);
            }

            var record = new VirtualLayerFrameRecord
            {
                frameId = nextFrameId++,
                timestampUnixMs = timestampUnixMs,
                resolution = resolution,
                cameraPosition = sourceCamera.transform.position,
                cameraRotation = sourceCamera.transform.rotation,
                projectionMatrix = sourceCamera.projectionMatrix,
                cameraLocalToWorldMatrix = sourceCamera.cameraToWorldMatrix,
                debugImagePath = string.Empty
            };

            currentFrame.SetFrame(texture, record);
        }

        private RenderTexture ResolveSourceTexture()
        {
            if (sourceTexture != null)
            {
                return sourceTexture;
            }

            if (sourceCamera.targetTexture != null)
            {
                return sourceCamera.targetTexture;
            }

            Vector2Int resolution = ResolveFallbackResolution();
            return GetNextOwnedTexture(resolution);
        }

        private Vector2Int ResolveFallbackResolution()
        {
            int width = sourceCamera.pixelWidth > 0 ? sourceCamera.pixelWidth : fallbackResolution.x;
            int height = sourceCamera.pixelHeight > 0 ? sourceCamera.pixelHeight : fallbackResolution.y;
            return new Vector2Int(Mathf.Max(16, width), Mathf.Max(16, height));
        }

        private void ReleaseOwnedTexture()
        {
            if (ownedTextures == null)
            {
                return;
            }

            if (sourceCamera != null && IsOwnedTexture(sourceCamera.targetTexture))
            {
                sourceCamera.targetTexture = null;
            }

            for (int i = 0; i < ownedTextures.Length; i++)
            {
                if (ownedTextures[i] == null)
                {
                    continue;
                }

                ownedTextures[i].Release();
                Destroy(ownedTextures[i]);
            }

            ownedTextures = null;
            nextOwnedTextureIndex = 0;
        }

        private RenderTexture GetNextOwnedTexture(Vector2Int resolution)
        {
            int poolSize = Mathf.Max(1, texturePoolSize);
            if (!IsPoolCompatible(poolSize, resolution))
            {
                ReleaseOwnedTexture();
                ownedTextures = new RenderTexture[poolSize];
                for (int i = 0; i < ownedTextures.Length; i++)
                {
                    ownedTextures[i] = new RenderTexture(resolution.x, resolution.y, 24, textureFormat)
                    {
                        name = "Q3DC Virtual Layer Snapshot " + i,
                        useMipMap = false,
                        autoGenerateMips = false
                    };
                    ownedTextures[i].Create();
                }
            }

            RenderTexture texture = ownedTextures[nextOwnedTextureIndex];
            nextOwnedTextureIndex = (nextOwnedTextureIndex + 1) % ownedTextures.Length;
            return texture;
        }

        private bool IsPoolCompatible(int poolSize, Vector2Int resolution)
        {
            if (ownedTextures == null || ownedTextures.Length != poolSize)
            {
                return false;
            }

            for (int i = 0; i < ownedTextures.Length; i++)
            {
                RenderTexture texture = ownedTextures[i];
                if (texture == null ||
                    texture.width != resolution.x ||
                    texture.height != resolution.y ||
                    texture.format != textureFormat)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsOwnedTexture(RenderTexture texture)
        {
            if (texture == null || ownedTextures == null)
            {
                return false;
            }

            for (int i = 0; i < ownedTextures.Length; i++)
            {
                if (ownedTextures[i] == texture)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
