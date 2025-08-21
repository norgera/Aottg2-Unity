Shader "AOTTG/StencilWrite"
{
    SubShader
	{
		Tags { "Queue" = "Geometry-10" "RenderType" = "Opaque" }
		Pass
		{
            // Do not write to depth; only mark stencil so we don't occlude transparent eyes
            ZWrite Off
            ZTest Always
			Cull Back
			ColorMask 0
			Stencil
			{
				Ref 7
                Comp Always
				Pass Replace
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
			};

			struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
			{
				v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o;
			}

			fixed4 frag(v2f i) : SV_Target { return 0; }
			ENDCG
		}
	}
}


