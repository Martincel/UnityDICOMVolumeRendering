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
    _HighlightColor("Highlight Color", Color) = (1, 0, 0, 0.85)

    [Header(Ranges)]
    _MinX("MinX", Range(0, 1)) = 0.0
    _MaxX("MaxX", Range(0, 1)) = 1.0
    _MinY("MinY", Range(0, 1)) = 0.0
    _MaxY("MaxY", Range(0, 1)) = 1.0
    _MinZ("MinZ", Range(0, 1)) = 0.0
    _MaxZ("MaxZ", Range(0, 1)) = 1.0
    _ClipMask("Clip Mask", Float) = 0
    _MaskOnly("Mask Only", Float) = 0

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
float _ClipMask; // 1 = oznaka se reže zajedno s volumenom, 0 = uvijek vidljiva
float _MaskOnly; // 0 = normalan prikaz, 1 = samo označeno (gustoća tkiva), 2 = samo označeno (puna boja)
float _ShowSlicePlane;     // 1 = na ravnini slicea prikaži oštru sivu sliku (za crtanje u 3D)
float _SlicePlaneZ;        // z ravnine u teksturnom prostoru, [0,1] (slice i = i/(D-1))
float _SlicePlaneOpacity;

// Anotacija (Zadatak 2): zaseban Texture2DArray, jedan element po Z-sliceu — NE dijeli
// kanale s _Volume (vidi AnnotationMaskManager.cs za obrazloženje). _MaskDepth = broj
// elemenata (postavlja AnnotationMaskManager nakon učitavanja); 0 dok maska ne postoji.
UNITY_DECLARE_TEX2DARRAY(_Mask);
float _MaskDepth;
fixed4 _HighlightColor;

struct Ray
{
    float3 from;
    float3 dir;
    float tmax;
};

void intersection(inout Ray ray)
{
    float3 invDir = 1.0 / ray.dir;
    float3 t1 = (-0.5 - ray.from) * invDir;
    float3 t2 = (+0.5 - ray.from) * invDir;
    float3 tmax3 = max(t1, t2);
    float2 tmax2 = min(tmax3.xx, tmax3.yz);
    ray.tmax = min(tmax2.x, tmax2.y);
}

// Jedno uzorkovanje vraća i gustoću (.r) i pre-computed gradMag (.g).
// Converter bake-a gradijent u G kanal pri konverziji → nema runtime centralnih razlika.
inline float inClipBox(float3 pos)
{
    float x = step(pos.x, _MaxX) * step(_MinX, pos.x);
    float y = step(pos.y, _MaxY) * step(_MinY, pos.y);
    float z = step(pos.z, _MaxZ) * step(_MinZ, pos.z);
    return x * y * z;
}

inline float2 sampleVolumeRaw(float3 pos)
{
    float4 s = tex3D(_Volume, pos);
    return float2(s.r, s.g);
}

inline float2 sampleVolume(float3 pos)
{
    return sampleVolumeRaw(pos) * inClipBox(pos);
}

inline float4 transferFunction(float density, float gradMag)
{
    return tex2D(_Transfer, float2(density, gradMag));
}

// Vraća 1 ako je pos unutar markirane regije, inače 0. pos.z je [0,1] (isti UVW prostor
// kao sampleVolume) — svaki element Texture2DArray-a je jedna Z-ravnina volumena, pa
// nema interpolacije između elemenata (namjerno, vidi plan: diskretno po sliceu je
// poželjno za binarnu masku, ne mana).
inline float sampleMask(float3 pos)
{
    if (_MaskDepth <= 0)
        return 0;

    float sliceIndex = floor(saturate(pos.z) * (_MaskDepth - 1) + 0.5);
    return UNITY_SAMPLE_TEX2DARRAY(_Mask, float3(pos.xy, sliceIndex)).r;
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
    float3 worldDir = i.worldPos - _WorldSpaceCameraPos;
    float3 localDir = normalize(mul(unity_WorldToObject, worldDir));

    Ray ray;
    ray.from = i.localPos;
    ray.dir = localDir;
    intersection(ray);

    int n = _Iteration * ray.tmax / sqrt(3);
    float3 localStep = localDir * ray.tmax / n;

    // Jitter: pomakni početak zrake za nasumičnu vrijednost iz noise teksture.
    // Svaki piksel na ekranu uzorkuje drugačiji noise piksel → susjedne zrake
    // počinju na blago različitim pozicijama → banding artefakti se rasipaju.
    float jitter = tex2D(_NoiseTex, i.vertex.xy / 64.0).r;
    float3 localPos = i.localPos + localDir * localStep * jitter;
    float4 output = 0;

    // Oštra ravnina slicea: udaljenost (duž zrake od ulaza u kocku) do ravnine z = _SlicePlaneZ.
    // Računa se prije petlje jer `i` u petlji zasjenjuje v2f. Ako zraka ne siječe ravninu, tPlane
    // ostaje vrlo velik i ravnina se ne crta.
    float3 startPos = i.localPos;
    float tPlane = 1e9;
    if (_ShowSlicePlane > 0.5 && abs(localDir.z) > 1e-5)
        tPlane = (_SlicePlaneZ - 0.5 - startPos.z) / localDir.z;
    float tCur = 0;
    float dt = ray.tmax / n;

    [loop]
    for (int i = 0; i < n; ++i)
    {
        float3 sp = localPos + 0.5;
        // "Samo oznaka" uz _ClipMask = 0: oznaka je vidljiva i izvan reza, pa tada gustoća
        // ne smije biti odrezana (inače bi u tom dijelu uzorkovala nulu).
        bool maskOnly = _MaskOnly > 0.5;
        float2 vg = (maskOnly && _ClipMask < 0.5) ? sampleVolumeRaw(sp) : sampleVolume(sp);  // .x = gustoća, .y = gradMag (pre-computed)
        float volume  = vg.x;
        float gradMag = saturate(vg.y * _GradScale);
        float4 color  = transferFunction(volume, gradMag) * volume * _Intensity;

        // Markirana regija "probija" kroz normalnu transfer funkciju — vidljiva čak i
        // preko gustoće/tkiva koje bi inače bilo skoro prozirno. Uz _ClipMask = 1 poštuje
        // isti rez kao volumen (SliceClipController), inače ostaje vidljiva cijela.
        bool inMask = sampleMask(sp) > 0.5 && (_ClipMask < 0.5 || inClipBox(sp) > 0.5);

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
        else if (inMask)
        {
            color.rgb = _HighlightColor.rgb;
            color.a   = max(color.a, _HighlightColor.a);
        }

        output += (1.0 - output.a) * color;

        // Ravnina slicea: kad zraka u ovom koraku prijeđe ravninu, složi njezinu boju (siva
        // gustoća kao u SliceExtract.shader, s maskom preko) — daje oštru sliku na rezu.
        if (tPlane >= tCur && tPlane < tCur + dt)
        {
            float3 pp = startPos + localDir * tPlane + 0.5;
            float inside = step(_MinX, pp.x) * step(pp.x, _MaxX) * step(_MinY, pp.y) * step(pp.y, _MaxY);
            float d = tex3D(_Volume, float3(pp.xy, _SlicePlaneZ)).r;
            float4 pc = float4(d, d, d, 1);
            if (_MaskDepth > 0)
            {
                float sliceIndex = floor(saturate(_SlicePlaneZ) * (_MaskDepth - 1) + 0.5);
                float marked = UNITY_SAMPLE_TEX2DARRAY(_Mask, float3(pp.xy, sliceIndex)).r;
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

        if (output.a >= 0.95) break;
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
    Cull Back
    ZWrite Off
    ZTest LEqual
    Blend [_BlendSrc] [_BlendDst]
    Lighting Off

    CGPROGRAM
    #pragma vertex vert
    #pragma fragment frag
    ENDCG
}

}

}