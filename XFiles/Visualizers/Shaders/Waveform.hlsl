// Waveform.hlsl — Waveform visualizer pixel shader
//
// Renders the time-domain waveform as a continuous line.
// Vertically mirrored for symmetry. Trail/ghosting effect.
// Colors shift left (cyan) to right (magenta).
//
// Compile: fxc /T ps_4_0 /E main /Fo Waveform.cso Waveform.hlsl

cbuffer Constants : register(b0)
{
    float2 uResolution;
    float uTime;
    float uBeat;
    float uWaveform[512];     // time-domain samples (-1.0 to 1.0)
    int uWaveformCount;       // valid sample count
};

float4 main(float2 pos : SV_Position) : SV_Target
{
    float2 uv = pos / uResolution;

    // Map x position to waveform index
    float waveX = uv.x * min((float)uWaveformCount, 512.0);
    int idx0 = (int)waveX;
    int idx1 = min(idx0 + 1, uWaveformCount - 1);
    float frac = waveX - (float)idx0;

    // Interpolate waveform value
    float waveValue = lerp(uWaveform[idx0], uWaveform[idx1], frac);

    // Map to y position (center = 0.5, amplitude scaled)
    float waveY = 0.5 - waveValue * 0.35;
    float mirrorY = 0.5 + waveValue * 0.35;

    // Distance to waveform line
    float dist = abs(uv.y - waveY);
    float mirrorDist = abs(uv.y - mirrorY);

    // Color: cyan (left) to magenta (right)
    float3 lineColor = lerp(
        float3(0.0, 0.9, 0.9),  // cyan
        float3(0.9, 0.0, 0.9),  // magenta
        uv.x
    );

    // Beat pulse on brightness
    float brightness = 0.8 + 0.2 * uBeat;

    // Primary line
    float thickness = 0.003;
    float alpha = smoothstep(thickness, 0.0, dist);

    // Mirror line (dimmer)
    float mirrorAlpha = smoothstep(thickness * 0.8, 0.0, mirrorDist) * 0.5;

    // Glow (wider, dimmer)
    float glowThickness = 0.015;
    float glowAlpha = smoothstep(glowThickness, 0.0, dist) * 0.15;
    float mirrorGlow = smoothstep(glowThickness, 0.0, mirrorDist) * 0.08;

    // Combine
    float totalAlpha = max(alpha, mirrorAlpha);
    float3 color = lineColor * brightness;

    // Add glow
    color += lineColor * (glowAlpha + mirrorGlow);

    // Trail effect: subtle horizontal lines at waveform position
    float trail = 0.0;
    for (int t = 1; t <= 4; t++)
    {
        float trailY = 0.5 - waveValue * 0.35 * (1.0 - t * 0.15);
        float trailDist = abs(uv.y - trailY);
        trail += smoothstep(0.008, 0.0, trailDist) * (0.1 / t);
    }
    color += trail * lineColor * 0.3;

    // Background
    float3 bg = lerp(float3(0.04, 0.04, 0.06), float3(0.01, 0.01, 0.02), length(uv - 0.5));

    return float4(lerp(bg, color, saturate(totalAlpha + trail * 0.5)), 1.0);
}
