using System;
using System.Globalization;
using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// UI refresh: small runtime helpers shared by the controllers (colours, easing, date text).
    /// The UI itself is authored in scenes/prefabs; nothing here builds UI.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Ink = new Color32(0xe9, 0xec, 0xe6, 0xff);
        public static readonly Color Muted = new Color32(0xa8, 0xb0, 0xa6, 0xff);
        public static readonly Color Rim = new Color(175f / 255f, 180f / 255f, 172f / 255f, 0.55f);

        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

        /// <summary>"Today 17:30", "Yesterday", "3 Oct" or "Never".</summary>
        public static string When(string iso)
        {
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)) return "Never";
            d = d.ToLocalTime();
            var inv = CultureInfo.InvariantCulture;
            if (d.Date == DateTime.Now.Date) return "Today " + d.ToString("HH:mm", inv);
            if (d.Date == DateTime.Now.Date.AddDays(-1)) return "Yesterday";
            return d.ToString("d MMM", inv);
        }

        public static string PlayTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            return m >= 60 ? (m / 60) + " h " + (m % 60).ToString("00") + " min" : m + " min";
        }
    }
}
