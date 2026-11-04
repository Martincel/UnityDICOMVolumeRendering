# RQ3: which methods fit VR real-time constraints, and does the evidence support our choice?

Corpus: 53 included studies are tagged RQ3 (`data/extraction.csv`), plus the HCI latency sources
(`miller1968response`, `card1991infovis`) and the latency-in-segmentation study s057. Labels: **[lit]** reported in a
paper, **[ours]** our reasoning or design target, **[synthetic]** measured by our unit tests on synthetic volumes (not
Unity, not the VR PC, not real CT/MR). Latency and hardware are quoted as reported; what is measured differs per paper.

## 1. Requirements (from the project)

| ID | Requirement | Source |
|---|---|---|
| Q1 | No frame over the 11.1 ms budget of a 90 Hz headset caused by the tool | project target **[ours]** (`08-evaluation-plan.md`, Section 3) |
| Q2 | Click to visible result: 150 ms or less in 2D, 400 ms or less for a 3D ROI | design-document targets **[ours]**, anchored in response-time thresholds (about 0.1 s feels instantaneous, about 1 s keeps the flow of thought; `miller1968response`, `card1991infovis`) |
| Q3 | GPU already used by volume rendering: no large extra VRAM or compute on the render GPU | **[ours]** |
| Q4 | Unity 2021.3 (Built-in pipeline, Mono), no heavy native dependencies | **[ours]** |
| Q5 | Accuracy sufficient for the target structures with few interactions | `08-evaluation-plan.md` |

## 2. Evidence per method class

### 2.1 Latency actually reported

| Class | Evidence [lit] | Measures | Hardware as reported |
|---|---|---|---|
| Classical, per case | 18 s per patient (r0316); 5 s per frame (r0375); 3.47 s (r0377); 10.5 s (r0329); about 4 s (r0359); 12–15 s per stack (r0293); 45 s per tumour (r0318); 2–4 min (r0585); about 1 min (r0174) | whole case or stack | desktop/laptop CPUs (r0293: no GPU; r0375: CPU; r0349: 2.3 GHz i3) |
| Classical, incremental | seconds for interaction updates on volumes above 100 GB (r0120); cluster-level random walk 0.0807 s (r0186) | per update | GTX 1060; RTX 3090 |
| Classical, GPU | level set about 3.5 s per volume, 400–500 ms per slice scheme (r0300) | evolution | Titan Xp |
| Interactive CNN | about 2 s (r0012), 6.18 s (r0128), 3.2 s (r0143), 129 ms per step (r0076), 5.13 s per round (r0049) | per interaction or round | V100, RTX 5090, 2080 Ti |
| Promptable foundation model, GPU | 8 ms (r0050); about 50 ms per prompt (r0250); 120–200 ms, up to 3700 ms (isensee2025nninteractive); 0.06 s (r0151); full cycle 0.06 s after a one-off 7.2–146.8 s embedding (r0185) | per prompt, decoder or full cycle | A100, RTX 4090, RTX 3090 |
| Promptable, CPU | **0.12 s per interaction (r0151, CPU mode on a 64-core server CPU, AMD EPYC 7742)**; 1.09 s per volume (r0009, laptop Ryzen 5 5500U) | per interaction / per volume inference | server CPU; laptop CPU |
| Older CNNs on laptop GPUs | DeepIGeoS "real-time response" (wang2019deepigeos, no per-click number extracted) and BIFSeg tested on a MacBook Pro with a laptop GPU (wang2018bifseg) | – | GeForce GT 750M class |
| **Our brush [synthetic]** | 2D 512×512, default ROI: **1.1 ms**; full slice ROI 13.3 ms; 3D ROI 129×129×51 median **102–125 ms** | compute only, no GPU upload | 4-vCPU cloud VM, .NET 8 (`06-rq4-method-description.md`) |

### 2.2 Compute and memory

| Class | Evidence [lit] |
|---|---|
| nnInteractive | VRAM below 10 GB (below 6 GB for small objects), RTX 4090 (isensee2025nninteractive) |
| Other GPU models | FastSAM3D 0.78 GB (r0050); Memorizing SAM 0.88 GB (r0118); CT-SAM3D 1240 MB (r0004); LIM-Net 2.7 GB (r0020); IMVS 3.23 GB (r0076) |
| Foundation-model training | 8× A100, 3 nodes × 4 H100, 64× V100 (r0004, ma2025medsam2, r0143): irrelevant at inference but shows the model class |
| Classical | CPU-only machines named in 8 classical studies (r0293 "no GPU") |

No included study measures how a segmentation model competes with volume rendering for the same GPU. **[ours]**: the
sum of render VRAM and model VRAM, and shared compute, is the risk; server offload removes it (r0184, r0394, r0482) at the
price of a second machine.

### 2.3 Integration in Unity and VR

- Included VR systems with learned segmentation send prompts to a separate machine (r0184 RTX 3090 server, r0394
  "separate server", r0482 DGX-1). No included study runs a medical segmentation network inside Unity: a keyword search of
  the extraction fields for ONNX, Barracuda, Sentis and TensorRT found no match (this is a search of our extraction, not of
  the full texts).
- The only VR papers with frame-rate evidence concern rendering: above 90 Hz with multi-volume compute-shader ray marching on
  an RTX 4080 in Unity 2022.1 Built-in pipeline (r0402); about 120 FPS (r0186); 72 FPS on a standalone Quest 3 (r0179).
- Slicer extensions measured latency of the wrapper, not of VR: 0.6 s per cycle (r0113), 0.06 s (r0185), 60 ms loop gap.

### 2.4 What latency does to users (s057)

s057 ran a controlled experiment with eight simulated latencies (100–2000 ms) and two refresh techniques (automatic
and user-initiated) for scribble segmentation. Reported: with short latencies and automatic refresh the user's attention
stays on the feedback and **labelling is slower**; the effect weakens as latency grows, and the two techniques perform
similarly at the largest latencies (abstract; full text not read). The medical modality is not stated in the abstract.
Implications **[ours]**: (a) "faster is better" is not guaranteed for continuously updating feedback; (b) our design,
one click then one result, is closer to the user-initiated condition; (c) the experiment should be repeated in VR.

## 3. Requirements × methods matrix

Symbols: **+** supported by evidence, **~** partly or conditional, **−** evidence against, **?** no evidence in the corpus.
Every non-empty cell cites its basis.

| Requirement | Our brush (statistical region growing + anti-leak) | Fast GrowCut / seeded competition | GPU level set / random walker | Local learned model (SLIP, FastSAM-3D class) | GPU learned model on render PC (nnInteractive class) | Remote learned model (r0184, r0394 style) |
|---|---|---|---|---|---|---|
| Q1 frame budget | ~ background thread, no GPU use **[ours]**; not measured in Unity | ~ CPU implementation; not measured **[ours]** | − shares the render GPU **[ours]** | ~ CPU variant claimed (r0151) but on a 64-core server CPU | − 6–10 GB VRAM and heavy compute on the render GPU **[ours]** | + no local cost; adds network (r0184 not quantified) |
| Q2 click to result | + 1.1 ms 2D, 102–125 ms 3D ROI **[synthetic]**; classical per-case seconds (r0316, r0375) | ~ GrowCut about 1 min per case (r0174), not per click | ~ 3.5 s per volume (r0300); 0.0807 s cluster update (r0186) | + 0.12 s (r0151); 1.09 s per volume on a laptop CPU (r0009) | + 120–200 ms, up to 3700 ms (isensee2025nninteractive) | ~ "within a few seconds" (r0394) |
| Q3 GPU shared | + CPU only **[ours]** | + CPU possible **[ours]** | − | + CPU mode (r0151) | − | + render GPU untouched |
| Q4 Unity 2021.3, light dependencies | + managed C# code, no native dependencies **[ours]** | + same **[ours]** | ~ compute shaders possible (r0402 uses them for rendering) | ? no Unity inference in the corpus | ? | ~ WebSocket/Python servers (r0184), Unity client only |
| Q5 accuracy with few clicks | ? no per-click evaluation of this method in the corpus; Dice 0.83–0.94 for application-specific classical tools (r0318 0.83, r0375 0.84–0.94, r0585 0.92 lungs) | ~ Dice 0.88 vs consensus (r0335), mean above 85 % (r0174) | ~ DC 0.96 or higher in most cases (r0300) | ~ CT liver lesions DSC 0.827 vs 0.763 for nnInteractive, 6 users (r0151) | + user-study Dice 0.794 vs specialist, 0.853 vs resident, inter-expert 0.842, one radiologist (isensee2025nninteractive); mDSC 78.4–82.2 % with 1–3 clicks (r0004) | + Dice 94.9 % refined brain MRI (r0184) |

Notes for the matrix. The "Q5" cells mix different datasets and are not comparable across columns. The "frame
budget" column is **[ours]** for every local method: no study measured it.

## 4. Does the evidence support our choice?

**Supported.**
1. Classical region-based methods run on CPU in seconds or less in the literature (r0316, r0375, r0377, r0293), so a
   CPU click-region tool is plausible for Q2 and Q3. Our synthetic numbers (1.1 ms; about 0.1 s) are in the range
   needed, but are not yet comparable to any measurement in the literature because no study measures per-click latency
   for this method class.
2. The heavy learned models in the corpus need a GPU of 0.8–10 GB VRAM or a second machine; no included study runs one
   in Unity. For a Unity 2021.3 tool without native dependencies, a classical method is the lowest-risk integration (Q4)
   **[ours]**.
3. Latency is not monotone in its effect on users (s057), so the very short 2D latency of our brush is not by itself a
   claim of better user performance.

**Not supported or contradicted.**
1. **Accuracy.** Learned promptable models are reported with high agreement at few clicks on many structures
   (isensee2025nninteractive, r0004, r0184). The corpus gives no evidence that a statistical region-growing brush reaches
   comparable Dice at few clicks on CT/MR in general; low-contrast lesions and MR intensity drift are the expected weak
   spots (`06-rq4-method-description.md`, Section 6). The claim cannot rest on the literature.
2. **"ML needs a GPU" is weakening.** SLIP reports 0.12 s per interaction on CPU (r0151, on a 64-core server CPU, not a
   consumer laptop), FastSAM-3D 1.09 s per volume on a laptop CPU (r0009), and older interactive CNNs ran on laptop GPUs
   (wang2019deepigeos, wang2018bifseg). A local learned model in a VR PC with limited spare compute is therefore a live alternative, and
   should be a baseline in the evaluation, not dismissed.
3. **Server offload works in VR.** r0184 reports SUS 90.0 with a second RTX 3090 server and r0394 reports usable
   interaction with a server model. Our argument that learned models are impractical in VR holds for a single-PC
   deployment, not for a lab with a server.
4. **Fast GrowCut** (our planned next step) has only per-case evidence in the corpus (r0174, r0335, r0319), no per-click
   latency.

**Not studied (open).** Click-to-display latency of any segmentation algorithm in VR; effect of segmentation on frame time
at 90 Hz; GPU contention between volume rendering and a learned model; Unity inference of medical segmentation networks.

## 5. Answer to RQ3

For the VR real-time constraints, the literature supports **CPU-capable, bounded-region methods** as plausible for Q1 to Q4,
supports **GPU learned models** for accuracy at few clicks, and offers **no measurement of the VR-specific constraints
for either class**. Our choice is consistent with the evidence on latency, compute and integration, and is not supported
by evidence on accuracy. The evaluation plan must therefore measure what the literature does not (Unity latency, frame
time, accuracy per click) and compare against a local learned baseline.
