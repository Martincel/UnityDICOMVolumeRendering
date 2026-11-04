using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using VolumeRendering.Runtime.Annotation;
using VolumeRendering.Runtime.UI;

namespace VolumeRendering.Runtime.Xr
{
    // Izbornik u headsetu, bez XR Interaction Toolkita: panel iznad lijevog kontrolera, bira se zrakom
    // iz desnog i desnim okidačem.
    //   lijevi menu — otvori/zatvori panel (desni menu i dalje pali crtanje u 3D)
    //   stavke      — CT preset, način prikaza, osvjetljenje, prag izopovršine, HU pojas (tkiva + granice),
    //                 "Samo oznaka", kosa ravnina
    // Dok je panel otvoren (ili se postavlja ravnina), desna ruka pripada izborniku: VrVolumeController
    // tada desnim okidačem ne hvata ni ne crta, a desni touchpad ne mijenja kist ni skalu.
    //
    // Panel je od Quad primitiva i TextMesha (kao VrLabel), a pogodak se računa presjekom zrake s
    // ravninom panela, pa nema collidera ni fizike. Stanje živi na VolumeRenderingWithTransferFunction i
    // SliceClipController; ovdje su samo prikaz i ulaz. U Unity 6 fazi ovo zamjenjuje world-space Canvas + XRI.
    [RequireComponent(typeof(VrVolumeController))]
    public class VrMenu : MonoBehaviour
    {
        [SerializeField] VolumeRenderingWithTransferFunction volumeRendering;
        // Opcionalno: bez njega se red "Samo oznaka" ne prikazuje.
        [SerializeField] SliceClipController sliceClip;
        // Opcionalno: bez njega se stavka "Poništi" ne prikazuje.
        [SerializeField] AnnotationMaskManager maskManager;
        // Opcionalno: bez njega se retci magic brusha ne prikazuju.
        [SerializeField] MagicBrushTool magicTool;
        // Proziran materijal bez zapisa dubine (Sprites/Default), da panel ne izreže rupu u volumenu.
        [SerializeField] Material panelMaterial;

        [Header("Panel")]
        // Poza panela u prostoru lijevog kontrolera (grip). Panel se čita s -Z strane, pa nagib oko X
        // okreće lice panela prema gore/natrag, prema glavi.
        [SerializeField] Vector3 panelOffset = new Vector3(0f, 0.1f, 0.02f);
        [SerializeField] Vector3 panelEuler = new Vector3(40f, 0f, 0f);
        [SerializeField] float rayLength = 1.5f;

        [Header("Prag izopovršine")]
        [SerializeField] float isoStepHU = 50f;
        [SerializeField] Vector2 isoRangeHU = new Vector2(-1000f, 3000f);

        [Header("Kosa ravnina")]
        [SerializeField] float planeOutlineSize = 0.2f;

        const float Width = 0.24f;
        const float RowHeight = 0.026f;
        const float Gap = 0.004f;
        const float SmallButton = 0.03f;
        const float CharacterSize = 0.0025f;

        static readonly Color BackgroundColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        static readonly Color ButtonColor = new Color(0.24f, 0.25f, 0.3f, 0.95f);
        static readonly Color HoverColor = new Color(0.2f, 0.5f, 0.85f, 0.95f);
        static readonly Color PressColor = new Color(0.95f, 0.55f, 0.2f, 0.95f);
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static readonly VolumeRenderingWithTransferFunction.CTPreset[] Presets =
        {
            VolumeRenderingWithTransferFunction.CTPreset.Bone,
            VolumeRenderingWithTransferFunction.CTPreset.SoftTissue,
            VolumeRenderingWithTransferFunction.CTPreset.Lung,
            VolumeRenderingWithTransferFunction.CTPreset.Brain,
            VolumeRenderingWithTransferFunction.CTPreset.BloodVessels,
            VolumeRenderingWithTransferFunction.CTPreset.AllTissue,
            VolumeRenderingWithTransferFunction.CTPreset.Anatomical,
        };

        class Item
        {
            public Rect Rect;               // u prostoru panela (metri, ishodište u sredini)
            public MeshRenderer Quad;       // null za natpis bez gumba
            public TextMesh Text;
            public Action OnClick;
            public bool Repeat;             // držanjem okidača se ponavlja (prag izopovršine)
            public Func<string> Label;      // null = fiksni tekst
        }

        VrVolumeController _controller;
        Transform _panel;
        Rect _panelRect;
        readonly List<Item> _items = new List<Item>();
        MaterialPropertyBlock _block;
        LineRenderer _line;

        bool _open;
        bool _prevMenuButton, _prevTrigger;
        Item _hover, _pressed;
        float _holdTime, _repeatTimer;

        // Kosa ravnina: objekt s ClipPlaneController; dok se postavlja prati desni kontroler, a zatim se veže
        // za volumen, pa hvatanje i (jednoliko) skaliranje volumena ne pomiču rez.
        ClipPlaneController _clip;
        LineRenderer _planeOutline;
        bool _placing, _planePlaced;

        public bool IsOpen => _open;
        public bool IsPlacingPlane => _placing;

        void Awake()
        {
            _controller = GetComponent<VrVolumeController>();
            _block = new MaterialPropertyBlock();
            if (volumeRendering == null)
                volumeRendering = FindObjectOfType<VolumeRenderingWithTransferFunction>();
        }

        void OnDisable()
        {
            SetOpen(false);
            if (_placing)
                CancelPlacement();
        }

        // Zove VrVolumeController.Update odmah nakon čitanja okidača (redoslijed ne ovisi o Script Execution
        // Orderu). Vraća true kad izbornik u ovom okviru drži desnu ruku.
        public bool Tick(bool rightTrigger)
        {
            bool menuButton = ReadButton(XRNode.LeftHand, CommonUsages.menuButton)
                           || ReadButton(XRNode.LeftHand, CommonUsages.primaryButton);
            if (menuButton && !_prevMenuButton)
                SetOpen(!_open);
            _prevMenuButton = menuButton;

            bool busy = _open || _placing;
            if (_placing)
                UpdatePlacement(rightTrigger);
            else if (_open)
                UpdatePointer(rightTrigger);
            else if (_line != null)
                _line.enabled = false;

            _prevTrigger = rightTrigger;
            return busy;
        }

        void LateUpdate()
        {
            if (!_open)
                return;
            foreach (var item in _items)
                if (item.Label != null)
                    SetText(item.Text, item.Label());
        }

        public void SetOpen(bool open)
        {
            if (open && _panel == null && !BuildPanel())
                return;
            _open = open;
            _hover = _pressed = null;
            if (_panel != null)
                _panel.gameObject.SetActive(open);
            if (!open && !_placing && _line != null)
                _line.enabled = false;
        }

        // ---- stavke ----

        public void NextPreset() => StepPreset(1);
        public void PreviousPreset() => StepPreset(-1);

        void StepPreset(int dir)
        {
            if (volumeRendering == null)
                return;
            int i = Array.IndexOf(Presets, volumeRendering.Preset);
            // Iz "Custom" (nije u listi) prvi korak ide na prvi, odnosno zadnji preset.
            i = i < 0 ? (dir > 0 ? 0 : Presets.Length - 1) : (i + dir + Presets.Length) % Presets.Length;
            volumeRendering.Preset = Presets[i];
        }

        public void CycleStyle()
        {
            if (volumeRendering == null)
                return;
            volumeRendering.Style = (VolumeRenderingWithTransferFunction.RenderStyle)(((int)volumeRendering.Style + 1) % 3);
        }

        public void ToggleLighting()
        {
            if (volumeRendering != null)
                volumeRendering.Lighting = !volumeRendering.Lighting;
        }

        public void ChangeIso(int steps)
        {
            if (volumeRendering == null)
                return;
            float hu = Mathf.Round(volumeRendering.IsoValueHU / isoStepHU + steps) * isoStepHU;
            volumeRendering.IsoValueHU = Mathf.Clamp(hu, isoRangeHU.x, isoRangeHU.y);
        }

        // Isključen → pluća → mast → meko tkivo → žile s kontrastom → kost → isključen.
        public void CycleTissueBand()
        {
            if (volumeRendering == null)
                return;
            var tissue = volumeRendering.HuBandEnabled ? volumeRendering.HuBandTissue : VolumeRenderingWithTransferFunction.TissueBand.Custom;
            int count = Enum.GetValues(typeof(VolumeRenderingWithTransferFunction.TissueBand)).Length;
            volumeRendering.SetTissueBand((VolumeRenderingWithTransferFunction.TissueBand)(((int)tissue + 1) % count));
        }

        // Ručno pomicanje granice (isti korak kao prag izopovršine); uključuje pojas ako je bio isključen.
        public void ChangeBand(int steps, bool lower)
        {
            if (volumeRendering == null)
                return;
            float current = lower ? volumeRendering.HuBandMinHU : volumeRendering.HuBandMaxHU;
            float hu = Mathf.Round(current / isoStepHU + steps) * isoStepHU;
            if (lower)
                volumeRendering.HuBandMinHU = hu;
            else
                volumeRendering.HuBandMaxHU = hu;
            volumeRendering.HuBandEnabled = true;
        }

        public void CycleMaskOnly()
        {
            if (sliceClip != null)
                sliceClip.CycleMaskOnly();
        }

        // Prvo uključivanje odmah ulazi u postavljanje; kasnije samo pali/gasi već postavljenu ravninu.
        public void ToggleClipPlane()
        {
            if (_placing)
            {
                CancelPlacement();
                return;
            }
            if (!EnsureClipPlane())
                return;
            if (_clip.enabled)
                _clip.enabled = false;
            else if (_planePlaced)
                _clip.enabled = true;
            else
                BeginPlacement();
        }

        // Ravnina prati desni kontroler (normala = smjer zrake, ostaje vidljiv dio dalje od ruke) dok se
        // ponovno ne pritisne desni okidač.
        public void BeginPlacement()
        {
            if (!EnsureClipPlane() || _controller.Rig == null || _controller.Rig.RightHand == null)
                return;
            _placing = true;
            _clip.transform.SetParent(null, true);
            _clip.enabled = true;
            _planeOutline.enabled = true;
            FollowRightHand();
        }

        // Zamrzne ravninu na trenutnoj pozi i veže je za volumen.
        public void FinishPlacement()
        {
            if (!_placing)
                return;
            _placing = false;
            _planePlaced = true;
            _planeOutline.enabled = false;
            _clip.transform.SetParent(volumeRendering.transform, true);
        }

        void CancelPlacement()
        {
            _placing = false;
            if (_planeOutline != null)
                _planeOutline.enabled = false;
            if (_clip == null)
                return;
            if (_planePlaced)
                _clip.transform.SetParent(volumeRendering.transform, true);
            else
                _clip.enabled = false;
        }

        bool EnsureClipPlane()
        {
            if (_clip != null)
                return true;
            if (volumeRendering == null)
                return false;
            var renderer = volumeRendering.GetComponent<Renderer>();
            if (renderer == null)
                return false;

            var go = new GameObject("VrClipPlane");
            _clip = go.AddComponent<ClipPlaneController>();
            _clip.enabled = false;
            _clip.Bind(volumeRendering.transform, renderer.sharedMaterial);

            float h = planeOutlineSize * 0.5f;
            _planeOutline = CreateLine(go.transform, "Outline", false);
            _planeOutline.loop = true;
            _planeOutline.positionCount = 4;
            _planeOutline.SetPositions(new[]
            {
                new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f)
            });
            _planeOutline.startWidth = _planeOutline.endWidth = 0.002f;
            _planeOutline.startColor = _planeOutline.endColor = new Color(1f, 0.8f, 0.2f, 0.9f);
            _planeOutline.enabled = false;
            return true;
        }

        void FollowRightHand()
        {
            Transform hand = _controller.Rig.RightHand;
            Ray ray = _controller.PointerRay(hand);
            _clip.transform.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction, hand.up));
        }

        void UpdatePlacement(bool trigger)
        {
            if (_line != null)
                _line.enabled = false;
            if (_controller.Rig == null || _controller.Rig.RightHand == null)
            {
                CancelPlacement();
                return;
            }
            FollowRightHand();
            if (trigger && !_prevTrigger)
            {
                FinishPlacement();
                Haptic();
            }
        }

        // ---- zraka i klik ----

        void UpdatePointer(bool trigger)
        {
            Transform hand = _controller.Rig != null ? _controller.Rig.RightHand : null;
            if (hand == null || _panel == null)
                return;

            Ray ray = _controller.PointerRay(hand);
            bool onPanel = HitPanel(ray, out Vector2 p, out Vector3 worldHit);
            Item hover = onPanel ? FindItem(p) : null;

            if (trigger && !_prevTrigger && hover != null)
            {
                _pressed = hover;
                _holdTime = 0f;
                _repeatTimer = 0.4f;
                hover.OnClick();
                Haptic();
            }
            else if (trigger && _pressed != null && _pressed.Repeat && _pressed == hover)
            {
                // Držanje ponavlja, sve brže (od 0.15 s do 0.03 s kroz ~1.5 s), kao listanje sliceova.
                _holdTime += Time.deltaTime;
                _repeatTimer -= Time.deltaTime;
                if (_repeatTimer <= 0f)
                {
                    _pressed.OnClick();
                    _repeatTimer = Mathf.Lerp(0.15f, 0.03f, Mathf.Clamp01(_holdTime / 1.5f));
                }
            }
            if (!trigger)
                _pressed = null;

            if (hover != _hover)
            {
                if (_hover != null) Tint(_hover, ButtonColor);
                _hover = hover;
            }
            if (_hover != null)
                Tint(_hover, trigger && _pressed == _hover ? PressColor : HoverColor);

            if (_line == null)
                _line = CreateLine(transform, "VrMenuRay", true);
            _line.enabled = true;
            _line.SetPosition(0, ray.origin);
            _line.SetPosition(1, onPanel ? worldHit : ray.origin + ray.direction * rayLength);
            Color c = hover != null ? HoverColor : Color.white;
            _line.startColor = c;
            _line.endColor = c;
        }

        // Presjek zrake s ravninom panela (z = 0 u prostoru panela, koji nema skalu).
        bool HitPanel(Ray ray, out Vector2 p, out Vector3 worldHit)
        {
            p = default;
            worldHit = default;
            Vector3 o = _panel.InverseTransformPoint(ray.origin);
            Vector3 d = _panel.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(d.z) < 1e-5f)
                return false;
            float t = -o.z / d.z;
            if (t < 0f || t > rayLength)
                return false;
            Vector3 hit = o + d * t;
            p = new Vector2(hit.x, hit.y);
            if (!_panelRect.Contains(p))
                return false;
            worldHit = _panel.TransformPoint(hit);
            return true;
        }

        Item FindItem(Vector2 p)
        {
            foreach (var item in _items)
                if (item.Quad != null && item.Rect.Contains(p))
                    return item;
            return null;
        }

        // ---- izgradnja panela ----

        bool BuildPanel()
        {
            Transform hand = _controller.Rig != null ? _controller.Rig.LeftHand : null;
            if (hand == null || volumeRendering == null)
                return false;

            _panel = new GameObject("VrMenuPanel").transform;
            _panel.SetParent(hand, false);
            _panel.localPosition = panelOffset;
            _panel.localRotation = Quaternion.Euler(panelEuler);

            var rows = new List<Action<float>>
            {
                y => Row(y, () => $"Preset: {PresetName(volumeRendering.Preset)}", NextPreset,
                         ("<", PreviousPreset, SmallButton, false), (">", NextPreset, SmallButton, false)),
                y => Row(y, () => "Način: " + StyleName(volumeRendering.Style), CycleStyle),
                y => Row(y, () => volumeRendering.Lighting ? "Osvjetljenje: uključeno" : "Osvjetljenje: isključeno",
                         ToggleLighting),
                y => Row(y, () => volumeRendering.HuBandEnabled
                             ? "Izopovršina: pojas"
                             : $"Izopovršina: {volumeRendering.IsoValueHU:0} HU", null,
                         ("-", () => ChangeIso(-1), SmallButton, true), ("+", () => ChangeIso(1), SmallButton, true)),
                y => Row(y, BandName, CycleTissueBand),
                y => Row(y, () => $"Pojas od: {volumeRendering.HuBandMinHU:0} HU", null,
                         ("-", () => ChangeBand(-1, true), SmallButton, true), ("+", () => ChangeBand(1, true), SmallButton, true)),
                y => Row(y, () => $"Pojas do: {volumeRendering.HuBandMaxHU:0} HU", null,
                         ("-", () => ChangeBand(-1, false), SmallButton, true), ("+", () => ChangeBand(1, false), SmallButton, true)),
            };
            if (sliceClip != null)
                rows.Add(y => Row(y, MaskOnlyName, CycleMaskOnly));
            rows.Add(y => Row(y, ClipName, ToggleClipPlane, ("Postavi", BeginPlacement, 0.06f, false)));
            if (magicTool != null)
            {
                rows.Add(y => Row(y, () => "Alat: " + MagicToolName(), magicTool.CycleTool));
                rows.Add(y => Row(y, () => magicTool.Subtract ? "Način: oduzmi" : "Način: dodaj", magicTool.ToggleSubtract));
                rows.Add(y => Row(y, () => $"Osjetljivost: {magicTool.Sensitivity:0.0}", null,
                                  ("-", () => magicTool.AdjustSensitivity(-0.1f), SmallButton, true),
                                  ("+", () => magicTool.AdjustSensitivity(0.1f), SmallButton, true)));
            }
            if (maskManager != null)
                rows.Add(y => Row(y, () => maskManager.CanUndo ? "Poništi oznaku" : "Poništi oznaku (nema)",
                                  () => maskManager.Undo()));

            float height = rows.Count * RowHeight + (rows.Count + 1) * Gap;
            _panelRect = new Rect(-Width * 0.5f, -height * 0.5f, Width, height);
            CreateQuad("Background", _panelRect, BackgroundColor, 0, 0f);

            float top = height * 0.5f - Gap - RowHeight * 0.5f;
            for (int i = 0; i < rows.Count; i++)
                rows[i](top - i * (RowHeight + Gap));

            _panel.gameObject.SetActive(false);
            return true;
        }

        // Jedan red: glavni dio (gumb ako ima akciju, inače natpis) i mali gumbi zdesna.
        void Row(float y, Func<string> label, Action onClick,
                 params (string text, Action action, float width, bool repeat)[] buttons)
        {
            float right = Width * 0.5f - Gap;
            foreach (var b in buttons)
            {
                var rect = new Rect(right - b.width, y - RowHeight * 0.5f, b.width, RowHeight);
                AddItem(rect, b.text, b.action, b.repeat, null, TextAnchor.MiddleCenter);
                right -= b.width + Gap;
            }

            float left = -Width * 0.5f + Gap;
            var main = new Rect(left, y - RowHeight * 0.5f, right - left, RowHeight);
            AddItem(main, label(), onClick, false, label, onClick != null ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
        }

        void AddItem(Rect rect, string text, Action onClick, bool repeat, Func<string> label, TextAnchor anchor)
        {
            var item = new Item { Rect = rect, OnClick = onClick, Repeat = repeat, Label = label };
            if (onClick != null)
                item.Quad = CreateQuad("Button", rect, ButtonColor, 1, -0.001f);

            item.Text = VrLabel.Create(_panel, "Text", Color.white);
            item.Text.characterSize = CharacterSize;
            item.Text.anchor = anchor;
            item.Text.alignment = anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : TextAlignment.Center;
            float x = anchor == TextAnchor.MiddleLeft ? rect.xMin + Gap : rect.center.x;
            item.Text.transform.localPosition = new Vector3(x, rect.center.y, -0.002f);
            item.Text.GetComponent<MeshRenderer>().sortingOrder = 2;
            item.Text.text = text;
            _items.Add(item);
        }

        MeshRenderer CreateQuad(string name, Rect rect, Color color, int order, float z)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_panel, false);
            go.transform.localPosition = new Vector3(rect.center.x, rect.center.y, z);
            go.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = panelMaterial != null ? panelMaterial : new Material(Shader.Find("Sprites/Default"));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = order;
            SetColor(renderer, color);
            return renderer;
        }

        LineRenderer CreateLine(Transform parent, string name, bool worldSpace)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = worldSpace;
            line.startWidth = 0.004f;
            line.endWidth = 0.002f;
            line.numCapVertices = 2;
            line.material = panelMaterial != null ? panelMaterial : new Material(Shader.Find("Sprites/Default"));
            return line;
        }

        void Tint(Item item, Color color)
        {
            if (item.Quad != null)
                SetColor(item.Quad, color);
        }

        void SetColor(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        static void SetText(TextMesh text, string value)
        {
            if (text.text != value)
                text.text = value;
        }

        // ---- natpisi ----

        static string PresetName(VolumeRenderingWithTransferFunction.CTPreset preset) => preset switch
        {
            VolumeRenderingWithTransferFunction.CTPreset.Bone => "kost",
            VolumeRenderingWithTransferFunction.CTPreset.SoftTissue => "meko tkivo",
            VolumeRenderingWithTransferFunction.CTPreset.Lung => "pluća",
            VolumeRenderingWithTransferFunction.CTPreset.Brain => "mozak",
            VolumeRenderingWithTransferFunction.CTPreset.BloodVessels => "krvne žile",
            VolumeRenderingWithTransferFunction.CTPreset.AllTissue => "sva tkiva",
            VolumeRenderingWithTransferFunction.CTPreset.Anatomical => "anatomski",
            _ => "ručno"
        };

        string BandName()
        {
            if (!volumeRendering.HuBandEnabled)
                return "Pojas HU: isključen";
            return "Pojas HU: " + volumeRendering.HuBandTissue switch
            {
                VolumeRenderingWithTransferFunction.TissueBand.Lung => "pluća",
                VolumeRenderingWithTransferFunction.TissueBand.Fat => "mast",
                VolumeRenderingWithTransferFunction.TissueBand.SoftTissue => "meko tkivo",
                VolumeRenderingWithTransferFunction.TissueBand.ContrastVessels => "žile (kontrast)",
                VolumeRenderingWithTransferFunction.TissueBand.Bone => "kost",
                _ => "ručno"
            };
        }

        static string StyleName(VolumeRenderingWithTransferFunction.RenderStyle style) => style switch
        {
            VolumeRenderingWithTransferFunction.RenderStyle.Mip => "MIP",
            VolumeRenderingWithTransferFunction.RenderStyle.Isosurface => "izopovršina",
            _ => "slaganje"
        };

        string MaskOnlyName() => sliceClip.MaskOnly switch
        {
            1 => "Samo oznaka: gustoća tkiva",
            2 => "Samo oznaka: puna boja",
            _ => "Prikaz: cijeli volumen"
        };

        string MagicToolName() => magicTool.CurrentTool switch
        {
            MagicBrushTool.Tool.Magic2D => "Magic 2D",
            MagicBrushTool.Tool.Magic3D => "Magic 3D",
            _ => "Kist"
        };

        string ClipName()
        {
            if (_placing)
                return "Ravnina: postavljanje";
            return _clip != null && _clip.enabled ? "Ravnina: uključena" : "Ravnina: isključena";
        }

        // ---- ulaz ----

        static void Haptic()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid)
                device.SendHapticImpulse(0, 0.3f, 0.02f);
        }

        static bool ReadButton(XRNode node, InputFeatureUsage<bool> usage)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;
        }
    }
}
