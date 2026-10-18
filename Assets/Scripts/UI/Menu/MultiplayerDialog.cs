using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Malý modální dialog hlavního menu (kolo MP 17.10.2026): heslo při hostování a při připojení.
    /// Hierarchie je autorovaná v MainMenu scéně (Tools/Everlost/Multiplayer/Author main menu), tady jen chování.
    /// Heslo se z pole předá volajícímu a pole se hned vymaže – nikam se neukládá ani neloguje.
    /// </summary>
    public class MultiplayerDialog : MonoBehaviour
    {
        public static MultiplayerDialog Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text message;
        [SerializeField] private TMP_Text error;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Button confirm;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Button cancel;

        private Action<string> onConfirm;
        private Action onCancel;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            Instance = this;
            if (confirm != null) confirm.onClick.AddListener(Confirm);
            if (cancel != null) cancel.onClick.AddListener(Cancel);
            if (input != null)
            {
                input.contentType = TMP_InputField.ContentType.Password;
                input.characterLimit = 64;
                input.onSubmit.AddListener(_ => Confirm());
            }
            if (root != null) root.SetActive(false);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Otevře dialog. withInput = pole pro heslo; errorText = červený řádek (null = žádný).</summary>
        public void Show(string titleText, string messageText, string confirmText, string errorText, bool withInput,
                         Action<string> confirmed, Action cancelled = null)
        {
            onConfirm = confirmed;
            onCancel = cancelled;
            if (title != null)
            {
                // Dlouhé jméno hosta (Steam až 32 znaků) nesmí přetéct rám dialogu: jeden řádek, výpustka.
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.overflowMode = TextOverflowModes.Ellipsis;
                title.text = titleText ?? "";
            }
            if (message != null) message.text = messageText ?? "";
            if (error != null) { error.text = errorText ?? ""; error.gameObject.SetActive(!string.IsNullOrEmpty(errorText)); }
            if (confirmLabel != null) confirmLabel.text = confirmText ?? "OK";
            if (input != null)
            {
                input.text = "";
                input.gameObject.SetActive(withInput);
            }
            if (root != null) root.SetActive(true);
            transform.SetAsLastSibling();
            if (withInput && input != null) { input.Select(); input.ActivateInputField(); }
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();
            else if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && (input == null || !input.isFocused)) Confirm();
        }

        public void Confirm()
        {
            if (!IsOpen) return;
            string pw = input != null && input.gameObject.activeSelf ? input.text : "";
            var cb = onConfirm;
            Close();
            cb?.Invoke(pw);
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            var cb = onCancel;
            Close();
            cb?.Invoke();
        }

        private void Close()
        {
            onConfirm = null; onCancel = null;
            if (input != null) input.text = "";
            if (root != null) root.SetActive(false);
        }
    }
}
