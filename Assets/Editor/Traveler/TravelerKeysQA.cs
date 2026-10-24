using System.IO;
using System.Text;
using Orivilon.Core;
using Orivilon.Player;
using Orivilon.UI.Settings;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Orivilon.EditorTools
{
    /// <summary>QA: podmínky F5 (textové pole, pauza, konzole), cyklus pohledů, uložené klávesy z settings.json.</summary>
    internal static class TravelerKeysQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Keys and F5 guard test")]
        public static void Run()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) fpc.gameObject.AddComponent<KeysQARunner>();
        }
    }

    internal sealed class KeysQARunner : MonoBehaviour
    {
        private int step; private float t; private readonly StringBuilder sb = new StringBuilder();
        private GameObject canvas; private TMP_InputField field; private TravelerViewMode startMode;

        private void Update()
        {
            t += Time.unscaledDeltaTime;
            if (t < 0.4f) return;
            t = 0f;
            var gm = GameManager.instance;
            switch (step++)
            {
                case 0:
                    var s = SettingsManager.Instance != null ? SettingsManager.Instance.Current : null;
                    sb.AppendLine("settings.json: " + (SettingsManager.Instance != null ? SettingsManager.Instance.LoadInfo : "bez SettingsManageru")
                        + (s != null ? $" | quickSave {s.quickSaveKey}, sprint {s.sprintKey}, jump {s.jumpKey}, crouch {s.crouchKey}, interact {s.interactKey}, inventory {s.inventoryKey}, zoom {s.zoomKey}, flight {s.flightKey}" : ""));
                    sb.AppendLine("F5 povoleno ve hře: " + TravelerCameraView.InputAllowed);
                    startMode = TravelerCameraView.Mode;
                    canvas = new GameObject("QA_Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                    canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                    var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
                    go.transform.SetParent(canvas.transform, false);
                    field = go.GetComponent<TMP_InputField>();
                    if (EventSystem.current == null) sb.AppendLine("POZOR: EventSystem chybí");
                    EventSystem.current?.SetSelectedGameObject(go);
                    field.ActivateInputField();
                    break;
                case 1:
                    sb.AppendLine($"textové pole aktivní (isFocused {field.isFocused}): F5 povoleno {TravelerCameraView.InputAllowed}");
                    field.DeactivateInputField();
                    EventSystem.current?.SetSelectedGameObject(null);
                    Destroy(canvas);
                    break;
                case 2:
                    sb.AppendLine("pole zavřené: F5 povoleno " + TravelerCameraView.InputAllowed);
                    gm.TogglePauseGame();
                    break;
                case 3:
                    sb.AppendLine($"pauza (IsPaused {gm.IsPaused}, timeScale {Time.timeScale}): F5 povoleno {TravelerCameraView.InputAllowed}");
                    gm.TogglePauseGame();
                    break;
                case 4:
                    sb.AppendLine($"návrat z pauzy (IsPaused {gm.IsPaused}, kurzor {Cursor.lockState}): F5 povoleno {TravelerCameraView.InputAllowed}");
                    var view = Object.FindFirstObjectByType<TravelerCameraView>();
                    var m = startMode;
                    for (int i = 0; i < 3; i++)
                    {
                        m = (TravelerViewMode)(((int)m + 1) % 3);
                        view.SetMode(m);
                        sb.AppendLine($"  F5 krok {i + 1}: {TravelerCameraView.Mode}, skrytý kříž {PlayerInteraction.HideCrosshair}");
                    }
                    break;
                default:
                    sb.AppendLine("cyklus skončil v " + TravelerCameraView.Mode + " (start " + startMode + ")");
                    string dir = Path.Combine(Directory.GetCurrentDirectory(), "_reports/traveler-v004");
                    File.WriteAllText(Path.Combine(dir, "keys_qa.txt"), sb.ToString());
                    Debug.Log("[KeysQA]\n" + sb);
                    Destroy(this);
                    break;
            }
        }
    }
}
