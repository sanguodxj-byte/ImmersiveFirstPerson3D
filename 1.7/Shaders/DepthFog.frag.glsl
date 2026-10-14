#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D sceneDepth;
layout(push_constant) uniform FogParams {
    vec4 right;
    vec4 up;
    vec4 forward;
    vec4 atmosphere;
    vec4 sunDir;
    vec4 horizon;
    vec4 sun;
    vec4 limits;
} fog;

float opticalDepth(float distance, float height, float directionY) {
    float endHeight = height + directionY * distance;
    if (height <= 0.0 && endHeight <= 0.0) return fog.atmosphere.x * distance;
    float airLength = distance;
    float groundLength = 0.0;
    float start = max(0.0, height);
    float end = max(0.0, endHeight);
    if (height < 0.0 || endHeight < 0.0) {
        float crossing = clamp(-height / directionY, 0.0, distance);
        airLength = height < 0.0 ? distance - crossing : crossing;
        groundLength = distance - airLength;
    }
    float scale = max(0.1, fog.atmosphere.y);
    float delta = directionY * airLength / scale;
    float average;
    if (abs(delta) < 0.01) {
        average = exp(-start / scale) * (1.0 - delta * 0.5 + delta * delta / 6.0 - delta * delta * delta / 24.0);
    } else {
        average = (exp(-start / scale) - exp(-end / scale)) / delta;
    }
    return fog.atmosphere.x * (groundLength + airLength * clamp(average, 0.0, 1.0));
}

void main() {
    vec4 source = texture(sceneColor, vUv);
    float depth = texture(sceneDepth, vUv).r;
    vec2 ndc = vUv * 2.0 - 1.0;
    vec3 localRay = vec3((ndc.x + fog.limits.y) * fog.right.w,
                        (ndc.y + fog.limits.z) * fog.up.w, 1.0);
    vec3 direction = normalize(fog.right.xyz * localRay.x + fog.up.xyz * localRay.y + fog.forward.xyz);
    float viewDistance = fog.atmosphere.w / max(depth + fog.forward.w, 1e-8);
    float distance = min(viewDistance, fog.limits.x) * length(localRay);
    float transmittance = exp(-opticalDepth(distance, fog.atmosphere.z, direction.y));
    float g = fog.sunDir.w;
    float c = dot(direction, fog.sunDir.xyz);
    float mie = (1.0 - g * g) / pow(max(1e-4, 1.0 + g * g - 2.0 * g * c), 1.5);
    vec3 tint = fog.horizon.rgb + fog.sun.rgb * fog.sun.w * clamp(mie, 0.0, 12.0);
    tint /= max(1.0, max(tint.r, max(tint.g, tint.b)));
    tint *= fog.horizon.w;
    outColor = vec4(mix(tint, source.rgb, transmittance), source.a);
}
