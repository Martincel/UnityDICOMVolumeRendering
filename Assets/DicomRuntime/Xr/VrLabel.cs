using UnityEngine;

namespace VolumeRendering.Runtime.Xr
{
    // Mali 3D natpis za headset (Canvas je Screen Space Overlay i u VR-u se ne vidi). TextMesh s
    // ugrađenim fontom, bez TMP-a i bez dodatnih asseta.
    public static class VrLabel
    {
        public static TextMesh Create(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 50;
            text.characterSize = 0.004f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            return text;
        }

        // Iznad kontrolera u svjetskom prostoru, a ne u prostoru ruke, da natpis ne rotira s rukom,
        // i okrenut prema glavi da se čita s bilo koje strane.
        public static void PlaceAbove(Transform label, Transform hand, Transform head, float height = 0.06f)
        {
            Vector3 pos = hand.position + Vector3.up * height;
            label.position = pos;
            label.rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);
        }
    }
}
