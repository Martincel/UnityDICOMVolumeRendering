Shader "VolumeRendering/SliceExtract"
{
    // Blit shader: uzima jedan Z-presjek iz postojeće _Volume Texture3D i renderira ga
    // kao grayscale 2D sliku preko Graphics.Blit(null, renderTexture, materijal).
    // Bez CPU čuvanja podataka — čita se izravno GPU-side ista tekstura koju koristi
    // VolumeRendering-Basic.shader za 3D prikaz (ista R=gustoća konvencija).
    Properties
    {
        _Volume("Volume", 3D) = "" {}
        _SliceZ("Slice Z", Range(0, 1)) = 0.5
        [NoScaleOffset] _Mask("Mask (Texture2DArray)", 2DArray) = "" {}
        _SliceIndex("Slice Index", Int) = -1
        _HighlightColor("Highlight Color", Color) = (1, 0, 0, 0.85)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler3D _Volume;
            float _SliceZ;

            // Ista anotacijska maska kao u VolumeRendering-Basic.shader (vidi AnnotationMaskManager.cs),
            // uzorkovana na TOČNOM cjelobrojnom sliceu koji se prikazuje (ne interpolirano kao u
            // raymarchu) — daje korisniku izravnu vizualnu potvrdu gdje je kist ostavio trag,
            // umjesto da jedini feedback bude teško uočljiv highlight u 3D prikazu.
            UNITY_DECLARE_TEX2DARRAY(_Mask);
            float _SliceIndex;
            fixed4 _HighlightColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float density = tex3D(_Volume, float3(i.uv.x, i.uv.y, _SliceZ)).r;
                float4 color = float4(density, density, density, 1);

                if (_SliceIndex >= 0)
                {
                    float marked = UNITY_SAMPLE_TEX2DARRAY(_Mask, float3(i.uv.xy, _SliceIndex)).r;
                    if (marked > 0.5)
                        color.rgb = lerp(color.rgb, _HighlightColor.rgb, _HighlightColor.a);
                }

                return color;
            }
            ENDCG
        }
    }
}
