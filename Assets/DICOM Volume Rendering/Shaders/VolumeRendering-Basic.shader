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

    [Header(Ranges)]
    _MinX("MinX", Range(0, 1)) = 0.0
    _MaxX("MaxX", Range(0, 1)) = 1.0
    _MinY("MinY", Range(0, 1)) = 0.0
    _MaxY("MaxY", Range(0, 1)) = 1.0
    _MinZ("MinZ", Range(0, 1)) = 0.0
    _MaxZ("MaxZ", Range(0, 1)) = 1.0
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
inline float2 sampleVolume(float3 pos)
{
    float x = step(pos.x, _MaxX) * step(_MinX, pos.x);
    float y = step(pos.y, _MaxY) * step(_MinY, pos.y);
    float z = step(pos.z, _MaxZ) * step(_MinZ, pos.z);
    float mask = x * y * z;
    float4 s = tex3D(_Volume, pos);
    return float2(s.r * mask, s.g * mask);
}

inline float4 transferFunction(float density, float gradMag)
{
    return tex2D(_Transfer, float2(density, gradMag));
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

    [loop]
    for (int i = 0; i < n; ++i)
    {
        float3 sp = localPos + 0.5;
        float2 vg = sampleVolume(sp);           // .x = gustoća, .y = gradMag (pre-computed)
        float volume  = vg.x;
        float gradMag = saturate(vg.y * _GradScale);
        float4 color  = transferFunction(volume, gradMag) * volume * _Intensity;
        output += (1.0 - output.a) * color;
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