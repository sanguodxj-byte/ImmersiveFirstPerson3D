#version 450

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec3 vWorldPos;
layout(location = 3) in vec3 vNormal;
layout(location = 4) in vec3 vPosLocal;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;          // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _SkyColor;       // @color @default = vec4(0.09, 0.30, 0.76, 1.0)
    vec4 _MidSkyColor;    // @color @default = vec4(0.24, 0.52, 0.90, 1.0)
    vec4 _HorizonColor;   // @color @default = vec4(0.68, 0.84, 0.97, 1.0)
    vec4 _GroundColor;    // @color @default = vec4(0.16, 0.18, 0.16, 1.0)
    vec4 _SunGlowHorizon; // @color @default = vec4(1.0, 0.44, 0.14, 1.0)
    vec4 _SunGlowUpper;   // @color @default = vec4(1.0, 0.75, 0.30, 1.0)
    vec4 _AntiSunHorizon; // @color @default = vec4(0.55, 0.35, 0.65, 1.0)
    vec4 _SunDir;         // @default = vec4(0.0, 1.0, 0.0, 0.0)
    vec4 _SunWarm;        // @color @default = vec4(1.0, 0.97, 0.90, 1.0)
    vec4 _Params;         // @default = vec4(0.0, 0.0, 0.0, 0.0)
} uMaterial;

void main()
{
    vec3 d = normalize(vPosLocal);
    float dy = d.y;
    float k = clamp(dy, 0.0, 1.0);

    vec3 horizon = uMaterial._HorizonColor.rgb;
    vec3 midSky = uMaterial._MidSkyColor.rgb;
    vec3 sky = uMaterial._SkyColor.rgb;
    vec3 ground = uMaterial._GroundColor.rgb;

    vec3 col;
    if (k < 0.42)
    {
        col = mix(horizon, midSky, pow(k / 0.42, 0.74));
    }
    else
    {
        col = mix(midSky, sky, pow((k - 0.42) / 0.58, 0.80));
    }

    float horGlow = exp(-k * k * 42.0);
    col = mix(col, horizon * 1.05, horGlow * (0.24 + uMaterial._Params.y * 0.35));

    float twilight = uMaterial._Params.x;
    if (twilight > 0.01)
    {
        vec3 sun = uMaterial._SunDir.xyz;
        vec2 sunH = sun.xz;
        float sunHLen = length(sunH);
        if (sunHLen > 0.0001) sunH /= sunHLen;

        float hLen = max(0.06, length(d.xz));
        float az = dot(d.xz, sunH) / hLen;
        float sunSide = pow(clamp(az * 0.5 + 0.5, 0.0, 1.0), 1.6);
        float antiSide = pow(clamp(-az * 0.5 + 0.5, 0.0, 1.0), 1.5);
        float lowBand = exp(-pow(k / 0.26, 1.6));
        float midDiff = (k - 0.22) / 0.22;
        float midBand = exp(-midDiff * midDiff);

        col = mix(col, uMaterial._SunGlowHorizon.rgb, twilight * sunSide * lowBand * 0.82);
        col = mix(col, uMaterial._SunGlowUpper.rgb, twilight * sunSide * midBand * 0.50);
        col = mix(col, uMaterial._AntiSunHorizon.rgb, twilight * antiSide * midBand * 0.42);
    }

    float sunVis = uMaterial._SunDir.w;
    if (sunVis > 0.01 && dy >= -0.05)
    {
        vec3 sun = uMaterial._SunDir.xyz;
        float nd = clamp(d.x * sun.x + max(0.0, dy) * sun.y + d.z * sun.z, 0.0, 1.0);
        float mie = pow(nd, 4.5) * 0.42 * sunVis;
        col = mix(col, uMaterial._SunWarm.rgb, mie);
    }

    float flash = uMaterial._Params.z;
    if (flash > 0.02)
    {
        vec3 flashCol = vec3(0.84, 0.91, 1.0);
        col = mix(col, flashCol, flash * mix(0.45, 0.72, k));
    }

    if (dy < 0.0)
    {
        float kd = smoothstep(0.0, 0.24, -dy);
        col = mix(col, ground, kd);
    }

    col = clamp(col, 0.0, 1.0) * uMaterial._Color.rgb * vColor.rgb;
    outColor = vec4(col, uMaterial._Color.a * vColor.a);
}
