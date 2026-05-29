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

        Texture2D tex = script.GetTransferTexture();

        if (tex == null)
        {
            EditorGUILayout.HelpBox(
                "Tekstura nije generirana. Pritisni 'Rebuild from Gradient'.",
                MessageType.Info);
        }
        else
        {
            DrawPreview(tex, script);
        }

        EditorGUILayout.Space(6);

        // Kontrole četkice
        EditorGUILayout.LabelField("Četkica", EditorStyles.boldLabel);
        brushColor  = EditorGUILayout.ColorField("Boja", brushColor);
        brushRadius = EditorGUILayout.Slider("Radius (px TF prostora)", brushRadius, 1f, 15f);

        EditorGUILayout.Space(4);

        // Gumbi
        if (GUILayout.Button("Rebuild from Gradient", GUILayout.Height(28)))
        {
            script.RebuildFromGradient();
            Repaint();
        }

        EditorGUILayout.HelpBox(
            "Lijevi klik / povlačenje po previewu = bojanje.\n" +
            "'Rebuild from Gradient' poništava ručne izmjene i vraća na gradient.",
            MessageType.None);
    }

    void DrawPreview(Texture2D tex, VolumeRenderingWithTransferFunction script)
    {
        // Gornja oznaka
        EditorGUILayout.LabelField(
            "Y: 0 (unutrašnjost tkiva) → 1 (rub/površina)   |   X: zrak → kost",
            EditorStyles.miniLabel);

        // Rezerviraj prostor za sliku
        Rect previewRect = GUILayoutUtility.GetRect(
            GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true),
            GUILayout.Height(PreviewHeight));

        // Crna pozadina + sama tekstura
        EditorGUI.DrawRect(previewRect, Color.black);
        GUI.DrawTexture(previewRect, tex, ScaleMode.StretchToFill, true);

        // Tanak obrub
        Handles.BeginGUI();
        Handles.color = new Color(0.6f, 0.6f, 0.6f);
        Handles.DrawPolyLine(
            new Vector3(previewRect.xMin, previewRect.yMin),
            new Vector3(previewRect.xMax, previewRect.yMin),
            new Vector3(previewRect.xMax, previewRect.yMax),
            new Vector3(previewRect.xMin, previewRect.yMax),
            new Vector3(previewRect.xMin, previewRect.yMin));
        Handles.EndGUI();

        // Donja oznaka osi X
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("← Zrak (t=0)", EditorStyles.miniLabel, GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Kost (t=1) →", EditorStyles.miniLabel, GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        // Obrada miša — mora biti poslije GetRect da je previewRect ispravan
        HandlePaint(previewRect, script);
    }

    void HandlePaint(Rect rect, VolumeRenderingWithTransferFunction script)
    {
        Event e = Event.current;

        // Mijenjamo kursor samo unutar previewRect
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Crosshair);

        bool isLeftClick = e.button == 0;
        bool insideRect  = rect.Contains(e.mousePosition);

        if (!insideRect || !isLeftClick) return;

        if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
        {
            // Pretvori koordinate miša u TF prostor (0..TFWidth-1, 0..TFHeight-1)
            float u = Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
            float v = Mathf.Clamp01((e.mousePosition.y - rect.y) / rect.height);
            v = 1f - v;     // GUI Y je odozgora prema dolje, tekstura odozdo prema gore

            int px = Mathf.RoundToInt(u * (VolumeRenderingWithTransferFunction.TFWidth  - 1));
            int py = Mathf.RoundToInt(v * (VolumeRenderingWithTransferFunction.TFHeight - 1));

            script.PaintTF(px, py, Mathf.RoundToInt(brushRadius), brushColor);

            e.Use();        // sprječava propagaciju do ostalih kontrola
            Repaint();      // osvježi Inspector i preview
        }
    }
}

}
