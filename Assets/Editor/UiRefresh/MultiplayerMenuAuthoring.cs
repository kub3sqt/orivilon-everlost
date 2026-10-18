using System;
using Orivilon.UI.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;
using Object = UnityEngine.Object;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Kolo MP 17.10.2026 (druhé kolo): multiplayerové prvky hlavního menu přes Editor API (ne ruční YAML).
    /// - tlačítko Multiplayer = kopie Play (stejná klikací plocha 75×75, stejné onClick až na poslední volání,
    ///   hover na ikonu); ikona dvou lidí je v potomkovi zvětšená tak, aby opticky odpovídala Play/Exit,
    /// - Play / Multiplayer / Exit se stejnými mezerami jako dřív Play–Exit; boční rám (UiFrame „side“) se
    ///   ve všech stavech roztáhne symetricky kolem středu (spoje FrameJoin se dopočítají samy),
    /// - režim Multiplayer = existující Play menu (FriendsListUI + MainMenuUI.FriendsMode), jen stavový text
    ///   v oblasti seznamu; dialog pro heslo ve stylu ConfirmDisplay z nastavení.
    /// Idempotentní (staré kopie nahradí). Spouští se z menu nebo mostem (mpmenu).
    /// </summary>
    public static class MultiplayerMenuAuthoring
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        /// <summary>Svislý rozestup středů ikon = původní rozestup Play–Exit (mezera 51 při ikoně 75).</summary>
        public const float Spacing = 126f;
        public const float IconSize = 75f;
        /// <summary>Zvětšení ikony dvou lidí: její neprůhledná plocha je 69×54 z 75×75 (Play 65×75, Exit 75×75).</summary>
        public const float MpIconScale = 1.15f;
        private const float SideCenter = 300f, SideOldMin = 172f, SideOldMax = 428f;

        [MenuItem("Tools/Everlost/Multiplayer/Author main menu")]
        private static void MenuRun() => Debug.Log(Run(Spacing));

        public static string Run(float spacing)
        {
            if (EditorApplication.isPlaying) return "ERR mpmenu: běží Play";
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty) return "ERR mpmenu: neuložená scéna " + scene.name;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui == null) return "ERR mpmenu: MainMenuUI chybí";
            Transform canvas = ui.GetComponentInParent<Canvas>(true).rootCanvas.transform;

            var play = canvas.Find("MainMenu/Play") as RectTransform;
            var exit = canvas.Find("MainMenu/Exit") as RectTransform;
            var worldMp = FindDeep(canvas, "SideGuide")?.Find("Multiplayer")?.GetComponent<Image>();
            var confirm = FindDeep(canvas, "ConfirmDisplay");
            var nameInput = FindDeep(canvas, "NameInput");
            var worldsPanel = FindDeep(ui.transform, "WorldsPanel");
            var viewport = worldsPanel != null ? worldsPanel.Find("Viewport") as RectTransform : null;
            var frameGo = FindDeep(canvas, "MainMenuFrame");
            var frame = frameGo != null ? frameGo.GetComponent<UiFrame>() : null;
            if (play == null || exit == null || worldMp == null || confirm == null || nameInput == null || viewport == null || frame == null)
                return "ERR mpmenu: chybí zdroj (Play/Exit/SideGuide.Multiplayer/ConfirmDisplay/NameInput/WorldsPanel.Viewport/MainMenuFrame)";

            foreach (var n in new[] { "MultiplayerPanel", "MultiplayerDialog", "MultiplayerFriends" })
            { var old = canvas.Find(n); if (old != null) Object.DestroyImmediate(old.gameObject); }
            var oldBtn = canvas.Find("MainMenu/Multiplayer");
            if (oldBtn != null) Object.DestroyImmediate(oldBtn.gameObject);
            var oldStatus = worldsPanel.Find("FriendsStatus");
            if (oldStatus != null) Object.DestroyImmediate(oldStatus.gameObject);

            var dialog = BuildDialog(canvas, confirm, nameInput);

            // ── stav v oblasti seznamu (jen v režimu Multiplayer) ─────────────
            var status = NewText("FriendsStatus", worldsPanel, "", false, 22f, Muted, TextAlignmentOptions.Center, false);
            status.textWrappingMode = TextWrappingModes.Normal; status.richText = false;
            var srt = status.rectTransform;
            srt.anchorMin = viewport.anchorMin; srt.anchorMax = viewport.anchorMax; srt.pivot = viewport.pivot;
            srt.offsetMin = viewport.offsetMin + new Vector2(24f, 0f); srt.offsetMax = viewport.offsetMax - new Vector2(24f, 0f);
            status.gameObject.SetActive(false);

            var host = NewRect("MultiplayerFriends", canvas);
            Stretch(host);
            var friends = host.gameObject.AddComponent<FriendsListUI>();
            var fso = new SerializedObject(friends);
            fso.FindProperty("menu").objectReferenceValue = ui;
            fso.FindProperty("statusText").objectReferenceValue = status;
            fso.FindProperty("dialog").objectReferenceValue = dialog;
            fso.ApplyModifiedPropertiesWithoutUndo();
            dialog.transform.SetAsLastSibling();

            var so = new SerializedObject(ui);
            so.FindProperty("friendsList").objectReferenceValue = friends;
            so.ApplyModifiedPropertiesWithoutUndo();

            // ── tlačítko Multiplayer = kopie Play ──────────────────────────
            var btn = Object.Instantiate(play.gameObject, play.parent).GetComponent<RectTransform>();
            btn.name = "Multiplayer";
            btn.SetSiblingIndex(play.GetSiblingIndex() + 1);
            var b = btn.GetComponent<Button>();
            // Play: PlayMenu.SetActive, NewWorlButton.SetActive, MainMenu.SetActive, MainMenuUI.OpenSelectWorldMenu
            // → poslední volání vyměnit za OpenMultiplayerMenu (otevření i přechod rámu zůstávají stejné).
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                if (b.onClick.GetPersistentMethodName(i) == nameof(MainMenuUI.OpenSelectWorldMenu)) UnityEventTools.RemovePersistentListener(b.onClick, i);
            UnityEventTools.AddVoidPersistentListener(b.onClick, new UnityAction(ui.OpenMultiplayerMenu));
            var hitbox = btn.GetComponent<Orivilon.Interaction.ButtonHitbox>();
            if (hitbox != null) Object.DestroyImmediate(hitbox);   // klikací plocha = celý čtverec 75×75 jako u Play/Exit (alpha test u nich neprojde)
            var rootImg = btn.GetComponent<Image>();
            rootImg.sprite = null; rootImg.color = new Color(1f, 1f, 1f, 0f); rootImg.raycastTarget = true;
            var icon = NewImage("Icon", btn, worldMp.sprite, worldMp.color, false, false);
            icon.preserveAspect = true;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f); irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = Vector2.one * (IconSize * MpIconScale);
            var hover = btn.GetComponent<HoverTint>();
            if (hover != null) { hover.targets = new Graphic[] { icon }; EditorUtility.SetDirty(hover); }

            PlaceIcon(play, SideCenter + spacing);
            PlaceIcon(btn, SideCenter);
            PlaceIcon(exit, SideCenter - spacing);

            // ── boční rám: symetricky kolem středu, aby ikony měly stejný okraj jako dřív ──
            float half = spacing + IconSize * 0.5f + (SideOldMax - SideOldMin - 126f - IconSize) * 0.5f;
            // Idempotence: měřítko se odvozuje z AKTUÁLNÍ výšky panelu ve stavu Menu (původně
            // SideOldMax − SideOldMin), takže opakovaný běh nic nenásobí.
            float hMenu = (SideOldMax - SideOldMin) * 0.5f;
            var menuSt = frame.Find("Menu");
            if (menuSt != null)
                foreach (var ps in menuSt.pieces)
                    if (ps.piece != null && ps.piece.name == "Panel_side" && ps.anchorMin.y == ps.anchorMax.y)
                        hMenu = (ps.offsetMax.y - ps.offsetMin.y) * 0.5f;
            float k = half / hMenu;
            int changed = 0;
            foreach (var st in frame.states)
                foreach (var ps in st.pieces)
                    if (ps.piece != null && ps.piece.name == "Panel_side")
                    {
                        // offsety jsou v souřadnicích rodiče; střed mezi nimi = střed panelu → škálovat kolem něj
                        float c = (ps.offsetMin.y + ps.offsetMax.y) * 0.5f, h = (ps.offsetMax.y - ps.offsetMin.y) * 0.5f;
                        if (ps.anchorMin.y != ps.anchorMax.y) continue;
                        float target = h * k;
                        if (Mathf.Abs(h - target) < 0.5f) continue;
                        ps.offsetMin.y = c - target; ps.offsetMax.y = c + target; changed++;
                    }
            EditorUtility.SetDirty(frame);
            var menuState = frame.Find("Menu");
            if (menuState != null) frame.ApplyInstant(menuState);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            return "mpmenu OK (rozestup " + spacing + ", ikona " + IconSize + ", MP ×" + MpIconScale + ", rám ×" + k.ToString("0.000") + " ve " + changed + " stavech) saved=" + saved;
        }

        private static void PlaceIcon(RectTransform rt, float cy)
        {
            rt.localScale = Vector3.one;
            Place(rt, L(62.5f - IconSize * 0.5f), L(cy - IconSize * 0.5f), L(62.5f + IconSize * 0.5f), L(cy + IconSize * 0.5f));
        }

        private static RectTransform Dup(Transform src, Transform parent, string name)
        {
            var go = Object.Instantiate(src.gameObject, parent);
            go.name = name;
            go.SetActive(true);
            return (RectTransform)go.transform;
        }

        private static void Label(Transform button, string text)
        {
            var t = button.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = text;
        }

        private static void ClearClicks(Button b)
        {
            if (b == null) return;
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
        }

        private static MultiplayerDialog BuildDialog(Transform canvas, Transform confirm, Transform nameInput)
        {
            var host = NewRect("MultiplayerDialog", canvas);
            Stretch(host);
            var root = Dup(confirm, host, "Root");
            Stretch(root);
            var box = root.Find("Box") as RectTransform;
            Place(box, M(-420f), M(-200f), M(420f), M(200f));

            var oldText = box.Find("Text");
            var keep = box.Find("Keep") as RectTransform;
            var revert = box.Find("Revert") as RectTransform;
            if (oldText != null) Object.DestroyImmediate(oldText.gameObject);
            ClearClicks(keep.GetComponent<Button>()); ClearClicks(revert.GetComponent<Button>());

            var title = NewText("Title", box, "Join world", true, 32f, Ink, TextAlignmentOptions.MidlineLeft);
            Place(title.rectTransform, M(-370f), M(120f), M(370f), M(170f));
            var msg = NewText("Message", box, "", false, 20f, Muted, TextAlignmentOptions.TopLeft, false);
            msg.textWrappingMode = TextWrappingModes.Normal;
            Place(msg.rectTransform, M(-370f), M(50f), M(370f), M(112f));

            var input = Dup(nameInput, box, "Password");
            Place(input, M(-370f), M(-36f), M(370f), M(40f));
            var field = input.GetComponent<TMP_InputField>();
            field.contentType = TMP_InputField.ContentType.Password;
            field.characterLimit = 64;
            field.text = "";
            if (field.placeholder is TMP_Text ph) ph.text = "Password";

            var err = NewText("Error", box, "", false, 18f, new Color32(0xff, 0x8a, 0x80, 0xff), TextAlignmentOptions.MidlineLeft, false);
            err.textWrappingMode = TextWrappingModes.Normal;
            Place(err.rectTransform, M(-370f), M(-92f), M(370f), M(-46f));

            Place(keep, M(110f), M(-175f), M(370f), M(-115f));
            Place(revert, M(-370f), M(-175f), M(-110f), M(-115f));
            Label(keep, "JOIN"); Label(revert, "Cancel");

            var d = host.gameObject.AddComponent<MultiplayerDialog>();
            var so = new SerializedObject(d);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("message").objectReferenceValue = msg;
            so.FindProperty("error").objectReferenceValue = err;
            so.FindProperty("input").objectReferenceValue = field;
            so.FindProperty("confirm").objectReferenceValue = keep.GetComponent<Button>();
            so.FindProperty("confirmLabel").objectReferenceValue = keep.GetComponentInChildren<TMP_Text>(true);
            so.FindProperty("cancel").objectReferenceValue = revert.GetComponent<Button>();
            so.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.SetActive(false);
            host.SetAsLastSibling();
            return d;
        }
    }
}
