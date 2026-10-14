#version 450

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec3 vWorldPos;
layout(location = 3) in vec3 vNormal;
layout(location = 4) in vec3 vPosLocal;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;       // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST;  // @default = vec4(0.0, 0.0, 1.0, 1.0)
    vec4 _CloudParams; // @default = vec4(0.35, 0.32, 0.08, 0.38)
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

void main()
{
    vec3 d = normalize(vPosLocal);
    float denom = max(0.0, d.y) + uMaterial._CloudParams.y;
    vec2 projUv = (d.xz / denom) * uMaterial._CloudParams.x;
    vec2 uv = projUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy;

    vec4 tex = texture(_MainTex, uv);
    float horizonFade = smoothstep(uMaterial._CloudParams.z, uMaterial._CloudParams.w, d.y);

    vec4 col = tex * uMaterial._Color * vColor;
    col.a *= horizonFade;

    if (col.a <= 0.0)
        discard;

    outColor = col;
}
