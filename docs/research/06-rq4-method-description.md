# RQ4 — Formal description of the magic brush method

Source of truth: `Assets/DicomRuntime/Segmentation/` (`RegionGrow2D.cs`, `RegionGrow3D.cs`, `Morphology2D.cs`,
`Morphology3D.cs`, `RegionStats.cs`, `MagicBrushSettings.cs`) on branch `feature/magic-brush` at commit `9b47415`.
This chapter describes what the code does; where the code and the internal plan (`docs/magic-brush-research.md`)
differ, the code wins. Every component is linked to its original source. Statements marked **[ours]** are our design
decisions, not claims from the literature.

## 1. Data and notation

- Volume $I:\Omega\to\{0,\dots,65535\}$, $\Omega\subset\mathbb{Z}^3$, voxel $p=(x,y,z)$, spacing
  $h=(h_x,h_y,h_z)$ in mm (anisotropic, e.g. $0.9\times0.9\times2.5$ mm). Stored density is linear in Hounsfield
  units: $I = (\mathrm{HU}+1000)\cdot 65535/4000$, so 1 HU ≈ 16.4 raw units. For MR the same scale is used, but only
  relative intensities matter (the method never uses absolute thresholds) **[ours]**.
- Gradient magnitude $G(p)\in[0,1]$: central differences in voxel units on $I/65535$, multiplied by 10 and clipped
  (pre-computed in the second texture channel for 3D; recomputed in-plane for 2D).
- Click $c$ (from the 2D slice view, from a ray–slice-plane intersection in the 3D view, or from the VR controller ray).
- $N_4/N_6$: 4-neighbourhood (2D) / 6-neighbourhood (3D).
- Parameters (defaults in brackets, 2D / 3D; `MagicBrushSettings`): sensitivity $k$ [2.5 / 2.5]; tolerance bounds
  $\tau_{\min},\tau_{\max}$ [15, 300 HU]; statistics window $w$ [5×5 / 3×3×3]; seed slide distance $d$ [2 / 1 voxel];
  ROI radius $R$ [60 / 40 mm]; anti-leak radius $\rho$ [2 mm]; edge stop $\gamma$ [off / 0.6]; robust statistics
  [off]; hole filling [on] with maximum hole area $A$ [200 mm² per slice]; voxel cap $V_{\max}$ [– / $3\cdot10^6$].

## 2. Algorithm

**Step 1 — bounded region of interest.** The ROI is a disc (2D) or ball (3D) of radius $R$ mm around $c$, i.e. an
ellipse/ellipsoid in voxel units:
$$B=\{p:\ \textstyle\sum_a \big((p_a-c_a)\,h_a/R\big)^2\le 1\}.$$
All later sets are subsets of $B$. Bounding the domain is a standard way to keep interactive region growing local and
fast; 3D Slicer's *Local threshold* effect also limits growth by an ROI (`slicer_extraeffects`). **[ours]**: $R$ in
mm, so behaviour does not depend on resolution.

**Step 2 — seed relocation and local statistics.**
Seed slide: among voxels within Chebyshev distance $d$ of $c$ that belong to "the same tissue",
$|I(q)-I(c)|\le\tau_{\max}$, pick the one with the smallest gradient, ties broken by distance to $c$:
$$s=\arg\min_{q:\ \|q-c\|_\infty\le d,\ |I(q)-I(c)|\le\tau_{\max}} \big(G(q),\ \|q-c\|_2\big).$$
This keeps the statistics window away from an edge when the user clicks next to a boundary **[ours]**; the idea of
starting from a low-gradient location is common in seeded methods but we do not attribute it to one source.

Statistics: let $S=\{q\in W_w(s): |I(q)-I(s)|\le\tau_{\max}\}$, where $W_w(s)$ is the $w$-window around $s$ (always
contains $s$). Then either mean and population standard deviation
$$\mu=\tfrac1{|S|}\sum_{q\in S}I(q),\qquad \sigma=\sqrt{\tfrac1{|S|}\sum_{q\in S}(I(q)-\mu)^2},$$
or (robust option) the median and $\sigma = 1.4826\cdot\mathrm{MAD}$, the consistent estimator of the normal
standard deviation (Rousseeuw & Croux, 1993, `rousseeuw1993mad`). Tolerance:
$$t=\operatorname{clamp}(k\sigma,\ \tau_{\min},\ \tau_{\max}).$$
Intervals of the form $\mu\pm k\sigma$ estimated from the seed neighbourhood are the criterion of ITK's
`ConfidenceConnectedImageFilter` (`itk_confidenceconnected`), which additionally re-estimates $\mu,\sigma$ from the
grown region for a number of iterations. We use a single estimate from the seed window (no iteration) and clamp $t$
**[ours]**, because iterative re-estimation can widen the interval and drift (internal analysis, chapter 2.2 of the
design document; not measured yet).

**Step 3 — candidate set.**
$$T_I=\{p\in B:\ |I(p)-\mu|\le t\},\qquad T=\{p\in T_I:\ \gamma=0\ \lor\ G(p)\le\gamma\}.$$
$T$ is the classical homogeneity predicate of seeded region growing (Adams & Bischof, 1994, `adams1994srg`), with a
fixed (seed-derived) interval instead of the running region mean of SRG. The optional edge stop $G\le\gamma$ is a
binary version of the edge-indicator idea used by geodesic active contours, where the front slows down where
$|\nabla I|$ is large (Caselles et al., 1997, `caselles1997gac`). If $s\notin T$ (noise on the clicked voxel), $s$ is
moved to the nearest voxel of $T$ within $\lfloor w/2\rfloor$; if none exists the result is empty.

**Step 3b — fill small holes in $T$** (only if hole filling is on): $T\leftarrow \mathcal H_A(T)$, where
$\mathcal H_A$ fills every background component (4-connected, per slice) that does not touch the ROI border and has
area $\le A$ mm². Hole filling by reconstruction of the complement from the border is a standard morphological
operation (Vincent, 1993, `vincent1993reconstruction`). **[ours]**: we fill before erosion, because isolated noise
voxels inside an organ would otherwise make the erosion in Step 4 break the organ into islands; the size limit $A$
prevents a click in fat from "filling" a whole organ that is surrounded by fat (unit test
`ClickInFatAroundOrgan*`).

**Step 4 — anti-leak erosion.** Per-axis radius $r_a=\operatorname{round}(\rho/h_a)$ (e.g. 2 mm → $r_x=r_y=2$,
$r_z=1$ at 2.5 mm slices). Structuring element $D_r$ = discrete ellipse/ellipsoid with semi-axes $r_a$.
$$E=T\ominus D_r=\{p:\ p+D_r\subseteq T\}.$$
Any connection of $T$ narrower than about $2r+1$ voxels disappears from $E$. If $s\notin E$ (the click is inside a
structure thinner than the element, e.g. a small vessel), all $r_a$ are decreased by 1 and $E$ is recomputed, down to
$r=0$ (then $E=T$ and the method degenerates to a plain flood fill) **[ours]**.

**Step 5 — connected component.** $R_0 = \mathrm{CC}_{N}(E,s)$, the 4- (2D) or 6-connected (3D) component of $E$
containing $s$ (stack-based flood fill; in 3D aborted if it exceeds $V_{\max}$).

**Step 6 — bounded geodesic dilation.** The shape lost by erosion is restored by $r$ steps of geodesic dilation
inside $T$:
$$R=\delta_T^{(r)}(R_0),\qquad \delta_T^{(1)}(X)=(X\oplus K)\cap T,\quad \delta_T^{(n)}=\delta_T^{(1)}\circ\delta_T^{(n-1)},$$
where in step $n$ the element $K$ allows moves along axis $a$ only while $n\le r_a$ (so after $r$ steps the reachable
set covers the box that contains $D_r$). Geodesic dilation and its limit, reconstruction by dilation
$\rho_T(R_0)=\delta_T^{(\infty)}(R_0)$, are defined in Vincent (1993). The combination erosion → component → geodesic
dilation is an *opening by reconstruction* that is deliberately **truncated** after $r$ steps **[ours]**: full
reconstruction would walk back through a thin bridge (bridge voxels are in $T$ and connected) and leak; truncation
restores the eroded rim (thickness $\le r$) but enters a bridge by at most $r$ voxels. This is the property tested by
the "two discs joined by a one-pixel bridge" unit tests. The same "minimum diameter" idea is used by 3D Slicer's
*Local threshold* effect, documented as preventing leaks through small regions at the cost of truncating thin
branches (`slicer_extraeffects`).

**Step 6b — edge rim (only if $\gamma>0$).** $R\leftarrow\delta_{T_I}^{(1)}(R)$ with a unit element: one step through
the intensity interval *without* the gradient condition, so that the high-gradient rim of the object, which belongs
to the object by intensity, is not cut off **[ours]**. Synthetic test (sphere 40 HU in −500 HU background): coverage
62 % without 6b, 100 % with 6b; 99.6 % with noise (**synthetic numbers** from our unit tests).

**Step 7 — fill holes in the result.** $R\leftarrow\mathcal H_A(R)$ (per slice in 3D).

**Step 8 — output.** A `MaskRegion` (ROI bounding rectangle + one byte mask per touched slice), the voxel count, and
the flag `TouchedRoiBorder` = some voxel of $R$ has a neighbour inside the image but outside $B$ ("may be leaking",
shown to the user). In 3D, if $|R|>V_{\max}$ the result is empty and a message asks the user to lower $k$.
The mask is merged into the per-slice annotation (add = OR, subtract = AND-NOT) through an undo stack; the GPU
upload is spread over frames (design document, chapter 3).

## 3. Pseudocode

```
function MagicBrush(I, G, h, c, params):
    B  ← ellipsoid(c, R / h)                                  # Step 1
    s  ← argmin G over {q : |q−c|∞ ≤ d, |I(q)−I(c)| ≤ τmax}   # Step 2 (ties → closest to c)
    S  ← {q ∈ W_w(s) : |I(q)−I(s)| ≤ τmax}
    (μ, σ) ← robust ? (median(S), 1.4826·MAD(S)) : (mean(S), std(S))
    t  ← clamp(k·σ, τmin, τmax)
    T_I ← {p ∈ B : |I(p)−μ| ≤ t}                              # Step 3
    T  ← {p ∈ T_I : γ = 0 or G(p) ≤ γ}
    s  ← nearest voxel of T within ⌊w/2⌋ of s, else return ∅
    if fillHoles: T ← FillHoles(T, A)                         # Step 3b (per slice)
    r  ← round(ρ / h)                                         # per axis
    loop:                                                     # Step 4
        E ← Erode(T, ellipsoid(r))
        if s ∈ E or r = 0: break
        r ← max(r − 1, 0)
    R  ← FloodFill(E, s, N4|N6, cap = Vmax)                   # Step 5
    R  ← GeodesicDilate(R, within = T, steps = r)             # Step 6
    if γ > 0: R ← GeodesicDilate(R, within = T_I, steps = 1)  # Step 6b
    if fillHoles: R ← FillHoles(R, A)                         # Step 7
    if |R| > Vmax: return ∅ with message                      # Step 8 (3D)
    return MaskRegion(R), touchedBorder(R, B)
```

All steps run on a background thread (`Task.Run`) and check a `CancellationToken`, so a new click or a slider change
cancels the previous run; the result is applied on the main thread **[ours]**.

## 4. Complexity

Let $n=|B|$ (voxels in the ROI), $r=\max_a r_a$, $|D_r|$ the element size.

| Step | Cost | Note |
|---|---|---|
| ROI, $T$, $T_I$ | $O(n)$ | one pass; 2D in-plane gradient is $O(1)$ per pixel |
| Seed slide, statistics | $O(d^{\,\dim})$, $O(w^{\dim}\log w)$ | negligible |
| Hole filling | $O(n)$ | border flood + component sizes |
| Erosion | 2D: $O(n\,(2r_y+1))$ with row prefix sums; 3D: $O(n\cdot\#\text{rows of }D_r)$ | ×(number of radius reductions) in the worst case, $\le r+1$ |
| Flood fill | $O(n)$ | explicit stack, no recursion |
| Geodesic dilation | $O(r\cdot n)$ worst case (frontier-based, each voxel enters once) | |
| **Total** | $O(n\,r^2)$ worst case, $O(n\,r)$ typical | memory $O(n)$ bytes (a few byte masks) |

With the defaults, $n\approx\pi(60/0.9)^2\approx1.4\cdot10^4$ pixels in 2D and $n\approx\tfrac43\pi\cdot44^2\cdot16\approx1.3\cdot10^5$
voxels inside a 40 mm ball at 0.9×0.9×2.5 mm (bounding box 89×89×33) in 3D, with $r\le2$.

**Measured (synthetic, our unit tests; not Unity/Mono, not the VR PC).** Re-run on 2026-11-04 on a 4-vCPU cloud VM
(Xeon 2.1 GHz, .NET 8 Release; 51/51 tests pass; log in `data/synthetic-timing-2026-11-04.txt`): 2D 512×512 slice,
default ROI **1.1 ms**, ROI over the full slice **13.3 ms**; 3D ROI 129×129×51 (438 519 voxels in the ellipsoid, edge
stop on) median **102–125 ms** over two runs (min 72, max 200 ms). Earlier runs recorded in the design document agree
(2D 1–4 ms; 3D 120–150 ms). These are **synthetic** volumes; they must be re-measured in Unity (Mono) on the target
PC and on real CT/MR (`08-evaluation-plan.md`).

## 5. Relation to the original methods (summary)

| Component | Original source | What we changed **[ours]** |
|---|---|---|
| Seeded growth with a homogeneity predicate | Adams & Bischof 1994 | interval fixed from the seed window, not the running region mean |
| $\mu\pm k\sigma$ interval | ITK ConfidenceConnected (`itk_confidenceconnected`) | one estimate, clamped to $[\tau_{\min},\tau_{\max}]$, "same tissue" sample filter |
| Robust spread | Rousseeuw & Croux 1993 (MAD·1.4826) | optional |
| Edge stop | edge indicator of geodesic active contours (Caselles et al. 1997) | binary threshold on $G$ + one-step rim restoration |
| Anti-leak (erode → component → dilate) | morphological reconstruction (Vincent 1993); "minimum diameter" in Slicer Local threshold | geodesic dilation truncated after $r$ steps; anisotropic element in mm; automatic radius reduction for thin structures |
| Hole filling | reconstruction from the border (Vincent 1993) | area limit in mm², applied before erosion and at the end |
| Bounded ROI | common practice (e.g. Slicer Local threshold ROI) | ball in mm + "touched border" warning |

## 6. Known limitations of the method (to state in the article)

- One global interval per click: intensity drift inside an organ (MR bias field, contrast washout) is not modelled;
  the user compensates with $k$ or several clicks.
- Thin connections wider than $2r+1$ voxels are not blocked; structures thinner than the element are segmented
  without anti-leak protection (radius falls to 0).
- The edge stop uses an un-normalised voxel-space gradient; on noisy data (low-dose CT, MR) it can stop growth too
  early, which is why it is off by default in 2D.
- No user correction other than undo / add / subtract clicks; seed-based correction (Fast GrowCut) is the planned
  next step.
- Timings so far are synthetic and from .NET 8, not from Unity on the VR PC.
