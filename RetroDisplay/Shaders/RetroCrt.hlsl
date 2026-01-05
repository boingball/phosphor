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

    float HSize;
    float VSize;
    float _pad0;
};

float3 ApplySlotMask(float2 uv, float3 rgb)
{
    float x = floor(uv.x * EffectiveWidth);
    float stripe = fmod(x, 3.0);

    float3 mask;
    if (MaskType < 0.5)
    {
        mask.r = (stripe < 0.5) ? 1.0 : 0.6;
        mask.g = (stripe >= 0.5 && stripe < 1.5) ? 1.0 : 0.6;
        mask.b = (stripe >= 1.5) ? 1.0 : 0.6;
    }
    else
    {
        mask = (fmod(x, 2.0) < 1.0)
            ? float3(1.0, 0.85, 0.85)
            : float3(0.85, 1.0, 0.85);
    }

    return lerp(rgb, rgb * mask, PhosphorStrength);
}

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    // ------------------------------------------------------------
    // Geometry / overscan
    // ------------------------------------------------------------
    float2 centered = uv - 0.5;

    float hScale = 1.07 + HSize;
    float vScale = 1.0 + VSize;

    centered.x /= hScale;
    centered.y /= vScale;

    uv = centered + 0.5;

    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
        return float4(0, 0, 0, 1);

    // ------------------------------------------------------------
    // Sample
    // ------------------------------------------------------------
    float4 col = tex0.Sample(samp0, uv);
    float safeGamma = max(Gamma, 0.01);

    // Brightness
    col.rgb += Brightness;

    // Contrast
    col.rgb = (col.rgb - 0.5) * Contrast + 0.5;

    // Gamma
    col.rgb = pow(saturate(col.rgb), 1.0 / safeGamma);

    // ------------------------------------------------------------
    // Horizontal beam spread (3-tap)
    // ------------------------------------------------------------
    float2 texel = float2(1.0 / max(EffectiveWidth, 1.0), 0.0);
    float3 left  = tex0.Sample(samp0, uv - texel).rgb;
    float3 right = tex0.Sample(samp0, uv + texel).rgb;

    col.rgb = lerp(col.rgb, (left + col.rgb + right) / 3.0, BeamWidth);

    // ------------------------------------------------------------
    // Saturation
    // ------------------------------------------------------------
    float grey = dot(col.rgb, float3(0.3333, 0.3333, 0.3333));
    col.rgb = grey + (col.rgb - grey) * Saturation;

    // ------------------------------------------------------------
    // TRUE CRT SCANLINE BEAM MODEL (VERTICAL)
    // ------------------------------------------------------------

    // Convert UV to scanline space
    float y = uv.y * EffectiveHeight + ScanlinePhase;

    // Fractional position within scanline [0..1)
    float fy = frac(y);

    // Distance from beam center
    float dist = abs(fy - 0.5);

    // Beam width control:
    // Smaller = thinner beam, darker gaps
    float bw = max(BeamWidth * 0.25, 0.0005);

    // Gaussian beam profile
    float beam = exp(-(dist * dist) / bw);

    // Shape it for stronger dark gaps
    beam = pow(beam, 1.4);

    // Mix with user strength
    float scanMod = lerp(1.0, beam, ScanlineStrength);

    col.rgb *= scanMod;

    // ------------------------------------------------------------
    // Phosphor mask
    // ------------------------------------------------------------
    col.rgb = ApplySlotMask(uv, col.rgb);

    return saturate(col);
}
