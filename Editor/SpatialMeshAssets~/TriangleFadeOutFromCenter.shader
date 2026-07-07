Shader "Custom/TriangleFadeOutFromCenter"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.5)
        _SecondColor ("Second Color", Color) = (0,1,0,1)
        _ScanningColor ("Scanning Color", Color) = (1,1,1,0.5)
        _WireColor ("Wire Color", Color) = (0,0,0,1)
        _StartTime ("Start Time", Float) = 0
        _ScaleDuration ("Scale Duration", Float) = 1.0
        _MaxScale ("Max Scale", Float) = 0.98
        _WireThickness ("Wire Thickness", Float) = 0.01
        _ColorExponent ("Alpha Exponent", Range(0, 30)) = 2.0 // 控制颜色衰减曲线的形状
        _ShapeExponent ("Shape Exponent", Range(0, 1)) = .2 // 控制缩放衰减曲线的形状
        _DistanceExponent ("Distance Exponent", Range(0, 1)) = .5 // 控制缩放衰减曲线的形状
        _AlphaAreaMin ("Alpha Area Min", Range(1, 5)) = 4.0
        _AlphaAreaMax ("Alpha Area Max", Range(1, 10)) = 10.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom

            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _SecondColor;
            fixed4 _ScanningColor;
            fixed4 _WireColor;
            float _StartTime;
            float _ScaleDuration;
            float _MaxScale;
            float _WireThickness;
            float _ColorExponent;
            float _ShapeExponent;
            float _DistanceExponent;
            float3 _TargetPosition;
            float _AlphaAreaMin;
            float _AlphaAreaMax;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2g
            {
                float4 projectionSpaceVertex : SV_POSITION;
                float4 worldSpacePosition : TEXCOORD1;
                float3 barycentric : TEXCOORD2; // Barycentric coordinates
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct g2f
            {
                float4 projectionSpaceVertex : SV_POSITION;
                float4 worldSpacePosition : TEXCOORD0;
                float3 barycentric : TEXCOORD2; // Barycentric coordinates
                float delta : TEXCOORD3;
                float area : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2g vert (appdata v)
            {
                v2g o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.projectionSpaceVertex = UnityObjectToClipPos(v.vertex);
                o.worldSpacePosition = mul(unity_ObjectToWorld, v.vertex);
                return o;
            }
            float diracDelta(int a,int x) {
                return max(0, 1 - abs(x - a));
            }
            [maxvertexcount(3)]
            void geom(triangle v2g i[3], inout TriangleStream<g2f> triangleStream)
            {
                
                float a = distance(i[0].worldSpacePosition.xyz, i[1].worldSpacePosition.xyz);
                float b = distance(i[1].worldSpacePosition.xyz, i[2].worldSpacePosition.xyz);
                float c = distance(i[2].worldSpacePosition.xyz, i[0].worldSpacePosition.xyz);
                float s = a + b + c;
                float area = pow(s * (s - a) * (s - b) * (s - c),0.25);
                float3 worldCenter = (i[0].worldSpacePosition.xyz + i[1].worldSpacePosition.xyz + i[2].worldSpacePosition.xyz) / 3.0;
                float distanceToTarget = distance(worldCenter, _TargetPosition);
                float time = _Time.y - _StartTime - distanceToTarget + _DistanceExponent;
                float scale = pow(saturate(time / _ScaleDuration),_ShapeExponent) * _MaxScale;
                for (int j = 0; j < 3; ++j)
                {
                    g2f o;
                    o.area = area;
                    float3 offset = i[j].worldSpacePosition.xyz - worldCenter;
                    float3 scaledVertex = worldCenter + offset * scale;
                    o.projectionSpaceVertex = UnityObjectToClipPos(float4(scaledVertex, 1));
                    o.worldSpacePosition = float4(scaledVertex, 1);
                    o.barycentric = float3(diracDelta(0,j), diracDelta(1,j), diracDelta(2,j));
                    o.delta = time;
                    UNITY_TRANSFER_VERTEX_OUTPUT_STEREO(i[j], o);
                    triangleStream.Append(o);
                }
                triangleStream.RestartStrip();
            }

            fixed4 frag (g2f i) : SV_Target
            {
                float alphaFactor = pow(i.delta, _ColorExponent);
                alphaFactor = saturate(alphaFactor); // 确保alphaFactor在0到1之间
                fixed4 color = lerp(_Color, _SecondColor, alphaFactor);
                fixed4 black = fixed4(0,0,0,0);

                fixed4 wireframeColor = lerp(black, _WireColor, alphaFactor);
                float edgeFactor = min(min(i.barycentric.x, i.barycentric.y), i.barycentric.z);
                float edgeThickness = (_WireThickness/i.area) / min(_MaxScale, 1.0);
                float wire = smoothstep(edgeThickness - 0.01, edgeThickness, edgeFactor);

                float len = distance(i.worldSpacePosition.xyz , _TargetPosition);
                float t = frac(0.5*len-_Time.y);
                color.rgba += lerp(black,_ScanningColor, pow(t,30.0));
                float4 finalColor = lerp(wireframeColor, color, wire);
                float alpha = smoothstep(_AlphaAreaMax,_AlphaAreaMin, len);
                finalColor.a *= alpha;
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
