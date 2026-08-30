using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Orivilon.Core;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Kompasovy pas ukotveny nahore uprostred HUD.
    /// Cely prvek se generuje za behu (stejny vzor jako <see cref="MinimapUI"/>), takze
    /// nevyzaduje zadny prefab ani upravu sceny.
    ///
    /// Princip: uhel se prevadi na pixel podle vzorce
    ///     x = Mathf.DeltaAngle(yawKamery, azimutCile) * (sirka / visibleDegrees)
    /// Vsechny carky maji pevny absolutni azimut a kazdy snimek se jen prepocita jejich X.
    /// Orezani a fade na krajich resi RectMask2D vcetne softness, takze se nic nemusi
    /// zapinat/vypinat pres SetActive.
    ///
    /// Markery (hraci, waypointy, ...) se registruji pres <see cref="CompassMarker"/>.
    /// </summary>
    public class CompassUI : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Sirka a vyska celeho pasu v pixelech.")]
        [SerializeField] private Vector2 size = new Vector2(760f, 92f);

        [Tooltip("Odsazeni od horniho okraje obrazovky.")]
        [SerializeField] private float topMargin = 16f;

        [Tooltip("Sirka fade efektu na levem a pravem okraji.")]
        [SerializeField] private int edgeSoftness = 90;

        [Header("Stupnice")]
        [Tooltip("Kolik stupnu je videt najednou (zorne pole kompasu).")]
        [SerializeField] private float visibleDegrees = 150f;

        [Tooltip("Krok nejmensich carek ve stupnich.")]
        [SerializeField] private int minorStep = 5;

        [Tooltip("Krok strednich carek ve stupnich.")]
        [SerializeField] private int mediumStep = 15;

        [Tooltip("Krok hlavnich carek (svetove strany) ve stupnich.")]
        [SerializeField] private int majorStep = 45;

        [Header("Rozmery carek")]
        [SerializeField] private float minorHeight = 5f;
        [SerializeField] private float mediumHeight = 10f;
        [SerializeField] private float majorHeight = 17f;
        [SerializeField] private float tickWidth = 2f;

        [Tooltip("Svisla pozice spolecne linky, od ktere carky rostou dolu.")]
        [SerializeField] private float tickTopY = 20f;

        [Header("Barvy")]
        [SerializeField] private Color minorColor = new Color(1f, 1f, 1f, 0.45f);
        [SerializeField] private Color mediumColor = new Color(1f, 1f, 1f, 0.7f);
        [SerializeField] private Color majorColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color labelColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color centerColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("Popisky svetovych stran")]
        [Tooltip("8 popisku po 45 stupnich, zacina na severu a pokracuje po smeru hodinovych rucicek.")]
        [SerializeField]
        private string[] cardinalLabels = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        [SerializeField] private float labelFontSize = 15f;
        [SerializeField] private float labelY = 32f;

        [Header("Markery")]
        [Tooltip("Svisla pozice ikon markeru.")]
        [SerializeField] private float markerY = -8f;

        [SerializeField] private float markerLabelFontSize = 11f;

        [Tooltip("Ikony mimo zorne pole prilepit na okraj misto skryti (jako Satisfactory).")]
        [SerializeField] private bool clampMarkersToEdge = false;

        [Tooltip("Jak casto se prepocitava text vzdalenosti (sekundy).")]
        [SerializeField] private float distanceRefreshInterval = 0.2f;

        private const string CompassName = "RuntimeCompass";

        private static bool sceneLoadedHooked;

        private Transform player;
        private Camera viewCamera;

        private RectTransform stripRect;
        private RectTransform ticksRoot;
        private RectTransform markersRoot;

        private readonly List<TickView> ticks = new List<TickView>();
        private readonly List<MarkerView> markerPool = new List<MarkerView>();

        private Sprite dotSprite;
        private float distanceTimer;

        /// <summary>Jedna carka stupnice s pevnym absolutnim azimutem.</summary>
        private struct TickView
        {
            public RectTransform rect;
            public RectTransform label; // null u carek bez popisku
            public float angle;
        }

        /// <summary>Jedna ikona markeru z poolu.</summary>
        private class MarkerView
        {
            public RectTransform root;
            public RectTransform stem;
            public Image stemImage;
            public Image icon;
            public TextMeshProUGUI label;
            public CanvasGroup group;
        }

        // ------------------------------------------------------------------ Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!sceneLoadedHooked)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.activeSceneChanged += OnActiveSceneChanged;
                sceneLoadedHooked = true;
            }
            TryCreateForActiveScene();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Game")
                CreateInScene(scene);
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (next.name == "Game")
                CreateInScene(next);
        }

        private static void TryCreateForActiveScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.name == "Game")
                CreateInScene(active);
        }

        /// <summary>Vytvori kompas v dane (Game) scene, pokud jeste neexistuje.</summary>
        private static void CreateInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            if (FindFirstObjectByType<CompassUI>(FindObjectsInactive.Include) != null)
                return;

            Canvas canvas = FindUsableCanvas(scene);
            if (canvas == null)
            {
                Debug.LogWarning("[CompassUI] CreateInScene: nenalezen pouzitelny Canvas – kompas se nevytvoril.");
                return;
            }

            GameObject root = new GameObject(CompassName, typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            root.AddComponent<CompassUI>();

            Debug.Log($"[CompassUI] Kompas vytvoren pod Canvasem '{canvas.name}' (scena '{canvas.gameObject.scene.name}').");
        }

        /// <summary>
        /// Najde Canvas, ktery prezije nacteni sceny. Stejna logika jako v MinimapUI –
        /// vyhne se Canvasu z LoadingScreenu nebo MainMenu.
        /// </summary>
        private static Canvas FindUsableCanvas(Scene gameScene)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Canvas inScene = null;
            Canvas persistent = null;
            Canvas other = null;

            foreach (Canvas c in canvases)
            {
                if (c == null) continue;
                Scene cs = c.gameObject.scene;

                if (cs == gameScene)
                {
                    if (inScene == null || c.isRootCanvas) inScene = c;
                }
                else if (cs.name == "DontDestroyOnLoad")
                {
                    if (persistent == null) persistent = c;
                }
                else if (cs.name != "LoadingScreen" && cs.name != "MainMenu")
                {
                    if (other == null) other = c;
                }
            }

            return inScene != null ? inScene : (persistent != null ? persistent : other);
        }

        // ------------------------------------------------------------------ Unity lifecycle

        private void Awake()
        {
            BuildUI();
            RegisterWithGameManager();
        }

        private void Start()
        {
            // Zaloha pro pripad, ze GameManager.instance jeste neexistoval v Awake.
            RegisterWithGameManager();

            if (player == null)
                TryResolvePlayer();
        }

        private void LateUpdate()
        {
            if (viewCamera == null || player == null)
            {
                TryResolvePlayer();
                if (viewCamera == null || player == null)
                    return;
            }

            float yaw = viewCamera != null
                ? viewCamera.transform.eulerAngles.y
                : player.eulerAngles.y;

            float pixelsPerDegree = size.x / Mathf.Max(1f, visibleDegrees);

            UpdateTicks(yaw, pixelsPerDegree);
            UpdateMarkers(yaw, pixelsPerDegree);
        }

        private void OnDestroy()
        {
            if (dotSprite != null)
            {
                if (dotSprite.texture != null) Destroy(dotSprite.texture);
                Destroy(dotSprite);
                dotSprite = null;
            }
        }

        // ------------------------------------------------------------------ Verejne API

        /// <summary>
        /// Vola GameManager po spawnu hrace. Bezpecne volat opakovane.
        /// </summary>
        public void Initialize(Transform playerTransform, Camera sourceCamera = null)
        {
            player = playerTransform;
            viewCamera = sourceCamera != null ? sourceCamera : ResolveCamera(playerTransform);
        }

        private void TryResolvePlayer()
        {
            if (GameManager.instance == null) return;

            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) return;

            player = go.transform;
            if (viewCamera == null)
                viewCamera = ResolveCamera(player);
        }

        private static Camera ResolveCamera(Transform playerTransform)
        {
            if (playerTransform != null)
            {
                Camera child = playerTransform.GetComponentInChildren<Camera>(true);
                if (child != null) return child;
            }
            return Camera.main;
        }

        // ------------------------------------------------------------------ Registrace

        private void RegisterWithGameManager()
        {
            if (GameManager.instance == null)
                return;

            if (GameManager.instance.compass == null)
            {
                GameManager.instance.compass = gameObject;
                // Startuje skryty; GameManager kompas zobrazi po dokonceni spawnu hrace.
                gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ Stavba UI

        private void BuildUI()
        {
            RectTransform rootRect = (RectTransform)transform;
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -topMargin);
            rootRect.sizeDelta = size;

            // Pas s orezanim a fade na krajich.
            GameObject stripObj = new GameObject("Strip", typeof(RectTransform), typeof(RectMask2D));
            stripObj.transform.SetParent(transform, false);

            stripRect = (RectTransform)stripObj.transform;
            stripRect.anchorMin = Vector2.zero;
            stripRect.anchorMax = Vector2.one;
            stripRect.offsetMin = Vector2.zero;
            stripRect.offsetMax = Vector2.zero;

            RectMask2D mask = stripObj.GetComponent<RectMask2D>();
            mask.softness = new Vector2Int(Mathf.Max(0, edgeSoftness), 0);

            ticksRoot = CreateChildRect("Ticks", stripRect);
            markersRoot = CreateChildRect("Markers", stripRect);

            BuildBaseline();
            BuildTicks();
            BuildCenterIndicator();
        }

        private static RectTransform CreateChildRect(string name, RectTransform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>Tenka vodorovna linka, po ktere carky "jedou".</summary>
        private void BuildBaseline()
        {
            GameObject go = new GameObject("Baseline", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(stripRect, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, tickTopY);

            Image img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.25f);
            img.raycastTarget = false;
        }

        private void BuildTicks()
        {
            int step = Mathf.Max(1, minorStep);
            int medium = Mathf.Max(step, mediumStep);
            int major = Mathf.Max(medium, majorStep);

            for (int angle = 0; angle < 360; angle += step)
            {
                bool isMajor = angle % major == 0;
                bool isMedium = !isMajor && angle % medium == 0;

                float height = isMajor ? majorHeight : (isMedium ? mediumHeight : minorHeight);
                Color color = isMajor ? majorColor : (isMedium ? mediumColor : minorColor);
                float width = isMajor ? tickWidth + 1f : tickWidth;

                RectTransform tickRect = BuildTick(angle, width, height, color);

                RectTransform labelRect = null;
                if (isMajor)
                    labelRect = BuildCardinalLabel(angle, major);

                ticks.Add(new TickView
                {
                    rect = tickRect,
                    label = labelRect,
                    angle = angle
                });
            }
        }

        private RectTransform BuildTick(int angle, float width, float height, Color color)
        {
            GameObject go = new GameObject($"Tick_{angle}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(ticksRoot, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            // Pivot nahore – vsechny carky zacinaji na spolecne lince a rostou dolu.
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, tickTopY);

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            return rect;
        }

        private RectTransform BuildCardinalLabel(int angle, int majorStepValue)
        {
            int index = angle / majorStepValue;
            if (cardinalLabels == null || index < 0 || index >= cardinalLabels.Length)
                return null;

            string text = cardinalLabels[index];
            if (string.IsNullOrEmpty(text))
                return null;

            GameObject go = new GameObject($"Label_{angle}", typeof(RectTransform));
            go.transform.SetParent(ticksRoot, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(60f, 20f);
            rect.anchoredPosition = new Vector2(0f, labelY);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = labelFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = labelColor;
            tmp.raycastTarget = false;
            tmp.overflowMode = TextOverflowModes.Overflow;

            return rect;
        }

        /// <summary>Pevna znacka uprostred, ktera ukazuje presny smer pohledu.</summary>
        private void BuildCenterIndicator()
        {
            // Mimo masku, aby ji fade na krajich neovlivnil.
            GameObject go = new GameObject("CenterIndicator", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(2f, majorHeight + 8f);
            rect.anchoredPosition = new Vector2(0f, tickTopY - majorHeight - 6f);

            Image img = go.GetComponent<Image>();
            img.color = centerColor;
            img.raycastTarget = false;
        }

        // ------------------------------------------------------------------ Update stupnice

        private void UpdateTicks(float yaw, float pixelsPerDegree)
        {
            for (int i = 0; i < ticks.Count; i++)
            {
                TickView tick = ticks[i];
                if (tick.rect == null) continue;

                float x = Mathf.DeltaAngle(yaw, tick.angle) * pixelsPerDegree;

                Vector2 pos = tick.rect.anchoredPosition;
                pos.x = x;
                tick.rect.anchoredPosition = pos;

                if (tick.label != null)
                {
                    Vector2 labelPos = tick.label.anchoredPosition;
                    labelPos.x = x;
                    tick.label.anchoredPosition = labelPos;
                }
            }
        }

        // ------------------------------------------------------------------ Update markeru

        private void UpdateMarkers(float yaw, float pixelsPerDegree)
        {
            bool refreshDistance = false;
            distanceTimer -= Time.unscaledDeltaTime;
            if (distanceTimer <= 0f)
            {
                distanceTimer = Mathf.Max(0.05f, distanceRefreshInterval);
                refreshDistance = true;
            }

            IReadOnlyList<CompassMarker> markers = CompassMarker.Active;
            Vector3 playerPos = player.position;
            float halfWidth = size.x * 0.5f;
            float halfFov = visibleDegrees * 0.5f;

            int used = 0;

            for (int i = 0; i < markers.Count; i++)
            {
                CompassMarker marker = markers[i];
                if (marker == null) continue;

                Vector3 delta = marker.WorldPosition - playerPos;
                delta.y = 0f;

                float sqrDistance = delta.sqrMagnitude;
                if (sqrDistance < 0.0001f) continue;

                if (marker.MaxDistance > 0f && sqrDistance > marker.MaxDistance * marker.MaxDistance)
                    continue;

                float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float angleDelta = Mathf.DeltaAngle(yaw, bearing);

                if (!clampMarkersToEdge && Mathf.Abs(angleDelta) > halfFov)
                    continue;

                float x = angleDelta * pixelsPerDegree;
                if (clampMarkersToEdge)
                    x = Mathf.Clamp(x, -halfWidth + 12f, halfWidth - 12f);

                MarkerView view = GetMarkerView(used);
                used++;

                ApplyMarkerView(view, marker, x, refreshDistance, Mathf.Sqrt(sqrDistance));
            }

            // Nepouzite ikony z poolu schovej (bez destrukce, pool se recykluje).
            for (int i = used; i < markerPool.Count; i++)
            {
                if (markerPool[i].group.alpha != 0f)
                    markerPool[i].group.alpha = 0f;
            }
        }

        private void ApplyMarkerView(MarkerView view, CompassMarker marker, float x, bool refreshDistance, float distance)
        {
            view.group.alpha = 1f;

            Vector2 pos = view.root.anchoredPosition;
            pos.x = x;
            view.root.anchoredPosition = pos;

            float iconSize = Mathf.Max(4f, marker.IconSize);
            view.icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            view.icon.color = marker.Color;
            view.icon.sprite = marker.Icon != null ? marker.Icon : GetDotSprite();

            // Stopka od linky stupnice k ikone.
            float stemTop = tickTopY;
            float stemBottom = markerY + iconSize * 0.5f;
            float stemHeight = Mathf.Max(0f, stemTop - stemBottom);
            view.stem.sizeDelta = new Vector2(1f, stemHeight);
            view.stem.anchoredPosition = new Vector2(0f, stemTop);
            view.stemImage.color = new Color(marker.Color.r, marker.Color.g, marker.Color.b, 0.45f);

            if (!refreshDistance)
                return;

            string text = marker.Label ?? string.Empty;
            if (marker.ShowDistance)
            {
                string distanceText = distance >= 1000f
                    ? $"{distance / 1000f:0.0} km"
                    : $"{Mathf.RoundToInt(distance)} m";

                text = string.IsNullOrEmpty(text) ? distanceText : $"{text}  {distanceText}";
            }

            if (view.label.text != text)
                view.label.text = text;
        }

        private MarkerView GetMarkerView(int index)
        {
            while (markerPool.Count <= index)
                markerPool.Add(CreateMarkerView(markerPool.Count));

            return markerPool[index];
        }

        private MarkerView CreateMarkerView(int index)
        {
            GameObject rootObj = new GameObject($"Marker_{index}", typeof(RectTransform), typeof(CanvasGroup));
            rootObj.transform.SetParent(markersRoot, false);

            RectTransform rootRect = (RectTransform)rootObj.transform;
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;

            CanvasGroup group = rootObj.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Stopka
            GameObject stemObj = new GameObject("Stem", typeof(RectTransform), typeof(Image));
            stemObj.transform.SetParent(rootRect, false);

            RectTransform stemRect = (RectTransform)stemObj.transform;
            stemRect.anchorMin = new Vector2(0.5f, 0.5f);
            stemRect.anchorMax = new Vector2(0.5f, 0.5f);
            stemRect.pivot = new Vector2(0.5f, 1f);
            stemRect.sizeDelta = new Vector2(1f, 10f);

            Image stemImg = stemObj.GetComponent<Image>();
            stemImg.raycastTarget = false;

            // Ikona
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(rootRect, false);

            RectTransform iconRect = (RectTransform)iconObj.transform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(18f, 18f);
            iconRect.anchoredPosition = new Vector2(0f, markerY);

            Image iconImg = iconObj.GetComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.sprite = GetDotSprite();

            Shadow iconShadow = iconObj.AddComponent<Shadow>();
            iconShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            iconShadow.effectDistance = new Vector2(1f, -1f);

            // Popisek
            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(rootRect, false);

            RectTransform labelRect = (RectTransform)labelObj.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(140f, 16f);
            labelRect.anchoredPosition = new Vector2(0f, markerY - 12f);

            TextMeshProUGUI labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            labelTmp.text = string.Empty;
            labelTmp.fontSize = markerLabelFontSize;
            labelTmp.alignment = TextAlignmentOptions.Top;
            labelTmp.color = new Color(1f, 1f, 1f, 0.85f);
            labelTmp.raycastTarget = false;
            labelTmp.overflowMode = TextOverflowModes.Overflow;

            return new MarkerView
            {
                root = rootRect,
                stem = stemRect,
                stemImage = stemImg,
                icon = iconImg,
                label = labelTmp,
                group = group
            };
        }

        // ------------------------------------------------------------------ Procedurální sprite

        /// <summary>Vygeneruje jednoduche bile kolecko pouzite jako vychozi ikona markeru.</summary>
        private Sprite GetDotSprite()
        {
            if (dotSprite != null) return dotSprite;

            const int Size = 64;
            Texture2D tex = new Texture2D(Size, Size, TextureFormat.ARGB32, false)
            {
                name = "CompassDot"
            };

            float radius = Size * 0.5f - 1f;
            Vector2 center = new Vector2(Size * 0.5f, Size * 0.5f);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(radius - d); // 1px antialiasing
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();

            dotSprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            return dotSprite;
        }
    }
}
