using System;

namespace RealWar.Viewer.PostProcessing;

/// <summary>
/// Works on ARGB1555 image data only.
/// </summary>
public static class TexturePostProcessing
{
    /// <summary>
    /// Makes all fully black (0,0,0) pixels transparent.
    /// </summary>
    public static void MaskOutBlackPixels(ushort[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            ushort pixel = pixels[i];

            int a = (pixel >> 15) & 0x1;
            int r = (pixel >> 10) & 0x1F;
            int g = (pixel >> 5) & 0x1F;
            int b = (pixel >> 0) & 0x1F;

            if (a != 1 && r == 0 && g == 0 && b == 0)
                a = 1;

            pixels[i] = (ushort)((a << 15) | (r << 10) | (g << 5) | (b));
        };
    }

    /// <summary>
    /// Applies the cammo to the given color pixels.
    /// Cammo pixels must include the top palette row.
    /// </summary>
    public static void ApplyCammo(
        ushort[] colorPixels,
        ushort[]? alphaPixels,
        int textureWidth,
        int textureHeight,
        ushort[] cammoColorPixels,
        ushort[]? cammoAlphaPixels,
        int cammoWidth,
        int cammoHeight)
    {
        var cammoPalette = new ArraySegment<ushort>(cammoColorPixels, 0, cammoWidth);
        var cammoColorWithoutPalette = new ArraySegment<ushort>(cammoColorPixels,
            cammoWidth, (cammoWidth * cammoHeight) - cammoWidth);
        var cammoAlphaWithoutPalette = cammoAlphaPixels == null
            ? null
            : (ArraySegment<ushort>?)new ArraySegment<ushort>(cammoAlphaPixels,
                cammoWidth, (cammoWidth * cammoHeight) - cammoWidth);

        for (int y = 0; y <= textureHeight; y += (cammoHeight - 1))
        {
            for (int x = 0; x <= textureWidth; x += cammoWidth)
            {
                ApplyCammoTile(
                    colorPixels,
                    alphaPixels,
                    textureWidth,
                    textureHeight,
                    cammoColorWithoutPalette,
                    cammoAlphaWithoutPalette,
                    x,
                    y,
                    cammoWidth,
                    cammoHeight - 1,
                    cammoPalette,
                    cammoAlphaMask: cammoColorPixels[0],
                    otherMask: 0);
            }
        }
    }

    static void ApplyCammoTile(
        ushort[] colorPixels,
        ushort[]? alphaPixels,
        int textureWidth,
        int textureHeight,
        ArraySegment<ushort> cammoColorPixels,
        ArraySegment<ushort>? cammoAlphaPixels,
        int x,
        int y,
        int cammoWidth,
        int cammoHeight,
        ArraySegment<ushort> cammoPalette,
        ushort cammoAlphaMask,
        ushort otherMask)
    {
        int otherWidth = (cammoWidth << 8) / cammoWidth;
        int otherHeight = (cammoHeight << 8) / cammoHeight;

        int right = x + cammoWidth;
        int bottom = y + cammoHeight;

        if (right > textureWidth)
            right = textureWidth;
        if (bottom > textureHeight)
            bottom = textureHeight;

        int pixOffset = textureWidth * y + x;

        for (int i = 0; i < (bottom - y); i++)
        {
            int cammoPixY = ((i * otherHeight) >> 8) * cammoWidth;
            int pixY = (i * textureWidth) + pixOffset;

            for (int k = 0; k < (right - x); k++)
            {
                int cammoPixX = (k * otherWidth) >> 8;
                int pixX = k;

                ushort cammoColorPixel = cammoColorPixels[cammoPixY + cammoPixX];
                ushort cammoAlphaPixel = cammoAlphaPixels == null
                    ? cammoColorPixel
                    : cammoAlphaPixels.Value[cammoPixY + cammoPixX];

                ushort colorPixel = colorPixels[pixY + pixX];
                ushort alphaPixel = alphaPixels == null
                    ? colorPixel
                    : alphaPixels[pixY + pixX];

                if (
                    cammoAlphaPixel != 0 &&
                    (cammoColorPixel != cammoAlphaMask || cammoAlphaPixels != null) &&
                    !SkipPixelForCammo(cammoColorPixel) &&
                    !SkipPixelForCammo(cammoAlphaPixel) &&
                    !SkipPixelForCammo(colorPixel) &&
                    !SkipPixelForCammo(alphaPixel))
                {
                    int alphaR;
                    ushort cammoPalettePixel;

                    if (alphaPixels == null)
                    {
                        alphaR = 0;
                        // the "alpha" texture specifies the cammo palette index, so if we dont have 
                        // an alpha tex, just default to the first entry (1-indexed)
                        cammoPalettePixel = cammoPalette[1];
                    }
                    else
                    {
                        alphaR = ((alphaPixel >> 10) & 0x1f) + 1;
                        // blue channel selects color from cammo palette
                        cammoPalettePixel = cammoPalette[(alphaPixel >> 0) & 0x1f];
                    }

                    int pr = (cammoPalettePixel >> 10) & 0x1f;
                    int pg = (cammoPalettePixel >> 5) & 0x1f;
                    int pb = (cammoPalettePixel >> 0) & 0x1f;

                    int cr = (cammoColorPixel >> 10) & 0x1f;
                    int cg = (cammoColorPixel >> 5) & 0x1f;
                    int cb = (cammoColorPixel >> 0) & 0x1f;

                    // mix in palette color to base cammo color
                    if (cammoPalettePixel != 0)
                    {
                        cr = ((cr + 1) * (pr + 1)) >> 5;
                        cg = ((cg + 1) * (pg + 1)) >> 5;
                        cb = ((cb + 1) * (pb + 1)) >> 5;
                    }

                    int r = (colorPixel >> 10) & 0x1f;
                    int g = (colorPixel >> 5) & 0x1f;
                    int b = (colorPixel >> 0) & 0x1f;

                    if (cammoAlphaPixels == null)
                    {
                        if (cammoPalettePixel != 0)
                        {
                            // no alpha texture, mix in default palette color to base color
                            r = ((pr + 1) * r) >> 5;
                            g = ((pg + 1) * g) >> 5;
                            b = ((pb + 1) * b) >> 5;
                        }

                        // mix in cammo color to base color modulated by the base alpha R channel
                        r = (((cr * alphaR) >> 5) + r) >> 1;
                        g = (((cg * alphaR) >> 5) + g) >> 1;
                        b = (((cb * alphaR) >> 5) + b) >> 1;
                    }
                    else
                    {
                        if (cammoPalettePixel != 0)
                        {
                            // mix in palette color to base color
                            r = ((pr + 1) * r) >> 5;
                            g = ((pg + 1) * g) >> 5;
                            b = ((pb + 1) * b) >> 5;
                        }

                        // get cammo alpha color modulated by base alpha R
                        int ar = (((cammoAlphaPixel >> 10) & 0x1f) * alphaR) >> 5;
                        int ag = (((cammoAlphaPixel >> 5) & 0x1f) * alphaR) >> 5;
                        int ab = (((cammoAlphaPixel >> 0) & 0x1f) * alphaR) >> 5;

                        // mix cammo color and base color modulated by calculated alpha from above
                        r = (((32 - ar) * r) + ((ar + 1) * cr)) >> 5;
                        g = (((32 - ag) * g) + ((ag + 1) * cg)) >> 5;
                        b = (((32 - ab) * b) + ((ab + 1) * cb)) >> 5;
                    }

                    if (r > 0x1f) r = 0x1f;
                    if (g > 0x1f) g = 0x1f;
                    if (b > 0x1f) b = 0x1f;

                    // update base color
                    ushort newPixel = (ushort)((r << 10) | (g << 5) | b);
                    if (colorPixel != otherMask && newPixel != 0)
                        colorPixels[pixY + pixX] = newPixel;
                }
            }
        }
    }

    static bool SkipPixelForCammo(ushort pixel)
    {
        int r = (pixel & 0x7c00) >> 10;
        int g = (pixel & 0x03e0) >> 5;
        int b = (pixel & 0x001f) >> 0;

        // Note: 256 - 32 = 224
        if (r >= (224 / 8) && g <= (32 / 8) && b >= (224 / 8))
            return true;
        else
            return false;
    }
}
