Texture2D tex0 : register(t0);
SamplerState samp0 : register(s0);

cbuffer CrtParams : register(b0)
{
    float Brightness;
    float Contrast;
    float Saturation;
    float ScanlineStrength;

    float Gamma;
    float PhosphorStrength;
    float ScreenWidth;
    float ScreenHeight;

    float EffectiveWidth;
    float EffectiveHeight;
    float ScanlinePhase;
    float MaskType;

    float BeamWidth;

    float _pad0;
    float _pad1;
    float _pad2;
};

float3 ApplySlotMask(float2 uv, float3 rgb)
{
    // Convert UV to approximate pixel space
    float x = floor(uv.x * EffectiveWidth);
    float stripe = fmod(x, 3.0);

    float3 mask;
    if (MaskType < 0.5)
    {
        // Slot mask RGB stripes
        mask.r = (stripe < 0.5) ? 1.0 : 0.6;
        mask.g = (stripe >= 0.5 && stripe < 1.5) ? 1.0 : 0.6;
        mask.b = (stripe >= 1.5) ? 1.0 : 0.6;
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

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    // 1) Sample
    float4 col = tex0.Sample(samp0, uv);
    float safeGamma = max(Gamma, 0.01);

    // 2) Brightness
    col.rgb += Brightness;

    // 3) Contrast
    col.rgb = (col.rgb - 0.5) * Contrast + 0.5;

    // 4) Gamma
    col.rgb = pow(saturate(col.rgb), 1.0 / safeGamma);

    // 5) Beam spread (horizontal 3-tap)
    float2 texel = float2(1.0 / max(ScreenWidth, 1.0), 0.0);
    float3 left = tex0.Sample(samp0, uv - texel).rgb;
    float3 right = tex0.Sample(samp0, uv + texel).rgb;
    col.rgb = lerp(col.rgb, (left + col.rgb + right) / 3.0, BeamWidth);

    // 6) Saturation
    float grey = (col.r + col.g + col.b) * 0.3333;
    col.rgb = grey + (col.rgb - grey) * Saturation;

    // 7) Scanlines
    float scan = 0.5 + 0.5 *
        sin((uv.y + ScanlinePhase) * EffectiveHeight * 0.5 * 3.14159265);

    float scanMod = lerp(1.0, scan * 1.15, ScanlineStrength);
    col.rgb *= scanMod;

    // 8) Phosphor mask
    col.rgb = ApplySlotMask(uv, col.rgb);

    return saturate(col);
}
