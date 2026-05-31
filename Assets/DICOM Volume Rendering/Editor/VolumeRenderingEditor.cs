using UnityEngine;
using UnityEditor;

namespace VolumeRendering
{

[CustomEditor(typeof(VolumeRenderingWithTransferFunction))]
public class VolumeRenderingEditor : Editor
{
    Color brushColor  = new Color(1f, 0.8f, 0.6f, 1f);
    float brushRadius = 4f;

    const float PreviewHeight = 160f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var script = (VolumeRenderingWithTransferFunction)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("── 2D Transfer Function Editor ──", EditorStyles.boldLabel);

        // Inicijaliziraj teksturu ako je null (npr. nakon recompile)
        Texture2D tex = script.GetTransferTexture();
        if (tex == null)
        {
            script.RebuildFromGradient();
            tex = script.GetTransferTexture();
        }

        if (tex != null)
            DrawPreview(tex, script);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Četkica", EditorStyles.boldLabel);
        brushColor  = EditorGUILayout.ColorField("Boja", brushColor);
        brushRadius = EditorGUILayout.Slider("Radius (px)", brushRadius, 1f, 15f);

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Rebuild from Gradient", GUILayout.Height(28)))
        {
            script.RebuildFromGradient();
            Repaint();
        }

        EditorGUILayout.HelpBox(
            "Lijevi klik / povlačenje po previewu = bojanje.\n" +
            "'Rebuild from Gradient' poništava ručne izmjene.",
            MessageType.None);
    }

    void DrawPreview(Texture2D tex, VolumeRenderingWithTransferFunction script)
    {
        EditorGUILayout.LabelField(
            "Y os: 0 (unutrašnjost tkiva)  →  1 (rub/površina)",
            EditorStyles.miniLabel);

        // Rezerviraj prostor za preview
        Rect previewRect = GUILayoutUtility.GetRect(
            GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true),
            GUILayout.Height(PreviewHeight));

        // Crta se samo za Repaint i Layout — ne za mouse eventove
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(previewRect, Color.black);
            GUI.DrawTexture(previewRect, tex, ScaleMode.StretchToFill, true);

            // Obrub
            Handles.BeginGUI();
            Handles.color = new Color(0.5f, 0.5f, 0.5f);
            var r = previewRect;
            Handles.DrawPolyLine(
                new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin),
                new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax),
                new Vector3(r.xMin, r.yMin));
            Handles.EndGUI();
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("← Zrak (t=0)", EditorStyles.miniLabel, GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Kost (t=1) →", EditorStyles.miniLabel, GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        // Kursor
        EditorGUIUtility.AddCursorRect(previewRect, MouseCursor.Arrow);

        // Obrada miša s GetControlID — jedini pouzdan način u custom Inspectoru
        HandlePaintWithControlID(previewRect, script);
    }

    void HandlePaintWithControlID(Rect rect, VolumeRenderingWithTransferFunction script)
    {
        // Svaki interaktivni element u Unity Editoru treba svoj control ID
        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        Event e = Event.current;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(e.mousePosition) && e.button == 0)
                {
                    // Preuzimamo "vlasništvo" nad drag eventovima
                    GUIUtility.hotControl = id;
                    PaintAt(e.mousePosition, rect, script);
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    PaintAt(e.mousePosition, rect, script);
                    e.Use();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
                break;
        }
    }

    void PaintAt(Vector2 mousePos, Rect rect, VolumeRenderingWithTransferFunction script)
    {
        // Pretvori piksel poziciju miša u UV koordinate [0, 1]
        float u = Mathf.Clamp01((mousePos.x - rect.x) / rect.width);
        float v = Mathf.Clamp01((mousePos.y - rect.y) / rect.height);
        v = 1f - v;     // GUI Y ide odozgora prema dolje; tekstura Y ide odozdo prema gore

        // Pretvori UV u TF koordinate (0..TFWidth-1, 0..TFHeight-1)
        int px = Mathf.RoundToInt(u * (VolumeRenderingWithTransferFunction.TFWidth  - 1));
        int py = Mathf.RoundToInt(v * (VolumeRenderingWithTransferFunction.TFHeight - 1));

        script.PaintTF(px, py, Mathf.RoundToInt(brushRadius), brushColor);
        Repaint();
    }
}

}
