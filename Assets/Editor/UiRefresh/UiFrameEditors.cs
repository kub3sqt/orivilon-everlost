using UnityEditor;
using UnityEngine;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>Inspector for UiFrame: preview any state and capture the current layout into a state.</summary>
    [CustomEditor(typeof(UiFrame))]
    public class UiFrameEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var f = (UiFrame)target;
            EditorGUILayout.HelpBox("Arrange the Panel_* pieces in the Scene view, then Capture into a state. " +
                                    "Preview applies a state without entering Play Mode. Fitted edges (Fit Side) follow their Fit To width.", MessageType.Info);
            foreach (var s in f.states)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(s.name, GUILayout.Width(90));
                if (GUILayout.Button("Preview"))
                {
                    foreach (var p in s.pieces) if (p.piece != null) Undo.RecordObject(p.piece, "Preview frame state");
                    f.ApplyInstant(s);
                }
                if (GUILayout.Button("Capture current layout"))
                {
                    Undo.RecordObject(f, "Capture frame state");
                    foreach (var p in s.pieces)
                    {
                        if (p.piece == null) continue;
                        p.anchorMin = p.piece.anchorMin; p.anchorMax = p.piece.anchorMax;
                        p.offsetMin = p.piece.offsetMin; p.offsetMax = p.piece.offsetMax;
                        if (p.fitTo != null)
                        {
                            // keep the fitted edge relative to its text
                            if (p.fit == UiFrame.FitSide.Right) p.fitPad = p.offsetMax.x - p.fitTo.rect.width;
                            else if (p.fit == UiFrame.FitSide.Left) p.fitPad = -p.offsetMin.x - p.fitTo.rect.width;
                        }
                    }
                    EditorUtility.SetDirty(f);
                    if (PrefabUtility.IsPartOfPrefabInstance(f)) PrefabUtility.RecordPrefabInstancePropertyModifications(f);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }

    /// <summary>Inspector for FrameStyle: regenerates the frame sprites/materials from the values.</summary>
    [CustomEditor(typeof(FrameStyle))]
    public class FrameStyleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("Regenerate sprites"))
            {
                FrameAuthoring.Regenerate((FrameStyle)target);
                foreach (var p in Object.FindObjectsByType<FramePiece>(FindObjectsInactive.Include, FindObjectsSortMode.None)) p.Refresh();
                foreach (var j in Object.FindObjectsByType<FrameJoin>(FindObjectsInactive.Include, FindObjectsSortMode.None)) j.ForceUpdate();
            }
        }
    }

    /// <summary>Inspector for HudController: preview the closed / open inventory without Play Mode.</summary>
    [CustomEditor(typeof(HudController))]
    public class HudControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var h = (HudController)target;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Preview closed")) Preview(h, "Closed", 0f);
            if (GUILayout.Button("Preview open")) Preview(h, "Open", 1f);
            EditorGUILayout.EndHorizontal();
            DrawDefaultInspector();
        }

        private static void Preview(HudController h, string state, float alpha)
        {
            if (h.frame != null)
            {
                var s = h.frame.Find(state);
                if (s != null) { foreach (var p in s.pieces) if (p.piece != null) Undo.RecordObject(p.piece, "Preview HUD"); h.frame.ApplyInstant(s); }
            }
            if (h.inventoryGroups != null)
                foreach (var g in h.inventoryGroups) if (g != null) { Undo.RecordObject(g, "Preview HUD"); g.alpha = alpha; }
        }
    }

    /// <summary>Inspector for MainMenuController: preview the three menu frame states.</summary>
    [CustomEditor(typeof(MainMenuController))]
    public class MainMenuControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var m = (MainMenuController)target;
            EditorGUILayout.HelpBox("Which screen is visible is decided by MainMenuUI (PlayMenu / CreateWorldMenu active). " +
                                    "These buttons only preview the frame shape for each screen.", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            foreach (var n in new[] { MainMenuController.MenuState, MainMenuController.WorldsState, MainMenuController.CreateState })
                if (GUILayout.Button(n) && m.frame != null)
                {
                    var s = m.frame.Find(n);
                    if (s != null) { foreach (var p in s.pieces) if (p.piece != null) Undo.RecordObject(p.piece, "Preview menu"); m.frame.ApplyInstant(s); }
                }
            EditorGUILayout.EndHorizontal();
            DrawDefaultInspector();
        }
    }
}
