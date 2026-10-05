using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Najde, kdo ve hře alokuje spravovanou paměť (zdroj GC pauz).
    ///
    /// <para>Ovládá se z herní konzole: <c>/profil start</c> smaže snímky profileru a začne
    /// nahrávat, <c>/profil dump štítek</c> projde nahrané snímky a do
    /// <c>_reports/gc_alloc.log</c> zapíše největší zdroje GC Alloc v KB/snímek. Zdroj je
    /// cesta vzorku profileru (poslední tři úrovně), vlastní alokace bez dětí.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class GcAllocProfiler
    {
        static GcAllocProfiler()
        {
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            string r = Orivilon.Core.GameConsole.EditorRequest;
            if (string.IsNullOrEmpty(r)) return;
            Orivilon.Core.GameConsole.EditorRequest = null;

            if (r == "start")
            {
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.profileEditor = false;
                ProfilerDriver.enabled = true;
                Write("[Profil] nahrávání spuštěno");
            }
            else if (r.StartsWith("dump"))
            {
                Dump(r.Length > 5 ? r.Substring(5).Trim() : "");
            }
        }

        private static void Dump(string label)
        {
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var self = new Dictionary<string, double>();
            var hits = new Dictionary<string, int>();
            int frames = 0;
            double total = 0;

            for (int f = first; f <= last && f >= 0; f++)
            {
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                           HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                           HierarchyFrameDataView.columnGcMemory, false))
                {
                    if (v == null || !v.valid) continue;
                    frames++;
                    var seen = new HashSet<string>();
                    Walk(v, v.GetRootItemID(), "", self, seen, ref total);
                    foreach (string k in seen) { hits.TryGetValue(k, out int h); hits[k] = h + 1; }
                }
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[GcAlloc] {0}: {1} snímků (profil {2}..{3}), celkem {4:0.0} KB/snímek\n",
                label, frames, first, last, frames > 0 ? total / 1024.0 / frames : 0);
            foreach (var kv in self.OrderByDescending(k => k.Value).Take(25))
            {
                hits.TryGetValue(kv.Key, out int h);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,8:0.00} KB/snímek {1,5:0.0} %  (v {2} sn.)  {3}\n",
                    kv.Value / 1024.0 / frames, 100.0 * kv.Value / total, h, kv.Key);
            }
            Write(sb.ToString());
            Write(SlowFrames(first, last, 15f));
            Write(ThreadTotals(first, last, "Render Thread"));
            ProfilerDriver.enabled = false;
        }

        /// <summary>
        /// Kolo 6: snímky delší než <paramref name="limitMs"/> a v nich největší položky vlastního
        /// času (hlavní vlákno, spojené podle jména). Odpovídá na otázku „co je v té špičce".
        /// </summary>
        private static string SlowFrames(int first, int last, float limitMs)
        {
            var sb = new StringBuilder();
            var all = new List<float>();
            var sum = new Dictionary<string, double>();
            int slow = 0;
            for (int f = first; f <= last && f >= 0; f++)
            {
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                           HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                           HierarchyFrameDataView.columnSelfTime, false))
                {
                    if (v == null || !v.valid) continue;
                    float ms = v.frameTimeMs;
                    all.Add(ms);
                    if (ms <= limitMs) continue;
                    slow++;
                    var own = new Dictionary<string, double>();
                    WalkTime(v, v.GetRootItemID(), "", own);
                    foreach (var kv in own) { sum.TryGetValue(kv.Key, out double c); sum[kv.Key] = c + kv.Value; }
                    if (slow <= 12)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "  snímek {0}: {1:0.0} ms |", f, ms);
                        foreach (var kv in own.OrderByDescending(k => k.Value).Take(7))
                            sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0.0};", kv.Key, kv.Value);
                        sb.Append('\n');
                    }
                }
            }
            all.Sort();
            string head = string.Format(CultureInfo.InvariantCulture,
                "[Pomalé snímky] > {0:0} ms: {1} z {2} | medián {3:0.0} ms, p99 {4:0.0} ms, max {5:0.0} ms\n",
                limitMs, slow, all.Count,
                all.Count > 0 ? all[all.Count / 2] : 0f, all.Count > 0 ? all[(int)(all.Count * 0.99f)] : 0f,
                all.Count > 0 ? all[all.Count - 1] : 0f);
            var tail = new StringBuilder("  součet přes pomalé snímky (ms):");
            foreach (var kv in sum.OrderByDescending(k => k.Value).Take(15))
                tail.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0.0};", kv.Key, kv.Value);
            return head + sb + tail + "\n";
        }

        /// <summary>Kolo 18: vlastní čas na zadaném vlákně (např. Render Thread) za všechny snímky, ms/snímek.</summary>
        private static string ThreadTotals(int first, int last, string thread)
        {
            int ti = -1;
            for (int i = 0; i < 64 && ti < 0 && first >= 0; i++)
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(first + 1 <= last ? first + 1 : first, i,
                           HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
                    if (v != null && v.valid && v.threadName == thread) ti = i;
            if (ti < 0) return "[Vlákno] " + thread + " nenalezeno\n";
            var sum = new Dictionary<string, double>();
            int n = 0;
            for (int f = first; f <= last && f >= 0; f++)
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, ti,
                           HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
                {
                    if (v == null || !v.valid) continue;
                    n++;
                    WalkTime(v, v.GetRootItemID(), "", sum);
                }
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "[Vlákno] {0} (index {1}), {2} snímků, vlastní čas ms/snímek:", thread, ti, n);
            foreach (var kv in sum.OrderByDescending(k => k.Value).Take(15))
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0.00};", kv.Key, kv.Value / System.Math.Max(1, n));
            return sb.Append('\n').ToString();
        }

        private static void WalkTime(HierarchyFrameDataView v, int id, string path, Dictionary<string, double> own)
        {
            string name = id == v.GetRootItemID() ? "" : v.GetItemName(id);
            string here = path.Length == 0 ? name : path + " / " + name;
            float self = id == v.GetRootItemID() ? 0f : v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
            if (self > 0.3f)
            {
                string key = Tail(here, 2);
                own.TryGetValue(key, out double c);
                own[key] = c + self;
            }
            var kids = new List<int>();
            v.GetItemChildren(id, kids);
            foreach (int k in kids) WalkTime(v, k, here, own);
        }

        private static float Walk(HierarchyFrameDataView v, int id, string path,
                                  Dictionary<string, double> self, HashSet<string> seen, ref double total)
        {
            float mine = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
            if (mine <= 0f) return 0f;

            string name = id == v.GetRootItemID() ? "" : v.GetItemName(id);
            string here = path.Length == 0 ? name : path + " / " + name;

            var kids = new List<int>();
            v.GetItemChildren(id, kids);
            float childSum = 0f;
            foreach (int k in kids) childSum += Walk(v, k, here, self, seen, ref total);

            float own = mine - childSum;
            if (own > 0.5f)
            {
                string key = Tail(name == "GC.Alloc" ? path : here, 3);
                self.TryGetValue(key, out double cur);
                self[key] = cur + own;
                seen.Add(key);
                total += own;
            }
            return mine;
        }

        private static string Tail(string path, int n)
        {
            string[] parts = path.Split(new[] { " / " }, System.StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" / ", parts.Skip(System.Math.Max(0, parts.Length - n)));
        }

        private static void Write(string text)
        {
            Debug.Log(text);
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "gc_alloc.log"),
                System.DateTime.Now.ToString("HH:mm:ss") + " " + text + "\n");
        }
    }
}
