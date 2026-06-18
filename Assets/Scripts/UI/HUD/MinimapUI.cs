using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Orivilon.Core;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Runtime minimap anchored in the bottom-right corner of the HUD.
    /// Creates a top-down orthographic camera above the player and renders it into a rounded UI panel.
    /// The camera rotates with the player – "up" on the minimap is always the player's forward direction.
    /// A yellow triangle in the centre marks the player's position.
    ///
    /// Fixes vs. previous version:
    ///   - URP: UniversalAdditionalCameraData is configured via reflection (renderType = Base,
    ///     post-processing and shadows disabled) instead of the fragile JsonUtility JSON-copy.
    ///   - Camera is no longer DontDestroyOnLoad; it is destroyed/recreated on Initialize() so
    ///     there is no stale-texture problem after scene reloads.
    ///   - OnEnable / OnDisable toggle the minimap camera so it stops rendering while hidden
    ///     (e.g. during inventory / pause) which also removes the previous "doesn't hide" bug.
    ///   - Self-registration: Awake() registers this object into GameManager.minimap so the
    ///     reference is available regardless of Bootstrap() timing.
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
        private Camera minimapCamera;
        private GameObject cameraObject;
        private RenderTexture renderTexture;
        private Sprite roundedSprite;

        // ------------------------------------------------------------------ Bootstrap

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

        // ------------------------------------------------------------------ Unity lifecycle

        private void Awake()
        {
            BuildUI();
            RegisterWithGameManager();
        }

        private void Start()
        {
            // Backup in case GameManager.instance was not yet available in Awake.
            RegisterWithGameManager();
        }

        private void LateUpdate()
        {
            if (player == null || minimapCamera == null)
                return;

            UpdateCameraTransform();
        }

        private void OnEnable()
        {
            if (minimapCamera != null)
                minimapCamera.enabled = true;
        }

        private void OnDisable()
        {
            if (minimapCamera != null)
                minimapCamera.enabled = false;
        }

        private void OnDestroy()
        {
            DestroyCamera();

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }

            if (roundedSprite != null)
            {
                Destroy(roundedSprite.texture);
                roundedSprite = null;
            }
        }

        // ------------------------------------------------------------------ Public API

        /// <summary>
        /// Called by GameManager after the player has been spawned.
        /// Safe to call multiple times – destroys any previous minimap camera first.
        /// </summary>
        public void Initialize(Transform playerTransform, Camera sourceCamera = null)
        {
            player = playerTransform;

            // Recreate the camera so the render texture reference stays valid.
            DestroyCamera();
            CreateMinimapCamera();
            UpdateCameraTransform();
        }

        // ------------------------------------------------------------------ Registration

        private void RegisterWithGameManager()
        {
            if (GameManager.instance == null)
                return;

            if (GameManager.instance.minimap == null)
            {
                GameManager.instance.minimap = gameObject;
                // Start hidden; GameManager shows it after the player has fully spawned.
                gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ UI construction

        private void BuildUI()
        {
            // Root – bottom-right anchor
            RectTransform rootRect = (RectTransform)transform;
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot    = new Vector2(1f, 0f);
            rootRect.anchoredPosition = new Vector2(-bottomRightMargin.x, bottomRightMargin.y);
            rootRect.sizeDelta = size;

            // Dark rounded background
            Image background = gameObject.AddComponent<Image>();
            background.color  = new Color(0.035f, 0.04f, 0.045f, 0.96f);
            background.sprite = GetRoundedSprite();
            background.type   = Image.Type.Sliced;

            Outline outline = gameObject.AddComponent<Outline>();
            outline.effectColor    = new Color(0.55f, 0.58f, 0.58f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);

            // Circular mask
            GameObject maskObj = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            maskObj.transform.SetParent(transform, false);

            RectTransform maskRect  = (RectTransform)maskObj.transform;
            maskRect.anchorMin  = Vector2.zero;
            maskRect.anchorMax  = Vector2.one;
            maskRect.offsetMin  = new Vector2(4f, 4f);
            maskRect.offsetMax  = new Vector2(-4f, -4f);

            Image maskImage  = maskObj.GetComponent<Image>();
            maskImage.sprite = GetRoundedSprite();
            maskImage.type   = Image.Type.Sliced;
            maskImage.color  = Color.white;

            Mask mask = maskObj.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            // Render-texture display
            GameObject mapImgObj = new GameObject("MapImage",
                typeof(RectTransform), typeof(RawImage));
            mapImgObj.transform.SetParent(maskObj.transform, false);

            RectTransform mapRect = (RectTransform)mapImgObj.transform;
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = Vector2.zero;
            mapRect.offsetMax = Vector2.zero;

            RawImage mapImage  = mapImgObj.GetComponent<RawImage>();
            mapImage.color     = Color.white;
            mapImage.texture   = GetRenderTexture();

            // Player direction indicator (triangle pointing up)
            BuildPlayerArrow(maskObj.transform);
        }

        private void BuildPlayerArrow(Transform parent)
        {
            GameObject arrowObj = new GameObject("PlayerArrow",
                typeof(RectTransform), typeof(Image));
            arrowObj.transform.SetParent(parent, false);

            RectTransform arrowRect = arrowObj.GetComponent<RectTransform>();
            arrowRect.anchorMin       = new Vector2(0.5f, 0.5f);
            arrowRect.anchorMax       = new Vector2(0.5f, 0.5f);
            arrowRect.pivot           = new Vector2(0.5f, 0.5f);
            arrowRect.sizeDelta       = new Vector2(14f, 20f);
            arrowRect.anchoredPosition = Vector2.zero;

            // Drop-shadow for readability
            Shadow shadow = arrowObj.AddComponent<Shadow>();
            shadow.effectColor    = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(1f, -1f);

            Image arrowImg  = arrowObj.GetComponent<Image>();
            arrowImg.color   = new Color(1f, 0.9f, 0.1f, 1f); // bright yellow
            arrowImg.sprite  = BuildArrowSprite();
        }

        /// <summary>Generates a procedural upward-pointing triangle sprite.</summary>
        private Sprite BuildArrowSprite()
        {
            const int W = 32, H = 32;
            Texture2D tex = new Texture2D(W, H, TextureFormat.ARGB32, false)
            {
                name = "MinimapArrow"
            };

            Color clear = Color.clear;
            Color fill  = Color.white;

            for (int y = 0; y < H; y++)
            {
                // At y=0 (bottom) the triangle is widest; at y=H-1 (top) it tapers to a point.
                float t        = y / (float)(H - 1);
                float halfW    = Mathf.Lerp(W * 0.45f, 0f, t);
                int   left     = Mathf.RoundToInt(W * 0.5f - halfW);
                int   right    = Mathf.RoundToInt(W * 0.5f + halfW);

                for (int x = 0; x < W; x++)
                    tex.SetPixel(x, y, (x >= left && x <= right) ? fill : clear);
            }
            tex.Apply();

            return Sprite.Create(tex,
                new Rect(0, 0, W, H),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        // ------------------------------------------------------------------ Camera

        private void CreateMinimapCamera()
        {
            cameraObject = new GameObject("MinimapCamera");

            minimapCamera = cameraObject.AddComponent<Camera>();
            minimapCamera.orthographic    = true;
            minimapCamera.orthographicSize = orthographicSize;
            minimapCamera.clearFlags      = CameraClearFlags.SolidColor;
            minimapCamera.backgroundColor = new Color(0.12f, 0.20f, 0.04f, 1f);
            minimapCamera.nearClipPlane   = 0.3f;
            minimapCamera.farClipPlane    = cameraHeight + 300f;
            minimapCamera.depth           = -20f;
            minimapCamera.useOcclusionCulling = false;
            minimapCamera.targetTexture   = GetRenderTexture();

            // Exclude the UI layer so no HUD bleeds into the minimap.
            int uiLayer = LayerMask.NameToLayer("UI");
            minimapCamera.cullingMask = uiLayer >= 0
                ? cullingMask & ~(1 << uiLayer)
                : cullingMask;

            // Remove any auto-added AudioListener.
            AudioListener al = cameraObject.GetComponent<AudioListener>();
            if (al != null) Destroy(al);

            // URP: set the camera as a standalone Base camera so it renders
            // to the render texture independently of the main camera stack.
            SetupURPCameraData();

            // Match the current enabled state of this UI component.
            minimapCamera.enabled = enabled;
        }

        /// <summary>
        /// Configures UniversalAdditionalCameraData via reflection so this code compiles
        /// even when URP is not installed, and works across different URP package versions.
        /// </summary>
        private void SetupURPCameraData()
        {
            System.Type urpType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                urpType = asm.GetType(
                    "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                if (urpType != null) break;
            }

            if (urpType == null) return; // Not using URP – nothing to do.

            Component urpData = cameraObject.GetComponent(urpType)
                             ?? cameraObject.AddComponent(urpType);

            // renderType  0 = Base  (camera renders to its own render texture independently)
            //             1 = Overlay (composited on top of a Base camera – we do NOT want this)
            TrySetProperty(urpData, urpType, "renderType", 0);

            // Disable expensive features that are pointless on a tiny top-down minimap.
            TrySetPropertyBool(urpData, urpType, "renderPostProcessing", false);
            TrySetPropertyBool(urpData, urpType, "renderShadows",        false);
        }

        private static void TrySetProperty(object obj, System.Type type, string propertyName, int enumValue)
        {
            System.Reflection.PropertyInfo prop = type.GetProperty(propertyName);
            if (prop == null || !prop.CanWrite) return;

            try
            {
                object value = System.Enum.ToObject(prop.PropertyType, enumValue);
                prop.SetValue(obj, value);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MinimapUI] Could not set '{propertyName}': {e.Message}");
            }
        }

        private static void TrySetPropertyBool(object obj, System.Type type, string propertyName, bool value)
        {
            System.Reflection.PropertyInfo prop = type.GetProperty(propertyName);
            if (prop == null || !prop.CanWrite) return;

            try { prop.SetValue(obj, value); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MinimapUI] Could not set '{propertyName}': {e.Message}");
            }
        }

        private void DestroyCamera()
        {
            if (cameraObject != null)
            {
                Destroy(cameraObject);
                cameraObject    = null;
                minimapCamera   = null;
            }
        }

        private void UpdateCameraTransform()
        {
            Vector3 pos = player.position;
            cameraObject.transform.position = new Vector3(pos.x, pos.y + cameraHeight, pos.z);
            // Rotate so that the player's forward direction always points "up" in the minimap.
            cameraObject.transform.rotation = Quaternion.Euler(90f, player.eulerAngles.y, 0f);
        }

        // ------------------------------------------------------------------ Render texture

        private RenderTexture GetRenderTexture()
        {
            if (renderTexture != null) return renderTexture;

            renderTexture = new RenderTexture(
                RenderTextureSize, RenderTextureSize, 16, RenderTextureFormat.ARGB32)
            {
                name         = "MinimapRenderTexture",
                antiAliasing = 2,
            };
            renderTexture.Create();
            return renderTexture;
        }

        // ------------------------------------------------------------------ Rounded sprite

        private Sprite GetRoundedSprite()
        {
            if (roundedSprite != null) return roundedSprite;

            const int TextureSize = 128;
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.ARGB32, false)
            {
                name = "MinimapRoundedMask"
            };

            float radius = Mathf.Clamp(cornerRadius, 1f, TextureSize * 0.5f);
            Color fill   = Color.white;
            Color clear  = Color.clear;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float px = Mathf.Min(x, TextureSize - 1 - x);
                    float py = Mathf.Min(y, TextureSize - 1 - y);
                    bool inside = px >= radius || py >= radius
                               || new Vector2(px - radius, py - radius).sqrMagnitude <= radius * radius;
                    texture.SetPixel(x, y, inside ? fill : clear);
                }
            }

            texture.Apply();

            Vector4 border = Vector4.one * radius;
            roundedSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);

            return roundedSprite;
        }
    }
}
