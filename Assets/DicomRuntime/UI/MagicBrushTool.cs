using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VolumeRendering.Runtime.Annotation;
using VolumeRendering.Runtime.Segmentation;

namespace VolumeRendering.Runtime.UI
{
    // Magic brush: klik na piksel pokreće region growing (2D na sliceu ili 3D u ROI-u) u pozadinskoj niti
    // i rezultat upisuje u masku kao jednu operaciju koja se može poništiti.
    //
    // Tok: Click → Task.Run(RegionGrow2D / RegionGrow3D nad loader.Voxels) → rezultat se u Update (prije
    // AnnotationMaskManager.LateUpdate) primjenjuje kroz ApplyRegion. Novi klik otkazuje stari posao.
    // Osjetljivost uživo: SetSensitivity poništi zadnju operaciju alata (ako je još na vrhu stoga) i nakon
    // kratkog debounce-a ponovno izračuna isti klik.
    //
    // Sučelje je privremeno IMGUI okvir (OnGUI) u gornjem desnom kutu 2D/3D prikaza; u headsetu izbornik
    // preuzima VrMenu. Tipka M prebacuje Kist ↔ Magic, Shift+klik oduzima.
    public class MagicBrushTool : MonoBehaviour
    {
        public enum Tool { Brush, Magic2D, Magic3D }

        public const float MinSensitivity = 0.5f;
        public const float MaxSensitivity = 10f;
        public const float MinRadiusMm = 5f;
        public const float MaxRadiusMm = 100f;

        [SerializeField] DicomVolumeLoader loader;
        [SerializeField] AnnotationMaskManager maskManager;
        [SerializeField] Tool tool = Tool.Brush;
        [SerializeField, Range(MinSensitivity, MaxSensitivity)] float sensitivity = 2.5f;
        // Najveći radijus regije (mm) oko klika; odvojeno za 2D i 3D jer 3D ima manji zadani ROI.
        [SerializeField, Range(MinRadiusMm, MaxRadiusMm)] float radius2DMm = 60f;
        [SerializeField, Range(MinRadiusMm, MaxRadiusMm)] float radius3DMm = 40f;
        [SerializeField] bool subtract;
        [SerializeField, Min(0f)] float debounceSeconds = 0.05f;
        [SerializeField] bool showPanel = true;

        // Rezultat pozadinskog posla; piše pozadinska nit, čita Update. Generacija odbacuje zastarjele rezultate.
        sealed class Job
        {
            public int Generation;
            public CancellationTokenSource Cts;
            public MaskRegion Region;
            public string Message;
            public Exception Error;
            public volatile bool Done;
            public bool Subtract;
            public Tool Tool;
        }

        Job _job;
        int _generation;

        // Zadnji klik (za ponovni izračun pri promjeni osjetljivosti).
        bool _hasClick;
        int _clickSlice, _clickX, _clickY;
        bool _clickSubtract;
        Tool _clickTool;

        // Verzija maske nakon zadnje primjene alata: ako se nije promijenila, operaciju smijemo poništiti.
        int _versionAfterApply = -1;
        float _recomputeAt = -1f;

        public bool Active => tool != Tool.Brush && loader != null && loader.Voxels != null;
        public bool IsMagicSelected => tool != Tool.Brush;
        public Tool CurrentTool => tool;
        public float Sensitivity => sensitivity;
        // Najveći radijus regije za odabrani alat (za Kist vrijedi 2D vrijednost).
        public float RadiusMm => tool == Tool.Magic3D ? radius3DMm : radius2DMm;
        public bool Subtract => subtract;
        public bool Busy => _job != null && !_job.Done;
        public string Message { get; private set; }
        // Broj završenih (ne otkazanih) poslova; VR po njemu zna kad treba javiti "gotovo".
        public int CompletedJobs { get; private set; }
        public event Action StateChanged;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.M) && !Input.GetKey(KeyCode.LeftControl))
                SetTool(tool == Tool.Brush ? Tool.Magic2D : Tool.Brush);

            if (_recomputeAt >= 0f && Time.unscaledTime >= _recomputeAt)
            {
                _recomputeAt = -1f;
                Recompute();
            }

            var job = _job;
            if (job != null && job.Done && job.Generation == _generation)
            {
                _job = null;
                Finish(job);
            }
        }

        void OnDisable() => CancelRunning();

        void OnDestroy() => CancelRunning();

        // ---- javno sučelje (kistovi, izbornik) ----

        public void SetTool(Tool value)
        {
            if (tool == value)
                return;
            tool = value;
            if (!IsMagicSelected)
                CancelRunning();
            ForgetClick();   // osjetljivost/radijus se odnose na novi alat, ne na klik starim
            Message = null;
            StateChanged?.Invoke();
        }

        public void SetToolIndex(int index) => SetTool((Tool)Mathf.Clamp(index, 0, 2));

        public void SetSubtract(bool value)
        {
            subtract = value;
            StateChanged?.Invoke();
        }

        public void CycleTool() => SetTool((Tool)(((int)tool + 1) % 3));

        public void ToggleSubtract() => SetSubtract(!subtract);

        // Mijenja osjetljivost i, ako postoji zadnji klik, poništava njegov rezultat i računa ponovno (debounce).
        public void SetSensitivity(float value)
        {
            value = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            if (Mathf.Approximately(value, sensitivity))
                return;
            sensitivity = value;
            if (_hasClick)
                _recomputeAt = Time.unscaledTime + debounceSeconds;
            StateChanged?.Invoke();
        }

        public void AdjustSensitivity(float delta) => SetSensitivity(sensitivity + delta);

        // Najveći radijus regije (mm) za odabrani alat; kao i osjetljivost, ponovno računa zadnji klik.
        public void SetRadiusMm(float value)
        {
            value = Mathf.Clamp(value, MinRadiusMm, MaxRadiusMm);
            if (Mathf.Approximately(value, RadiusMm))
                return;
            if (tool == Tool.Magic3D)
                radius3DMm = value;
            else
                radius2DMm = value;
            if (_hasClick)
                _recomputeAt = Time.unscaledTime + debounceSeconds;
            StateChanged?.Invoke();
        }

        public void AdjustRadiusMm(float delta) => SetRadiusMm(RadiusMm + delta);

        // Klik na piksel (px, py) sliceu `slice`. subtractOverride (Shift) obrće zadani način.
        public void Click(int slice, int px, int py, bool subtractOverride = false)
        {
            if (!Active)
                return;

            _hasClick = true;
            _clickSlice = slice;
            _clickX = px;
            _clickY = py;
            _clickSubtract = subtract ^ subtractOverride;
            _clickTool = tool;
            _versionAfterApply = -1;   // novi klik je nova operacija, ne dira prethodnu
            _recomputeAt = -1f;
            StartJob();
        }

        // Poništi: ako se nešto još računa, otkaže se taj posao; inače se poništi zadnja operacija u maski.
        // Zadnji klik se zaboravlja, da kasnija promjena osjetljivosti ne vrati poništenu regiju.
        // Vraća true ako se nešto poništilo.
        public bool UndoLast()
        {
            bool wasBusy = Busy;
            CancelRunning();
            ForgetClick();
            _versionAfterApply = -1;

            bool undone = false;
            if (!wasBusy && maskManager != null && maskManager.CanUndo)
            {
                maskManager.Undo();
                undone = true;
            }
            Message = null;
            StateChanged?.Invoke();
            return wasBusy || undone;
        }

        // Zove se kad korisnik počne novi potez/klik pa stari rezultat više nije "zadnji klik" za osjetljivost.
        public void ForgetClick()
        {
            _hasClick = false;
            _recomputeAt = -1f;
        }

        // ---- izračun ----

        void Recompute()
        {
            if (!_hasClick || !Active)
                return;

            // Poništi zadnju operaciju alata, ali samo ako je još zadnja u maski.
            if (_versionAfterApply >= 0 && maskManager != null && maskManager.EditVersion == _versionAfterApply)
                maskManager.Undo();
            _versionAfterApply = -1;
            StartJob();
        }

        void StartJob()
        {
            CancelRunning();

            var voxels = loader.Voxels;
            var job = new Job
            {
                Generation = ++_generation,
                Cts = new CancellationTokenSource(),
                Subtract = _clickSubtract,
                Tool = _clickTool
            };
            _job = job;
            Message = null;

            int slice = Mathf.Clamp(_clickSlice, 0, voxels.Depth - 1);
            int x = _clickX, y = _clickY;
            double k = sensitivity;
            var token = job.Cts.Token;
            bool is3D = _clickTool == Tool.Magic3D;

            // Postavke se klonira na main threadu; nit ih samo čita.
            var settings = is3D ? MagicBrushSettings.Default3D() : new MagicBrushSettings();
            settings.Sensitivity = k;
            settings.MaxRadiusMm = is3D ? radius3DMm : radius2DMm;

            Task.Run(() =>
            {
                try
                {
                    if (is3D)
                    {
                        job.Region = RegionGrow3D.Run(voxels.Data, voxels.Width, voxels.Height, voxels.Depth,
                            voxels.SpacingXMm, voxels.SpacingYMm, voxels.SpacingZMm,
                            x, y, slice, settings, token, out var diag3);
                        job.Message = diag3.Message;
                    }
                    else
                    {
                        job.Region = RegionGrow2D.Run(voxels.Data, voxels.Width, voxels.Height, slice,
                            voxels.SpacingXMm, voxels.SpacingYMm, x, y, settings, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    job.Region = null;
                }
                catch (Exception e)
                {
                    job.Error = e;
                }
                finally
                {
                    job.Done = true;
                }
            }, token);

            StateChanged?.Invoke();
        }

        void Finish(Job job)
        {
            job.Cts.Dispose();
            CompletedJobs++;

            if (job.Error != null)
            {
                Message = "Greška: " + job.Error.Message;
                Debug.LogWarning("[MagicBrush] " + job.Error);
            }
            else if (job.Region != null)
            {
                if (job.Region.IsEmpty)
                {
                    Message = job.Message ?? "Ništa nije označeno.";
                }
                else if (maskManager != null)
                {
                    maskManager.ApplyRegion(job.Region,
                        job.Subtract ? MaskEditMode.Subtract : MaskEditMode.Add);
                    _versionAfterApply = maskManager.EditVersion;
                    Message = job.Region.TouchedRoiBorder ? "Regija dodiruje rub područja, možda curi." : null;
                }
            }

            StateChanged?.Invoke();
        }

        void CancelRunning()
        {
            var job = _job;
            _job = null;
            if (job == null)
                return;

            _generation++;   // rezultat otkazanog posla se odbacuje
            try { job.Cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        // ---- privremeno sučelje ----

        const float PanelWidth = 230f;
        const float PanelHeight = 168f;

        static Rect PanelRect() => new Rect(Screen.width - PanelWidth - 10f, 10f, PanelWidth, PanelHeight);

        // True ako je miš (Input.mousePosition, y odozdo) iznad IMGUI okvira, da klik na njega ne crta po sliceu.
        public bool IsOverPanel(Vector3 mousePosition)
        {
            if (!showPanel || loader == null || loader.Voxels == null)
                return false;
            return PanelRect().Contains(new Vector2(mousePosition.x, Screen.height - mousePosition.y));
        }

        void OnGUI()
        {
            if (!showPanel || loader == null || loader.Voxels == null)
                return;

            var rect = PanelRect();
            GUI.Box(rect, "Magic brush");
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 22f, rect.width - 16f, rect.height - 28f));

            int t = GUILayout.Toolbar((int)tool, new[] { "Kist", "Magic 2D", "Magic 3D" });
            if (t != (int)tool)
                SetToolIndex(t);

            if (IsMagicSelected)
            {
                bool sub = GUILayout.Toggle(subtract, " Oduzmi (Shift+klik obrće)");
                if (sub != subtract)
                    SetSubtract(sub);

                GUILayout.Label($"Osjetljivost k = {sensitivity:0.0}" + (Busy ? "  · računam…" : ""));
                float k = GUILayout.HorizontalSlider(sensitivity, MinSensitivity, MaxSensitivity);
                if (!Mathf.Approximately(k, sensitivity))
                    SetSensitivity(k);

                GUILayout.Label($"Najveći radijus {RadiusMm:0} mm");
                float r = GUILayout.HorizontalSlider(RadiusMm, MinRadiusMm, MaxRadiusMm);
                if (!Mathf.Approximately(r, RadiusMm))
                    SetRadiusMm(r);

                if (!string.IsNullOrEmpty(Message))
                    GUILayout.Label(Message);
            }
            else
            {
                GUILayout.Label("M = prebaci na Magic");
            }

            GUILayout.EndArea();
        }
    }
}
