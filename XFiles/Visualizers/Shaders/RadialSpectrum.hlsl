// RadialSpectrum.hlsl — Radial spectrum visualizer pixel shader
//
// Renders 26 frequency bars arranged in a circle.
// Each bar's height = BandLevel, peak indicator = BandPeak.
// Colors shift by frequency (blue → green → yellow → red).
//
// Compile: fxc /T ps_4_0 /E main /Fo RadialSpectrum.cso RadialSpectrum.hlsl
// Load in C#: new PixelShaderEffect(FileIO.ReadBuffer("Shaders/RadialSpectrum.cso"))

cbuffer Constants : register(b0)
{
    float2 uResolution;       // canvas size in pixels
    float uTime;              // accumulated time in seconds
    float uBeat;              // beat detector (0.0–1.0)
    float uBandLevels[26];    // normalized band levels (0.0–1.0)
    float uBandPeaks[26];     // normalized band peaks (0.0–1.0)
};

// HSL to RGB conversion
float3 HslToRgb(float h, float s, float l)
{
    h = frac(h) * 6.0;
    float c = (1.0 - abs(2.0 * l - 1.0)) * s;
    float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
    float m = l - c * 0.5;

    float3 rgb;
    if (h < 1.0) rgb = float3(c, x, 0);
    else if (h < 2.0) rgb = float3(x, c, 0);
    else if (h < 3.0) rgb = float3(0, c, x);
    else if (h < 4.0) rgb = float3(0, x, c);
    else if (h < 5.0) rgb = float3(x, 0, c);
    else rgb = float3(c, 0, x);

    return rgb + m;
}

float4 main(float2 pos : SV_Position) : SV_Target
{
    float2 center = uResolution * 0.5;
    float2 uv = pos - center;
    float radius = length(uv);
    float angle = atan2(uv.y, uv.x);

    float minDim = min(uResolution.x, uResolution.y);
    float innerRadius = minDim * 0.15;
    float maxBarHeight = minDim * 0.30;

    // Map angle to band index (0–25), offset so bar 0 is at top
    float normalizedAngle = (angle + 3.14159265) / (2.0 * 3.14159265);
    float bandFloat = normalizedAngle * 26.0;
    int bandIndex = (int)(bandFloat + 0.5) % 26;

    // Band color: HSL gradient blue (240°) → red (0°)
    float hue = (1.0 - (float)bandIndex / 25.0) * 0.667; // 0.667 = 240/360
    float3 barColor = HslToRgb(hue, 0.85, 0.55);

    // Bar height
    float barHeight = uBandLevels[bandIndex] * maxBarHeight;
    barHeight = max(barHeight, 1.0);

    // Distance from inner radius
    float relRadius = radius - innerRadius;

    // Bar rendering
    if (relRadius >= 0.0 && relRadius <= barHeight)
    {
        // Fade at edges for smoothness
        float edgeFade = 1.0;
        float edgeDist = min(relRadius, barHeight - relRadius);
        edgeFade = smoothstep(0.0, 3.0, edgeDist);

        return float4(barColor * edgeFade, 1.0);
    }

    // Peak indicator
    float peakRadius = innerRadius + uBandPeaks[bandIndex] * maxBarHeight;
    float peakDist = abs(radius - peakRadius);
    if (peakDist < 2.0 && uBandPeaks[bandIndex] > 0.05)
    {
        float peakAlpha = smoothstep(2.0, 0.0, peakDist);
        return float4(1.0, 1.0, 1.0, peakAlpha * 0.9);
    }

    // Inner circle outline
    float circleDist = abs(radius - (innerRadius - 2.0));
    if (circleDist < 1.5)
    {
        float circleAlpha = smoothstep(1.5, 0.0, circleDist);
        return float4(1.0, 1.0, 1.0, circleAlpha * 0.3);
    }

    // Background
    return float4(0.02, 0.02, 0.03, 1.0);
}
