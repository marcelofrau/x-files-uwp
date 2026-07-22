// Plasma.hlsl — Plasma visualizer pixel shader
//
// Colorful plasma reactive to audio. 3 sin/cos waves with frequencies
// modulated by band levels. Beat controls intensity/saturation.
//
// Compile: fxc /T ps_4_0 /E main /Fo Plasma.cso Plasma.hlsl

cbuffer Constants : register(b0)
{
    float2 uResolution;
    float uTime;
    float uBeat;
    float uBandLevels[26];
    float uBandPeaks[26];
};

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
    float2 uv = pos / uResolution;

    // Average band energy by range
    float bassEnergy = 0.0;
    for (int i = 0; i < 6; i++) bassEnergy += uBandLevels[i];
    bassEnergy /= 6.0;

    float midEnergy = 0.0;
    for (int i = 10; i < 16; i++) midEnergy += uBandLevels[i];
    midEnergy /= 6.0;

    float trebleEnergy = 0.0;
    for (int i = 20; i < 26; i++) trebleEnergy += uBandLevels[i];
    trebleEnergy /= 6.0;

    // 3 sine waves modulated by audio
    float freq1 = 3.0 + bassEnergy * 8.0;
    float freq2 = 4.0 + midEnergy * 6.0;
    float freq3 = 5.0 + trebleEnergy * 10.0;

    float wave1 = sin(uv.x * freq1 + uTime * 1.0);
    float wave2 = cos(uv.y * freq2 + uTime * 0.7);
    float wave3 = sin((uv.x + uv.y) * freq3 + uTime * 1.3);

    // Combine into plasma value
    float plasma = (wave1 + wave2 + wave3) / 3.0;

    // Color from plasma value + time
    float hue = frac(plasma * 0.5 + uTime * 0.05);

    // Beat response: saturation pulse
    float saturation = 0.7 + 0.3 * uBeat;

    // Brightness from overall energy
    float avgEnergy = (bassEnergy + midEnergy + trebleEnergy) / 3.0;
    float brightness = 0.5 + 0.5 * avgEnergy;

    float3 color = HslToRgb(hue, saturation, brightness);

    // Vignette
    float vignette = 1.0 - length(uv - 0.5) * 1.2;
    vignette = saturate(vignette);
    color *= vignette;

    return float4(color, 1.0);
}
