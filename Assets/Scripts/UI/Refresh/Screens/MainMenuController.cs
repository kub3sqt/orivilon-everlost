using System.Reflection;
using Orivilon.SaveSystem;
using Orivilon.UI.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Main menu binding. The three screens (Menu, Select World, Create New World) are authored in
    /// the MainMenu scene; MainMenuUI still decides which one is open. This component follows it:
    /// switches the frame state (220 ms morph), fades the main-menu buttons, fills the
    /// "Selected world" details and scrolls the world list to the top when it is rebuilt.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        public const string MenuState = "Menu", WorldsState = "Worlds", CreateState = "Create";

        public MainMenuUI ui;
        public UiFrame frame;
        public CanvasGroup menuGroup;
        public float menuFade = 0.2f;

        [Header("World list")]
        public RectTransform listContent;
        public ScrollRect scroll;

        [Header("Selected world")]
        public TMP_Text detailName;
        public TMP_Text[] detailKeys = new TMP_Text[3];
        public TMP_Text[] detailValues = new TMP_Text[3];
        public Button playButton;
        public TMP_Text playLabel;
        public string emptyName = "Select a world";

        private static readonly FieldInfo SelectedField =
            typeof(MainMenuUI).GetField("selectedWorldData", BindingFlags.Instance | BindingFlags.NonPublic);

        private string state;
        private float menuAlpha = 1f;
        private int rowCount = -1;
        private object lastSelected = new object();   // first frame always fills the details

        // Režim Multiplayer (kolo MP 17.10.2026): detail ukazuje vybranou lobby kamaráda, PLAY = JOIN.
        private int lastFriendVersion = -1;
        private bool friendDetailShown;
        private string[] savedKeys;
        private string savedPlayLabel;
        private string savedSectionLabel;

        /// <summary>Malý nadpis „SELECTED WORLD“ nad jménem (sourozenec Label v Detail).</summary>
        private TMP_Text SectionLabel()
        {
            var t = detailName != null && detailName.transform.parent != null ? detailName.transform.parent.Find("Label") : null;
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        private void Start()
        {
            Update();
            if (!string.IsNullOrEmpty(Orivilon.Core.SceneLoader.PendingMenuError)) OpenWorldsForError();
        }

        /// <summary>A multiplayer start failed in the loading screen: reopen Select World through the saved Play
        /// button wiring (MainMenuUI then reselects the world and shows the error).</summary>
        private void OpenWorldsForError()
        {
            if (ui == null || ui.gameObject.activeInHierarchy) return;
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                    if (b.onClick.GetPersistentTarget(i) == ui && b.onClick.GetPersistentMethodName(i) == nameof(MainMenuUI.OpenSelectWorldMenu))
                    { b.onClick.Invoke(); return; }
        }

        private string Current()
        {
            if (ui == null || !ui.gameObject.activeInHierarchy) return MenuState;
            return ui.createMenu != null && ui.createMenu.activeSelf ? CreateState : WorldsState;
        }

        private void Update()
        {
            string s = Current();
            if (s != state)
            {
                bool animate = state != null;
                state = s;
                if (frame != null) frame.SetState(s, animate ? frame.morphTime : 0f);
            }

            if (listContent != null && listContent.childCount != rowCount)
            {
                rowCount = listContent.childCount;
                if (scroll != null) { scroll.StopMovement(); Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f; }
            }

            if (ui != null && ui.FriendsMode)
            {
                if (FriendsListUI.Version != lastFriendVersion || !friendDetailShown)
                {
                    lastFriendVersion = FriendsListUI.Version;
                    UpdateFriendDetail(FriendsListUI.Current != null ? FriendsListUI.Current.Selected : null);
                }
            }
            else
            {
                if (friendDetailShown) { RestoreWorldDetailLabels(); lastSelected = new object(); }
                object sel = SelectedField != null && ui != null ? SelectedField.GetValue(ui) : null;
                if (!ReferenceEquals(sel, lastSelected)) { lastSelected = sel; UpdateDetail(sel as WorldData); }
            }

            float target = state == MenuState ? 1f : 0f;
            if (menuGroup != null && !Mathf.Approximately(menuAlpha, target))
            {
                menuAlpha = Mathf.MoveTowards(menuAlpha, target, Time.unscaledDeltaTime / menuFade);
                menuGroup.alpha = UiKit.EaseOut(menuAlpha);
                menuGroup.blocksRaycasts = menuGroup.interactable = target > 0.5f;
            }
        }

        private void UpdateFriendDetail(Orivilon.Multiplayer.FriendLobbyInfo? sel)
        {
            if (!friendDetailShown)
            {
                savedKeys = new string[detailKeys != null ? detailKeys.Length : 0];
                for (int i = 0; i < savedKeys.Length; i++) savedKeys[i] = detailKeys[i] != null ? detailKeys[i].text : null;
                savedPlayLabel = playLabel != null ? playLabel.text : null;
                var sl = SectionLabel();
                savedSectionLabel = sl != null ? sl.text : null;
                if (sl != null) sl.text = "SELECTED HOST";
                friendDetailShown = true;
            }
            string[] keys = { "Host", "Players", "Access" };
            string[] v = { "", "", "" };
            bool has = sel.HasValue;
            if (has)
            {
                var f = sel.Value;
                v[0] = string.IsNullOrEmpty(f.hostName) ? f.friendName : f.hostName;
                v[1] = f.maxMembers > 0 ? f.memberCount + " / " + f.maxMembers : "-";
                v[2] = !f.joinable ? f.blockedReason : f.locked ? "Password" : "Open";
            }
            if (detailName != null)
            {
                detailName.richText = false;
                detailName.text = has ? (string.IsNullOrEmpty(sel.Value.worldName) ? v[0] : sel.Value.worldName) : "Select a friend";
            }
            for (int i = 0; i < 3; i++)
            {
                if (detailKeys != null && i < detailKeys.Length && detailKeys[i] != null) { detailKeys[i].text = keys[i]; detailKeys[i].enabled = has; }
                if (detailValues != null && i < detailValues.Length && detailValues[i] != null) { detailValues[i].richText = false; detailValues[i].text = v[i]; }
            }
            bool can = has && sel.Value.joinable;
            if (playButton != null) playButton.interactable = can;
            if (playLabel != null) { playLabel.text = "JOIN"; playLabel.color = can ? Color.black : new Color(0f, 0f, 0f, 0.35f); }
        }

        private void RestoreWorldDetailLabels()
        {
            friendDetailShown = false;
            if (savedKeys != null)
                for (int i = 0; i < savedKeys.Length && i < detailKeys.Length; i++) if (detailKeys[i] != null && savedKeys[i] != null) detailKeys[i].text = savedKeys[i];
            if (playLabel != null && savedPlayLabel != null) playLabel.text = savedPlayLabel;
            var sl = SectionLabel();
            if (sl != null && savedSectionLabel != null) sl.text = savedSectionLabel;
        }

        private void UpdateDetail(WorldData w)
        {
            if (detailName != null) detailName.text = w != null ? w.worldName : emptyName;
            string[] v = { w != null ? w.seed : "", w != null ? UiKit.When(w.lastPlayed) : "", w != null ? UiKit.PlayTime(w.totalPlayTime) : "" };
            for (int i = 0; i < 3; i++)
            {
                if (detailValues != null && i < detailValues.Length && detailValues[i] != null) detailValues[i].text = v[i];
                if (detailKeys != null && i < detailKeys.Length && detailKeys[i] != null) detailKeys[i].enabled = w != null;
            }
            if (playButton != null) playButton.interactable = w != null;
            if (playLabel != null) playLabel.color = w != null ? Color.black : new Color(0f, 0f, 0f, 0.35f);
        }
    }
}
