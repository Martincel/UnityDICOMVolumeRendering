Shader "VolumeRendering/Basic"
{

Properties
{
    [Header(Rendering)]
    _Volume("Volume", 3D) = "" {}
    _Transfer("Transfer", 2D) = "" {}
    _Iteration("Iteration", Int) = 10
    _Intensity("Intensity", Range(0.0, 10.0)) = 1.0
    _GradScale("Gradient Scale", Range(1.0, 20.0)) = 5.0
    [Enum(UnityEngine.Rendering.BlendMode)] _BlendSrc ("Blend Src", Float) = 5
    [Enum(UnityEngine.Rendering.BlendMode)] _BlendDst ("Blend Dst", Float) = 10

    _NoiseTex("Noise (Generated)", 2D) = "white" {}

    [Header(Annotation)]
    [NoScaleOffset] _Mask("Mask (Texture2DArray)", 2DArray) = "" {}
    _MaskDepth("Mask Depth", Int) = 0
    _MaskZRange("Mask Z Range", Vector) = (1, 0, 0, 0)
    _HighlightColor("Highlight Color", Color) = (1, 0, 0, 0.85)
    _HighlightOccluded("Highlight Occluded", Float) = 0

    [Header(Ranges)]
    _MinX("MinX", Range(0, 1)) = 0.0
    _MaxX("MaxX", Range(0, 1)) = 1.0
    _MinY("MinY", Range(0, 1)) = 0.0
    _MaxY("MaxY", Range(0, 1)) = 1.0
    _MinZ("MinZ", Range(0, 1)) = 0.0
    _MaxZ("MaxZ", Range(0, 1)) = 1.0
    // Kosa ravnina reza u lokalnom prostoru volumena (ClipPlaneController): vidi se strana gdje je
    // dot(xyz, p) + w >= 0, p u rasponu [-0.5, 0.5] po osi.
    _ClipPlane("Clip Plane (local)", Vector) = (0, 0, 0, 0)
    _ClipPlaneOn("Clip Plane On", Float) = 0
    _ClipMask("Clip Mask", Float) = 0
    _MaskOnly("Mask Only", Float) = 0

    [Header(Display Mode)]
    // Dimenzije teksture volumena (w, h, d) za korak gradijenta; postavlja DicomVolumeLoader / VolumeRenderingWithTransferFunction.
    _VolumeDims("Volume Dims", Vector) = (1, 1, 1, 0)
    _IsoValue("Iso Value (density)", Range(0, 1)) = 0.35
    _MipWindow("MIP Window (low, high)", Vector) = (0.2, 0.625, 0, 0)
    // x = donja, y = gornja gustoća, z = 1 uključeno; postavlja VolumeRenderingWithTransferFunction iz HU.
    _HuBand("HU Band (low, high, on)", Vector) = (0, 1, 0, 0)

    [Header(Slice Plane)]
    _ShowSlicePlane("Show Slice Plane", Float) = 0
    _SlicePlaneZ("Slice Plane Z", Range(0, 1)) = 0
    _SlicePlaneOpacity("Slice Plane Opacity", Range(0, 1)) = 1
}

CGINCLUDE

#include "UnityCG.cginc"

struct appdata
{
    float4 vertex : POSITION;
};

struct v2f
{
    float4 vertex   : SV_POSITION;
    float4 localPos : TEXCOORD0;
    float4 worldPos : TEXCOORD1;
};

sampler3D _Volume;
sampler2D _Transfer;
sampler2D _NoiseTex;
int _Iteration;
float _Intensity;
float _GradScale;
float _MinX, _MaxX, _MinY, _MaxY, _MinZ, _MaxZ;
float4 _ClipPlane;
float _ClipPlaneOn;
float _ClipMask; // 1 = oznaka se reže zajedno s volumenom, 0 = uvijek vidljiva
float _MaskOnly; // 0 = normalan prikaz, 1 = samo označeno (gustoća tkiva), 2 = samo označeno (puna boja)
float _HighlightOccluded;  // 0 (zadano) = oznaka se vidi kroz tkivo ("probija"); 1 = zaklanja je tkivo ispred
float _ShowSlicePlane;     // 1 = na ravnini slicea prikaži oštru sivu sliku (za crtanje u 3D)
float _SlicePlaneZ;        // z ravnine u teksturnom prostoru, [0,1] (središte slicea i = (i + 0.5)/D)
float _SlicePlaneOpacity;
float4 _VolumeDims;   // xyz = broj voksela po osi
float _IsoValue;      // prag izopovršine u prostoru gustoće [0,1]
float4 _MipWindow;    // x = donja, y = gornja gustoća prozora sive skale za MIP
// HU pojas: kad je uključen (z > 0.5), prikazuje se samo gustoća u [x, y]. U slaganju se ostali uzorci
// preskaču, MIP traži najveću gustoću unutar pojasa (i sivu skalu razvlači preko pojasa), a izopovršina
// je prva ploha gdje zraka uđe u pojas. Oznaka (maska) i ravnina slicea ga ne poštuju.
float4 _HuBand;

// Način prikaza je odabran ključnim riječima (VolumeRenderingWithTransferFunction.RenderStyle):
// nijedna = slaganje (composite), MODE_MIP = najveća gustoća duž zrake, MODE_ISO = prva izopovršina.
// LIGHTING_ON = headlight osvjetljenje (izopovršina je uvijek osvijetljena).
#if defined(MODE_MIP)
    static const int STYLE = 1;
#elif defined(MODE_ISO)
    static const int STYLE = 2;
#else
    static const int STYLE = 0;
#endif

// Anotacija (Zadatak 2): zaseban Texture2DArray, jedan element po Z-sliceu — NE dijeli
// kanale s _Volume (vidi AnnotationMaskManager.cs za obrazloženje). _MaskDepth = broj
// elemenata (postavlja AnnotationMaskManager nakon učitavanja); 0 dok maska ne postoji.
UNITY_DECLARE_TEX2DARRAY(_Mask);
float _MaskDepth;
// Raspon z (teksturni prostor) sliceova koji imaju oznaku: x = donji rub najnižeg, y = gornji rub
// najvišeg. Postavlja AnnotationMaskManager; x >= y znači da oznaka nema (zadano).
float4 _MaskZRange;
fixed4 _HighlightColor;

// Presjek zrake (from + t * dir) s osno poravnatom kutijom [lo, hi]: x = ulaz, y = izlaz (parametar t).
// Ulaz je negativan kad je ishodište zrake unutar kutije; izlaz <= ulaz znači da zraka promašuje kutiju.
float2 boxRange(float3 from, float3 dir, float3 lo, float3 hi)
{
    float3 invDir = 1.0 / dir;
    float3 t1 = (lo - from) * invDir;
    float3 t2 = (hi - from) * invDir;
    float3 tNear3 = min(t1, t2);
    float3 tFar3 = max(t1, t2);
    return float2(max(tNear3.x, max(tNear3.y, tNear3.z)),
                  min(tFar3.x, min(tFar3.y, tFar3.z)));
}

// Jedno uzorkovanje vraća i gustoću (.r) i pre-computed gradMag (.g).
// Converter bake-a gradijent u G kanal pri konverziji → nema runtime centralnih razlika.
// Strana kose ravnine koja ostaje vidljiva: 1 = zadržano, 0 = odrezano. pos je u teksturnom prostoru [0,1].
inline float inClipPlane(float3 pos)
{
    return _ClipPlaneOn > 0.5 ? step(0.0, dot(_ClipPlane.xyz, pos - 0.5) + _ClipPlane.w) : 1.0;
}

inline float inClipBox(float3 pos)
{
    float x = step(pos.x, _MaxX) * step(_MinX, pos.x);
    float y = step(pos.y, _MaxY) * step(_MinY, pos.y);
    float z = step(pos.z, _MaxZ) * step(_MinZ, pos.z);
    return x * y * z * inClipPlane(pos);
}

// Uzorkovanje u petlji ide s eksplicitnim LOD-om 0: derivacije su nedefinirane u petlji s promjenjivim
// brojem koraka, a teksture (volumen, TF, maska) ionako nemaju mipmape.
inline float2 sampleVolumeRaw(float3 pos)
{
    float4 s = tex3Dlod(_Volume, float4(pos, 0));
    return float2(s.r, s.g);
}

inline float2 sampleVolume(float3 pos)
{
    return sampleVolumeRaw(pos) * inClipBox(pos);
}

inline float4 transferFunction(float density, float gradMag)
{
    return tex2Dlod(_Transfer, float4(density, gradMag, 0, 0));
}

// Indeks slicea maske za z u teksturnom prostoru. Kao u Texture3D, slice i pokriva
// [i/D, (i+1)/D] i središte mu je na (i + 0.5)/D, pa maska i volumen gledaju isti slice.
inline float maskSliceIndex(float z)
{
    return min(floor(saturate(z) * _MaskDepth), _MaskDepth - 1);
}

// Vraća 1 ako je pos unutar markirane regije, inače 0. pos.z je [0,1] (isti UVW prostor
// kao sampleVolume) — svaki element Texture2DArray-a je jedna Z-ravnina volumena, pa
// nema interpolacije između elemenata (namjerno, vidi plan: diskretno po sliceu je
// poželjno za binarnu masku, ne mana).
inline float sampleMask(float3 pos)
{
    if (_MaskDepth <= 0)
        return 0;

    return UNITY_SAMPLE_TEX2DARRAY_LOD(_Mask, float3(pos.xy, maskSliceIndex(pos.z)), 0).r;
}

// Gradijent gustoće (centralne razlike nad .r, 6 uzoraka) u lokalnom prostoru volumena. Razlika je
// podijeljena razmakom uzoraka po osi, jer voksel nije kubičan (različit broj voksela po osi).
float3 densityGradient(float3 sp)
{
    float3 e = 1.0 / _VolumeDims.xyz;
    float dx = tex3Dlod(_Volume, float4(sp + float3(e.x, 0, 0), 0)).r - tex3Dlod(_Volume, float4(sp - float3(e.x, 0, 0), 0)).r;
    float dy = tex3Dlod(_Volume, float4(sp + float3(0, e.y, 0), 0)).r - tex3Dlod(_Volume, float4(sp - float3(0, e.y, 0), 0)).r;
    float dz = tex3Dlod(_Volume, float4(sp + float3(0, 0, e.z), 0)).r - tex3Dlod(_Volume, float4(sp - float3(0, 0, e.z), 0)).r;
    return float3(dx, dy, dz) / (2.0 * e);
}

// Faktor osvjetljenja (headlight): ambijentalni pod 0.5 da volumen ne potamni, difuzno po |N·V|
// (dvostrano, jer je unutrašnjost tkiva iz svake strane "površina") i blagi Blinn-Phong odsjaj.
// Gradijent je kovektor: u svijet ide kroz unutarnju transponiranu matricu (točno i uz neuniformnu skalu).
// viewDirWorld = jedinični vektor od uzorka prema kameri.
float lightingFactor(float3 sp, float3 viewDirWorld)
{
    float3 nWorld = mul(densityGradient(sp), (float3x3)unity_WorldToObject);
    float len = length(nWorld);
    if (len < 1e-6)
        return 1.0;

    float ndv = abs(dot(nWorld / len, viewDirWorld));
    return 0.5 + 0.5 * ndv + 0.25 * pow(ndv, 24.0);
}

v2f vert(appdata v)
{
    v2f o;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.localPos = v.vertex;
    o.worldPos = mul(unity_ObjectToWorld, v.vertex);
    return o;
}

float4 frag(v2f i) : SV_Target
{
    // Zraka počinje u kameri (u lokalnom prostoru volumena), a ne na prednjoj plohi kocke. Crta se
    // stražnja ploha (Cull Front), pa volumen ostaje vidljiv i kad je kamera ili glava unutar kocke.
    // Svi t-ovi niže mjere se od kamere.
    float3 camLocal = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
    float3 localDir = normalize(i.localPos.xyz - camLocal);

    // MIP i izopovršina ne rade uz ravninu slicea (crtanje u 3D) ni uz "Samo oznaka": tada slaganje.
    bool mip = STYLE == 1 && _ShowSlicePlane < 0.5 && _MaskOnly < 0.5;
    bool iso = STYLE == 2 && _ShowSlicePlane < 0.5 && _MaskOnly < 0.5;
    bool bandOn = _HuBand.z > 0.5;
    // Uz HU pojas izopovršina je donja granica pojasa (prag izopovršine se tada ne koristi).
    float isoValue = bandOn ? _HuBand.x : _IsoValue;
    #if defined(LIGHTING_ON) || defined(MODE_ISO)
    float3 viewDirWorld = -normalize(mul((float3x3)unity_ObjectToWorld, localDir));
    #endif

    float2 cube = boxRange(camLocal, localDir, -0.5, 0.5);
    float tStart = max(cube.x, 0.0);   // kamera unutar kocke: početak je u kameri
    float tEnd = cube.y;
    if (tEnd <= tStart)
        return 0;

    // Zraka se skraćuje na dio unutar kutije reza (_MinX.._MaxZ): inače tanki sloj (npr. 9 sliceova,
    // ~2 % visine) pogodi 0 ili 1 uzorak kad zraka prolazi okomito na njega, pa je odozgo gotovo
    // nevidljiv. Ne skraćuje se ako je vidljivo nešto izvan reza (oznaka uz _ClipMask = 0) ili ako je
    // aktivna ravnina slicea (ona računa udaljenost od kamere).
    bool trim = _ShowSlicePlane < 0.5 && (_ClipMask > 0.5 || _MaskZRange.x >= _MaskZRange.y);
    if (trim)
    {
        float2 clip = boxRange(camLocal, localDir,
            float3(_MinX, _MinY, _MinZ) - 0.5, float3(_MaxX, _MaxY, _MaxZ) - 0.5);
        tStart = max(tStart, clip.x);
        tEnd = min(tEnd, clip.y);
        if (tEnd <= tStart)
            return 0;

        // Kosa ravnina sužava zraku na dio u zadržanoj poluravnini: f(t) = a * t + b >= 0.
        // Bez ovoga bi tanki sloj uz kosu ravninu pogodio premalo uzoraka.
        if (_ClipPlaneOn > 0.5)
        {
            float a = dot(_ClipPlane.xyz, localDir);
            float b = dot(_ClipPlane.xyz, camLocal) + _ClipPlane.w;
            if (abs(a) < 1e-6)
            {
                if (b < 0)
                    return 0;
            }
            else
            {
                float tPlaneCut = -b / a;
                if (a > 0)
                    tStart = max(tStart, tPlaneCut);
                else
                    tEnd = min(tEnd, tPlaneCut);
            }
            if (tEnd <= tStart)
                return 0;
        }
    }

    float segLen = tEnd - tStart;
    int n = _Iteration * segLen / sqrt(3);
    // Skraćena zraka dobiva barem nekoliko uzoraka da tanki sloj ostane vidljiv.
    n = max(n, trim ? 6 : 1);
    float3 localStep = localDir * segLen / n;

    // Jitter: pomakni početak zrake za nasumičnu vrijednost iz noise teksture.
    // Svaki piksel na ekranu uzorkuje drugačiji noise piksel → susjedne zrake
    // počinju na blago različitim pozicijama → banding artefakti se rasipaju.
    // Pomak je dio jednog koraka duž zrake (localStep već sadrži smjer).
    float jitter = tex2D(_NoiseTex, i.vertex.xy / 64.0).r;
    float3 localPos = camLocal + localDir * tStart + localStep * jitter;
    float4 output = 0;

    // Oštra ravnina slicea: udaljenost (duž zrake od kamere) do ravnine z = _SlicePlaneZ.
    // Računa se prije petlje jer `i` u petlji zasjenjuje v2f. Ako zraka ne siječe ravninu, tPlane
    // ostaje vrlo velik i ravnina se ne crta.
    float3 startPos = camLocal;
    float tPlane = 1e9;
    if (_ShowSlicePlane > 0.5 && abs(localDir.z) > 1e-5)
        tPlane = (_SlicePlaneZ - 0.5 - startPos.z) / localDir.z;
    float tCur = tStart;
    float dt = segLen / n;

    // Oznaka "kroz tkivo": zraka ne staje na neprozirnom tkivu, nego do kraja prikuplja
    // oznaku zasebno (maskA) i na kraju je slaže preko slike. Isključeno kad je ravnina slicea
    // aktivna (crtanje u 3D) i u načinu "Samo oznaka", gdje oznaka ionako nije zaklonjena.
    float maskA = 0;
    // Bez ijedne oznake zraka staje na neprozirnom tkivu kao inače (early ray termination).
    // U MIP i izopovršini oznaka se uvijek prikuplja zasebno, da ostane vidljiva u svim načinima.
    bool through = (_HighlightOccluded < 0.5 || mip || iso) && _ShowSlicePlane < 0.5 && _MaskOnly < 0.5 && _MaskDepth > 0
                && _MaskZRange.x < _MaskZRange.y;

    float mipMax = 0;
    float3 prevSp = localPos + 0.5;   // prethodni uzorak (za bisekciju izopovršine)
    float prevVol = 0;

    [loop]
    for (int i = 0; i < n; ++i)
    {
        float3 sp = localPos + 0.5;
        // Dok je ravnina slicea aktivna (crtanje u 3D), oznaka se uvijek reže zajedno s volumenom:
        // inače bi oznake iz prethodnih sliceova, koje su ispred ravnine u odrezanom dijelu,
        // prekrile ravninu pa bi izgledalo da su "svi idući" sliceovi označeni.
        bool maskFree = _ClipMask < 0.5 && _ShowSlicePlane < 0.5;
        bool inMask = sampleMask(sp) > 0.5 && (maskFree || inClipBox(sp) > 0.5);
        if (through && inMask)
            maskA += (1.0 - maskA) * _HighlightColor.a;

        // Kad je zraka već neprozirna, a oznaka se prati zasebno, preskoči uzorkovanje volumena.
        if (!(through && output.a >= 0.95))
        {
        // "Samo oznaka" uz _ClipMask = 0: oznaka je vidljiva i izvan reza, pa tada gustoća
        // ne smije biti odrezana (inače bi u tom dijelu uzorkovala nulu).
        bool maskOnly = _MaskOnly > 0.5;
        float2 vg = (maskOnly && _ClipMask < 0.5) ? sampleVolumeRaw(sp) : sampleVolume(sp);  // .x = gustoća, .y = gradMag (pre-computed)
        float volume  = vg.x;
        float gradMag = saturate(vg.y * _GradScale);
        bool outBand  = bandOn && (volume < _HuBand.x || volume > _HuBand.y);
        float4 color  = outBand ? 0 : transferFunction(volume, gradMag) * volume * _Intensity;

        #if defined(LIGHTING_ON)
        // Osvjetljenje samo za vidljive uzorke (alpha > ~0.004): 6 dodatnih uzoraka nije jeftino.
        // MIP ne slaže uzorke, a izopovršina osvjetljava tek pogodak.
        if (!mip && !iso && color.a > 0.004)
            color.rgb *= lightingFactor(sp, viewDirWorld);
        #endif

        if (mip)
        {
            // MIP: samo pamtimo najveću gustoću, slika se slaže nakon petlje.
            if (!outBand)
                mipMax = max(mipMax, volume);
            color = 0;
        }
        #if defined(MODE_ISO)
        else if (iso && volume >= isoValue && !outBand)
        {
            // Prvi uzorak iznad praga + jedan korak bisekcije između njega i prethodnog uzorka.
            float3 hitSp = sp;
            float hitVol = volume;
            if (i > 0 && prevVol < isoValue)
            {
                float3 midSp = 0.5 * (prevSp + sp);
                float midVol = sampleVolume(midSp).x;
                if (midVol >= isoValue)
                {
                    hitSp = midSp;
                    hitVol = midVol;
                }
            }

            float hitGrad = saturate(sampleVolumeRaw(hitSp).y * _GradScale);
            color.rgb = transferFunction(hitVol, hitGrad).rgb * _Intensity * lightingFactor(hitSp, viewDirWorld);
            color.a = 1.0;
        }
        else if (iso)
        {
            color = 0;
        }
        #endif

        // Markirana regija "probija" kroz normalnu transfer funkciju — vidljiva čak i
        // preko gustoće/tkiva koje bi inače bilo skoro prozirno. Uz _ClipMask = 1 poštuje
        // isti rez kao volumen (SliceClipController), inače ostaje vidljiva cijela.
        if (maskOnly)
        {
            // Prikaz samo označenog dijela: sve izvan maske je prozirno. Način 1 zadržava
            // gustoću tkiva unutar oznake (vidi se struktura), način 2 je puna boja
            // zasjenjena gradijentom da se oblik ne sliježe u mrlju.
            if (!inMask)
                color = 0;
            else if (_MaskOnly > 1.5)
            {
                color.rgb = _HighlightColor.rgb * (0.55 + 0.45 * gradMag);
                color.a   = _HighlightColor.a;
            }
        }
        else if (inMask && !through)
        {
            color.rgb = _HighlightColor.rgb;
            color.a   = max(color.a, _HighlightColor.a);
        }

        output += (1.0 - output.a) * color;
        prevSp = sp;
        prevVol = volume;
        }

        // Ravnina slicea: kad zraka u ovom koraku prijeđe ravninu, složi njezinu boju (siva
        // gustoća kao u SliceExtract.shader, s maskom preko) — daje oštru sliku na rezu.
        if (tPlane >= tCur && tPlane < tCur + dt)
        {
            float3 pp = startPos + localDir * tPlane + 0.5;
            float inside = step(_MinX, pp.x) * step(pp.x, _MaxX) * step(_MinY, pp.y) * step(pp.y, _MaxY) * inClipPlane(pp);
            float d = tex3Dlod(_Volume, float4(pp.xy, _SlicePlaneZ, 0)).r;
            float4 pc = float4(d, d, d, 1);
            if (_MaskDepth > 0)
            {
                float marked = UNITY_SAMPLE_TEX2DARRAY_LOD(_Mask, float3(pp.xy, maskSliceIndex(_SlicePlaneZ)), 0).r;
                if (marked > 0.5)
                    pc.rgb = lerp(pc.rgb, _HighlightColor.rgb, _HighlightColor.a);
            }
            pc.a = _SlicePlaneOpacity * inside;
            // Ravnina je presudna: zamjenjuje ono što je do nje nakupljeno (inače bi rijetki uzorci
            // ispred nje utjecali na oštru sliku), a volumen iza nje je zaklonjen.
            output = lerp(output, pc, pc.a);
        }

        tCur += dt;
        localPos += localStep;

        if (output.a >= 0.95)
        {
            if (!through) break;

            // Neprozirna zraka dalje prikuplja samo oznaku. Stani kad je oznaka već zasićena ili
            // kad zraka više ne može doći do sliceova s oznakama (izašla je iz raspona i ide dalje od njega).
            float zNext = localPos.z + 0.5;
            bool pastMarks = (localDir.z >= 0 && zNext > _MaskZRange.y) || (localDir.z <= 0 && zNext < _MaskZRange.x);
            if (maskA >= 0.99 || pastMarks) break;
        }
    }

    if (mip)
    {
        float2 window = bandOn ? _HuBand.xy : _MipWindow.xy;
        float g = saturate((mipMax - window.x) / max(window.y - window.x, 1e-4));
        output = float4(g, g, g, saturate(g * 4.0));
    }

    // Oznaka koja "probija" kroz tkivo: slaže se preko slike s jačinom 0.75, da se tkivo ispod
    // još naslućuje (struktura oko oznake ostaje vidljiva).
    if (through && maskA > 0)
    {
        float k = maskA * 0.75;
        output.rgb = lerp(output.rgb, _HighlightColor.rgb, k);
        output.a   = max(output.a, k);
    }

    return output;
}

ENDCG

SubShader
{

Tags 
{ 
    "Queue" = "Transparent"
    "RenderType" = "Transparent" 
}

Pass
{
    Cull Front
    ZWrite Off
    ZTest LEqual
    Blend [_BlendSrc] [_BlendDst]
    Lighting Off

    CGPROGRAM
    #pragma vertex vert
    #pragma fragment frag
    #pragma multi_compile_local _ MODE_MIP MODE_ISO
    #pragma multi_compile_local _ LIGHTING_ON
    ENDCG
}

}

}