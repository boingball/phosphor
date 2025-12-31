sampler2D input : register(s0);

float Brightness : register(c0); // -0.25 .. +0.25 (default 0)
float Contrast : register(c1); //  0.8  ..  1.4  (default 1)
float Saturation : register(c2); //  0.0  ..  1.5  (default 1)
float ScanlineStrength : register(c3); //  0.0  ..  0.5  (default 0)
float LineCount : register(c4); // e.g. 240, 288, 480, 576
float Gamma : register(c5); //  1.0  ..  1.3  (default 1.0)
float PhosphorStrength : register(c6); //  0.0  ..  0.3  (default 0)

float3 ApplySlotMask(float2 uv, float3 rgb)
{
    // Convert UV to approximate pixel space
    float x = floor(uv.x * LineCount * (4.0 / 3.0));
    int stripe = (int) fmod(x, 3.0);

    float3 mask = float3(1.0, 1.0, 1.0);

    // Slot mask (RGB vertical stripes)
    if (stripe == 0)
        mask = float3(1.0, 0.6, 0.6); // R
    if (stripe == 1)
        mask = float3(0.6, 1.0, 0.6); // G
    if (stripe == 2)
        mask = float3(0.6, 0.6, 1.0); // B

    return lerp(rgb, rgb * mask, PhosphorStrength);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 col = tex2D(input, uv);
    float safeGamma = max(Gamma, 0.01);

    // Brightness
    col.rgb += Brightness;

    // Contrast
    col.rgb = (col.rgb - 0.5) * Contrast + 0.5;

    // Saturation
    float grey = dot(col.rgb, float3(0.299, 0.587, 0.114));
    col.rgb = lerp(grey.xxx, col.rgb, Saturation);

    // Gamma correction (CRT response)
    col.rgb = pow(saturate(col.rgb), 1.0 / safeGamma);

    // Scanlines (vertical brightness modulation)
    float scan = sin(uv.y * LineCount * 3.14159265);
    col.rgb *= lerp(1.0, scan, ScanlineStrength);

    // Phosphor slot mask (final stage)
    col.rgb = ApplySlotMask(uv, col.rgb);

    return saturate(col);
}
