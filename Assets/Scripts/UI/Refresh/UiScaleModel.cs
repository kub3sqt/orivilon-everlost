using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// UI refresh (revize 3): jeden model měřítka. Canvas se řídí výškou (1080 reference px);
    /// pokud je obrazovka užší než <see cref="minRefWidth"/> reference px, přejde na šířku,
    /// takže se HUD jen rovnoměrně zmenší a nic se nepřekrývá (16:10).
    /// </summary>
    public class UiScaleModel : MonoBehaviour
    {
        public float referenceHeight = 1080f;
        public float minRefWidth;
        private CanvasScaler scaler;
        private int w, h;

        private void Awake() { Apply(); }

        private void Update() { if (Screen.width != w || Screen.height != h) Apply(); }

        public void Apply()
        {
            if (scaler == null) scaler = GetComponent<CanvasScaler>();
            w = Screen.width; h = Screen.height;
            if (scaler == null || h <= 0) return;
            float f = h / referenceHeight;
            if (minRefWidth > 0f) f = Mathf.Min(f, w / minRefWidth);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = f;
            var cv = GetComponent<Canvas>();
            if (cv != null) cv.scaleFactor = f;   // hned, ne až v příštím Update CanvasScaleru
        }
    }
}
