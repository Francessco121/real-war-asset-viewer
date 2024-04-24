using System;

namespace RealWar.Viewer.Utils;

public static class ImageUtils
{
    public static uint[] Argb1555ToRgba8888(ushort[] pixels)
    {
        var outPixels = new uint[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            ushort pixel = pixels[i];

            int a = (pixel & 0x8000) == 0 ? 255 : 0;
            int r = (((pixel >> 10) & 0x1F) * 255) / 31;
            int g = (((pixel >> 5) & 0x1F) * 255) / 31;
            int b = (((pixel >> 0) & 0x1F) * 255) / 31;

            outPixels[i] = (uint)((r << 24) | (g << 16) | (b << 8) | (a));
        }

        return outPixels;
    }
}
