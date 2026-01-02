sampler2D input : register(s0);

float Brightness : register(c0); // -0.25 .. +0.25 (default 0)
float Contrast : register(c1); //  0.8  ..  1.4  (default 1)
float Saturation : register(c2); //  0.0  ..  1.5  (default 1)
float ScanlineStrength : register(c3); //  0.0  ..  0.5  (default 0)
float Gamma : register(c5); //  1.0  ..  1.3  (default 1.0)
float PhosphorStrength : register(c6); //  0.0  ..  0.3  (default 0)
float ScreenWidth : register(c7);
float ScreenHeight : register(c8);
float EffectiveWidth : register(c9);
float EffectiveHeight : register(c10);
float ScanlinePhase : register(c11); // 0.0 .. 1.0
float MaskType : register(c12); // 0 = slot, 1 = aperture
float BeamWidth : register(c13); // 0.0 .. 0.4

float3 ApplySlotMask(float2 uv, float3 rgb)
{
    // Convert UV to approximate pixel space
    float x = floor(uv.x * EffectiveWidth);
    int stripe = (int) fmod(x, 3.0);

    // Slot mask (RGB vertical stripes)
    float3 mask;
    if (MaskType < 0.5)
    {
    // Slot mask (current)
        mask.r = (stripe == 0) ? 1.0 : 0.6;
        mask.g = (stripe == 1) ? 1.0 : 0.6;
        mask.b = (stripe == 2) ? 1.0 : 0.6;
    }
    else
    {
    // Aperture mask (paired RGB)
        mask = (fmod(x, 2.0) < 1.0)
        ? float3(1.0, 0.85, 0.85)
        : float3(0.85, 1.0, 0.85);
    }

    return lerp(rgb, rgb * mask, PhosphorStrength);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // 1. Sample input
    float4 col = tex2D(input, uv);
    float safeGamma = max(Gamma, 0.01);

    // 2. Brightness (signal-level offset)
    col.rgb += Brightness;

    // 3. Contrast (signal gain around mid-point)
    col.rgb = (col.rgb - 0.5) * Contrast + 0.5;

    // 4. Gamma (beam/phosphor response)
    col.rgb = pow(saturate(col.rgb), 1.0 / safeGamma);

    // 5. Subtle horizontal beam spread (kills moiré & scaler artefacts)
    float2 texel = float2(1.0 / ScreenWidth, 0.0);
    float3 left = tex2D(input, uv - texel).rgb;
    float3 right = tex2D(input, uv + texel).rgb;

    // Asymmetric beam spread
    col.rgb = lerp(col.rgb, (left + col.rgb + right) / 3.0, BeamWidth);

    // 6. Saturation (perceptual colour, after gamma)
    float grey = (col.r + col.g + col.b) * 0.3333;
    col.rgb = grey + (col.rgb - grey) * Saturation;


    // 7. Scanlines (energy modulation, display-space correct)
    float scan = 0.5 + 0.5 *
    sin((uv.y + ScanlinePhase) * EffectiveHeight * 0.5 * 3.14159265);

    float scanMod = lerp(1.0, scan * 1.15, ScanlineStrength);
    col.rgb *= scanMod;

    // 8. Phosphor mask (absolute final stage)
    col.rgb = ApplySlotMask(uv, col.rgb);

    return saturate(col);
}

