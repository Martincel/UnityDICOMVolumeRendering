using UnityEngine;
using UnityEditor;

namespace VolumeRendering
{

[CustomEditor(typeof(VolumeRenderingWithTransferFunction))]
public class VolumeRenderingEditor : Editor
{
    // Stanje četkice — zadržava se između framova Inspectora
    Color  brushColor  = new Color(1f, 0.8f, 0.6f, 1f);
    float  brushRadius = 4f;
    bool   isPainting  = false;

    // Visina preview slike u pikselima Inspectora
    const float PreviewHeight = 160f;

    // ── Glavni GUI ────────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var script = (VolumeRenderingWithTransferFunction)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("2D Transfer Function Editor", EditorStyles.boldLabel);

        Texture2D tex = script.GetTransferTexture();

        if (tex == null)
        {
            EditorGUILayout.HelpBox(
                "Tekstura nije generirana. Pokreni scenu ili pritisni 'Rebuild from Gradient'.",
                MessageType.Info);
        }
        else
        {
            DrawPreviewWithAxes(tex, script);
        }

        EditorGUILayout.Space(6);
        DrawBrushControls();
        DrawActionButtons(script);
    }

    // ── Preview teksture s osima ──────────────────────────────────────────────

    void DrawPreviewWithAxes(Texture2D tex, VolumeRenderingWithTransferFunction script)
    {
        // Y os oznaka (vertikalna, lijevo od slike)
        EditorGUILayout.BeginHorizontal();

        // Oznaka osi Y rotirana kroz ručni layout
        Rect yLabelRect = GUILayoutUtility.GetRect(14f, PreviewHeight);
        DrawVerticalLabel(yLabelRect, "gradient magnitude  ↑  0 → 1");

        // Sama slika — širi se na dostupnu širinu Inspectora
        Rect previewRect = GUILayoutUtility.GetRect(
            GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true),
            GUILayout.Height(PreviewHeight));

        // Crna pozadina iza slike (da bude vidljivo gdje završava TF)
        EditorGUI.DrawRect(previewRect, Color.black);
        GUI.DrawTexture(previewRect, tex, ScaleMode.StretchToFill, true);

        // Obrub
        Handles.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        Handles.DrawSolidRectangleWithOutline(previewRect, Color.clear, new Color(0.5f, 0.5f, 0.5f));

        EditorGUILayout.EndHorizontal();

        // X os oznaka ispod slike
        Rect xAxisRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.Height(18));
        xAxisRect.x     += 14f;                       // poravnanje s previewom
        xAxisRect.width -= 14f;

        EditorGUI.LabelField(new Rect(xAxisRect.x, xAxisRect.y, 80f, 18f),
            "← Zrak", EditorStyles.miniLabel);
        EditorGUI.LabelField(new Rect(xAxisRect.x + xAxisRect.width * 0.35f, xAxisRect.y, 80f, 18f),
            "meko tkivo", EditorStyles.miniLabel);
        EditorGUI.LabelField(new Rect(xAxisRect.xMax - 60f, xAxisRect.y, 60f, 18f),
            "Kost →", EditorStyles.miniLabel);

        // Interakcija mišem — bojanje
        HandlePaintInput(previewRect, script);
    }

    // ── Obrada miša ───────────────────────────────────────────────────────────

    void HandlePaintInput(Rect rect, VolumeRenderingWithTransferFunction script)
    {
        Event e = Event.current;

        if (!rect.Contains(e.mousePosition)) return;

        // Kursor mijenjamo u crosshair dok smo unutar pregleda
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.CustomCursor);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            isPainting = true;
            PaintAtMouse(e.mousePosition, rect, script);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && isPainting)
        {
            PaintAtMouse(e.mousePosition, rect, script);
            e.Use();
        }
        else if (e.type == EventType.MouseUp)
        {
            isPainting = false;
        }

        // Repaint Inspectora dok mičemo miš po previewu da kursor reagira odmah
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            Repaint();
    }

    void PaintAtMouse(Vector2 mousePos, Rect rect, VolumeRenderingWithTransferFunction script)
    {
        // Pretvori poziciju miša u UV koordinate [0,1]
        float u = Mathf.Clamp01((mousePos.x - rect.x) / rect.width);
        float v = Mathf.Clamp01((mousePos.y - rect.y) / rect.height);

        // Tekstura: y=0 je dno (gradMag=0), ali GUI y=0 je vrh → invertiramo V
        v = 1f - v;

        int px = Mathf.RoundToInt(u * (VolumeRenderingWithTransferFunction.TFWidth  - 1));
        int py = Mathf.RoundToInt(v * (VolumeRenderingWithTransferFunction.TFHeight - 1));

        Undo.RecordObject(script, "Paint Transfer Function");
        script.PaintTF(px, py, Mathf.RoundToInt(brushRadius), brushColor);
        Repaint();
    }

    // ── Kontrole četkice ──────────────────────────────────────────────────────

    void DrawBrushControls()
    {
        EditorGUILayout.LabelField("Četkica", EditorStyles.boldLabel);
        brushColor  = EditorGUILayout.ColorField("Boja", brushColor);
        brushRadius = EditorGUILayout.Slider("Radius (px)", brushRadius, 1f, 15f);
        EditorGUILayout.HelpBox(
            "Lijevi klik / povlačenje po previewu boji TF teksturu.\n" +
            "Desni klik → 'Rebuild from Gradient' resetira ručne izmjene.",
            MessageType.None);
    }

    // ── Gumbi ─────────────────────────────────────────────────────────────────

    void DrawActionButtons(VolumeRenderingWithTransferFunction script)
    {
        EditorGUILayout.Space(4);
        if (GUILayout.Button("Rebuild from Gradient", GUILayout.Height(28)))
        {
            Undo.RecordObject(script, "Rebuild TF from Gradient");
            script.RebuildFromGradient();
            Repaint();
        }
    }

    // ── Helper: vertikalni tekst ──────────────────────────────────────────────

    static void DrawVerticalLabel(Rect rect, string text)
    {
        var style = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = false
        };

        // Rotiraj canvas, nacrtaj, vrati
        GUIUtility.RotateAroundPivot(-90f, rect.center);
        GUI.Label(new Rect(rect.center.x - rect.height / 2f, rect.center.y - 7f, rect.height, 14f), text, style);
        GUIUtility.RotateAroundPivot(90f, rect.center);
    }
}

}
