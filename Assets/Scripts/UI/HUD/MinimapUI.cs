using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Runtime minimap anchored in the bottom-right corner of the HUD.
    /// Creates a top-down camera above the player and renders it into a rounded UI panel.
    /// </summary>
    public class MinimapUI : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 size = new Vector2(220f, 220f);
        [SerializeField] private Vector2 bottomRightMargin = new Vector2(20f, 20f);
        [SerializeField] private float cornerRadius = 26f;

        [Header("Camera")]
        [SerializeField] private float cameraHeight = 80f;
        [SerializeField] private float orthographicSize = 55f;
        [SerializeField] private LayerMask cullingMask = ~0;

        private const string MinimapName = "RuntimeMinimap";
        private const int RenderTextureSize = 512;

        private static bool sceneLoadedHooked;

        private Transform player;
        private Camera playerCamera;
        private Camera minimapCamera;
        private GameObject cameraObject;
        private RenderTexture renderTexture;
        private Sprite roundedSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!sceneLoadedHooked)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                sceneLoadedHooked = true;
            }

            TryCreateForActiveScene();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Game")
                TryCreateForActiveScene();
        }

        private static void TryCreateForActiveScene()
        {
            if (SceneManager.GetActiveScene().name != "Game")
                return;

            if (FindFirstObjectByType<MinimapUI>(FindObjectsInactive.Include) != null)
                return;

            Canvas canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            GameObject root = new GameObject(MinimapName, typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            root.AddComponent<MinimapUI>();
        }

        private void Awake()
        {
            BuildUI();
        }

        public void Initialize(Transform playerTransform, Camera sourceCamera = null)
        {
            player = playerTransform;
            playerCamera = sourceCamera;

            CreateMinimapCamera();
            UpdateCameraTransform();
        }

        private void LateUpdate()
        {
            if (player == null || minimapCamera == null)
                return;

            UpdateCameraTransform();
        }

        private void BuildUI()
        {
            RectTransform rootRect = (RectTransform)transform;
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot = new Vector2(1f, 0f);
            rootRect.anchoredPosition = new Vector2(-bottomRightMargin.x, bottomRightMargin.y);
            rootRect.sizeDelta = size;

            Image background = gameObject.AddComponent<Image>();
            background.color = new Color(0.035f, 0.04f, 0.045f, 0.96f);
            background.sprite = GetRoundedSprite();
            background.type = Image.Type.Sliced;

            Outline outline = gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.58f, 0.58f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);

            GameObject maskObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            maskObject.transform.SetParent(transform, false);

            RectTransform maskRect = (RectTransform)maskObject.transform;
            maskRect.anchorMin = Vector2.zero;
            maskRect.anchorMax = Vector2.one;
            maskRect.offsetMin = new Vector2(4f, 4f);
            maskRect.offsetMax = new Vector2(-4f, -4f);

            Image maskImage = maskObject.GetComponent<Image>();
            maskImage.sprite = GetRoundedSprite();
            maskImage.type = Image.Type.Sliced;
            maskImage.color = Color.white;

            Mask mask = maskObject.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject mapImageObject = new GameObject("MapImage", typeof(RectTransform), typeof(RawImage));
            mapImageObject.transform.SetParent(maskObject.transform, false);

            RectTransform mapRect = (RectTransform)mapImageObject.transform;
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = Vector2.zero;
            mapRect.offsetMax = Vector2.zero;

            RawImage mapImage = mapImageObject.GetComponent<RawImage>();
            mapImage.color = Color.white;
            mapImage.texture = GetRenderTexture();
        }

        private void CreateMinimapCamera()
        {
            if (minimapCamera != null)
                return;

            cameraObject = new GameObject("MinimapCamera");
            DontDestroyOnLoad(cameraObject);

            minimapCamera = cameraObject.AddComponent<Camera>();
            CopyRenderPipelineData();

            minimapCamera.enabled = true;
            minimapCamera.orthographic = true;
            minimapCamera.orthographicSize = orthographicSize;
            minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            minimapCamera.backgroundColor = new Color(0.18f, 0.28f, 0.04f, 1f);
            int uiLayer = LayerMask.NameToLayer("UI");
            minimapCamera.cullingMask = uiLayer >= 0 ? cullingMask & ~(1 << uiLayer) : cullingMask;
            minimapCamera.depth = -20f;
            minimapCamera.targetTexture = GetRenderTexture();
            minimapCamera.useOcclusionCulling = false;

            AudioListener listener = cameraObject.GetComponent<AudioListener>();
            if (listener != null)
                Destroy(listener);
        }

        private void CopyRenderPipelineData()
        {
            if (playerCamera == null)
                return;

            Component[] sourceComponents = playerCamera.GetComponents<Component>();
            foreach (Component sourceComponent in sourceComponents)
            {
                if (sourceComponent == null)
                    continue;

                System.Type sourceType = sourceComponent.GetType();
                if (!sourceType.FullName.Contains("AdditionalCameraData"))
                    continue;

                Component targetComponent = cameraObject.GetComponent(sourceType);
                if (targetComponent == null)
                    targetComponent = cameraObject.AddComponent(sourceType);

                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(sourceComponent), targetComponent);
            }
        }

        private void UpdateCameraTransform()
        {
            if (player == null || minimapCamera == null)
                return;

            Vector3 playerPosition = player.position;
            minimapCamera.transform.position = new Vector3(playerPosition.x, playerPosition.y + cameraHeight, playerPosition.z);
            minimapCamera.transform.rotation = Quaternion.Euler(90f, player.eulerAngles.y, 0f);
        }

        private RenderTexture GetRenderTexture()
        {
            if (renderTexture != null)
                return renderTexture;

            renderTexture = new RenderTexture(RenderTextureSize, RenderTextureSize, 16, RenderTextureFormat.ARGB32)
            {
                name = "MinimapRenderTexture",
                antiAliasing = 2
            };
            renderTexture.Create();
            return renderTexture;
        }

        private Sprite GetRoundedSprite()
        {
            if (roundedSprite != null)
                return roundedSprite;

            int textureSize = 128;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.ARGB32, false)
            {
                name = "MinimapRoundedMask"
            };

            float radius = Mathf.Clamp(cornerRadius, 1f, textureSize * 0.5f);
            Color fill = Color.white;
            Color clear = Color.clear;

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float px = Mathf.Min(x, textureSize - 1 - x);
                    float py = Mathf.Min(y, textureSize - 1 - y);
                    bool insideCorner = px >= radius || py >= radius || new Vector2(px - radius, py - radius).sqrMagnitude <= radius * radius;
                    texture.SetPixel(x, y, insideCorner ? fill : clear);
                }
            }

            texture.Apply();

            Vector4 border = Vector4.one * radius;
            roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            return roundedSprite;
        }

        private void OnDestroy()
        {
            if (cameraObject != null)
                Destroy(cameraObject);

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }

            if (roundedSprite != null)
                Destroy(roundedSprite.texture);
        }
    }
}
