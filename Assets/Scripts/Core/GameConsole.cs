using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Orivilon.Inventory.Hotbar;
using Orivilon.Player;
using Orivilon.World.Biomes;
using Orivilon.World.Generation;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// Herní chat a konzole. Otevírá se klávesou <see cref="openKey"/>, příkaz začíná lomítkem.
    ///
    /// <para><b>Proč IMGUI a ne Canvas.</b> Konzole má fungovat okamžitě a v každé scéně,
    /// včetně té, do které se hráč teleportoval a která ještě nemá hotové UI. Canvas by
    /// znamenal prefab, reference v inspektoru a riziko, že se po přejmenování objektu
    /// tiše rozpadne – přesně to, co už jednou potkalo animátory GUI. IMGUI nemá ve scéně
    /// co ztratit.</para>
    ///
    /// <para>Komponenta se zakládá sama přes <see cref="RuntimeInitializeOnLoadMethod"/>,
    /// takže se do scény nic nepřidává. Otevřít jde jen ve hře – ne v menu, ne v pauze
    /// a ne během načítání.</para>
    /// </summary>
    public partial class GameConsole : MonoBehaviour
    {
        public static GameConsole Instance { get; private set; }

        /// <summary>Píše se právě do konzole? Ostatní systémy podle toho ignorují vstup.</summary>
        public static bool IsOpen => Instance != null && Instance.open;

        [Tooltip("Klávesa otevírající chat.")]
        public KeyCode openKey = KeyCode.T;

        [Tooltip("Klávesa otevírající chat rovnou s lomítkem (příkaz).")]
        public KeyCode commandKey = KeyCode.Slash;

        [Tooltip("Kolik řádků historie se drží.")]
        public int maxLines = 200;

        [Tooltip("Kolik řádků je vidět naráz.")]
        public int visibleLines = 12;

        [Tooltip("Jak dlouho po zavření chatu zůstane výpis na obrazovce, v sekundách.")]
        public float fadeAfter = 8f;

        private readonly List<string> log = new List<string>();
        private readonly List<string> history = new List<string>();
        private int historyIndex = -1;

        private string input = "";
        private bool open;
        private bool justOpened;
        private float lastPrint = -999f;

        private Coroutine search;
        private GUIStyle logStyle, fieldStyle;

        // ── životní cyklus ─────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("Game Console");
            go.AddComponent<GameConsole>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Konzole kreslí jen přes GUI.*, nikdy přes GUILayout. Bez tohohle by Unity
            // kvůli OnGUI každý snímek připravovalo layout (GUIUtility.BeginGUI) –
            // i se zavřenou konzolí to profil ukázal jako ~0,36 KB GC alokací na snímek.
            useGUILayout = false;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
        }

        private void Update()
        {
            TickPendingLook();
            if (!open)
            {
                GameManager gm = GameManager.instance;
                if (gm == null || gm.IsPaused || gm.isMenuOpen) return;
                if (SceneLoader.InputBlocked) return;

                if (Input.GetKeyDown(openKey)) Open("");
                else if (Input.GetKeyDown(commandKey)) Open("/");
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow)) StepHistory(-1);
            else if (Input.GetKeyDown(KeyCode.DownArrow)) StepHistory(1);
        }

        /// <summary>
        /// Otevře chat.
        ///
        /// <para><b>Vstup se blokuje přes <see cref="IsOpen"/>, NE přes
        /// <c>SceneLoader.InputBlocked</c>.</b> Původně to konzole dělala tak, že si původní
        /// hodnotu zapamatovala a při zavření ji vrátila. Jenže ta hodnota patří načítání
        /// scény a mění ji i někdo jiný: stačilo, aby se mezi otevřením a zavřením změnila,
        /// a konzole vrátila zastaralý stav – vstup pak zůstal zablokovaný napořád a
        /// projevilo se to jako „nejde kopat", protože nástroje se na ten příznak ptají.
        /// Dva vlastníci jednoho globálního příznaku jsou vždycky chyba; konzole si teď
        /// drží svůj vlastní.</para>
        /// </summary>
        private void Open(string prefix)
        {
            open = true;
            justOpened = true;
            input = prefix;
            historyIndex = -1;
        }

        private void Close()
        {
            open = false;
            input = "";
            historyIndex = -1;
        }

        private void StepHistory(int dir)
        {
            if (history.Count == 0) return;

            if (historyIndex < 0) historyIndex = history.Count;
            historyIndex = Mathf.Clamp(historyIndex + dir, 0, history.Count);

            input = historyIndex >= history.Count ? "" : history[historyIndex];
        }

        // ── výpis ──────────────────────────────────────────────────────

        public void Print(string line)
        {
            log.Add(line);
            if (log.Count > maxLines) log.RemoveRange(0, log.Count - maxLines);
            lastPrint = Time.unscaledTime;
        }

        private void Ok(string line) => Print("<color=#b6e3a0>" + line + "</color>");
        private void Error(string line) => Print("<color=#ff9a8a>" + line + "</color>");
        private void Note(string line) => Print("<color=#9aa4b2>" + line + "</color>");

        // ── kreslení ───────────────────────────────────────────────────

        private void OnGUI()
        {
            bool showLog = open || Time.unscaledTime - lastPrint < fadeAfter;
            if (!showLog) return;

            EnsureStyles();

            Event e = Event.current;
            if (open && e.type == EventType.KeyDown)
            {
                if (justOpened)
                {
                    // Klávesa, kterou se konzole otevřela, dorazí i sem – bez tohohle
                    // by se „t" objevilo rovnou v poli a hráč by ho pokaždé mazal.
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Close(); e.Use(); return;
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Submit(); e.Use();
                    if (!open) return;
                }
            }

            float w = Mathf.Min(Screen.width - 20f, 960f);
            float lineH = logStyle.lineHeight + 3f;
            float inputY = Screen.height - 34f;
            float logH = Mathf.Min(visibleLines, Mathf.Max(1, log.Count)) * lineH + 6f;
            float logY = inputY - logH - 8f;

            if (log.Count > 0)
            {
                Fill(new Rect(6f, logY - 4f, w + 8f, logH + 8f), new Color(0f, 0f, 0f, 0.55f));

                int start = Mathf.Max(0, log.Count - visibleLines);
                for (int i = start; i < log.Count; i++)
                    GUI.Label(new Rect(12f, logY + (i - start) * lineH, w, lineH), log[i], logStyle);
            }

            if (open)
            {
                Fill(new Rect(6f, inputY - 5f, w + 8f, 28f), new Color(0f, 0f, 0f, 0.78f));

                GUI.SetNextControlName("orivilonConsoleInput");
                input = GUI.TextField(new Rect(12f, inputY, w, 22f), input, 256, fieldStyle);

                if (GUI.GetNameOfFocusedControl() != "orivilonConsoleInput")
                    GUI.FocusControl("orivilonConsoleInput");
            }

            if (e.type == EventType.Repaint) justOpened = false;
        }

        private static void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>GUI.skin je platný jen uvnitř OnGUI, takže se styly staví až tady.</summary>
        private void EnsureStyles()
        {
            if (logStyle != null) return;

            logStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = false,
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(2, 2, 0, 0),
            };
            logStyle.normal.textColor = Color.white;

            fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
            fieldStyle.normal.textColor = Color.white;
            fieldStyle.focused.textColor = Color.white;
        }

        // ── příkazy ────────────────────────────────────────────────────

        private void Submit()
        {
            string line = input.Trim();
            input = "";
            historyIndex = -1;

            if (line.Length == 0) { Close(); return; }

            history.Add(line);
            if (history.Count > 50) history.RemoveAt(0);

            if (line[0] == '/')
            {
                Print("<color=#8fb8ff>" + line + "</color>");
                Execute(line.Substring(1));
            }
            else
            {
                Print(line);
            }

            Close();
        }

        /// <summary>
        /// Příkaz zvenčí (editorový most <c>Assets/Editor/RemoteBridge.cs</c>, jen diagnostika).
        /// Chová se jako napsaný příkaz, jen bez otevírání konzole.
        /// </summary>
        public void RunRemote(string line)
        {
            line = (line ?? "").Trim();
            if (line.StartsWith("/")) line = line.Substring(1);
            if (line.Length == 0) return;
            Print("<color=#8fb8ff>/" + line + "</color>");
            Execute(line);
        }

        /// <summary>Běží dlouhý příkaz (okruh, bench, hledání)?</summary>
        public bool IsBusy => search != null;

        /// <summary>Kolo 25: řádky konzole (jen čtení) – editorový most je ukládá pro kontrolu textů.</summary>
        public IReadOnlyList<string> Lines => log;

        private void Execute(string cmd)
        {
            string[] a = cmd.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return;

            switch (a[0].ToLowerInvariant())
            {
                case "help": case "?": case "napoveda": Help(); break;
                case "clear": case "cls": log.Clear(); break;
                case "arcanum": CmdArcanum(a); break;
                case "pos": case "kde": CmdPos(); break;
                case "seed": CmdSeed(a); break;
                case "biome": case "biom":
                    if (a.Length > 1 && IsRegionSub(a[1])) CmdRegions(a);
                    else if (a.Length > 1) Error("Unknown /biome option " + LeftQuote + a[1] + RightQuote + ". Use /biome list, /biome tp <biome-id> or /biome where.");
                    else CmdBiome();
                    break;
                case "tp": case "teleport": CmdTeleport(a); break;
                case "find": case "najdi": CmdFind(a); break;
                case "terrain": case "teren": CmdTerrain(a); break;
                case "dig": case "kop": CmdDig(); break;
                case "look": case "pohled": CmdLook(a); break;
                case "time": case "cas": CmdTime(a); break;
                case "veg": case "vegetation": CmdVegetation(a); break;
                case "atmo": case "atmosphere": CmdAtmosphere(a); break;
                case "stop": CmdStop(); break;
                case "voda": case "water": CmdWater(a); break;
                case "pauza": case "pause": CmdPause(); break;
                case "profil": CmdProfile(a); break;
                case "props": case "propy": CmdProps(a); break;
                case "art": case "umeni": CmdArt(a); break;
                case "art9": CmdArt9(a); break;
                case "folaz": case "foliage": CmdFoliage(a); break;
                case "kmen": case "trunk": CmdTrunk(a); break;
                case "pruhy": case "bands": CmdBands(a); break;
                case "let": case "fly": CmdFly(a); break;
                case "vyhled": case "vista": CmdVista(a); break;
                case "biomy": case "regiony": case "regions": CmdRegions(a); break;
                default: Error("Unknown command " + LeftQuote + a[0] + RightQuote + ". Try /help."); break;
            }
        }

        /// <summary>
        /// Otevře pauzu (s tlačítky Resume / Main Menu) jinou cestou než Escape a vypíše,
        /// proč by Escape nemusel projít: GameManager.Update ho ignoruje při
        /// SceneLoader.IsLoading, a pauza se nenastaví před dokončením spawnu.
        /// </summary>
        /// <summary>
        /// Požadavek pro editorový nástroj GcAllocProfiler (Assets/Editor). Běhový kód
        /// na editor sahat nesmí, tak jen nastaví text a editor si ho vyzvedne.
        /// </summary>
        public static string EditorRequest;

        /// <summary>/profil start | /profil dump [štítek] – nahrávání profileru a výpis zdrojů GC Alloc.</summary>
        /// <summary>
        /// Osazení (kolo 5): statistika podle biomů, audit proti skutečné geometrii,
        /// vypnutí/zapnutí pro měření výkonu a rozměry prefabů.
        /// </summary>
        private void CmdProps(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "stat";
            float r = 90f;
            if (a.Length > 2 && TryNumber(a[2], out float rr)) r = Mathf.Clamp(rr, 8f, 400f);

            switch (sub)
            {
                case "stat":
                {
                    string text = PropAudit.Stat(t, p.position, r);
                    Diag(text);
                    foreach (string line in text.Split('\n')) Note(line);
                    break;
                }
                case "audit":
                {
                    string text = PropAudit.Run(t, p.position, Mathf.Min(r, 160f), out int problems);
                    Diag(text);
                    if (problems == 0) Ok("props audit: 0 problems"); else Error("props audit: " + problems + " problems");
                    foreach (string line in text.Split('\n')) Note(line);
                    break;
                }
                case "off":
                    Ok("props OFF, removed from " + t.SetPropsSuspended(true) + " columns");
                    Diag("[props] OFF");
                    break;
                case "on":
                    t.SetPropsSuspended(false);
                    Ok("props ON (new columns only)");
                    Diag("[props] ON");
                    break;
                case "tvary": case "shapes":
                {
                    var list = new List<World.Spawning.ObjectSpawner>();
                    t.CollectPropSpawners(list);
                    if (list.Count == 0) { Error("no spawner"); break; }
                    var sp = list[0];
                    var sb = new System.Text.StringBuilder("[props tvary] druh: r / minY / h (měřítko 1) -> ve světě při střední velikosti\n");
                    for (int i = 0; i < 400; i++)   // kolo 19: i položky registru za 133
                    {
                        var so = sp.GetSpawnable(i);
                        if (so == null || so.prefab == null) continue;
                        var sh = sp.GetShape(i);
                        float sc = (so.scaleRange.x + so.scaleRange.y) * 0.5f * sp.GlobalScale;
                        sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,3} {1,-22} {2:0.00}/{3:0.00}/{4:0.00} -> r {5:0.0} m, h {6:0.0} m\n",
                            i, so.name, sh.radius, sh.minY, sh.height, sh.radius * sc, sh.height * sc * (so.category == World.Spawning.SpawnCategory.Trees ? 1.3f : 1f));
                    }
                    Diag(sb.ToString());
                    Ok("shapes -> _reports/diag.log");
                    break;
                }
                case "bench":
                {
                    if (search != null) { Error("Something is already running (/stop)."); break; }
                    bool on = !(a.Length > 2 && (a[2] == "off" || a[2] == "0"));
                    search = StartCoroutine(PropsBench(t, p, on));
                    break;
                }
                case "jeskyne":
                    if (a.Length > 2) World.Generation.EcologyPlacer.CaveLipCheck = a[2] != "off" && a[2] != "0";
                    Ok("kontrola vstupu jeskyně za hranou sloupce: " + (World.Generation.EcologyPlacer.CaveLipCheck ? "ZAP" : "VYP") + ", odmítnuto " + World.Generation.EcologyPlacer.CaveLipRejects);
                    Diag("[props jeskyne] " + (World.Generation.EcologyPlacer.CaveLipCheck ? "ZAP" : "VYP") + " odmítnuto " + World.Generation.EcologyPlacer.CaveLipRejects);
                    break;
                case "bod":
                {
                    // Kolo 12c: /props bod x z – vrstvy colliderů a objekty v jednom místě.
                    if (a.Length > 3 && TryNumber(a[2], out float bx) && TryNumber(a[3], out float bz))
                    {
                        float bs = 0.5f;
                        if (a.Length > 4) TryNumber(a[4], out bs);
                        Diag(PropAudit.Probe(t, bx, bz, Mathf.Clamp(bs, 0.05f, 2f)));
                        Ok("props bod -> diag.log");
                    }
                    else Error("Usage: /props bod x z");
                    break;
                }
                case "pool":
                    if (a.Length > 2) World.Generation.VoxelProps.Pooling = a[2] != "off" && a[2] != "0";
                    Ok("pool spawnerů: " + (World.Generation.VoxelProps.Pooling ? "ZAP" : "VYP") + ", znovupoužito " + World.Generation.VoxelProps.PooledReuses);
                    Diag("[props pool] " + (World.Generation.VoxelProps.Pooling ? "ZAP" : "VYP") + " reuses " + World.Generation.VoxelProps.PooledReuses);
                    break;
                case "pravidla": case "rules":
                    if (a.Length > 2) World.Generation.EcologyPlacer.Rules6 = a[2] != "off" && a[2] != "0";
                    Ok("pravidla kola 6: " + (World.Generation.EcologyPlacer.Rules6 ? "ZAP" : "VYP") + " (platí pro nově osazené sloupce)");
                    Diag("[props pravidla] " + (World.Generation.EcologyPlacer.Rules6 ? "ZAP" : "VYP"));
                    break;
                case "kaminky": case "pebbles":
                    if (a.Length > 2) World.Generation.EcologyPlacer.Pebbles = a[2] != "off" && a[2] != "0";
                    Ok("sebratelné kamínky: " + (World.Generation.EcologyPlacer.Pebbles ? "ZAP" : "VYP") + " (platí pro nově osazené sloupce)");
                    Diag("[props kaminky] " + (World.Generation.EcologyPlacer.Pebbles ? "ZAP" : "VYP"));
                    break;
                case "sber": case "pickup":
                    CmdPickupDiag(a.Length > 2 ? a[2] : "");
                    break;
                case "kompozice": case "compose":
                    if (a.Length > 2) World.Generation.EcologyPlacer.Compose8 = a[2] != "off" && a[2] != "0";
                    Ok("kompozice kola 8: " + (World.Generation.EcologyPlacer.Compose8 ? "ZAP" : "VYP") + " (platí pro nově osazené sloupce)");
                    Diag("[props kompozice] " + (World.Generation.EcologyPlacer.Compose8 ? "ZAP" : "VYP"));
                    break;
                case "daleko": case "far":
                {
                    // Kolo 31: A/B vzdálených jehličnanů (/props daleko jehlic on|off) – ostatní stromy beze změny.
                    if (a.Length > 3 && (a[2] == "jehlic" || a[2] == "conifer"))
                    { World.Spawning.TreeProxies.ConiferProxies = a[3] != "off" && a[3] != "0"; World.Spawning.TreeProxies.MarkDirty(); }
                    else if (a.Length > 3 && (a[2] == "orez" || a[2] == "cull"))
                    { World.Spawning.TreeProxies.Cull = a[3] != "off" && a[3] != "0"; World.Spawning.TreeProxies.MarkDirty(); }
                    else if (a.Length > 2) World.Spawning.TreeProxies.Enabled = a[2] != "off" && a[2] != "0";
                    if (!World.Spawning.TreeProxies.Enabled) World.Spawning.TreeProxies.Clear();
                    string ft = string.Format(CultureInfo.InvariantCulture, "[props daleko] {0}, jehličnany {4}, ořez {5}: {1} stromů v {2} sloupcích, {3} dávek kreslení",
                        World.Spawning.TreeProxies.Enabled ? "ZAP" : "VYP", World.Spawning.TreeProxies.Instances,
                        World.Spawning.TreeProxies.Columns, World.Spawning.TreeProxies.DrawGroups, World.Spawning.TreeProxies.ConiferProxies ? "ZAP" : "VYP", World.Spawning.TreeProxies.Cull ? "ZAP" : "VYP");
                    Diag(ft); Ok(ft);
                    break;
                }
                case "jizda": case "ride":
                    if (search != null) { Error("Something is already running (/stop)."); break; }
                    search = StartCoroutine(PropsRide(t, p, a.Length > 2 && TryNumber(a[2], out float steps) ? (int)steps : 180));
                    break;
                case "otisk": case "fingerprint":
                {
                    string fp = t.PropFingerprint(p.position, a.Length > 2 && TryNumber(a[2], out float fr) ? fr : 100f);
                    Diag(fp); Ok(fp.Split('\n')[0]);
                    break;
                }
                case "tour": case "okruh":
                    if (search != null) { Error("Something is already running (/stop)."); break; }
                    search = StartCoroutine(PropsTour(t, p));
                    break;
                case "render":
                {
                    // Kolo 18: rozpad ceny vykreslení (snímky, GPU/CPU čas, dávky, trojúhelníky, stínové objekty).
                    if (search != null) { Error("Something is already running (/stop)."); break; }
                    float secs = a.Length > 2 && TryNumber(a[2], out float rs2) ? Mathf.Clamp(rs2, 2f, 60f) : 10f;
                    search = StartCoroutine(RenderStats(secs, a.Length > 3 ? a[3] : ""));
                    break;
                }
                case "mlhastromy": case "treefog":
                {
                    // Kolo 26 (A/B): stromy za koncem mlhy – off = původní stav, číslo = pásmo viditelnosti pro dither.
                    if (a.Length > 2)
                        World.Spawning.ObjectSpawner.TreeFogFade = a[2] == "off" || a[2] == "0" ? 0f
                            : TryNumber(a[2], out float tf) ? Mathf.Clamp(tf, 0f, 1f) : 0.08f;
                    World.Spawning.ObjectSpawner.UpdateTrunkLook();
                    string tfs = string.Format(CultureInfo.InvariantCulture, "[props mlhastromy] pásmo {0:0.00}, mlha {1} {2:0}-{3:0} m",
                        World.Spawning.ObjectSpawner.TreeFogFade, RenderSettings.fogMode, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance);
                    Diag(tfs); Ok(tfs);
                    break;
                }
                case "stinkoruny": case "canopyshadow":
                {
                    // Kolo 18: stín koruny ze zjednodušené kopie. on/off = pro nové stromy i ty, co stojí; číslo = podíl karet.
                    string arg = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                    var all = FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    int changed = 0;
                    if (arg == "off" || arg == "0")
                    {
                        World.Spawning.ObjectSpawner.CanopyShadowProxy = false;
                        foreach (var lg in all) if (World.Spawning.ObjectSpawner.RemoveCanopyShadow(lg.gameObject)) changed++;
                    }
                    else if (arg.Length > 0)
                    {
                        if (arg != "on" && TryNumber(arg, out float keep)) World.Spawning.ObjectSpawner.CanopyShadowKeep = Mathf.Clamp01(keep);
                        World.Spawning.ObjectSpawner.CanopyShadowProxy = true;
                        foreach (var lg in all)
                        {
                            World.Spawning.ObjectSpawner.RemoveCanopyShadow(lg.gameObject);
                            if (World.Spawning.ObjectSpawner.ApplyCanopyShadow(lg.gameObject)) changed++;
                        }
                    }
                    string cs = string.Format(CultureInfo.InvariantCulture, "[props stinkoruny] {0}, podíl karet {1:0.00}, změněno {2}, stromů se stínovou kopií {3}",
                        World.Spawning.ObjectSpawner.CanopyShadowProxy ? "ZAP" : "VYP", World.Spawning.ObjectSpawner.CanopyShadowKeep, changed,
                        World.Spawning.ObjectSpawner.CanopyShadowCount);
                    Diag(cs); Ok(cs);
                    break;
                }
                case "stiny": case "shadowaudit":
                {
                    // Kolo 19: kontrola stínu koruny z kola 18 – dvojitý/visící stín, LOD, osiřelý Leaves_Shadow.
                    string txt = CanopyShadowAudit(p.position, a.Length > 2 && TryNumber(a[2], out float sr) ? sr : 250f, a.Length > 3 ? a[3] : "");
                    Diag(txt); Ok(txt.Split('\n')[0]);
                    break;
                }
                case "kry": case "floes":
                {
                    // Kolo 20: pochozí kry (A/B osazení ker: /props kry off|on – platí pro nově osazené sloupce).
                    if (a.Length > 2 && (a[2] == "off" || a[2] == "on")) { World.Generation.EcologyPlacer.Floes = a[2] == "on"; Ok("kry " + a[2]); break; }
                    string txt = FloeWalkAudit(t, p.position, a.Length > 2 && TryNumber(a[2], out float fr2) ? fr2 : 300f);
                    Diag(txt); Ok(txt);
                    break;
                }
                case "pasti": case "traps":
                {
                    // Kolo 19: ruiny a vápencové portály – úzké škvíry mezi kusy (past pro hráče) a průchodnost oblouků.
                    string txt = RuinTrapAudit(p, a.Length > 2 && TryNumber(a[2], out float pr2) ? pr2 : 300f);
                    Diag(txt); Ok(txt.Split('\n')[0]);
                    break;
                }
                case "kacej": case "fell":
                    // Kolo 19: skutečné kácení nejbližšího stromu se stínovou kopií (cesta HarvestableObject.TryHarvest).
                    if (search != null) { Error("Something is already running (/stop)."); break; }
                    search = StartCoroutine(FellTest(p.position, a.Length > 2 ? a[2] : ""));
                    break;
                case "skryj": case "hide":
                    // Kolo 18: diagnostické A/B jen pro tenhle běh – skryje část blízkých stromů (nic neukládá).
                    Ok(PropsHide(a.Length > 2 ? a[2].ToLowerInvariant() : "nic"));
                    break;
                default:
                    Error("props stat|audit [r] | off | on | tvary | tour");
                    break;
            }
        }

        /// <summary>
        /// Okruh po kritických místech u vody (řeka, jezero, moře, horská řeka, vodopád) od
        /// výchozí pozice: na každém audit osazení r=150 m a audit vody r=60 m, na startu
        /// navíc LOD audit r=700 m. Všechno do _reports/diag.log.
        /// </summary>
        private IEnumerator PropsTour(VoxelTerrain t, Transform p)
        {
            Vector3 home = p.position;
            string[] names = { "start", "reka", "jezero", "more", "hory", "vodopad", "usti" };
            int total = 0;
            Diag(string.Format(CultureInfo.InvariantCulture, "[props tour] seed {0}, start ({1:0},{2:0})", t.WorldSeed, home.x, home.z));
            for (int k = -1; k <= 5; k++)
            {
                string name = names[k + 1];
                if (k >= 0)
                {
                    if (!t.FindWater(home, k, 0f, 3000f, out Vector3 at))
                    {
                        Diag("[props tour] " + name + ": nenalezeno do 3 km");
                        continue;
                    }
                    t.Teleport(new Vector3(at.x, 0f, at.z));
                }
                yield return new WaitForSeconds(2f);
                float t0 = Time.realtimeSinceStartup;
                while ((t.IsTeleporting || !t.IsSettled) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
                yield return new WaitForSeconds(4f);   // propy se sázejí 1 sloupec/snímek za zveřejněním

                string text = PropAudit.Run(t, p.position, 150f, out int pr);
                total += pr;
                WaterAudit.Result w = WaterAudit.Run(t, p.position, 60f);
                string water = string.Format(CultureInfo.InvariantCulture,
                    "  voda r=60: mokrých {0}, visící hrana {1} (max {2:0.00} m), skoky {3}, ostrůvky {4}, díry {5} | přesně na hranách: visí {6} z {7} (max {8:0.00} m) | strmé trojúhelníky {9} ({10:0.0} m², max {11:0}°)",
                    w.visibleWet, w.floatingEdges, w.worstFloat, w.steps, w.islands, w.holes, w.edgeFloat, w.freeEdges, w.edgeWorst, w.steepTris, w.steepArea, w.steepMaxDeg);
                Diag("[props tour] " + name + "\n" + text + water);
                if (k < 0) Diag("[props tour] LOD " + WaterAudit.RunLod(t, p.position, 700f));
                Note(name + ": props problems " + pr + ", water floating " + w.floatingEdges + ", holes " + w.holes);
                yield return null;
            }
            Diag("[props tour] konec, problémů osazení celkem " + total);
            Ok("props tour done, problems " + total);
            search = null;
        }

        private struct FrameStats
        {
            public int frames, over33, over50, gc;
            public float sum, worst;
            public void Add(float ms) { frames++; sum += ms; if (ms > worst) worst = ms; if (ms > 33.3f) over33++; if (ms > 50f) over50++; }
            public string Text(float seconds) => string.Format(CultureInfo.InvariantCulture,
                "{0:0} s, {1} snímků, průměr {2:0.00} ms, nejhorší {3:0.0} ms, >33 ms {4}×, >50 ms {5}×, GC sběrů {6}",
                seconds, frames, frames > 0 ? sum / frames : 0f, worst, over33, over50, gc);
        }

        /// <summary>
        /// Srovnávací běh osazení: klid, pět skoků do nových regionů a „průjezd" (180 kroků po 6 m
        /// každých 0,25 s, tj. 24 m/s). Snímky a GC se měří zvlášť pro skoky a průjezd; u průjezdu
        /// se navíc počítá, jak blízko hráče se objevují nově osazené sloupce (pop-in).
        /// Pro srovnání pustit jednou s „off" a jednou bez, vždy z čerstvého Play Mode.
        /// </summary>
        private IEnumerator PropsBench(VoxelTerrain t, Transform p, bool propsOn)
        {
            if (!propsOn) t.SetPropsSuspended(true);
            Diag(string.Format(CultureInfo.InvariantCulture, "[props bench] seed {0}, propy {1}", t.WorldSeed, propsOn ? "ZAP" : "VYP"));
            var jumps = new FrameStats();
            var walk = new FrameStats();
            var idle = new FrameStats();

            // klid 15 s
            int gc0 = System.GC.CollectionCount(0);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 15f) { yield return null; idle.Add(Time.unscaledDeltaTime * 1000f); }
            idle.gc = System.GC.CollectionCount(0) - gc0;
            Diag("[props bench] klid: " + idle.Text(15f));

            // skoky
            Vector2[] route = { new Vector2(1500, 0), new Vector2(1500, 1500), new Vector2(-1500, 1500), new Vector2(-1500, -1500), new Vector2(0, -1500) };
            gc0 = System.GC.CollectionCount(0);
            t0 = Time.realtimeSinceStartup;
            foreach (Vector2 r in route)
            {
                t.Teleport(new Vector3(r.x, 0f, r.y));
                float s0 = Time.realtimeSinceStartup;
                yield return null;
                while ((t.IsTeleporting || !t.IsSettled) && Time.realtimeSinceStartup - s0 < 60f) { yield return null; jumps.Add(Time.unscaledDeltaTime * 1000f); }
                float s1 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s1 < 8f) { yield return null; jumps.Add(Time.unscaledDeltaTime * 1000f); }
            }
            jumps.gc = System.GC.CollectionCount(0) - gc0;
            Diag("[props bench] skoky: " + jumps.Text(Time.realtimeSinceStartup - t0));

            // průjezd
            VoxelTerrain.PropSpawnCount = VoxelTerrain.PropSpawnNear100 = VoxelTerrain.PropSpawnNear150 = 0;
            gc0 = System.GC.CollectionCount(0);
            t0 = Time.realtimeSinceStartup;
            Vector3 pos = p.position;
            for (int i = 0; i < 180; i++)
            {
                pos.x += 6f;
                t.Teleport(new Vector3(pos.x, 0f, pos.z));
                float s0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s0 < 0.25f) { yield return null; walk.Add(Time.unscaledDeltaTime * 1000f); }
            }
            float s2 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - s2 < 5f) { yield return null; walk.Add(Time.unscaledDeltaTime * 1000f); }
            walk.gc = System.GC.CollectionCount(0) - gc0;
            Diag("[props bench] průjezd 1080 m: " + walk.Text(Time.realtimeSinceStartup - t0)
                 + string.Format(CultureInfo.InvariantCulture, " | nově osazené sloupce {0}, z toho střed < 100 m: {1}, 100–150 m: {2}",
                     VoxelTerrain.PropSpawnCount, VoxelTerrain.PropSpawnNear100, VoxelTerrain.PropSpawnNear150));
            Ok("props bench done");
            search = null;
        }

        /// <summary>
        /// Kolo 6: jen „průjezd" z benche (kroky po 6 m každých 0,25 s = 24 m/s), bez klidu
        /// a skoků – pro profil špiček. Počítá snímky nad 25 a 33 ms a pop-in sloupců.
        /// </summary>
        /// <summary>
        /// Kolo 6: A/B audit opravy schodu hladiny. Pro každé místo, kde oprava zasáhla,
        /// postaví sloupce jednou bez opravy a jednou s ní (odskok 6 km a zpět) a změří
        /// vodu r = 25 m ze stejného středu. Všechno do _reports/diag.log.
        /// </summary>
        private IEnumerator LipAB(VoxelTerrain t, Transform p, List<Vector2> extra, bool sillMode)
        {
            var sites = new List<Vector2>(extra);
            if (!sillMode)
            foreach (Vector2 ls in WaterSurface.LipSites)
            {
                bool dup = false;
                foreach (Vector2 e in sites) dup |= (e - ls).sqrMagnitude < 24f * 24f;
                if (!dup) sites.Add(ls);
            }
            string tag = sillMode ? "[Voda prahab]" : "[Voda lipab]";
            Diag(string.Format(CultureInfo.InvariantCulture, "{0} seed {1}, {2} míst", tag, t.WorldSeed, sites.Count));
            bool keep = WaterSurface.LipFix;
            bool keepSill = t.HydroSillOn;
            foreach (Vector2 s in sites)
            {
                string line = string.Format(CultureInfo.InvariantCulture, "{0} ({1:0},{2:0})", tag, s.x, s.y);
                for (int pass = 0; pass < 2; pass++)
                {
                    if (sillMode) t.SetHydroSill(pass == 1); else WaterSurface.LipFix = pass == 1;
                    t.Teleport(new Vector3(s.x + 6000f, 0f, s.y + 6000f));
                    yield return new WaitForSeconds(1f);
                    float t0 = Time.realtimeSinceStartup;
                    while ((t.IsTeleporting || !t.IsSettled) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
                    t.Teleport(new Vector3(s.x, 0f, s.y));
                    yield return new WaitForSeconds(1f);
                    t0 = Time.realtimeSinceStartup;
                    while ((t.IsTeleporting || !t.IsSettled) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
                    yield return new WaitForSeconds(1f);
                    WaterAudit.Result w = WaterAudit.Run(t, new Vector3(s.x, 0f, s.y), 25f);
                    line += string.Format(CultureInfo.InvariantCulture,
                        " | {0}: mokrých {1}, visí mřížka {2} ({3:0.00} m), přesně na hranách {4} ({5:0.00} m), skoky {6}, ostrůvky {7}, díry {8}, strmé {9} ({10:0.0} m², {11:0}°)",
                        pass == 1 ? "S opravou" : "BEZ", w.visibleWet, w.floatingEdges, w.worstFloat, w.edgeFloat, w.edgeWorst, w.steps, w.islands, w.holes, w.steepTris, w.steepArea, w.steepMaxDeg);
                }
                Diag(line);
            }
            WaterSurface.LipFix = keep;
            t.SetHydroSill(keepSill);
            Ok("lipab done");
            search = null;
        }

        private IEnumerator PropsRide(VoxelTerrain t, Transform p, int steps)
        {
            var walk = new FrameStats();
            int over25 = 0;
            VoxelTerrain.PropSpawnCount = VoxelTerrain.PropSpawnNear100 = VoxelTerrain.PropSpawnNear150 = 0;
            VoxelTerrain.PopInReset();
            var probeLog = new System.Text.StringBuilder();
            int gc0 = System.GC.CollectionCount(0);
            float t0 = Time.realtimeSinceStartup;
            Vector3 pos = p.position;
            // Kolo 22 diagnostika: Teleport se během rozběhnutého teleportu ignoruje (čeká na collider
            // pod cílem) – pak hráč stojí a další krok skočí o víc než 6 m. Měří se, jestli k tomu došlo.
            int skipped = 0, skipRun = 0, skipRunMax = 0; float jumpMax = 0f, jumpAtT = 0f, lastX = p.position.x;
            var skipLog = new System.Text.StringBuilder();
            for (int i = 0; i < steps; i++)
            {
                pos.x += 6f;
                if (t.IsTeleporting) { skipped++; skipRun++; if (skipRun > skipRunMax) skipRunMax = skipRun; }
                else
                {
                    if (skipRun > 0 && skipLog.Length < 600)
                        skipLog.AppendFormat(CultureInfo.InvariantCulture, " t{0:0.00} stál {1} kroků u x{2:0} → skok na x{3:0};",
                            Time.realtimeSinceStartup - t0, skipRun, p.position.x, pos.x);
                    skipRun = 0;
                }
                t.Teleport(new Vector3(pos.x, 0f, pos.z));
                float s0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s0 < 0.25f)
                {
                    yield return null;
                    float ms = Time.unscaledDeltaTime * 1000f;
                    walk.Add(ms);
                    if (ms > 25f) over25++;
                }
                float jx = Mathf.Abs(p.position.x - lastX);
                if (jx > jumpMax) { jumpMax = jx; jumpAtT = Time.realtimeSinceStartup - t0; }
                lastX = p.position.x;
                // Kolo 22 diagnostika pop-inu: kdy chybí LOD0 sloupce do 150 m a proč.
                string pr = t.PopInProbe(out int miss);
                if (miss > 0 && probeLog.Length < 3000)
                    probeLog.AppendFormat(CultureInfo.InvariantCulture, " t{0:0.00} x{1:0}: {2};", Time.realtimeSinceStartup - t0, pos.x, pr);
            }
            walk.gc = System.GC.CollectionCount(0) - gc0;
            Diag("[props jizda] " + steps * 6 + " m: " + walk.Text(Time.realtimeSinceStartup - t0)
                 + string.Format(CultureInfo.InvariantCulture, ", >25 ms {0}× | nově osazené sloupce {1}, střed < 100 m: {2}, 100–150 m: {3} | vzdálené stromy {4} ve {5} sloupcích",
                     over25, VoxelTerrain.PropSpawnCount, VoxelTerrain.PropSpawnNear100, VoxelTerrain.PropSpawnNear150,
                     World.Spawning.TreeProxies.Instances, World.Spawning.TreeProxies.Columns)
                 + string.Format(CultureInfo.InvariantCulture, "\n  [pop-in <150 m] A dluh z doby před jízdou {0}, B terén sám zveřejněn <150 m {1}, C zpoždění osazení {2} (max {3:0.00} s)\n  ",
                     VoxelTerrain.PopInA, VoxelTerrain.PopInB, VoxelTerrain.PopInC, VoxelTerrain.PopInMaxLagC)
                 + VoxelTerrain.PopInLog.ToString() + "\n  [stav LOD0 <150 m, jen kroky s chybějícími]" + probeLog.ToString()
                 + string.Format(CultureInfo.InvariantCulture, "\n  [teleport jízdy] přeskočeno kroků {0} (nejdelší řada {1}), největší posun hráče za krok {2:0.0} m v t{3:0.00};",
                     skipped, skipRunMax, jumpMax, jumpAtT) + skipLog.ToString() + "\n  [čekání teleportu]" + VoxelTerrain.TeleportStalls.ToString());
            Ok("props ride done");
            search = null;
        }

        private static readonly string[] RenderCounterNames =
            { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Triangles Count", "Vertices Count", "Shadow Casters Count" };

        /// <summary>Kolo 18: /props render [s] [štítek] – snímky, FrameTimingManager (CPU/GPU) a čítače vykreslení do diag.log.</summary>
        private IEnumerator RenderStats(float seconds, string label)
        {
            var recs = new Unity.Profiling.ProfilerRecorder[RenderCounterNames.Length];
            for (int i = 0; i < recs.Length; i++)
                recs[i] = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, RenderCounterNames[i]);
            var sum = new double[recs.Length];
            var max = new long[recs.Length];
            var fs = new FrameStats();
            var ft = new FrameTiming[1];
            double gpuSum = 0, mainSum = 0, rtSum = 0; double gpuMax = 0, rtMax = 0; int ftN = 0;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return null;
                fs.Add(Time.unscaledDeltaTime * 1000f);
                for (int i = 0; i < recs.Length; i++)
                {
                    if (!recs[i].Valid) continue;
                    long v = recs[i].LastValue;
                    sum[i] += v; if (v > max[i]) max[i] = v;
                }
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].gpuFrameTime > 0)
                {
                    ftN++;
                    gpuSum += ft[0].gpuFrameTime; mainSum += ft[0].cpuMainThreadFrameTime; rtSum += ft[0].cpuRenderThreadFrameTime;
                    if (ft[0].gpuFrameTime > gpuMax) gpuMax = ft[0].gpuFrameTime;
                    if (ft[0].cpuRenderThreadFrameTime > rtMax) rtMax = ft[0].cpuRenderThreadFrameTime;
                }
            }
            var sb = new System.Text.StringBuilder();
            Transform pl = Camera.main != null ? Camera.main.transform : null;
            sb.AppendFormat(CultureInfo.InvariantCulture, "[props render] {0} pozice ({1:0},{2:0}) | {3}\n  ", label,
                pl != null ? pl.position.x : 0f, pl != null ? pl.position.z : 0f, fs.Text(seconds));
            sb.AppendFormat(CultureInfo.InvariantCulture, "FrameTiming ({0} sn.): GPU průměr {1:0.00} ms, max {2:0.0} | CPU main {3:0.00} | render vlákno {4:0.00}, max {5:0.0}\n  čítače (průměr / max):",
                ftN, ftN > 0 ? gpuSum / ftN : 0, gpuMax, ftN > 0 ? mainSum / ftN : 0, ftN > 0 ? rtSum / ftN : 0, rtMax);
            for (int i = 0; i < recs.Length; i++)
            {
                if (!recs[i].Valid) { sb.Append(' ').Append(RenderCounterNames[i]).Append(" (-);"); continue; }
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0} / {2};", RenderCounterNames[i].Replace(" Count", ""),
                    fs.frames > 0 ? sum[i] / fs.frames : 0, max[i]);
                recs[i].Dispose();
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, " | QualitySettings: lodBias {0}, stíny {1} kaskád, vzdálenost stínů {2:0} m", QualitySettings.lodBias,
                QualitySettings.shadowCascades, QualitySettings.shadowDistance);
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (urp != null) sb.Append(", pipeline ").Append(urp.name);
            Diag(sb.ToString());
            Ok("props render done");
            search = null;
        }

        /// <summary>Kolo 18: skryje vybrané renderery blízkých stromů (jen diagnostika; „nic" vrátí vše).</summary>
        private static readonly List<(Renderer r, bool en, UnityEngine.Rendering.ShadowCastingMode sh)> hidden =
            new List<(Renderer, bool, UnityEngine.Rendering.ShadowCastingMode)>();
        private static readonly List<LODGroup> forcedLods = new List<LODGroup>();
        private static readonly List<(Renderer r, Material m)> matSwaps = new List<(Renderer, Material)>();
        private string PropsHide(string what)
        {
            foreach (var h in hidden) if (h.r != null) { h.r.enabled = h.en; h.r.shadowCastingMode = h.sh; }
            int restored = hidden.Count;
            hidden.Clear();
            foreach (var lgf in forcedLods) if (lgf != null) lgf.ForceLOD(-1);
            forcedLods.Clear();
            foreach (var mr in matSwaps) if (mr.r != null) mr.r.sharedMaterial = mr.m;
            matSwaps.Clear();
            if (what == "nic" || what == "none") return "skryj: obnoveno " + restored;
            int n = 0;
            if (what == "lod0" || what == "lod1" || what == "lod2")
            {
                int lv = what[3] - '0';
                foreach (var lg in FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (lg.gameObject.name.StartsWith("SM_Strom_Dzungle")) { lg.ForceLOD(lv); forcedLods.Add(lg); n++; }
                Diag("[props skryj] " + what + ": vynuceno na " + n + " stromech");
                return what + ": " + n;
            }
            Material windOff = null;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                LODGroup lg = r.GetComponentInParent<LODGroup>();
                if (lg == null) continue;
                string sp = lg.gameObject.name, part = r.gameObject.name;
                bool jungle = sp.StartsWith("SM_Strom_Dzungle");
                bool leaves = part.Contains("Leaves");
                bool hit = what == "listy" || what == "listystin" || what == "jenstin" || what == "vitr" ? jungle && leaves
                         : what == "kmeny" ? jungle && !leaves
                         : what == "dzungle" ? jungle
                         : what == "stiny" ? jungle
                         : what == "vsestiny" ? true
                         : what == "jine" ? !jungle : false;
                if (!hit) continue;
                hidden.Add((r, r.enabled, r.shadowCastingMode));
                if (what == "stiny" || what == "vsestiny" || what == "listystin") r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                else if (what == "jenstin") r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                else if (what == "vitr")
                {
                    // dočasná kopie materiálu bez větru – asset se nemění
                    if (windOff == null) { windOff = new Material(r.sharedMaterial); windOff.SetFloat("_EnableWind", 0f); }
                    matSwaps.Add((r, r.sharedMaterial));
                    r.sharedMaterial = windOff;
                }
                else r.enabled = false;
                n++;
            }
            string msg = "skryj " + what + ": " + n + " rendererů (obnoveno předtím " + restored + ")";
            Diag("[props skryj] " + msg);
            return msg;
        }

        /// <summary>
        /// Kolo 19: audit stínové kopie koruny (kolo 18) u stromů v okruhu r. Pro každý druh: stromy s listím
        /// Leaves_LOD, se stínovou kopií, chybějící / dvojitá kopie, listí, které dál vrhá stín (dvojitý stín),
        /// kopie mimo některou úroveň LODGroup (blikání při přechodu LOD), kopie posunutá proti listí (visící stín)
        /// a osiřelé kopie v celé scéně (bez stromu nebo mimo jeho LODGroup).
        /// </summary>
        private static string CanopyShadowAudit(Vector3 c, float r, string label)
        {
            var per = new SortedDictionary<string, int[]>();   // druh → stromy, s kopií, chybí, dvojitá, listí se stínem, mimo LOD, posun
            int trees = 0, bad = 0, lodChecks = 0, outOfScope = 0;
            foreach (var lg in FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Vector3 d = lg.transform.position - c; d.y = 0f;
                if (d.sqrMagnitude > r * r) continue;
                MeshRenderer leaf0 = null; int castLeaves = 0;
                var shadows = new List<MeshRenderer>();
                foreach (var mr in lg.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string n = mr.gameObject.name;
                    if (n == "Leaves_Shadow") shadows.Add(mr);
                    else if (n.Contains("Leaves_LOD"))
                    {
                        if (n.EndsWith("Leaves_LOD0")) leaf0 = mr;
                        if (mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) castLeaves++;
                    }
                }
                if (leaf0 == null) continue;
                // Kolo 18 se týká jen stromů, jejichž nejnižší LOD má listí Leaves_LOD (StyleMatched); herní stromy
                // (Tree_Birch_0x…) mají jiný nejnižší LOD a stín listí jim zůstává – ty se nepočítají jako „chybí".
                LOD[] lods = lg.GetLODs();
                MeshRenderer leafLow = null;
                if (lods.Length >= 2)
                    foreach (var rr in lods[lods.Length - 1].renderers)
                        if (rr is MeshRenderer lm && lm != null && lm.gameObject.name.Contains("Leaves_LOD")) leafLow = lm;
                if (leafLow == null) continue;
                // Herní stromy (Tree_Birch_0x…) mají nejnižší LOD listí bez stínu už v prefabu – ApplyCanopyShadow je
                // záměrně vynechá a jejich LOD0/1 listí vrhá stín jako před kolem 18. Nejsou v rozsahu kola 18.
                MeshFilter lowMf = leafLow.GetComponent<MeshFilter>();
                if (shadows.Count == 0 && (leafLow.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off
                    || lowMf == null || lowMf.sharedMesh == null || !lowMf.sharedMesh.isReadable)) { outOfScope++; continue; }   // herní FBX mesh není čitelný → kolo 18 ho vynechává
                string sp = lg.gameObject.name.Replace("(Clone)", "").Trim();
                if (!per.TryGetValue(sp, out int[] k)) per[sp] = k = new int[7];
                k[0]++; trees++;
                if (shadows.Count > 0) k[1]++;
                if (shadows.Count == 0 && ObjectSpawnerShadowOn()) k[2]++;
                if (shadows.Count > 1) k[3]++;
                if (shadows.Count > 0 && castLeaves > 0) k[4]++;
                foreach (var sh in shadows)
                {
                    for (int l = 0; l < lods.Length; l++)
                    {
                        lodChecks++;
                        if (System.Array.IndexOf(lods[l].renderers, sh) < 0) { k[5]++; break; }
                    }
                    Transform a0 = leafLow.transform, b0 = sh.transform;
                    // kopie je z nejnižšího LOD listí – stejný rodič a lokální transformace jako listí LOD0..2
                    if (a0.parent != b0.parent || (a0.localPosition - b0.localPosition).sqrMagnitude > 1e-4f
                        || Quaternion.Angle(a0.localRotation, b0.localRotation) > 0.1f || (a0.localScale - b0.localScale).sqrMagnitude > 1e-4f
                        || (sh.bounds.center - leafLow.bounds.center).magnitude > 0.05f + 0.02f * leafLow.bounds.extents.magnitude)
                        k[6]++;
                }
            }
            int orphans = 0, all = 0;
            foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mr.gameObject.name != "Leaves_Shadow") continue;
                all++;
                LODGroup lg = mr.GetComponentInParent<LODGroup>(true);
                bool inLod = false;
                if (lg != null) foreach (var lod in lg.GetLODs()) if (System.Array.IndexOf(lod.renderers, mr) >= 0) { inLod = true; break; }
                if (!inLod || mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) orphans++;
            }
            var sb = new System.Text.StringBuilder();
            var rows = new System.Text.StringBuilder();
            foreach (var kv in per)
            {
                int[] k = kv.Value;
                int b = k[2] + k[3] + k[4] + k[5] + k[6];
                bad += b;
                rows.AppendFormat(CultureInfo.InvariantCulture, "\n  {0}: stromů {1}, s kopií {2}, chybí {3}, dvojitá {4}, listí se stínem {5}, mimo LOD {6}, posun {7}",
                    kv.Key, k[0], k[1], k[2], k[3], k[4], k[5], k[6]);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[props stiny] {0} r {1:0} m u ({2:0},{3:0}): stromů se stínem koruny z kola 18 (nejnižší LOD = Leaves_LOD) {4}, problémů {5}, kontrol LOD {6}; Leaves_Shadow ve scéně {7}, osiřelých {8}; přepínač {9}, podíl {10:0.00}; herních stromů mimo rozsah kola 18 {11}",
                label, r, c.x, c.z, trees, bad, lodChecks, all, orphans, ObjectSpawnerShadowOn() ? "ZAP" : "VYP",
                World.Spawning.ObjectSpawner.CanopyShadowKeep, outOfScope);
            sb.Append(rows);
            return sb.ToString();
        }

        /// <summary>
        /// Kolo 19: kontrola kolizních pastí u staveb (SM_Ruina_*, SM_Vapenec_Portal). Pro každou dvojici kusů změří
        /// nejmenší mezeru mezi jejich collidery (ClosestPoint) – mezera užší než průměr hráče, ale nenulová, je
        /// škvíra, kde hráč uvízne. U oblouků/portálů ověří, že průchodem projde kapsle hráče (CheckCapsule jen proti
        /// collidérům té stavby).
        /// </summary>
        private static string RuinTrapAudit(Transform player, float r)
        {
            float pr = 0.5f, ph = 2f;
            var pc = player != null ? player.GetComponentInChildren<CapsuleCollider>() : null;
            if (pc != null) { pr = pc.radius * Mathf.Max(player.lossyScale.x, player.lossyScale.z); ph = pc.height * player.lossyScale.y; }
            var pieces = new List<(string name, Collider[] cols, Transform tr)>();
            foreach (var c in FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Transform root = c.transform;
                while (root.parent != null && !root.name.StartsWith("SM_")) root = root.parent;
                if (!TrapPiece(root.name)) continue;
                if ((root.position - player.position).sqrMagnitude > r * r) continue;
                if (pieces.Exists(x => x.tr == root)) continue;
                pieces.Add((root.name.Replace("(Clone)", "").Trim(), root.GetComponentsInChildren<Collider>(), root));
            }
            int pairs = 0, slots = 0, touching = 0; float minGap = float.MaxValue;
            var ex = new System.Text.StringBuilder();
            for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                if ((pieces[i].tr.position - pieces[j].tr.position).magnitude > 80f) continue;
                float gap = float.MaxValue;
                foreach (var ca in pieces[i].cols)
                foreach (var cb in pieces[j].cols)
                {
                    Vector3 pa = ca.ClosestPoint(cb.bounds.center), pb = cb.ClosestPoint(pa); pa = ca.ClosestPoint(pb);
                    gap = Mathf.Min(gap, Vector3.Distance(pa, pb));
                }
                pairs++; minGap = Mathf.Min(minGap, gap);
                if (gap < 0.01f) touching++;
                else if (gap < 2f * pr + 0.1f) { slots++; if (ex.Length < 600) ex.AppendFormat(CultureInfo.InvariantCulture, " {0}↔{1} {2:0.00}", pieces[i].name, pieces[j].name, gap); }
            }
            int arches = 0, archOk = 0; var aex = new System.Text.StringBuilder();
            foreach (var pc2 in pieces)
            {
                bool arch = pc2.name.StartsWith("SM_Ruina_Oblouk"), portal = pc2.name.StartsWith("SM_Vapenec_Portal")
                     || pc2.name.StartsWith("SM_Krystal_Jeskyne") || pc2.name.StartsWith("SM_Utes_Brana")
                     || pc2.name.StartsWith("SM_Alabastr_Oblouk")
                     || pc2.name.StartsWith("SM_Koral_Brana") || pc2.name.StartsWith("SM_Koral_Kostra");   // kolo 29: otvor u počátku ≥ portál (x ±1,3, výška 0–4,2)   // kolo 28: otvor ≥ portál (x ±1,2, výška 0–4,2)   // kolo 21: stejný otvor jako portál (0–3,6 m)
                if (!arch && !portal) continue;
                arches++;
                // otvor v souřadnicích meshe: oblouk x ±1,4, výška 0–3,2; portál x ±0,85, výška 0–3,7
                float sy = pc2.tr.lossyScale.y;
                Vector3 b0 = pc2.tr.TransformPoint(new Vector3(0f, 0f, 0f)) + Vector3.up * (pr + 0.3f);
                Vector3 b1 = b0 + Vector3.up * Mathf.Max(0f, ph - 2f * pr);
                bool free = true;
                foreach (var c in pc2.cols)
                {
                    Collider cap = TempCapsule(b0, b1, pr);
                    if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                        cap, cap.transform.position, cap.transform.rotation, out _, out _)) { free = false; break; }
                }
                float open = (arch ? 3.2f : 3.7f) * sy;
                if (free && open > ph) archOk++; else if (aex.Length < 300) aex.AppendFormat(" {0} (otvor {1:0.0} j.)", pc2.name, open);
            }
            if (tempCap != null) tempCap.enabled = false;
            return string.Format(CultureInfo.InvariantCulture,
                "[props pasti] r {0:0} u ({1:0},{2:0}): kusů ruin/portálů {3}, dvojic do 80 m {4}, škvíry užší než hráč (0 < mezera < {5:0.00}) {6}, dotyk {7}, nejmenší mezera {8:0.00}; oblouky/portály {9}, průchozích {10} (hráč r {11:0.00}, výška {12:0.0})\n  škvíry:{13}\n  neprůchozí:{14}",
                r, player.position.x, player.position.z, pieces.Count, pairs, 2f * pr + 0.1f, slots, touching, pairs > 0 ? minGap : 0f, arches, archOk, pr, ph, ex, aex)
                + JumpReach(player);
        }

        /// <summary>Kolo 28: teoretický dosah skoku hráče (impulz / hmotnost, gravitace × násobek) a výška schodu – pro skákací pasáže sloupů.</summary>
        private static string JumpReach(Transform player)
        {
            var f = player != null ? player.GetComponentInChildren<Orivilon.Player.FirstPersonController>() : null;
            var rb = player != null ? player.GetComponentInChildren<Rigidbody>() : null;
            if (f == null || rb == null) return "";
            float v = f.jumpPower / Mathf.Max(0.01f, rb.mass), g = -Physics.gravity.y * f.gravityMultiplier;
            return string.Format(CultureInfo.InvariantCulture, "\n  skok: v0 {0:0.0} j./s, g {1:0.0}, výška skoku ≈ {2:0.00} j., schod bez skoku ≤ {3:0.00} j.", v, g, v * v / (2f * g), f.stepHeight);
        }

        /// <summary>Kolo 19 stavby + kolo 20 kusy s velkým colliderem (ledové stěny, séraky, terasy, kry, hřebeny, kužely, mangrovníky).</summary>
        private static readonly string[] TrapPrefixes = { "SM_Ruina", "SM_Vapenec_Portal", "SM_Led_Stena", "SM_Led_Serak", "SM_Travertin", "SM_Kra_",
                                                          "SM_Snih_Hreben", "SM_Snih_Balvan", "SM_Bludny_Balvan", "SM_Gejzir", "SM_Vyduch", "SM_Sul_Kopa", "SM_Mangrovnik",
                                                          "SM_Krystal_Shluk", "SM_Krystal_Velky", "SM_Krystal_Jeskyne", "SM_Mineral_Stena", "SM_Houba_Obri", "SM_Utes_",
                                                          "SM_Cedic_", "SM_Obsidian_Strep", "SM_Obsidian_Hreben", "SM_Obsidian_Balvan", "SM_Alabastr_Oblouk", "SM_Alabastr_Vez", "SM_Alabastr_Plotna", "SM_Alabastr_Balvan",
                                                          "SM_Koral_Vetevnaty", "SM_Koral_Stolovy", "SM_Koral_Mozkovy", "SM_Koral_Brana", "SM_Koral_Kostra", "SM_Meteorit", "SM_Kraterovy_Balvan", "SM_Tektit_Sklo", "SM_Ruda_", "SM_Bahenni_Kuzel", "SM_Sirne_Krystaly" };   // kolo 29   // kolo 28   // kolo 21 (drobné krystaly vrstvy kamenů ne – jako ostatní kameny)
        private static bool TrapPiece(string n) { foreach (var pf in TrapPrefixes) if (n.StartsWith(pf)) return true; return false; }

        /// <summary>
        /// Kolo 20: kry jako pevná plocha – paprsek shora na střed každé kry v okruhu musí dopadnout na její collider
        /// (normála ≥ 0,9), vršek nad hladinou a kra nesmí ležet na dně. Hráč tak po kře chodí, mezi krami spadne do vody.
        /// </summary>
        private static string FloeWalkAudit(VoxelTerrain t, Vector3 c, float r)
        {
            int n = 0, ok = 0; var ex = new System.Text.StringBuilder();
            foreach (var col in FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Transform root = col.transform;
                while (root.parent != null && !root.name.StartsWith("SM_")) root = root.parent;
                if (!root.name.StartsWith("SM_Kra_")) continue;
                Vector3 pc = root.position;
                if ((pc - c).sqrMagnitude > r * r) continue;
                n++;
                bool hit = Physics.Raycast(new Vector3(pc.x, pc.y + 30f, pc.z), Vector3.down, out RaycastHit h, 60f, ~0, QueryTriggerInteraction.Ignore);
                bool onFloe = hit && h.collider.transform.IsChildOf(root) || hit && h.collider.transform == root;
                bool lvl = t.WaterLevelAt(pc.x, pc.z, out float wl);
                bool good = onFloe && h.normal.y >= 0.9f && (!lvl || h.point.y > wl + 0.3f);
                if (good) ok++; else if (ex.Length < 400) ex.AppendFormat(CultureInfo.InvariantCulture, " {0} ({1:0},{2:0}) zásah {3} n.y {4:0.00} hladina {5}", root.name, pc.x, pc.z, hit ? h.collider.name : "—", hit ? h.normal.y : 0f, lvl ? wl.ToString("0.00", CultureInfo.InvariantCulture) : "?");
            }
            return string.Format(CultureInfo.InvariantCulture, "[props kry] r {0:0}: ker s colliderem {1}, pochozích (paprsek na kru, n.y ≥ 0,9, vršek ≥ 0,3 nad hladinou) {2}{3}", r, n, ok, ex.Length > 0 ? " | chyby:" + ex : "");
        }

        private static CapsuleCollider tempCap;
        private static Collider TempCapsule(Vector3 a, Vector3 b, float r)
        {
            if (tempCap == null) { var go = new GameObject("K19_TrapProbe") { hideFlags = HideFlags.DontSave }; tempCap = go.AddComponent<CapsuleCollider>(); tempCap.isTrigger = true; }
            tempCap.enabled = true;
            tempCap.transform.position = (a + b) * 0.5f; tempCap.transform.rotation = Quaternion.identity;
            tempCap.radius = r; tempCap.height = (b - a).magnitude + 2f * r; tempCap.direction = 1;
            return tempCap;
        }

        private static bool ObjectSpawnerShadowOn() => World.Spawning.ObjectSpawner.CanopyShadowProxy;

        /// <summary>
        /// Kolo 19: pokácí nejbližší strom se stínovou kopií (filtr = část jména druhu) skutečnou herní cestou
        /// <see cref="World.Objects.HarvestableObject.TryHarvest"/> se správným nástrojem (dočasný předmět, nic se
        /// neukládá – bez vybraného světa SaveSystem registr nezapisuje). Pak ověří, že strom i jeho Leaves_Shadow
        /// zmizely, že nevznikla osiřelá kopie a že je objekt v registru zničených.
        /// </summary>
        private IEnumerator FellTest(Vector3 c, string filter)
        {
            World.Objects.HarvestableObject best = null; float bd = float.MaxValue;
            foreach (var hv in FindObjectsByType<World.Objects.HarvestableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                // Kolo 26: „!jméno“ = i strom bez stínové kopie (jehličnany), kontrola kácení po změně shaderu.
                bool anyTree = filter.StartsWith("!");
                string f = anyTree ? filter.Substring(1) : filter;
                if (f.Length > 0 && hv.gameObject.name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool hasShadow = false;
                foreach (Transform ch in hv.transform) if (ch.name == "Leaves_Shadow") { hasShadow = true; break; }
                if (!hasShadow && !anyTree) continue;
                float dd = (hv.transform.position - c).sqrMagnitude;
                if (dd < bd) { bd = dd; best = hv; }
            }
            if (best == null) { Error("kacej: žádný strom se stínovou kopií (" + filter + ")"); search = null; yield break; }
            GameObject tree = best.gameObject;
            GameObject shadow = null;
            foreach (Transform ch in tree.transform) if (ch.name == "Leaves_Shadow") shadow = ch.gameObject;
            var id = tree.GetComponent<World.Objects.DeterministicObjectId>();
            long hash = id != null ? id.Hash : 0;
            string name = tree.name; Vector3 pos = tree.transform.position;
            string before = CanopyShadowAudit(pos, 40f, "pred_kacenim");
            var tool = ScriptableObject.CreateInstance<Orivilon.Inventory.Inventory.InventoryItemData>();
            tool.toolType = best.requiredTool;
            int hits = 0;
            Exception err = null;
            while (tree != null && hits < best.maxHealth + 2)
            {
                try { best.TryHarvest(tool); } catch (Exception e) { err = e; break; }
                hits++;
                yield return null;
            }
            Destroy(tool);
            yield return null; yield return null;
            bool treeGone = tree == null, shadowGone = shadow == null;
            bool reg = hash != 0 && SaveSystem.SaveSystem.IsObjectDestroyed(hash);
            string after = CanopyShadowAudit(pos, 40f, "po_kaceni");
            string txt = string.Format(CultureInfo.InvariantCulture,
                "[props kacej] {0} u ({1:0.0},{2:0.0},{3:0.0}): úderů {4}, strom zmizel {5}, Leaves_Shadow zmizel {6}, v registru zničených {7}{8}\n  {9}\n  {10}",
                name, pos.x, pos.y, pos.z, hits, treeGone, shadowGone, reg, err != null ? ", chyba " + err.GetType().Name + ": " + err.Message : "",
                before.Split('\n')[0], after.Split('\n')[0]);
            Diag(txt);
            if (treeGone && shadowGone) Ok(txt.Split('\n')[0]); else Error(txt.Split('\n')[0]);
            search = null;
        }

        private void CmdProfile(string[] a)
        {
#if UNITY_EDITOR
            if (a.Length > 1 && a[1] == "start") { EditorRequest = "start"; Ok("Profiler: nahrávání spuštěno."); return; }
            if (a.Length > 1 && a[1] == "dump")
            {
                EditorRequest = "dump " + (a.Length > 2 ? a[2] : "");
                Ok("Profiler: výpis do _reports/gc_alloc.log.");
                return;
            }
            Error("Použití: /profil start | /profil dump [štítek]");
#else
            Error("Profil jen v editoru.");
#endif
        }

        private void CmdPause()
        {
            GameManager gm = GameManager.instance;
            string state = string.Format(CultureInfo.InvariantCulture,
                "GameManager {0}, pauseMenu {1}, IsLoading {2}, InputBlocked {3}, IsPaused {4}",
                gm != null ? "ok" : "null", gm != null && gm.pauseMenu != null ? "ok" : "null",
                SceneLoader.IsLoading, SceneLoader.InputBlocked, gm != null && gm.IsPaused);
            Diag("[Pauza] " + state);
            Note(state);
            if (gm == null) { Error("GameManager chybí."); return; }
            gm.TogglePauseGame();
            Ok("Pauza " + (gm.IsPaused ? "otevřena" : "zavřena") + ".");
        }

        private void Help()
        {
            Note("/biome list               - all teleportable biomes and their IDs");
            Note("/biome tp <biome-id>      - teleport to the nearest safe spot of a biome (e.g. /biome tp snowy_peaks)");
            Note("/biome where | help       - which biome you are in | more biome options");
            Note("/tp <x> <z>               - jump to coordinates, height from terrain");
            Note("/tp <x> <y> <z>           - jump to exact height (~ = relative, e.g. ~ ~40 ~)");
            Note("/tp grove | basalt        - nearest micro-biome (golden grove, basalt field)");
            Note("/pos, /seed               - where am I, world seed");
            Note("/fly on | off             - flight on or off");
            Note("/look <yaw> [pitch]       - aim the view; 0 = north, negative pitch = down");
            Note("/time [set day|night|dawn|dusk|noon|midnight|<0-24>] - show or set the time of day");
            Note("/stop, /clear             - cancel a running biome search, clear this console");
        }

        // ── dotazy ─────────────────────────────────────────────────────

        /// <summary>
        /// Kořen hráče. Nejdřív viewer streameru (ten je zaručeně ten, kolem kterého se
        /// streamuje), teprve pak hledání podle tagu.
        /// </summary>
        private static Transform PlayerRoot()
        {
            VoxelTerrain t = VoxelTerrain.instance;
            if (t != null && t.viewer != null) return t.viewer.root != null ? t.viewer.root : t.viewer;

            GameObject go = GameObject.FindGameObjectWithTag("Player");
            return go != null ? go.transform : null;
        }

        private bool RequireTerrain(out VoxelTerrain terrain, out Transform player)
        {
            terrain = VoxelTerrain.instance;
            player = PlayerRoot();

            if (terrain == null) { Error("Voxel terrain is not running."); return false; }
            if (player == null) { Error("Player not found."); return false; }
            return true;
        }

        private void CmdPos()
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;

            Vector3 v = p.position;
            VoxelBiome b = t.BiomeAt(v.x, v.z, out float surf);
            t.ClimateAt(v.x, v.z, out float temp, out float hum);

            Ok(string.Format(CultureInfo.InvariantCulture,
                "x {0:0.0}  y {1:0.0}  z {2:0.0}   surface {3:0.0} m   biome {4}   temp {5:0.00}  humidity {6:0.00}",
                v.x, v.y, v.z, surf, RegionLabel(t.RegionAt(v.x, v.z, true, out _, out _).Dominant()), temp, hum));
        }

        private void CmdBiome()
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;

            Vector3 v = p.position;
            VoxelBiome b = t.BiomeAt(v.x, v.z, out float surf);
            Ok(WherePlayer(t, v.x, v.z));

            MicroBiome m = t.MicroAt(v.x, v.z, out float mw);
            if (m != MicroBiome.None && mw > 0.01f)
                Ok(string.Format(CultureInfo.InvariantCulture,
                    "micro-biome: {0} ({1:0}% strength)", MicroName(m), mw * 100f));
        }

        /// <summary>Jméno mikro-biomu pro hráče.</summary>
        private static string MicroName(MicroBiome m) => m switch
        {
            MicroBiome.GoldenGrove => "golden grove",
            MicroBiome.BasaltField => "basalt field",
            _ => "none",
        };

        /// <summary>
        /// Název mikro-biomu z příkazu. Zvlášť od <c>VoxelBiomes.TryParse</c> schválně:
        /// mikro-biom není biom, hledá se jinak a nemá se objevit v nabídce biomů, kde
        /// by vypadal jako klimatická oblast.
        /// </summary>
        private static bool TryParseMicro(string name, out MicroBiome m)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "grove": case "golden grove": case "haj": case "zlaty haj":
                    m = MicroBiome.GoldenGrove; return true;
                case "basalt": case "basalt field": case "cedic": case "cedicove pole":
                    m = MicroBiome.BasaltField; return true;
                default:
                    m = MicroBiome.None; return false;
            }
        }

        private void CmdSeed(string[] a)
        {
#if UNITY_EDITOR
            // Seed pro přímý start scény Game v editoru. Platí od dalšího Play Mode.
            if (a.Length >= 3 && a[1] == "set" && int.TryParse(a[2], out int ns))
            {
                UnityEditor.EditorPrefs.SetInt(VoxelTerrain.EditorSeedPrefKey, ns);
                Ok("Direct-start seed set to " + ns + " (applies on next Play Mode).");
                return;
            }
#endif
            if (!RequireTerrain(out VoxelTerrain t, out Transform _)) return;

            Ok(string.Format(CultureInfo.InvariantCulture,
                "seed {0}   sea level {1:0.0} m   render distance {2} chunks",
                t.WorldSeed, t.SeaLevel, t.renderDistance));
        }

        /// <summary>
        /// Stav streameru. Měření samo píše do konzole Unity, protože je to mnoho řádků
        /// s čísly – herní chat na to není a v zápalu ladění se stejně kouká do Editoru.
        /// </summary>
        private void CmdTerrain(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform pl)) return;

            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (sub == "bench")
            {
                int n = 24;
                if (a.Length > 2) int.TryParse(a[2], out n);
                string res = t.BenchColumns(pl.position, Mathf.Clamp(n, 4, 200));
                Diag("[Bench] " + res);
                foreach (string part in res.Split('|')) if (part.Trim().Length > 0) Note(part.Trim());
                return;
            }
            if (sub == "sevfall")
            {
                VoxelChunkBuilder.NoSeamFalloff = a.Length > 2 && (a[2] == "off" || a[2] == "0");
                Ok("Seam falloff " + (VoxelChunkBuilder.NoSeamFalloff ? "OFF" : "ON") + " – platí pro nově stavěné chunky.");
                return;
            }
            if (sub == "snap")
            {
                VoxelChunkBuilder.SnapWallsLegacy = a.Length > 2 && a[2] == "legacy";
                Ok("Snap na stěnách chunku " + (VoxelChunkBuilder.SnapWallsLegacy ? "ZAPNUTÝ (legacy)" : "vypnutý") + " – platí pro nově stavěné chunky.");
                return;
            }
            if (sub == "spike")
            {
                bool on = a.Length > 2 && (a[2] == "on" || a[2] == "1");
                VoxelTerrain.SpikeProbe = on;
                VoxelTerrain.SpikeLog = on ? (msg => Diag(msg)) : null;
                Ok("Spike probe " + (on ? "ON (log do _reports/diag.log)" : "OFF"));
                return;
            }
            if (sub == "legacy")
            {
                VoxelTerrain.LegacySampler = a.Length > 2 && (a[2] == "on" || a[2] == "1");
                Ok("Legacy river sampler " + (VoxelTerrain.LegacySampler ? "ON" : "OFF") + " (new columns only).");
                return;
            }
            if (sub == "cekej" || sub == "wait")
            {
                if (search != null) StopCoroutine(search);
                search = StartCoroutine(WaitSettled(t));
                return;
            }

            t.ColliderStats(out int total, out int lod0, out int withCollider);
            Ok(string.Format(CultureInfo.InvariantCulture,
                "chunks {0}, in LOD0 {1}, with collider {2} - details in the Unity console",
                total, lod0, withCollider));

            t.MeasureTerrain();
        }

        /// <summary>
        /// Měří, za jak dlouho se svět dostaví: od spuštění do chvíle, kdy streamer nemá nic
        /// rozestavěného a všechny žádané sloupce jsou zveřejněné. Hlásí i nejhorší snímek.
        /// </summary>
        private IEnumerator WaitSettled(VoxelTerrain t)
        {
            float start = Time.realtimeSinceStartup;
            float worst = 0f;
            int frames = 0;
            Note("Measuring until the streamer settles...");
            yield return null;
            while (t != null && !t.IsSettled && Time.realtimeSinceStartup - start < 120f)
            {
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                frames++;
                yield return null;
            }
            float dt = Time.realtimeSinceStartup - start;
            string msg = string.Format(CultureInfo.InvariantCulture,
                "settled in {0:0.00} s, {1} frames, avg {2:0.0} ms, worst frame {3:0.0} ms",
                dt, frames, frames > 0 ? dt * 1000f / frames : 0f, worst * 1000f);
            Diag("[Stream] " + msg);
            Ok(msg);
            search = null;
        }

        /// <summary>
        /// Natočí pohled na daný azimut a sklon.
        ///
        /// <para>Myš mění pohled jen přírůstkově, takže se na konkrétní směr nedá zamířit
        /// a dva záběry z téhož místa nejdou porovnat. Tohle je proto nástroj na rámování:
        /// <c>/pohled 90 0</c> je pohled na východ přesně k horizontu, pokaždé stejný.</para>
        /// </summary>
        // ── Kolo 8: /vyhled – najde vyvýšené místo s výhledem na nejbližší vodu ──
        private bool pendingLook;
        private float pendingYaw, pendingPitch, pendingSince;

        private void TickPendingLook()
        {
            if (!pendingLook) return;
            VoxelTerrain t = VoxelTerrain.instance;
            if (t == null) { pendingLook = false; return; }
            if (t.IsTeleporting) { pendingSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - pendingSince < 0.5f) return;
            var fpc = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) fpc.SetLook(pendingYaw, pendingPitch);
            pendingLook = false;
        }

        private static bool WaterAtProbe(VoxelTerrain t, float x, float z, out float surf, out float wy)
        {
            wy = 0f;
            if (!t.ProbeSurface(x, z, 1f, out surf, out float core, out float ry, out float ly)) return false;
            wy = float.MinValue;
            if (core > 0.15f && ry > surf) wy = Mathf.Max(wy, ry);
            if (ly > surf) wy = Mathf.Max(wy, ly);
            if (surf < 0f) wy = Mathf.Max(wy, 0f);
            return wy > float.MinValue;
        }

        /// <summary>
        /// /vyhled [dosah] – deterministicky vybere místo 45–130 m od nejbližší vody, 4–60 m nad
        /// její hladinou, odkud na ni je přímý výhled, teleportuje tam hráče a natočí pohled.
        /// Nástroj pro srovnávací screenshoty (stejné místo před/po), ne herní mechanika.
        /// </summary>
        private void CmdVista(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;
            Vector3 o = p.position;
            if (a.Length >= 2 && (a[1] == "vrchol" || a[1] == "peak"))
            {
                // Nejvyšší bod v okruhu 300 m a pohled směrem, kam terén nejvíc padá.
                float top = float.MinValue, tx = o.x, tz = o.z;
                for (float gx = -300f; gx <= 300f; gx += 20f)
                for (float gz = -300f; gz <= 300f; gz += 20f)
                {
                    if (gx * gx + gz * gz > 300f * 300f) continue;
                    if (WaterAtProbe(t, o.x + gx, o.z + gz, out float hs, out _)) continue;
                    if (hs > top) { top = hs; tx = o.x + gx; tz = o.z + gz; }
                }
                float low = float.MaxValue, ly = 0f;
                for (int k = 0; k < 24; k++)
                {
                    float ang = k * Mathf.PI * 2f / 24f;
                    float sum = 0f;
                    for (int i = 1; i <= 3; i++)
                        if (t.ProbeSurface(tx + Mathf.Sin(ang) * 120f * i, tz + Mathf.Cos(ang) * 120f * i, 1f, out float hs, out _, out _, out _)) sum += hs;
                    // blízké okolí nesmí zakrýt výhled (stěna, vedlejší hřeben)
                    for (int i = 1; i <= 4; i++)
                        if (t.ProbeSurface(tx + Mathf.Sin(ang) * 15f * i, tz + Mathf.Cos(ang) * 15f * i, 1f, out float ns, out _, out _, out _)
                            && ns > top - 1f) sum += 1e5f;
                    if (sum < low) { low = sum; ly = ang * Mathf.Rad2Deg; }
                }
                pendingYaw = ly; pendingPitch = 6f;
                pendingLook = true; pendingSince = Time.unscaledTime;
                t.Teleport(new Vector3(tx, 0f, tz));
                Ok(string.Format(CultureInfo.InvariantCulture, "vrchol: ({0:0},{1:0}) výška {2:0}, yaw {3:0}", tx, tz, top, ly));
                Diag(string.Format(CultureInfo.InvariantCulture, "[vyhled] vrchol ({0:0},{1:0}) h {2:0} yaw {3:0}", tx, tz, top, ly));
                return;
            }
            float reach = 90f;
            if (a.Length >= 2 && TryNumber(a[1], out float rr)) reach = Mathf.Clamp(rr, 10f, 400f);
            bool found = false; float wx = 0f, wz = 0f, wY = 0f;
            for (float r = 0f; r <= reach && !found; r += 6f)
            {
                int n = r < 1f ? 1 : 24;
                for (int k = 0; k < n && !found; k++)
                {
                    float ang = k * Mathf.PI * 2f / n;
                    float x = o.x + Mathf.Sin(ang) * r, z = o.z + Mathf.Cos(ang) * r;
                    if (WaterAtProbe(t, x, z, out _, out float wy)) { found = true; wx = x; wz = z; wY = wy; }
                }
            }
            if (!found) { Error("Voda v dosahu nenalezena."); return; }

            float best = float.MinValue, bx = 0f, bz = 0f, bs = 0f;
            float[] ds = { 45f, 60f, 80f, 100f, 130f };
            foreach (float d in ds)
            for (int k = 0; k < 24; k++)
            {
                float ang = k * Mathf.PI * 2f / 24f;
                float x = wx + Mathf.Sin(ang) * d, z = wz + Mathf.Cos(ang) * d;
                if (WaterAtProbe(t, x, z, out float s, out _)) continue;
                float rise = s - wY;
                if (rise < 4f || rise > 60f) continue;
                float eye = s + 7.7f;
                bool clear = true;
                for (int i = 1; i < 8 && clear; i++)
                {
                    float f = i / 8f;
                    float sx = Mathf.Lerp(x, wx, f), sz = Mathf.Lerp(z, wz, f);
                    float line = Mathf.Lerp(eye, wY, f);
                    if (t.ProbeSurface(sx, sz, 1f, out float gs, out _, out _, out _) && gs > line - 1f) clear = false;
                }
                if (!clear) continue;
                float score = -Mathf.Abs(rise - 18f) * 0.6f - d * 0.05f;
                if (score > best) { best = score; bx = x; bz = z; bs = s; }
            }
            if (best == float.MinValue) { Error("Žádné místo s výhledem na vodu."); return; }

            float dx = wx - bx, dz = wz - bz;
            pendingYaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (pendingYaw < 0f) pendingYaw += 360f;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            pendingPitch = Mathf.Clamp(Mathf.Atan2(bs + 7.7f - wY, dist) * Mathf.Rad2Deg * 0.8f, 2f, 35f);
            pendingLook = true; pendingSince = Time.unscaledTime;
            t.Teleport(new Vector3(bx, 0f, bz));
            Ok(string.Format(CultureInfo.InvariantCulture, "vyhled: ({0:0},{1:0}) → voda ({2:0},{3:0}) hladina {4:0.0}, yaw {5:0} pitch {6:0}",
                bx, bz, wx, wz, wY, pendingYaw, pendingPitch));
            Diag(string.Format(CultureInfo.InvariantCulture, "[vyhled] ({0:0},{1:0}) -> ({2:0},{3:0}) yaw {4:0} pitch {5:0}", bx, bz, wx, wz, pendingYaw, pendingPitch));
        }

        // ── Kolo 10: diagnostika černých lemů vegetace ──
        private static readonly Dictionary<Material, float> folCutoff = new Dictionary<Material, float>();
        private static bool folSsaoTouched;

        private static void FoliageMaterials(Dictionary<Material, int> mats)
        {
            var vr = World.Spawning.VegetationRenderer.Instance;
            if (vr != null) vr.CollectMaterials(mats);
            foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    if (!m.shader.name.Contains("Vegetation")) continue;
                    mats.TryGetValue(m, out int n); mats[m] = n + 1;
                }
            }
        }

        private static UnityEngine.Rendering.Universal.ScriptableRendererFeature FindSsao()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp == null) return null;
            var f = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var list = f?.GetValue(urp) as UnityEngine.Rendering.Universal.ScriptableRendererData[];
            if (list == null) return null;
            foreach (var d in list)
                if (d != null)
                    foreach (var feat in d.rendererFeatures)
                        if (feat != null && feat.GetType().Name.Contains("AmbientOcclusion")) return feat;
            return null;
        }

        /// <summary>
        /// /folaz info | ssao on|off | stiny on|off | vrhani on|off | cutoff x | cutoff reset.
        /// Jen za běhu; změny sdílených materiálů a SSAO se vrací příkazem „reset“ (materiál i asset URP
        /// se jinak v editoru neukládají, pokud se projekt výslovně neuloží).
        /// </summary>
        // ── meziúkol: sebrání trávy a kamínků ─────────────────────────
        private static int InvTotal(string itemName)
        {
            var inv = Orivilon.Inventory.Inventory.InventoryData.Instance;
            if (inv == null || inv.Slots == null) return -1;
            int n = 0;
            foreach (var s in inv.Slots)
                if (s != null && !s.IsEmpty && s.item.itemName == itemName) n += s.amount;
            return n;
        }

        private void CmdPickupDiag(string mode)
        {
            Camera cam = Camera.main;
            if (cam == null) { Ok("sber: bez kamery"); return; }
            Vector3 eye = cam.transform.position;
            var sb = new System.Text.StringBuilder();

            // kamínky a jiné sebratelné GameObjecty kolem hráče
            var items = FindObjectsByType<World.Objects.PickupItem>(FindObjectsSortMode.None);
            var byName = new Dictionary<string, int[]>();
            World.Objects.PickupItem nearestStone = null; float nsd = float.MaxValue;
            foreach (var it in items)
            {
                if (it == null || it.itemData == null) continue;
                float d = Vector3.Distance(it.transform.position, eye);
                if (!byName.TryGetValue(it.itemData.itemName, out int[] c)) byName[it.itemData.itemName] = c = new int[3];
                c[0]++; if (d < 30f) c[1]++; if (d < 60f) c[2]++;
                if (it.GetComponent<Collider>() != null && (mode.StartsWith("cil:") ? it.itemData.itemName == mode.Substring(4) : it.gameObject.name.StartsWith("Stone")) && d < nsd) { nsd = d; nearestStone = it; }
            }
            sb.Append("GO pickupy (vše/<30m/<60m): ");
            foreach (var kv in byName) sb.AppendFormat("{0} {1}/{2}/{3}; ", kv.Key, kv.Value[0], kv.Value[1], kv.Value[2]);
            sb.AppendFormat(CultureInfo.InvariantCulture, "| nejbližší kamínek {0:0.0} m | tráva: sloupců {1}, stébel {2} | inventář Grass {3}, Stone {4}",
                nearestStone != null ? nsd : -1f, World.Spawning.ObjectSpawner.LiveGrassColumns, World.Spawning.ObjectSpawner.LiveGrassDrawn(),
                InvTotal("Grass"), InvTotal("Stone"));
            if (nearestStone != null)
            {
                var r = nearestStone.GetComponentInChildren<Renderer>();
                sb.AppendFormat(CultureInfo.InvariantCulture, " | kamínek {0}: měřítko {1:0.000}, renderer {2:0.00}×{3:0.00}×{4:0.00}, collider {5}",
                    nearestStone.name, nearestStone.transform.lossyScale.x,
                    r != null ? r.bounds.size.x : -1f, r != null ? r.bounds.size.y : -1f, r != null ? r.bounds.size.z : -1f,
                    nearestStone.GetComponent<Collider>().bounds.size.ToString("0.00"));
            }

            if (mode.StartsWith("cil:")) mode = "kamen";
            {
                // Co je teď uprostřed obrazovky (stejný paprsek jako PlayerInteraction).
                var pi = UnityEngine.Object.FindFirstObjectByType<PlayerInteraction>();
                float reach = pi != null ? pi.interactionDistance : 3f;
                Ray cr = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                if (Physics.Raycast(cr, out RaycastHit ch, 200f))
                    sb.AppendFormat(CultureInfo.InvariantCulture, " | střed: {0} (vrstva {1}, trigger {2}, pickup {3}) na {4:0.0} m, dosah {5:0.0}",
                        ch.collider.name, ch.collider.gameObject.layer, ch.collider.isTrigger,
                        ch.collider.GetComponent<World.Objects.PickupItem>() != null, ch.distance, reach);
            }
            if (mode == "kamen" || mode == "trava")
            {
                // Namíří pohled na nejbližší kamínek / stéblo (pro snímek s výzvou „Press E").
                Vector3 target; bool have;
                if (mode == "kamen") { have = nearestStone != null; target = have ? nearestStone.GetComponent<Collider>().bounds.center : default; }
                else have = World.Spawning.ObjectSpawner.NearestInstancedGrass(eye, 40f, out target, out _);
                var fpc = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
                if (have && fpc != null)
                {
                    Vector3 dv = target - eye;
                    float yaw = Mathf.Atan2(dv.x, dv.z) * Mathf.Rad2Deg;
                    float pitch = Mathf.Atan2(-dv.y, new Vector2(dv.x, dv.z).magnitude) * Mathf.Rad2Deg;
                    fpc.SetLook(yaw, pitch);
                    sb.AppendFormat(CultureInfo.InvariantCulture, " || cíl {0} na {1:0.0} {2:0.0} {3:0.0}, vzdál {4:0.0} m, yaw {5:0} pitch {6:0}",
                        mode, target.x, target.y, target.z, dv.magnitude, yaw, pitch);
                }
                else sb.Append(" || cíl " + mode + " nenalezen");
            }

            if (mode == "test")
            {
                // Tráva: paprsek od kamery na nejbližší stéblo → musí ho najít; sebrat → zmizí z dávky.
                if (World.Spawning.ObjectSpawner.NearestInstancedGrass(eye, 25f, out Vector3 anchor, out long gh))
                {
                    Vector3 dir = (anchor - eye).normalized;
                    float dist = Vector3.Distance(anchor, eye);
                    var ray = new Ray(eye, dir);
                    bool found = World.Spawning.ObjectSpawner.FindInstancedPickup(ray, dist + 0.5f, out var pick);
                    string nm = found ? pick.template.itemData.itemName : "-";
                    int inv0 = found ? InvTotal(nm) : -1;
                    int drawn0 = World.Spawning.ObjectSpawner.LiveGrassDrawn();
                    bool ok = found && World.Spawning.ObjectSpawner.PickUpInstanced(pick);
                    int drawn1 = World.Spawning.ObjectSpawner.LiveGrassDrawn();
                    bool again = World.Spawning.ObjectSpawner.FindInstancedPickup(ray, dist + 0.5f, out var pick2) && pick2.hash == pick.hash;
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        " || TRÁVA: vzdál {0:0.0} m, nalezeno {1} (stejné stéblo {2}), item {3}, sebráno {4}, inventář {5}→{6}, stébel {7}→{8}, zničeno {9}, znovu nalezeno {10}",
                        dist, found, found && pick.hash == gh, nm, ok, inv0, found ? InvTotal(nm) : -1, drawn0, drawn1,
                        found && SaveSystem.SaveSystem.IsObjectDestroyed(pick.hash), again);
                }
                else sb.Append(" || TRÁVA: v dosahu 25 m žádná sebratelná");

                // Kamínek: skutečný raycast jako PlayerInteraction → PickupItem → PickUp().
                if (nearestStone != null)
                {
                    Collider col = nearestStone.GetComponent<Collider>();
                    Vector3 target = col.bounds.center;
                    var ray = new Ray(eye, (target - eye).normalized);
                    bool hitIt = Physics.Raycast(ray, out RaycastHit hit, Vector3.Distance(eye, target) + 1f)
                                 && hit.collider.TryGetComponent(out World.Objects.PickupItem hp) && hp == nearestStone;
                    string nm = nearestStone.itemData.itemName;
                    var did = nearestStone.GetComponent<World.Objects.DeterministicObjectId>();
                    long h = did != null ? did.Hash : 0;
                    int inv0 = InvTotal(nm);
                    Vector3 sz = col.bounds.size;
                    nearestStone.PickUp();
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        " || KAMÍNEK: vzdál {0:0.0} m, velikost {1:0.00}×{2:0.00}×{3:0.00}, raycast ho trefí {4}, item {5}, inventář {6}→{7}, zničeno {8}",
                        nsd, sz.x, sz.y, sz.z, hitIt, nm, inv0, InvTotal(nm), h != 0 && SaveSystem.SaveSystem.IsObjectDestroyed(h));
                }
                else sb.Append(" || KAMÍNEK: žádný načtený");
            }

            Ok("sber: " + sb);
            Diag("[props sber] " + sb);
        }

        /// <summary>
        /// Kolo 11: /kmen info | look on|off | param &lt;stín&gt; &lt;obloha&gt; | maska on|off.
        /// Info vypíše kmenové materiály, doplněk oblohy, SH a průměrné albedo palety na UV kmenů.
        /// Maska zbarví kmeny plnou purpurovou bez světla – pro měření pixelů kmene ve snímku.
        /// </summary>
        /// <summary>
        /// Kolo 11 diagnostika: /let on|off – zapne let hráče (existující režim FirstPersonController,
        /// jinak na klávese), aby šly snímky vody shora. Nic neukládá; enableFlight vrací „off“.
        /// </summary>
        private void CmdFly(string[] a)
        {
            var fpc = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) { Error("FirstPersonController not found."); return; }
            bool on = a.Length < 2 || a[1] == "on";
            if (on) { if (!flyWasEnabled.HasValue) flyWasEnabled = fpc.enableFlight; fpc.enableFlight = true; }
            else { fpc.enableFlight = flyWasEnabled ?? false; flyWasEnabled = null; }
            var f = typeof(FirstPersonController).GetField("flightRequested",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f != null) f.SetValue(fpc, on);
            Ok("Flight " + (on ? "ON" : "OFF") + (f == null ? " (flightRequested field not found)" : ""));
        }
        private static bool? flyWasEnabled;

        /// <summary>
        /// Kolo 12: /pruhy diag N (0 normálně, 1 bez strmé barvy, 2 šedá, 3 strmá barva, 4 terasy+strmá, 5 bez mikro-variace)
        /// | profil x z smer delka – výškový profil povrchu po 0,5 m (hledání stupňů v geometrii)
        /// | mapa x z r krok – vrstvy výšky do CSV | teras on|off – měkké stupně slabého terasování (kolo 12).
        /// Barvení platí pro nově stavěné chunky (odskok a návrat).
        /// </summary>
        private void CmdBands(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;
            string sub = a.Length > 1 ? a[1] : "";
            if (sub == "diag" && a.Length > 2 && TryNumber(a[2], out float m))
            {
                t.SetPaletteDiag(m);
                Ok("pruhy: diagnostika barvení " + m.ToString(CultureInfo.InvariantCulture) + " – platí pro nově postavené chunky");
                Diag("[pruhy] diag " + m.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (sub == "profil" && a.Length > 5 && TryNumber(a[2], out float x0) && TryNumber(a[3], out float z0)
                && TryNumber(a[4], out float ang) && TryNumber(a[5], out float len))
            {
                var sb = new System.Text.StringBuilder();
                float dx = Mathf.Sin(ang * Mathf.Deg2Rad), dz = Mathf.Cos(ang * Mathf.Deg2Rad);
                sb.AppendFormat(CultureInfo.InvariantCulture, "[pruhy profil] ({0:0},{1:0}) směr {2:0}° délka {3:0} m, krok 0,5 m:", x0, z0, ang, len);
                for (float s2 = 0f; s2 <= len; s2 += 0.5f)
                {
                    float px = x0 + dx * s2, pz = z0 + dz * s2;
                    t.ProbeSurface(px, pz, 0.5f, out float h, out _, out _, out _);
                    t.ProbeLayers(px, pz, out float lm, out float lnt, out float ls, out float lcl);
                    sb.Append(' ').Append(lm.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                      .Append(lnt.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                      .Append(ls.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                      .Append(lcl.ToString("0.00", CultureInfo.InvariantCulture)).Append('|');
                    float hr = float.NaN;
                    if (Physics.Raycast(new Vector3(px, h + 40f, pz), Vector3.down, out RaycastHit rh, 120f, ~0, QueryTriggerInteraction.Ignore))
                        hr = rh.point.y;
                    sb.Append(' ').Append(h.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                      .Append(hr.ToString("0.00", CultureInfo.InvariantCulture));
                }
                Diag(sb.ToString());
                Ok("pruhy: profil zapsán do diag.log");
                return;
            }
            if (sub == "detail" && a.Length > 5 && TryNumber(a[2], out float dr) && TryNumber(a[3], out float dg)
                && TryNumber(a[4], out float dov) && TryNumber(a[5], out float dsq))
            {
                float dcv = 1f, dte = 1f;
                if (a.Length > 6) TryNumber(a[6], out dcv);
                if (a.Length > 7) TryNumber(a[7], out dte);
                t.SetDetailDiag(dr, dg, dov, dsq, dcv, dte);
                Ok(string.Format(CultureInfo.InvariantCulture, "pruhy: detail ridge×{0} grain×{1} převisy×{2} boule×{3} jeskyně×{4} terasy×{5} – platí pro nově postavené sloupce", dr, dg, dov, dsq, dcv, dte));
                Diag(string.Format(CultureInfo.InvariantCulture, "[pruhy] detail {0} {1} {2} {3} {4} {5}", dr, dg, dov, dsq, dcv, dte));
                return;
            }
            if (sub == "pix" && a.Length > 3)
            {
                // Kolo 12b: pixely snímku (u, v v 0–1, v shora) → světový bod povrchu. Víc dvojic naráz.
                Camera cam = Camera.main;
                var sbp = new System.Text.StringBuilder("[pruhy pix]");
                for (int k = 2; k + 1 < a.Length; k += 2)
                {
                    if (cam == null || !TryNumber(a[k], out float pu) || !TryNumber(a[k + 1], out float pv)) continue;
                    Ray ray = cam.ViewportPointToRay(new Vector3(pu, 1f - pv, 0f));
                    // Kolidery jsou jen u hráče, takže se paprsek pochoduje proti analytickému povrchu.
                    float hitT = -1f;
                    for (float tt = 1f; tt < 3000f; tt += 0.5f)
                    {
                        Vector3 q = ray.origin + ray.direction * tt;
                        t.ProbeLayers(q.x, q.z, out _, out _, out float qs, out _);
                        if (q.y <= qs) { hitT = tt; break; }
                    }
                    if (hitT > 0f)
                    {
                        Vector3 hp = ray.origin + ray.direction * hitT;
                        sbp.AppendFormat(CultureInfo.InvariantCulture, " ({0:0.###},{1:0.###})→{2:0.0},{3:0.0},{4:0.0}", pu, pv, hp.x, hp.y, hp.z);
                    }
                    else sbp.AppendFormat(CultureInfo.InvariantCulture, " ({0:0.###},{1:0.###})→nic", pu, pv);
                }
                Diag(sbp.ToString());
                Ok("pruhy: pix zapsán do diag.log");
                return;
            }
            if (sub == "sev" && a.Length > 2)
            {
                // Kolo 12b, jen diagnostika: vypne šití švů LOD (LodSeamJob + pás sklonu/ořezu).
                VoxelChunkBuilder.BenchNoSeam = a[2] == "off";
                Ok("pruhy: šití švů LOD " + (VoxelChunkBuilder.BenchNoSeam ? "VYP" : "ZAP") + " – platí pro nově postavené chunky");
                Diag("[pruhy] sev " + (VoxelChunkBuilder.BenchNoSeam ? "VYP" : "ZAP"));
                return;
            }
            if (sub == "klid" && a.Length > 2)
            {
                // Kolo 12b: klidný povrchový 3D detail; volitelně svislý násobič warpu a oktávy.
                // /pruhy klid on|off [maska ystretch oktávyWarp oktávyBoulí]; -1 = beze změny.
                float ys = -1f, oo = -1f, so = -1f, mk = -1f;
                if (a.Length > 3) TryNumber(a[3], out mk);
                if (a.Length > 4) TryNumber(a[4], out ys);
                if (a.Length > 5) TryNumber(a[5], out oo);
                if (a.Length > 6) TryNumber(a[6], out so);
                t.SetCalmDetail(a[2] == "on", ys, (int)oo, (int)so, (int)mk);
                Ok("pruhy: klidný detail " + (t.CalmDetailOn ? "ZAP" : "VYP") + " – platí pro nově postavené sloupce");
                Diag(string.Format(CultureInfo.InvariantCulture, "[pruhy] klid {0} maska {1} {2} {3} {4}", t.CalmDetailOn ? "ZAP" : "VYP", mk, ys, oo, so));
                return;
            }
            if (sub == "teras" && a.Length > 2)
            {
                t.SetSoftTerrace(a[2] == "on");
                Ok("pruhy: měkké stupně slabého terasování " + (t.SoftTerraceOn ? "ZAP" : "VYP") + " – platí pro nově postavené sloupce");
                Diag("[pruhy] teras " + (t.SoftTerraceOn ? "ZAP" : "VYP"));
                return;
            }
            if (sub == "mapa" && a.Length > 5 && TryNumber(a[2], out float mx) && TryNumber(a[3], out float mz)
                && TryNumber(a[4], out float mr) && TryNumber(a[5], out float mstep))
            {
                // Kolo 12: výškové vrstvy na mřížce do CSV (_reports/kolo12/mapa_<seed>_<x>_<z>.csv).
                var csv = new System.Text.StringBuilder("x,z,macro,bezteras,povrch,teras\n");
                for (float gz = -mr; gz <= mr; gz += mstep)
                for (float gx = -mr; gx <= mr; gx += mstep)
                {
                    t.ProbeLayers(mx + gx, mz + gz, out float lm, out float lnt, out float ls, out float lcl);
                    csv.AppendFormat(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.000},{3:0.000},{4:0.000},{5:0.000}\n", mx + gx, mz + gz, lm, lnt, ls, lcl);
                }
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_reports", "kolo12");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, string.Format(CultureInfo.InvariantCulture, "mapa_{0}_{1:0}_{2:0}_{3}{4}.csv", t.WorldSeed, mx, mz, t.SoftTerraceOn ? "zap" : "vyp",
                    a.Length > 6 ? "_" + a[6] : ""));
                System.IO.File.WriteAllText(file, csv.ToString());
                Ok("pruhy: mapa → " + file);
                return;
            }
            Ok("pruhy: diag " + t.PaletteDiag.ToString(CultureInfo.InvariantCulture) + " | /pruhy diag N | /pruhy profil x z smer delka");
        }

        private void CmdTrunk(string[] a)
        {
            string sub = a.Length > 1 ? a[1] : "info";
            var sb = new System.Text.StringBuilder();
            if (sub == "look" && a.Length > 2) World.Spawning.ObjectSpawner.TrunkLook = a[2] == "on";
            else if (sub == "param" && a.Length > 3 && TryNumber(a[2], out float fs) && TryNumber(a[3], out float fa))
            {
                World.Spawning.ObjectSpawner.TrunkShadowFloor = Mathf.Max(0f, fs); World.Spawning.ObjectSpawner.TrunkAmbient = Mathf.Max(0f, fa);
                if (a.Length > 4 && TryNumber(a[4], out float fl)) World.Spawning.ObjectSpawner.TrunkAlbedoFloor = Mathf.Max(0f, fl);
            }
            else if (sub == "maska" && a.Length > 2) World.Spawning.ObjectSpawner.TrunkDiagMask = a[2] == "on" ? 1 : 0;
            World.Spawning.ObjectSpawner.UpdateTrunkLook();
            sb.Append(World.Spawning.ObjectSpawner.TrunkInfo());

            if (sub == "info")
            {
                var sh = RenderSettings.ambientProbe;
                var dirs = new[] { Vector3.up, Vector3.right, Vector3.down };
                var res = new Color[3];
                sh.Evaluate(dirs, res);
                sb.AppendFormat(CultureInfo.InvariantCulture, " | SH nahoru {0:0.000}, bok {1:0.000}, dolů {2:0.000}, slunce {3:0.00}",
                    res[0].grayscale, res[1].grayscale, res[2].grayscale, RenderSettings.sun != null ? RenderSettings.sun.intensity : -1f);

                // Albedo palety na UV kmene: průměr přes vrcholy submeshe s kmenovým materiálem.
                var seen = new HashSet<Mesh>();
                int listed = 0;
                foreach (MeshRenderer r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (listed >= 12) break;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Material[] ms = r.sharedMaterials;
                    for (int k = 0; k < ms.Length && k < mf.sharedMesh.subMeshCount; k++)
                    {
                        if (ms[k] == null || !World.Spawning.ObjectSpawner.TrunkMaterials.Contains(ms[k])) continue;
                        if (!seen.Add(mf.sharedMesh)) continue;
                        Color alb = PaletteAlbedo(mf.sharedMesh, k, ms[k].GetTexture("_BaseMap"));
                        Color lin = alb.linear;
                        sb.AppendFormat(CultureInfo.InvariantCulture, " | {0}: albedo sRGB {1:0.00} {2:0.00} {3:0.00} (lin. jas {4:0.000}), smooth {5:0.00}, kw {6}",
                            mf.sharedMesh.name, alb.r, alb.g, alb.b, lin.grayscale,
                            ms[k].HasProperty("_Smoothness") ? ms[k].GetFloat("_Smoothness") : -1f, string.Join(",", ms[k].shaderKeywords));
                        listed++;
                    }
                }
            }
            Ok("kmen: " + sb); Diag("[kmen] " + sb);
        }

        /// <summary>Průměrná barva textury (sRGB) na UV vrcholů jednoho submeshe – čte se přes RT, textura nemusí být čitelná.</summary>
        private static Color PaletteAlbedo(Mesh mesh, int sub, Texture tex)
        {
            if (tex == null) return Color.white;
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            if (uvs.Count == 0) return Color.white;
            int[] idx = mesh.GetIndices(sub);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false);
            t2.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            t2.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color sum = Color.black; int n = 0;
            for (int i = 0; i < idx.Length; i++)
            {
                Vector2 uv = uvs[idx[i]];
                sum += t2.GetPixelBilinear(uv.x - Mathf.Floor(uv.x), uv.y - Mathf.Floor(uv.y));
                n++;
            }
            UnityEngine.Object.Destroy(t2);
            return n > 0 ? sum / n : Color.white;
        }

        private void CmdFoliage(string[] a)
        {
            string sub = a.Length > 1 ? a[1] : "info";
            var mats = new Dictionary<Material, int>();
            FoliageMaterials(mats);
            var sb = new System.Text.StringBuilder();
            if (sub == "info")
            {
                foreach (var kv in mats)
                {
                    Material m = kv.Key;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0} [{1}] ×{2} q{3} cut {4:0.00} kw {5} | ",
                        m.name, m.shader.name, kv.Value, m.renderQueue,
                        m.HasProperty("_AlphaCutoff") ? m.GetFloat("_AlphaCutoff") : -1f,
                        string.Join(",", m.shaderKeywords));
                }
                var ss = FindSsao();
                sb.Append("SSAO ").Append(ss == null ? "nenalezeno" : (ss.isActive ? "aktivní" : "neaktivní"));
                var sun = RenderSettings.sun;
                sb.Append(", slunce stíny ").Append(sun != null ? sun.shadows.ToString() : "?");
                sb.Append(", MSAA ").Append(QualitySettings.antiAliasing);
                var sh = RenderSettings.ambientProbe;
                var dirs = new[] { Vector3.up, Vector3.right, Vector3.down };
                var res = new Color[3];
                sh.Evaluate(dirs, res);
                sb.AppendFormat(CultureInfo.InvariantCulture, ", ambient {0} int {1:0.00}, sky {2}, SH nahoru {3}, bok {4}, dolů {5}, slunce {6:0.00}×{7}",
                    RenderSettings.ambientMode, RenderSettings.ambientIntensity, RenderSettings.ambientSkyColor, res[0], res[1], res[2],
                    RenderSettings.sun != null ? RenderSettings.sun.intensity : -1f, RenderSettings.sun != null ? RenderSettings.sun.color.ToString() : "?");
            }
            else if (sub == "ssao" && a.Length > 2)
            {
                var ss = FindSsao();
                if (ss != null) { ss.SetActive(a[2] == "on"); folSsaoTouched = true; sb.Append("SSAO ").Append(ss.isActive ? "ZAP" : "VYP"); }
                else sb.Append("SSAO nenalezeno");
            }
            else if (sub == "stiny" && a.Length > 2)
            {
                var sun = RenderSettings.sun;
                if (sun != null) sun.shadows = a[2] == "on" ? LightShadows.Soft : LightShadows.None;
                SunRotation.DiagNoShadows = a[2] != "on";
                sb.Append("stíny slunce ").Append(a[2]);
            }
            else if (sub == "vrhani" && a.Length > 2)
            {
                bool off = a[2] != "on";
                World.Spawning.VegetationRenderer.DiagNoShadows = off;
                int n = 0;
                foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    Material m = r.sharedMaterial;
                    if (m == null || m.shader == null || !m.shader.name.Contains("Vegetation")) continue;
                    r.shadowCastingMode = off ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                    n++;
                }
                sb.Append("vrhání stínů vegetace ").Append(off ? "VYP" : "ZAP").Append(", rendererů ").Append(n);
            }
            else if (sub == "cutoff" && a.Length > 2)
            {
                bool reset = a[2] == "reset";
                TryNumber(a[2], out float c);
                foreach (var kv in mats)
                {
                    Material m = kv.Key;
                    if (!m.HasProperty("_AlphaCutoff")) continue;
                    if (!folCutoff.ContainsKey(m)) folCutoff[m] = m.GetFloat("_AlphaCutoff");
                    m.SetFloat("_AlphaCutoff", reset ? folCutoff[m] : c);
                }
                if (reset) folCutoff.Clear();
                sb.Append("cutoff ").Append(a[2]).Append(" na ").Append(mats.Count).Append(" materiálech");
            }
            else if (sub == "param" && a.Length > 3 && TryNumber(a[3], out float pv))
            {
                Shader.SetGlobalFloat("_Fol" + a[2], pv);
                sb.Append("_Fol").Append(a[2]).Append(" = ").Append(pv.ToString(CultureInfo.InvariantCulture));
            }
            else if (sub == "look" && a.Length > 2)
            {
                VoxelTerrain.ApplyFoliageLook(a[2] == "on");
                sb.Append("světlo vegetace kola 10 ").Append(a[2] == "on" ? "ZAP" : "VYP");
            }
            else if (sub == "tok" && a.Length > 2)
            {
                WaterSurface.FlowAttributes = a[2] == "on";
                sb.Append("tok vody (atributy) ").Append(a[2] == "on" ? "ZAP" : "VYP").Append(" – platí pro nově postavené sloupce");
            }
            else if (sub == "tokcheck")
            {
                // Směr toku proti skutečnému spádu hladiny: v bodech koryta v okruhu 400 m
                // se porovná riverY 3 m po a proti směru. Po proudu nesmí hladina stoupat.
                if (!RequireTerrain(out VoxelTerrain tt, out Transform pp)) return;
                int tot = 0, down = 0, flat = 0, up = 0, turns = 0; float maxTurn = 0f;
                Vector3 c = pp.position;
                for (float gx = -400f; gx <= 400f; gx += 8f)
                for (float gz = -400f; gz <= 400f; gz += 8f)
                {
                    float x = c.x + gx, z = c.z + gz;
                    if (!tt.ProbeSurface(x, z, 1f, out float sf, out float core, out float ry, out float ly)) continue;
                    if (core < 0.5f || ry <= sf || ly > ry) continue;
                    if (!tt.FlowAt(x, z, out Vector2 d, out float sl) || d.sqrMagnitude < 0.5f) continue;
                    tt.ProbeSurface(x + d.x * 3f, z + d.y * 3f, 1f, out _, out _, out float ryDn, out _);
                    tt.ProbeSurface(x - d.x * 3f, z - d.y * 3f, 1f, out _, out _, out float ryUp, out _);
                    tot++;
                    float diff = ryUp - ryDn;
                    if (diff > 0.005f) down++; else if (diff < -0.005f) up++; else flat++;
                    if (tt.FlowAt(x + 4f, z, out Vector2 d2, out _))
                    {
                        float ang = Vector2.Angle(d, d2);
                        if (ang > 45f) turns++;
                        maxTurn = Mathf.Max(maxTurn, ang);
                    }
                }
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "tokcheck: bodů koryta {0}, hladina po proudu klesá {1}, rovná (±5 mm) {2}, STOUPÁ {3}; skok směru >45° na 4 m: {4} (max {5:0}°)",
                    tot, down, flat, up, turns, maxTurn);
            }
            else if (sub == "tokblend" && a.Length > 2)
            {
                if (!RequireTerrain(out VoxelTerrain tb, out _)) return;
                tb.SetFlowBlend(a[2] == "on");
                sb.Append("plynulý směr u soutoků (kolo 11) ").Append(tb.FlowBlendOn ? "ZAP" : "VYP").Append(" – hladina platí pro nově postavené sloupce");
            }
            else if (sub == "tokmapa" || sub == "tokcheck2")
            {
                // Kolo 11: hustší kontrola směru toku. Mřížka 4 m v okruhu r (výchozí 400 m), body koryta
                // jako v tokcheck. Skok směru se měří k sousedům +4 m v OBOU osách, skok rychlosti jako
                // rozdíl vektorů rychlosti (tentýž vzorec jako WaterSurface: 0,35 + 7·spád, max 3 m/s).
                // „Proti spádu“: hladina 3 m po proudu výš než 3 m proti proudu (o víc než 5 mm).
                // tokmapa navíc vypíše shluky skoků se seznamem úseček kolem (soutok / ohyb / souběh).
                if (!RequireTerrain(out VoxelTerrain tt, out Transform pp)) return;
                float rad = 400f;
                if (a.Length > 2) TryNumber(a[2], out rad);
                Vector3 c = pp.position;
                int tot = 0, up = 0, t45 = 0, t30 = 0, dv = 0, fast = 0; float maxTurn = 0f, maxDv = 0f, maxV = 0f;
                var bad = new List<Vector3>();
                for (float gx = -rad; gx <= rad; gx += 4f)
                for (float gz = -rad; gz <= rad; gz += 4f)
                {
                    float x = c.x + gx, z = c.z + gz;
                    if (!tt.ProbeSurface(x, z, 1f, out float sf, out float core, out float ry, out float ly)) continue;
                    if (core < 0.5f || ry <= sf || ly > ry) continue;
                    if (!tt.FlowAt(x, z, out Vector2 d, out float sl) || d.sqrMagnitude < 0.5f) continue;
                    tot++;
                    tt.ProbeSurface(x + d.x * 3f, z + d.y * 3f, 1f, out _, out _, out float ryDn, out _);
                    tt.ProbeSurface(x - d.x * 3f, z - d.y * 3f, 1f, out _, out _, out float ryUp, out _);
                    if (ryUp - ryDn < -0.005f) up++;
                    Vector2 v0 = d * Mathf.Clamp(0.35f + 7f * sl, 0.35f, 3f);
                    if (v0.magnitude > 0.9f) fast++;
                    maxV = Mathf.Max(maxV, v0.magnitude);
                    float worst = 0f, worstDv = 0f;
                    for (int k = 0; k < 2; k++)
                    {
                        float nx = x + (k == 0 ? 4f : 0f), nz = z + (k == 1 ? 4f : 0f);
                        if (!tt.ProbeSurface(nx, nz, 1f, out float sf2, out float core2, out float ry2, out float ly2)) continue;
                        if (core2 < 0.5f || ry2 <= sf2 || ly2 > ry2) continue;
                        if (!tt.FlowAt(nx, nz, out Vector2 d2, out float sl2) || d2.sqrMagnitude < 0.5f) continue;
                        worst = Mathf.Max(worst, Vector2.Angle(d, d2));
                        worstDv = Mathf.Max(worstDv, (v0 - d2 * Mathf.Clamp(0.35f + 7f * sl2, 0.35f, 3f)).magnitude);
                    }
                    if (worst > 45f) { t45++; bad.Add(new Vector3(x, worst, z)); }
                    if (worst > 30f) t30++;
                    if (worstDv > 0.6f) dv++;
                    maxTurn = Mathf.Max(maxTurn, worst); maxDv = Mathf.Max(maxDv, worstDv);
                }
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{0} r={1:0}: plynulý směr {2}; bodů koryta {3}, proti spádu {4}; skok směru na 4 m (x i z) >30° {5}, >45° {6} (max {7:0}°); skok rychlosti >0,6 m/s {8} (max {9:0.00}); peřejí (>0,9 m/s) {10}, max rychlost {11:0.00}",
                    sub, rad, tt.FlowBlendOn ? "ZAP" : "VYP", tot, up, t30, t45, maxTurn, dv, maxDv, fast, maxV);
                if (sub == "tokmapa")
                {
                    var used = new bool[bad.Count];
                    int nCl = 0;
                    for (int i = 0; i < bad.Count && nCl < 12; i++)
                    {
                        if (used[i]) continue;
                        Vector3 w = bad[i]; int cnt = 0;
                        for (int j = i; j < bad.Count; j++)
                            if (!used[j] && new Vector2(bad[j].x - bad[i].x, bad[j].z - bad[i].z).magnitude < 40f)
                            { used[j] = true; cnt++; if (bad[j].y > w.y) w = bad[j]; }
                        nCl++;
                        tt.FlowAt(w.x, w.z, out Vector2 dw, out float slw);
                        Diag(string.Format(CultureInfo.InvariantCulture,
                            "[folaz tokmapa] shluk {0}: {1} bodů, nejhorší ({2:0},{3:0}) {4:0}°, směr {5:0}°, spád {6:0.000} | {7}",
                            nCl, cnt, w.x, w.z, w.y, Mathf.Atan2(dw.y, dw.x) * Mathf.Rad2Deg, slw, tt.FlowDescribe(w.x, w.z, 70f)));
                    }
                }
            }
            else if (sub == "soutoky")
            {
                // Kolo 11: najde soutoky (D8 buňky se dvěma přítoky nad prahem řeky) v okruhu r
                // a u každého změří tok v okruhu 70 m (mřížka 3 m): skoky směru, rychlosti, proti spádu.
                if (!RequireTerrain(out VoxelTerrain tt, out Transform pp)) return;
                float rad = 1500f;
                if (a.Length > 2) TryNumber(a[2], out rad);
                var list = new List<Vector3>();
                tt.FindConfluences(pp.position, rad, list);
                int allTot = 0, allUp = 0, allT45 = 0, allT30 = 0, allDv = 0; float allMax = 0f;
                foreach (Vector3 cf in list)
                {
                    int tot = 0, up = 0, t45 = 0, t30 = 0, dv = 0; float maxTurn = 0f;
                    for (float gx = -70f; gx <= 70f; gx += 3f)
                    for (float gz = -70f; gz <= 70f; gz += 3f)
                    {
                        float x = cf.x + gx, z = cf.z + gz;
                        if (!tt.ProbeSurface(x, z, 1f, out float sf, out float core, out float ry, out float ly)) continue;
                        if (core < 0.5f || ry <= sf || ly > ry) continue;
                        if (!tt.FlowAt(x, z, out Vector2 d, out float sl) || d.sqrMagnitude < 0.5f) continue;
                        tot++;
                        tt.ProbeSurface(x + d.x * 3f, z + d.y * 3f, 1f, out _, out _, out float ryDn, out _);
                        tt.ProbeSurface(x - d.x * 3f, z - d.y * 3f, 1f, out _, out _, out float ryUp, out _);
                        if (ryUp - ryDn < -0.005f)
                        {
                            up++;
                            if (a.Length > 3 && a[3] == "detail" && up <= 3)
                            {
                                // Kolo 11: proti spádu i na 10 m? A proti směru nejbližší úsečky (bez míchání)?
                                tt.ProbeSurface(x + d.x * 10f, z + d.y * 10f, 1f, out _, out _, out float dn10, out _);
                                tt.ProbeSurface(x - d.x * 10f, z - d.y * 10f, 1f, out _, out _, out float up10, out _);
                                Diag(string.Format(CultureInfo.InvariantCulture,
                                    "[folaz soutoky detail] ({0:0.0},{1:0.0}) hl {2:0.000} | ±3 m: proti {3:0.000} po {4:0.000} | ±10 m: proti {5:0.000} po {6:0.000} | směr {7:0}° |{8}| | {9}",
                                    x, z, ry, ryUp, ryDn, up10, dn10, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, d.magnitude, tt.FlowDescribe(x, z, 50f)));
                            }
                        }
                        Vector2 v0 = d * Mathf.Clamp(0.35f + 7f * sl, 0.35f, 3f);
                        float worst = 0f, worstDv = 0f;
                        for (int k = 0; k < 2; k++)
                        {
                            float nx = x + (k == 0 ? 3f : 0f), nz = z + (k == 1 ? 3f : 0f);
                            if (!tt.ProbeSurface(nx, nz, 1f, out float sf2, out float core2, out float ry2, out float ly2)) continue;
                            if (core2 < 0.5f || ry2 <= sf2 || ly2 > ry2) continue;
                            if (!tt.FlowAt(nx, nz, out Vector2 d2, out float sl2) || d2.sqrMagnitude < 0.5f) continue;
                            worst = Mathf.Max(worst, Vector2.Angle(d, d2));
                            worstDv = Mathf.Max(worstDv, (v0 - d2 * Mathf.Clamp(0.35f + 7f * sl2, 0.35f, 3f)).magnitude);
                        }
                        if (worst > 45f) t45++;
                        if (worst > 30f) t30++;
                        if (worstDv > 0.6f) dv++;
                        maxTurn = Mathf.Max(maxTurn, worst);
                    }
                    allTot += tot; allUp += up; allT45 += t45; allT30 += t30; allDv += dv; allMax = Mathf.Max(allMax, maxTurn);
                    Diag(string.Format(CultureInfo.InvariantCulture,
                        "[folaz soutoky] ({0:0},{1:0}) úhel přítoků {2:0}°: bodů hladiny {3}, proti spádu {4}, skok směru na 3 m >30° {5}, >45° {6} (max {7:0}°), skok rychlosti >0,6 m/s {8}",
                        cf.x, cf.z, cf.y, tot, up, t30, t45, maxTurn, dv));
                }
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "soutoky r={0:0}: plynulý směr {1}; buněk řeky {9}, s přítokem {10}; soutoků {2}, bodů hladiny {3}, proti spádu {4}, skok směru na 3 m >30° {5}, >45° {6} (max {7:0}°), skok rychlosti >0,6 m/s {8}",
                    rad, tt.FlowBlendOn ? "ZAP" : "VYP", list.Count, allTot, allUp, allT30, allT45, allMax, allDv,
                    VoxelTerrain.ConfluenceStats.x, VoxelTerrain.ConfluenceStats.y);
            }
            else if (sub == "tokpole" && a.Length > 4 && TryNumber(a[2], out float px0) && TryNumber(a[3], out float pz0) && TryNumber(a[4], out float pr))
            {
                // Kolo 11: pole toku kolem bodu do CSV (_reports/kolo11/pole_<seed>_<x>_<z>_<zap|vyp>.csv) pro graf.
                if (!RequireTerrain(out VoxelTerrain tt, out _)) return;
                var csv = new System.Text.StringBuilder("x,z,voda,dx,dz,rychlost,hladina\n");
                int n = 0;
                for (float gx = -pr; gx <= pr; gx += 2f)
                for (float gz = -pr; gz <= pr; gz += 2f)
                {
                    float x = px0 + gx, z = pz0 + gz;
                    if (!tt.ProbeSurface(x, z, 1f, out float sf, out float core, out float ry, out float ly)) continue;
                    bool water = core >= 0.5f && ry > sf && !(ly > ry);
                    Vector2 d = Vector2.zero; float sl = 0f;
                    if (water) tt.FlowAt(x, z, out d, out sl);
                    csv.AppendFormat(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2},{3:0.000},{4:0.000},{5:0.000},{6:0.000}\n",
                        x, z, water ? 1 : 0, d.x, d.y, Mathf.Clamp(0.35f + 7f * sl, 0.35f, 3f) * d.magnitude, ry);
                    n++;
                }
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_reports", "kolo11");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, string.Format(CultureInfo.InvariantCulture, "pole_{0}_{1:0}_{2:0}_{3}.csv",
                    tt.WorldSeed, px0, pz0, tt.FlowBlendOn ? "zap" : "vyp"));
                System.IO.File.WriteAllText(file, csv.ToString());
                sb.Append("tokpole: ").Append(n).Append(" bodů → ").Append(file);
            }
            else if (sub == "reset")
            {
                foreach (var kv in folCutoff) if (kv.Key != null) kv.Key.SetFloat("_AlphaCutoff", kv.Value);
                folCutoff.Clear();
                if (folSsaoTouched) { var ss = FindSsao(); if (ss != null) ss.SetActive(true); folSsaoTouched = false; }
                World.Spawning.VegetationRenderer.DiagNoShadows = false;
                SunRotation.DiagNoShadows = false;
                sb.Append("reset diagnostiky vegetace");
            }
            Ok(sb.ToString()); Diag("[folaz] " + sb);
        }

        /// <summary>
        /// Kolo 9: /art9 on|off | lesk &lt;power&gt; &lt;síla&gt; | mlha &lt;near&gt; &lt;nearHory&gt; &lt;expoHory&gt; | barva r g b.
        /// Odlesk vody platí hned (sdílená kopie materiálu), mlha při dalším snímku.
        /// </summary>
        private void CmdArt9(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;
            string sub = a.Length > 1 ? a[1] : "";
            if (sub == "on" || sub == "off") WorldGenSettings.Art9Enabled = sub == "on";
            else if (sub == "lesk" && a.Length >= 4 && TryNumber(a[2], out float pw) && TryNumber(a[3], out float st))
            { VoxelTerrain.Art9SpecPower = pw; VoxelTerrain.Art9SpecStrength = st; }
            else if (sub == "mlha" && a.Length >= 5 && TryNumber(a[2], out float n0) && TryNumber(a[3], out float n1) && TryNumber(a[4], out float ex))
            { World.Biomes.BiomeAtmosphere.Art9FogNear = n0; World.Biomes.BiomeAtmosphere.Art9FogNearMountain = n1; World.Biomes.BiomeAtmosphere.Art9MountainExposure = ex; }
            else if (sub == "barva" && a.Length >= 5 && TryNumber(a[2], out float r) && TryNumber(a[3], out float g) && TryNumber(a[4], out float b))
            { World.Biomes.BiomeAtmosphere.Art9FogTintMountain = new Color(r, g, b, 1f); }
            t.ApplyWaterSpec();
            string msg = string.Format(CultureInfo.InvariantCulture,
                "art9 {0}: lesk {1:0}/{2:0.00}, mlha near {3:0.00}/{4:0.00}, expo hory {5:0.00}, barva hory {6}",
                WorldGenSettings.Art9Enabled ? "ZAP" : "VYP", VoxelTerrain.Art9SpecPower, VoxelTerrain.Art9SpecStrength,
                World.Biomes.BiomeAtmosphere.Art9FogNear, World.Biomes.BiomeAtmosphere.Art9FogNearMountain,
                World.Biomes.BiomeAtmosphere.Art9MountainExposure, World.Biomes.BiomeAtmosphere.Art9FogTintMountain);
            Ok(msg); Diag("[Art9] " + msg);
        }

        /// <summary>Kolo 8: /art on|off – umělecká paleta biomů (platí pro nově stavěné chunky).</summary>
        private void CmdArt(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;
            if (a.Length >= 2 && (a[1] == "on" || a[1] == "off")) t.SetArtPass(a[1] == "on");
            Ok("art paleta " + (t.ArtPassOn ? "ZAP" : "VYP") + " (platí pro nově postavené chunky)");
            Diag("[Art] " + (t.ArtPassOn ? "ZAP" : "VYP"));
        }

        private void CmdLook(string[] a)
        {
            var fpc = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) { Error("FirstPersonController not found."); return; }

            if (a.Length < 2)
            {
                Vector2 cur = fpc.CurrentLook;
                Ok(string.Format(CultureInfo.InvariantCulture,
                    "yaw {0:0.0}, pitch {1:0.0}. Usage: /look <yaw> [pitch]", cur.x, cur.y));
                return;
            }

            if (!TryNumber(a[1], out float yaw))
            {
                Error("Yaw does not make sense. /look <yaw> [pitch]");
                return;
            }

            float pitch = fpc.CurrentLook.y;
            if (a.Length >= 3 && !TryNumber(a[2], out pitch))
            {
                Error("Pitch does not make sense. /look <yaw> [pitch]");
                return;
            }

            fpc.SetLook(yaw, pitch);
            Ok(string.Format(CultureInfo.InvariantCulture, "yaw {0:0.0}, pitch {1:0.0}", yaw, pitch));
        }

        /// <summary>
        /// Herní čas. Bez argumentu vypíše, kolik je a jak dlouhý je cyklus;
        /// <c>/time set &lt;co&gt;</c> ho přestaví.
        ///
        /// <para>Čas se dřív posouval klávesami Z a T. T ale zároveň otevírá tuhle konzoli,
        /// takže každé psaní do chatu poskočilo o hodinu – proto to má být příkaz, který
        /// se nedá spustit omylem a je po něm vidět, co se stalo.</para>
        /// </summary>
        private void CmdTime(string[] a)
        {
            var sun = UnityEngine.Object.FindFirstObjectByType<SunRotation>();
            if (sun == null) { Error("SunRotation not found - there is no day cycle in this scene."); return; }

            if (a.Length < 2)
            {
                Ok(string.Format(CultureInfo.InvariantCulture,
                    "{0} (full cycle {1:0} real minutes). Usage: /time set day|night|dawn|dusk|noon|midnight|<0-24>",
                    sun.GetTimeString(), sun.FullDayMinutes));
                return;
            }

            // "set" je volitelné: /time day i /time set day dělají totéž.
            int at = (a[1].ToLowerInvariant() == "set") ? 2 : 1;
            if (at >= a.Length)
            {
                Error("Usage: /time set day|night|dawn|dusk|noon|midnight|<0-24>");
                return;
            }

            string what = a[at].ToLowerInvariant();
            float target;

            switch (what)
            {
                case "day": case "den": target = 9f; break;
                case "noon": case "poledne": target = 12f; break;
                case "dusk": case "sunset": case "vecer": target = 20f; break;
                case "night": case "noc": target = 22f; break;
                case "midnight": case "pulnoc": target = 0f; break;
                case "dawn": case "sunrise": case "rano": target = 6.5f; break;
                default:
                    if (!TryNumber(what, out target))
                    {
                        Error("Unknown time " + LeftQuote + a[at] + RightQuote +
                              ". Use day|night|dawn|dusk|noon|midnight or a number 0-24.");
                        return;
                    }
                    break;
            }

            sun.SetTime(Mathf.Repeat(target, 24f));
            Ok("Time set to " + sun.GetTimeString() + ".");
        }

        /// <summary>
        /// Kolik vegetace se kreslí instancovaně. Slouží k ověření, že se tráva opravdu
        /// nekreslí po jednom objektu – z obrazu se to nepozná, vypadá to stejně.
        /// </summary>
        private void CmdVegetation(string[] a)
        {
            var vr = World.Spawning.VegetationRenderer.Instance;
            if (vr == null) { Error("VegetationRenderer is not running - grass falls back to GameObjects."); return; }

            Ok(string.Format(CultureInfo.InvariantCulture,
                "batches {0} registered, {1} drawn last frame, {2} instances",
                vr.BatchCount, vr.LastDrawCalls, vr.LastInstances));

            if (a.Length > 1 && (a[1] == "detail" || a[1] == "d")) VegetationDetail();
        }

        /// <summary>
        /// Kdo ve scéně kreslí jako GameObject. Statistika v okně Statistics řekne JEN součet,
        /// takže z ní se nepozná, jestli je na vině jeden hustý druh, nebo tisíc drobností –
        /// a bez toho se optimalizuje poslepu. Počítá se podle KOŘENE objektu, protože prefab
        /// s LODGroup má rendererů několik a zajímá nás jeho celkový podíl.
        /// </summary>
        private void VegetationDetail()
        {
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            var byRoot = new Dictionary<string, int>(64);
            var byObject = new Dictionary<string, int>(64);
            var sample = new Dictionary<string, string>(64);
            var roots = new HashSet<Transform>();
            int active = 0;

            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;

                active++;

                // Kořen = nejvyšší předek pod objektem chunku. Klony mají v názvu "(Clone)".
                Transform top = r.transform;
                while (top.parent != null
                       && top.parent.name.IndexOf("Chunk", StringComparison.OrdinalIgnoreCase) < 0
                       && !top.parent.name.StartsWith("Props", StringComparison.OrdinalIgnoreCase))
                    top = top.parent;

                string key = top.name.Replace("(Clone)", "").Trim();
                byRoot.TryGetValue(key, out int n);
                byRoot[key] = n + 1;

                // Objekty se počítají zvlášť: podíl rendererů na objekt řekne, jestli jde
                // o prefab s LODGroup (několik rendererů) nebo o jeden mesh.
                if (roots.Add(top))
                {
                    byObject.TryGetValue(key, out int m);
                    byObject[key] = m + 1;
                }

                // Jméno meshe a počet materiálů rozliší dva prefaby stejného jména z různých
                // asset packů – a hlavně řekne, proč se prefab nedá instancovat.
                if (!sample.ContainsKey(key))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    sample[key] = (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?")
                                  + " x" + r.sharedMaterials.Length + "mat, parent "
                                  + (top.parent != null ? top.parent.name : "-");
                }
            }

            Note(string.Format(CultureInfo.InvariantCulture,
                "{0} active renderers on {1} objects, {2} kinds", active, roots.Count, byRoot.Count));

            var list = new List<KeyValuePair<string, int>>(byRoot);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));

            int shown = Mathf.Min(10, list.Count);
            for (int i = 0; i < shown; i++)
            {
                byObject.TryGetValue(list[i].Key, out int objs);
                sample.TryGetValue(list[i].Key, out string info);
                Note(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-16} {1,5} rend ({2:0.0}%) / {3,4} obj = {4:0.0}/obj | {5}",
                    list[i].Key, list[i].Value, 100f * list[i].Value / Mathf.Max(1, active),
                    objs, list[i].Value / (float)Mathf.Max(1, objs), info));
            }
        }

        /// <summary>
        /// Jaké tónování zrovna běží. Grading je schválně slabý, takže se z obrazu nedá
        /// poznat, jestli funguje, nebo jestli je celý vypnutý – tenhle výpis to rozsoudí.
        /// </summary>
        private void CmdAtmosphere(string[] a)
        {
            var atmo = World.Biomes.BiomeAtmosphere.Instance;
            if (atmo == null) { Error("BiomeAtmosphere is not running - the world uses flat grading."); return; }

            // Kolo 30 (A/B): /atmo vyhled on|off – výšková mlha; /atmo stromy <od> <do> | off – pásmo konce stromů.
            if (a.Length > 2 && a[1] == "vyhled") SunRotation.VistaEnabled = a[2] != "off" && a[2] != "0";
            if (a.Length > 2 && a[1] == "stromy")
            {
                if (a[2] == "off" || a[2] == "0") World.Spawning.ObjectSpawner.TreeFadeEnd = 0f;
                else if (a.Length > 3 && TryNumber(a[2], out float f0) && TryNumber(a[3], out float f1))
                { World.Spawning.ObjectSpawner.TreeFadeStart = f0; World.Spawning.ObjectSpawner.TreeFadeEnd = f1; }
                World.Spawning.ObjectSpawner.UpdateTrunkLook();
            }
            Diag("[atmo] " + atmo.Describe() + string.Format(CultureInfo.InvariantCulture, ", stromy {0:0}-{1:0} m",
                World.Spawning.ObjectSpawner.TreeFadeStart, World.Spawning.ObjectSpawner.TreeFadeEnd));

            Ok(atmo.Describe());

            var parts = World.Biomes.AmbientParticles.Instance;
            if (parts != null) Note(parts.Describe());
        }

        /// <summary>
        /// Voda: audit souladu hladiny s terénem kolem hráče, nebo skok k nejbližší řece,
        /// jezeru či mořskému břehu. Skoky jsou deterministické, takže se dá po opravě
        /// vrátit na totéž místo a porovnat záběr.
        /// </summary>
        /// <summary>
        /// Diagnostika navíc do souboru _reports/diag.log vedle projektu (jen v editoru) –
        /// aby šly dlouhé výpisy měření číst celé, ne jen useknuté v chatu.
        /// </summary>
        private static void Diag(string text)
        {
            Debug.Log(text);
#if UNITY_EDITOR
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_reports");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "diag.log"),
                    System.DateTime.Now.ToString("HH:mm:ss ") + text + "\n");
            }
            catch (Exception) { }
#endif
        }

        private void CmdWater(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;

            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "audit";
            if (sub == "sev" && a.Length > 3 && TryNumber(a[2], out float svx) && TryNumber(a[3], out float svz))
            {
                int la = 2, lb = 3;
                if (a.Length > 5) { int.TryParse(a[4], out la); int.TryParse(a[5], out lb); }
                string rr = t.SeamProbe(svx, svz, la, lb);
                Diag("[Voda sev] " + rr);
                Note(rr);
                return;
            }
            if (sub == "sloupec" && a.Length > 4 && int.TryParse(a[2], out int sl)
                && int.TryParse(a[3], out int sx) && int.TryParse(a[4], out int sz))
            {
                string rr = t.ProbeColumnWater(sl, sx, sz);
                Diag("[Voda sloupec] " + rr);
                Note(rr);
                return;
            }
            if (sub == "detail" && a.Length > 3 && TryNumber(a[2], out float dx) && TryNumber(a[3], out float dz))
            {
                float dh = 2f, ds = 0.25f;
                if (a.Length > 4) TryNumber(a[4], out dh);
                if (a.Length > 5) TryNumber(a[5], out ds);
                Diag(WaterAudit.Detail(t, dx, dz, Mathf.Clamp(dh, 0.5f, 8f), Mathf.Clamp(ds, 0.05f, 2f)));
                Ok("water detail -> _reports/diag.log");
                return;
            }
            if (sub == "prah" || sub == "sill")
            {
                if (a.Length > 2) t.SetHydroSill(a[2] != "off" && a[2] != "0");
                Ok("práh u hydro schodu " + (t.HydroSillOn ? "ZAP" : "VYP") + " (platí pro nově postavené sloupce)");
                Diag("[Voda prah] " + (t.HydroSillOn ? "ZAP" : "VYP"));
                return;
            }
            if (sub == "prahab" || sub == "sillab")
            {
                if (search != null) { Error("Something is already running (/stop)."); return; }
                var ex7 = new List<Vector2>();
                for (int k = 2; k + 1 < a.Length; k += 2)
                    if (TryNumber(a[k], out float ex) && TryNumber(a[k + 1], out float ez)) ex7.Add(new Vector2(ex, ez));
                search = StartCoroutine(LipAB(t, p, ex7, true));
                return;
            }
            if (sub == "lipab")
            {
                if (search != null) { Error("Something is already running (/stop)."); return; }
                var extra = new List<Vector2>();
                for (int k = 2; k + 1 < a.Length; k += 2)
                    if (TryNumber(a[k], out float ex) && TryNumber(a[k + 1], out float ez)) extra.Add(new Vector2(ex, ez));
                search = StartCoroutine(LipAB(t, p, extra, false));
                return;
            }
            if (sub == "sevlod")
            {
                // Kolo 22: šev hladiny podle skutečného LOD souseda (A/B; přestaví hladinu LOD0 hned).
                var vt = VoxelTerrain.instance;
                if (vt == null) return;
                if (a.Length > 2 && a[2] == "reset") vt.ResetWaterSeamStats();
                else if (a.Length > 2) vt.SetWaterSeamByNeighbor(a[2] != "off" && a[2] != "0");
                string sevl = vt.WaterSeamStatus();
                Diag(sevl); Ok(sevl);
                return;
            }
            if (sub == "sevteren")
            {
                // Kolo 24: šev terénu podle skutečného LOD souseda (A/B; přestaví dotčené LOD0 sloupce omezeně za běhu).
                var vt = VoxelTerrain.instance;
                if (vt == null) return;
                if (a.Length > 2 && a[2] == "reset") vt.ResetTerrainSeamStats();
                else if (a.Length > 2) vt.SetTerrainSeamByNeighbor(a[2] != "off" && a[2] != "0");
                string sevt = vt.TerrainSeamStatus();
                Diag(sevt); Ok(sevt);
                return;
            }
            if (sub == "sevcap" || sub == "seamcap")
            {
                // Kolo 21: A/B uzlů švu hladiny (platí pro nově postavené sloupce).
                if (a.Length > 2) WaterSurface.SeamKnotCap = a[2] != "off" && a[2] != "0";
                string sc = "[Voda sevcap] šev hladiny " + (WaterSurface.SeamKnotCap ? "nesuší mokré uzly (kolo 21)" : "plná lomená čára (kolo 20)") + ", ponecháno uzlů " + WaterSurface.SeamKept;
                Diag(sc); Ok(sc);
                return;
            }
            if (sub == "lip")
            {
                if (a.Length > 2) WaterSurface.LipFix = a[2] != "off" && a[2] != "0";
                Ok("lip fix " + (WaterSurface.LipFix ? "ON" : "OFF") + ", osušených uzlů zatím " + WaterSurface.LipNodes + " (platí pro nově postavené sloupce)");
                Diag("[Voda lip] " + (WaterSurface.LipFix ? "ON" : "OFF") + " nodes " + WaterSurface.LipNodes);
                return;
            }
            if (sub == "profil" && a.Length > 6 && TryNumber(a[2], out float pfx) && TryNumber(a[3], out float pz0) && TryNumber(a[4], out float pz1)
                && TryNumber(a[5], out float pla) && TryNumber(a[6], out float plb))
            {
                VoxelTerrain.WaterSeamProbeOffset = a.Length > 7 && TryNumber(a[7], out float pof) ? pof : 0.02f;
                string pr = t.WaterSeamProfile(pfx, pz0, pz1, (int)pla, (int)plb);
                Diag(pr); Note(pr);
                return;
            }
            if (sub == "stale" && a.Length > 3 && TryNumber(a[2], out float stx) && TryNumber(a[3], out float stz))
            {
                string sr = t.WaterStaleProbe(stx, stz);
                Diag(sr); Note(sr);
                return;
            }
            if (sub == "lod")
            {
                float lr = 500f;
                if (a.Length > 2 && TryNumber(a[2], out float lrr)) lr = Mathf.Clamp(lrr, 50f, 2000f);
                string lt = WaterAudit.RunLod(t, p.position, lr);
                Diag(lt);
                foreach (string line in lt.Split('\n')) if (line.Trim().Length > 0) Note(line.Trim());
                return;
            }
            if (sub == "audit")
            {
                float r = 40f;
                if (a.Length > 2 && TryNumber(a[2], out float rr)) r = Mathf.Clamp(rr, 8f, 90f);
                WaterAudit.EdgeTestLift = a.Length > 3 && TryNumber(a[3], out float lift) ? lift : 0f;
                WaterAudit.Result res = WaterAudit.Run(t, p.position, r);
                if (WaterAudit.EdgeTestLift != 0f) Diag("[WaterAudit] test citlivosti: hladina v testu hran zvednuta o " + WaterAudit.EdgeTestLift.ToString(CultureInfo.InvariantCulture) + " m");
                WaterAudit.EdgeTestLift = 0f;
                string text = WaterAudit.Describe(res, r);
                Diag(text);
                Ok(string.Format(CultureInfo.InvariantCulture,
                    "wet {0}, floating edge {1} (max {2:0.00} m), steps {3}, islets {4}, holes {5}",
                    res.visibleWet, res.floatingEdges, res.worstFloat, res.steps, res.islands, res.holes));
                foreach (string line in text.Split('\n'))
                    if (line.StartsWith("  ") && line.Contains(":") && line.Contains("(")) Note(line.Trim());
                Note(t.WaterLayersAt(p.position));
                return;
            }

            if (sub == "bod" || sub == "probe")
            {
                Vector3 q = p.position;
                if (a.Length > 3 && TryCoord(a[2], q.x, out float bx) && TryCoord(a[3], q.z, out float bz))
                    q = new Vector3(bx, 0f, bz);
                float st = 1f;
                if (a.Length > 4 && int.TryParse(a[4], out int pl)) st = Mathf.Pow(2f, Mathf.Clamp(pl, 0, 4));
                string info = t.ProbeWater(q.x, q.z, st);
                Diag("[Voda bod] " + q.x.ToString("0.0", CultureInfo.InvariantCulture) + " "
                          + q.z.ToString("0.0", CultureInfo.InvariantCulture) + ": " + info + " | " + t.ChunksAt(q.x, q.z) + " | " + t.WaterLayersAt(q));
                foreach (string part in info.Split('|')) Note(part.Trim());
                Note(t.ChunksAt(q.x, q.z));
                Note(t.WaterLayersAt(q));
                return;
            }

            int kind = sub == "reka" || sub == "river" ? 0
                     : sub == "jezero" || sub == "lake" ? 1
                     : sub == "more" || sub == "sea" || sub == "breh" ? 2
                     : sub == "hory" || sub == "mountain" ? 3
                     : sub == "vodopad" || sub == "waterfall" ? 4 : -1;
            if (kind < 0) { Error("Usage: /voda audit [r] | /voda reka|hory|vodopad|jezero|more [min distance]"); return; }

            float min = 0f;
            if (a.Length > 2) TryNumber(a[2], out min);

            if (!t.FindWater(p.position, kind, min, 3000f, out Vector3 at))
            {
                Error("Nothing found within 3 km (only built hydrology regions are searched).");
                return;
            }

            Ok(string.Format(CultureInfo.InvariantCulture, "{0}: x {1:0} y {2:0.0} z {3:0}, teleporting...",
                sub, at.x, at.y, at.z));
            t.Teleport(new Vector3(at.x, 0f, at.z));
        }

        private void CmdStop()
        {
            if (search == null) { Note("No search is running."); return; }

            StopCoroutine(search);
            search = null;
            Ok("Search cancelled.");
        }

        /// <summary>
        /// Proč nejde kopat.
        ///
        /// <para>Těžba je řetěz nezávislých podmínek – nástroj v ruce, nezablokovaný vstup,
        /// běžící streamer a paprsek, který má do čeho trefit. <b>Když selže kterákoli,
        /// neděje se prostě nic a z chování se nepozná která.</b> Výpis proto hlásí každou
        /// zvlášť.</para>
        ///
        /// <para>Navíc střílí paprsek dolů: vzdálenost k zemi odliší „mířím do vzduchu"
        /// od „terén nemá collider", a konstantní hodnota napříč polohami prozradí pevný
        /// offset kamery – přesně tak se našlo, že hráč má oči 7,7 m nad zemí.</para>
        ///
        /// <para><b>Past:</b> tenhle příkaz běží uvnitř <see cref="Submit"/>, tedy zatímco
        /// je konzole otevřená. Vlastní blokaci si proto nesmí započítat, jinak hlásí
        /// poruchu, kterou sám způsobil. Přesně to se jednou stalo a odvedlo pozornost.</para>
        /// </summary>
        private void CmdDig()
        {
            VoxelDigTool tool = UnityEngine.Object.FindFirstObjectByType<VoxelDigTool>();
            if (tool == null) { Error("VoxelDigTool is not in the scene - digging cannot work at all."); return; }

            Note("tool in hand: " + (PlayerEquipment.HoldingPickaxe ? "pickaxe" : "NO (" +
                 (PlayerEquipment.HeldTool != null ? PlayerEquipment.HeldTool.name : "empty hand") + ")") +
                 (tool.requirePickaxe ? "" : "  - the pickaxe requirement is off"));

            // Konzole je právě otevřená, takže se GameConsole.IsOpen schválně nepočítá.
            GameManager gm = GameManager.instance;
            bool blocked = SceneLoader.InputBlocked || (gm != null && (gm.IsPaused || gm.isMenuOpen));
            Note("input: " + (blocked ? "BLOCKED (loading, pause or menu)" : "free"));

            VoxelTerrain t = VoxelTerrain.instance;
            if (t == null) { Error("terrain streamer is not running - nothing to dig into."); return; }

            t.ColliderStats(out int total, out int lod0, out int withCollider);
            Note(string.Format(CultureInfo.InvariantCulture,
                "chunks: total {0}, in LOD0 {1}, with collider {2}", total, lod0, withCollider));

            Transform aim = tool.aim != null ? tool.aim
                          : (Camera.main != null ? Camera.main.transform : null);
            if (aim == null) { Error("nothing to aim from - no camera."); return; }

            if (Physics.Raycast(aim.position, Vector3.down, out RaycastHit down, 200f,
                                tool.terrainMask, QueryTriggerInteraction.Ignore))
            {
                Note(string.Format(CultureInfo.InvariantCulture,
                    "ground below is {0:0.0} m away, reach {1:0.0} m - {2}",
                    down.distance, tool.reach,
                    down.distance <= tool.reach ? "reaches" : "DOES NOT REACH, raise reach"));
            }
            else
            {
                Error("the downward ray hit nothing within 200 m - terrain has no collider, or is elsewhere.");
            }

            if (Physics.Raycast(aim.position, aim.forward, out RaycastHit fwd, tool.reach,
                                tool.terrainMask, QueryTriggerInteraction.Ignore))
                Note(string.Format(CultureInfo.InvariantCulture,
                    "aiming at: {0} at {1:0.0} m", fwd.collider.name, fwd.distance));
            else
                Note("aiming at: nothing in reach");
        }

        // Uvozovky jako konstanty: obyčejná uvozovka uvnitř řetězce se snadno zapomene
        // escapovat a rozsype celý zbytek souboru na dvacet nesouvisejících chyb.
        private const string LeftQuote = "'";    // kolo 25: hráčská konzole je anglicky
        private const string RightQuote = "'";

        // ── teleport ───────────────────────────────────────────────────

        private void CmdTeleport(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;

            if (t.IsTeleporting) { Error("A teleport is already running."); return; }

            Vector3 v = p.position;

            // Souřadnice mají přednost, ale rozhodují až po pokusu o převod – ne podle počtu
            // slov. Biomy se totiž jmenují i dvěma slovy („zasněžené štíty") a dřív takový
            // název spadl do větve pro tři souřadnice a skončil hláškou o nesmyslných číslech.
            if (a.Length == 3 && TryCoord(a[1], v.x, out float cx) && TryCoord(a[2], v.z, out float cz))
            {
                Ok(string.Format(CultureInfo.InvariantCulture, "Teleporting to {0:0} / {1:0}, height from terrain...", cx, cz));
                t.Teleport(new Vector3(cx, 0f, cz));
                return;
            }

            if (a.Length == 4 && TryCoord(a[1], v.x, out float tx) && TryCoord(a[2], v.y, out float ty)
                              && TryCoord(a[3], v.z, out float tz))
            {
                Ok(string.Format(CultureInfo.InvariantCulture, "Teleporting to {0:0} / {1:0} / {2:0}...", tx, ty, tz));
                t.Teleport(new Vector3(tx, ty, tz), false);
                return;
            }

            if (a.Length >= 2)
            {
                string name = JoinArgs(a, 1);
                // Kolo 25: biomy jdou jednotně přes /biome tp (bezpečné místo, let beze změny).
                if (ParseRegion(name) >= 0) { CmdRegions(new[] { "biome", "tp", name.Replace(' ', '_') }); return; }
                if (VoxelBiomes.TryParse(name, out VoxelBiome want))
                {
                    StartSearch(want, v, true);
                    return;
                }

                if (TryParseMicro(name, out MicroBiome wantMicro))
                {
                    StartMicroSearch(wantMicro, v, true);
                    return;
                }

                if (a.Length == 3 || a.Length == 4)
                {
                    Error("Coordinates do not make sense and " + LeftQuote + name + RightQuote +
                          " is not a biome. Use /tp <x> <z>, /tp <x> <y> <z> or /biome tp <biome-id>.");
                    return;
                }

                Error("Unknown biome " + LeftQuote + name + RightQuote + ". Type /biome list to see all IDs, then /biome tp <biome-id>.");
                return;
            }

            Error("Usage: /tp <x> <z>, /tp <x> <y> <z> or /biome tp <biome-id>");
        }

        /// <summary>Spojí argumenty od indexu <paramref name="from"/> mezerou – víceslovné biomy.</summary>
        private static string JoinArgs(string[] a, int from)
            => from >= a.Length ? "" : string.Join(" ", a, from, a.Length - from);

        private void CmdFind(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;

            string what = JoinArgs(a, 1);

            if (a.Length >= 2 && TryParseMicro(what, out MicroBiome wantMicro))
            {
                StartMicroSearch(wantMicro, p.position, false);
                return;
            }

            if (a.Length >= 2 && ParseRegion(what) >= 0) { CmdRegions(new[] { "biome", "find", what.Replace(' ', '_') }); return; }
            if (a.Length < 2 || !VoxelBiomes.TryParse(what, out VoxelBiome want))
            {
                Error("Usage: /biome find <biome-id> (see /biome list), or /find grove | basalt.");
                return;
            }
            StartSearch(want, p.position, false);
        }

        /// <summary>Relativní souřadnice ve stylu Minecraftu: <c>~</c> nebo <c>~50</c>.</summary>
        private static bool TryCoord(string s, float current, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(s)) return false;

            if (s[0] != '~') return TryNumber(s, out value);

            string rest = s.Substring(1);
            if (rest.Length == 0) { value = current; return true; }

            if (!TryNumber(rest, out float d)) return false;
            value = current + d;
            return true;
        }

        /// <summary>
        /// Čísla se čtou invariantně a čárka se překlápí na tečku. Na české klávesnici
        /// se na numerické části píše čárka a bez tohohle by „12,5" tiše selhalo.
        /// </summary>
        private static bool TryNumber(string s, out float v)
            => float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        // ── hledání biomu ──────────────────────────────────────────────

        private void StartMicroSearch(MicroBiome want, Vector3 from, bool teleport)
        {
            if (search != null) StopCoroutine(search);
            search = StartCoroutine(MicroSearchRoutine(want, from, teleport));
        }

        /// <summary>
        /// Spirálové hledání mikro-biomu.
        ///
        /// <para>Krok je jemnější než u biomů: skvrna má napříč jen několik set metrů,
        /// kdežto klimatický biom kilometry. S hrubou roztečí by se dala minout a hráč
        /// by pak hledal něco, co je dvě stě metrů vedle.</para>
        ///
        /// <para>Míří se na STŘED skvrny, ne na první bod uvnitř. Na okraji je efekt
        /// potlačený, takže dopad na hranici vypadá, jako by se nic nestalo.</para>
        /// </summary>
        private IEnumerator MicroSearchRoutine(MicroBiome want, Vector3 from, bool teleport)
        {
            VoxelTerrain t = VoxelTerrain.instance;

            const float step = 64f;
            const float maxRadius = 24000f;
            const float tau = 6.28318531f;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int samples = 0;
            float bestW = 0f;
            Vector3 best = Vector3.zero;
            bool found = false;

            Note("Searching for " + MicroName(want) + "...");

            for (float r = 0f; r <= maxRadius; r += step)
            {
                float da = r < step ? tau : step / r;

                for (float ang = 0f; ang < tau; ang += da)
                {
                    float x = from.x + r * Mathf.Cos(ang);
                    float z = from.z + r * Mathf.Sin(ang);
                    samples++;

                    if (t.MicroAt(x, z, out float w) == want && w > bestW)
                    {
                        bestW = w;
                        best = new Vector3(x, 0f, z);
                        found = true;
                    }

                    if (sw.Elapsed.TotalMilliseconds > 4.0)
                    {
                        sw.Restart();
                        yield return null;
                        if (VoxelTerrain.instance == null) { search = null; yield break; }
                    }
                }

                // Jakmile je skvrna skoro celá, další prstence lepší střed nenajdou.
                if (found && bestW > 0.92f) break;
            }

            if (!found)
            {
                Error(MicroName(want) + " not found within " + (int)maxRadius + " m (" + samples + " samples).");
                search = null;
                yield break;
            }

            float y = t.SurfaceHeight(best.x, best.z);
            best.y = y;
            float dist = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(best.x, 0f, best.z));

            Ok(string.Format(CultureInfo.InvariantCulture,
                "{0}: x {1:0} y {2:0} z {3:0} - {4:0} m away, strength {5:0}% ({6} samples)",
                MicroName(want), best.x, y, best.z, dist, bestW * 100f, samples));

            if (teleport) t.Teleport(best);
            search = null;
        }

        private void StartSearch(VoxelBiome want, Vector3 from, bool teleport)
        {
            if (search != null) StopCoroutine(search);
            search = StartCoroutine(SearchRoutine(want, from, teleport));
        }

        /// <summary>
        /// Prohledá okolí po spirále a najde nejbližší bod daného biomu.
        ///
        /// <para>Jde to jen proto, že <see cref="VoxelTerrain.BiomeAt"/> je analytický –
        /// nepotřebuje chunk ani collider, takže se dá vzorkovat statisíckrát. Přesto se
        /// hledání rozkládá do snímků s rozpočtem 4 ms: při dohledu 24 km je vzorků kolem
        /// sta tisíc a naráz by to byl půlvteřinový zásek.</para>
        ///
        /// <para>Úhlový krok klesá s poloměrem, aby hustota vzorků zůstala konstantní.
        /// Rozteč 96 m je kompromis: menší biom než sto metrů se dá minout, ale žádný
        /// z klimatických biomů takhle malý není – jejich perioda je jednotky kilometrů.</para>
        /// </summary>
        private IEnumerator SearchRoutine(VoxelBiome want, Vector3 from, bool teleport)
        {
            VoxelTerrain t = VoxelTerrain.instance;

            const float step = 96f;
            const float maxRadius = 24000f;
            const float tau = 6.28318531f;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            float nextReport = 2000f;
            int samples = 0;

            Note("Searching for biome " + VoxelBiomes.Name(want) + "...");

            for (float r = 0f; r <= maxRadius; r += step)
            {
                float da = r < step ? tau : step / r;

                for (float ang = 0f; ang < tau; ang += da)
                {
                    float x = from.x + r * Mathf.Cos(ang);
                    float z = from.z + r * Mathf.Sin(ang);
                    samples++;

                    if (t.BiomeAt(x, z, out float y) == want && Standable(t, want, x, z))
                    {
                        var hit = new Vector3(x, y, z);
                        float dist = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(x, 0f, z));

                        Ok(string.Format(CultureInfo.InvariantCulture,
                            "{0}: x {1:0} y {2:0} z {3:0} - {4:0} m away ({5} samples)",
                            VoxelBiomes.Name(want), x, y, z, dist, samples));

                        if (teleport) t.Teleport(hit);
                        search = null;
                        yield break;
                    }

                    if (sw.Elapsed.TotalMilliseconds > 4.0)
                    {
                        sw.Restart();
                        yield return null;
                    }
                }

                if (r >= nextReport)
                {
                    nextReport += 4000f;
                    Note(string.Format(CultureInfo.InvariantCulture, "...{0:0} km, nothing yet", r / 1000f));
                }
            }

            Error("Biome " + VoxelBiomes.Name(want) + " not found within " + (maxRadius / 1000f) + " km.");
            search = null;
        }

        /// <summary>
        /// Dá se na tom místě stát? Bod uprostřed útesu je sice ve správném biomu, ale
        /// hráč po něm sjede – a u zasněžených štítů je to skoro pravidlo, protože jsou
        /// z definice na strmém terénu.
        /// </summary>
        private static bool Standable(VoxelTerrain t, VoxelBiome want, float x, float z)
        {
            if (want == VoxelBiome.Ocean) return true;

            float e = t.SurfaceHeight(x + 4f, z), w = t.SurfaceHeight(x - 4f, z);
            float n = t.SurfaceHeight(x, z + 4f), s = t.SurfaceHeight(x, z - 4f);

            float lo = Mathf.Min(Mathf.Min(e, w), Mathf.Min(n, s));
            float hi = Mathf.Max(Mathf.Max(e, w), Mathf.Max(n, s));

            return hi - lo <= 6f && lo > t.SeaLevel + 1f;
        }
    }
}
