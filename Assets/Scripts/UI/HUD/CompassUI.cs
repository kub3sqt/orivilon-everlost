using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Orivilon.Core;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Compass strip at the top centre of the HUD. The hierarchy (black edge-attached frame panel, masked strip,
    /// ticks, N/NE/…/NW labels, centre marker, marker template) is authored and saved in the Game scene
    /// (prefab Assets/UI/Refresh/Prefabs/HUD/Compass.prefab, editor step "compassui build") – nothing is
    /// built here except pooled copies of the marker template for registered <see cref="CompassMarker"/>s.
    ///
    /// Heading convention (unchanged): yaw = camera eulerAngles.y, 0 = north (+Z), clockwise.
    /// Every scale item has a fixed absolute azimuth; each frame only its X is set:
    ///     x = Mathf.DeltaAngle(yaw, azimuth) * (stripWidth / visibleDegrees)
    /// DeltaAngle wraps to ±180°, so each item has exactly one position (no duplicates); the jump at ±180°
    /// happens far outside the strip's RectMask2D (it only shows ±visibleDegrees/2), so nothing pops.
    /// </summary>
    public class CompassUI : MonoBehaviour
    {
        [Serializable]
        public struct ScaleItem
        {
            public RectTransform rect;
            public float azimuth;
        }

        [Tooltip("Degrees visible across the strip width.")]
        [SerializeField] private float visibleDegrees = 150f;

        [Tooltip("Masked strip; its width maps to visibleDegrees.")]
        [SerializeField] private RectTransform strip;

        [Tooltip("Ticks and direction labels with their fixed azimuths (authored).")]
        [SerializeField] private ScaleItem[] items = new ScaleItem[0];

        [Header("Markers")]
        [SerializeField] private RectTransform markersRoot;
        [Tooltip("Inactive authored marker (Stem, Icon, Label, CanvasGroup); copied for each visible marker.")]
        [SerializeField] private RectTransform markerTemplate;
        [Tooltip("Show off-screen markers clamped at the strip ends instead of hiding them.")]
        [SerializeField] private bool clampMarkersToEdge = false;
        [Tooltip("How often the distance text is refreshed (seconds).")]
        [SerializeField] private float distanceRefreshInterval = 0.2f;

        private Transform player;
        private Camera viewCamera;
        private float distanceTimer;
        private float lastYaw = float.NaN, lastWidth = -1f;

        private class MarkerView
        {
            public RectTransform root;
            public RectTransform stem;
            public Image stemImage;
            public Image icon;
            public TextMeshProUGUI label;
            public CanvasGroup group;
            public float stemTop;
            public Sprite defaultSprite;
        }

        private readonly List<MarkerView> markerPool = new List<MarkerView>();

        // ------------------------------------------------------------------ Unity lifecycle

        private void Awake()
        {
            if (markerTemplate != null) markerTemplate.gameObject.SetActive(false);
            RegisterWithGameManager();
        }

        private void Start()
        {
            RegisterWithGameManager();
            if (player == null) TryResolvePlayer();
        }

        private void LateUpdate()
        {
            if (viewCamera == null || player == null)
            {
                TryResolvePlayer();
                if (viewCamera == null || player == null) return;
            }

            float yaw = viewCamera.transform.eulerAngles.y;
            float width = strip != null ? strip.rect.width : 0f;
            if (width <= 1f) return;
            float pixelsPerDegree = width / Mathf.Max(1f, visibleDegrees);

            // the scale only moves when the heading (or the strip size) changes
            if (yaw != lastYaw || width != lastWidth)
            {
                lastYaw = yaw; lastWidth = width;
                UpdateScale(yaw, pixelsPerDegree);
            }
            UpdateMarkers(yaw, pixelsPerDegree, width);
        }

        // ------------------------------------------------------------------ Public API

        /// <summary>Called by GameManager after the player spawned. Safe to call repeatedly.</summary>
        public void Initialize(Transform playerTransform, Camera sourceCamera = null)
        {
            player = playerTransform;
            viewCamera = sourceCamera != null ? sourceCamera : ResolveCamera(playerTransform);
            lastYaw = float.NaN;
        }

        private void TryResolvePlayer()
        {
            if (GameManager.instance == null) return;
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) return;
            player = go.transform;
            if (viewCamera == null) viewCamera = ResolveCamera(player);
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

        private void RegisterWithGameManager()
        {
            if (GameManager.instance == null) return;
            if (GameManager.instance.compass == null)
            {
                GameManager.instance.compass = gameObject;
                // hidden until GameManager shows it after the player has spawned
                gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ Scale

        private void UpdateScale(float yaw, float pixelsPerDegree)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var r = items[i].rect;
                if (r == null) continue;
                Vector2 p = r.anchoredPosition;
                p.x = Mathf.DeltaAngle(yaw, items[i].azimuth) * pixelsPerDegree;
                r.anchoredPosition = p;
            }
        }

        // ------------------------------------------------------------------ Markers

        private void UpdateMarkers(float yaw, float pixelsPerDegree, float width)
        {
            IReadOnlyList<CompassMarker> markers = CompassMarker.Active;
            int used = 0;
            if (markers.Count > 0 && markerTemplate != null && markersRoot != null)
            {
                bool refreshDistance = false;
                distanceTimer -= Time.unscaledDeltaTime;
                if (distanceTimer <= 0f) { distanceTimer = Mathf.Max(0.05f, distanceRefreshInterval); refreshDistance = true; }

                Vector3 playerPos = player.position;
                float halfWidth = width * 0.5f;
                float halfFov = visibleDegrees * 0.5f;

                for (int i = 0; i < markers.Count; i++)
                {
                    CompassMarker marker = markers[i];
                    if (marker == null) continue;
                    Vector3 delta = marker.WorldPosition - playerPos;
                    delta.y = 0f;
                    float sqrDistance = delta.sqrMagnitude;
                    if (sqrDistance < 0.0001f) continue;
                    if (marker.MaxDistance > 0f && sqrDistance > marker.MaxDistance * marker.MaxDistance) continue;

                    float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                    float angleDelta = Mathf.DeltaAngle(yaw, bearing);
                    if (!clampMarkersToEdge && Mathf.Abs(angleDelta) > halfFov) continue;

                    float x = angleDelta * pixelsPerDegree;
                    if (clampMarkersToEdge) x = Mathf.Clamp(x, -halfWidth + 12f, halfWidth - 12f);

                    MarkerView view = GetMarkerView(used++);
                    ApplyMarkerView(view, marker, x, refreshDistance, Mathf.Sqrt(sqrDistance));
                }
            }

            for (int i = used; i < markerPool.Count; i++)
                if (markerPool[i].group.alpha != 0f) markerPool[i].group.alpha = 0f;
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
            Sprite sprite = marker.Icon != null ? marker.Icon : view.defaultSprite;
            if (view.icon.sprite != sprite) view.icon.sprite = sprite;

            // stem from the tick line down to the icon
            float iconY = view.icon.rectTransform.anchoredPosition.y;
            float stemHeight = Mathf.Max(0f, view.stemTop - (iconY + iconSize * 0.5f));
            view.stem.sizeDelta = new Vector2(view.stem.sizeDelta.x, stemHeight);
            view.stemImage.color = new Color(marker.Color.r, marker.Color.g, marker.Color.b, 0.45f);

            if (!refreshDistance) return;
            string text = marker.Label ?? string.Empty;
            if (marker.ShowDistance)
            {
                string d = distance >= 1000f ? $"{distance / 1000f:0.0} km" : $"{Mathf.RoundToInt(distance)} m";
                text = string.IsNullOrEmpty(text) ? d : $"{text}  {d}";
            }
            if (view.label.text != text) view.label.text = text;
        }

        private MarkerView GetMarkerView(int index)
        {
            while (markerPool.Count <= index)
            {
                var go = Instantiate(markerTemplate.gameObject, markersRoot, false);
                go.name = "Marker_" + markerPool.Count;
                go.SetActive(true);
                var root = (RectTransform)go.transform;
                var stem = (RectTransform)root.Find("Stem");
                var view = new MarkerView
                {
                    root = root,
                    stem = stem,
                    stemImage = stem.GetComponent<Image>(),
                    icon = root.Find("Icon").GetComponent<Image>(),
                    label = root.Find("Label").GetComponent<TextMeshProUGUI>(),
                    group = go.GetComponent<CanvasGroup>(),
                    stemTop = stem.anchoredPosition.y
                };
                // Popisky markerů nesou jména hráčů ze Steamu – jako prostý text, bez TMP tagů.
                if (view.label != null) view.label.richText = false;
                view.defaultSprite = view.icon.sprite;
                view.group.alpha = 0f;
                markerPool.Add(view);
            }
            return markerPool[index];
        }
    }
}
