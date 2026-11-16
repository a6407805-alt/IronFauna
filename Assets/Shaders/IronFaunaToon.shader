// IronFauna two-band toon shader with inverted-hull ink outlines (URP).
// - Lit band / shade band split on N.L and main-light shadows (art bible: two-band toon lighting).
// - Outline pass extrudes along smoothed normals that ClamshellMeshFactory stores in the mesh tangents
//   (tangent.w == 0 marks them); other meshes fall back to their regular normals.
Shader "IronFauna/Toon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.78, 0.16, 0.18, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.42
        _RampThreshold ("Ramp Threshold", Range(0, 1)) = 0.52
        _RampSmooth ("Ramp Smoothness", Range(0.001, 0.2)) = 0.03
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.18
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.3
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _OutlineColor ("Outline Color", Color) = (0.118, 0.133, 0.161, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 8)) = 2.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EmissionColor;
            float4 _OutlineColor;
            float _ShadowStrength;
            float _RampThreshold;
            float _RampSmooth;
            float _RimStrength;
            float _AmbientStrength;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndl = dot(n, mainLight.direction) * 0.5 + 0.5;
                float band = smoothstep(_RampThreshold - _RampSmooth, _RampThreshold + _RampSmooth, ndl);
                float shadowBand = smoothstep(0.45, 0.55, mainLight.shadowAttenuation);
                band = min(band, shadowBand);

                float3 albedo = _BaseColor.rgb;
                float3 shade = albedo * (1.0 - _ShadowStrength);
                float3 lightCol = min(mainLight.color, float3(1.0, 1.0, 1.0));
                float3 color = lerp(shade, albedo, band) * lightCol;

                color += SampleSH(n) * albedo * _AmbientStrength;

                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 4.0) * _RimStrength * band;
                color += rim * lightCol;

                // Keep lit (non-emissive) surfaces under the bloom threshold so pale paint schemes don't glow;
                // only emissive parts (joint cores, visors, hard-light) should bloom.
                color = min(color, float3(0.9, 0.9, 0.9));
                color += _EmissionColor.rgb;
                color = MixFog(color, i.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "InkOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                // Smoothed normals are stored in the tangent with w == 0; otherwise use the mesh normal.
                float3 extrudeOS = abs(input.tangentOS.w) < 0.5 && dot(input.tangentOS.xyz, input.tangentOS.xyz) > 0.01
                    ? input.tangentOS.xyz
                    : input.normalOS;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(extrudeOS);
                float3 nCS = TransformWorldToHClipDir(nWS);
                float2 dir = nCS.xy;
                float len = max(length(dir), 1e-5);
                float2 offset = (dir / len) * (_OutlineWidth * 2.0 / _ScreenParams.xy) * pos.positionCS.w;
                o.positionCS = pos.positionCS;
                o.positionCS.xy += offset;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half Frag(Varyings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
