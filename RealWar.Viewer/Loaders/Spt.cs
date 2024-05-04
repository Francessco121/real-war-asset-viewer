using System;
using System.IO;

namespace RealWar.Viewer.Loaders;

public class SptFrame(uint width, uint height, ushort[] pixels)
{
    public readonly uint Width = width;
    public readonly uint Height = height;
    /// <summary>
    /// ARGB1555 pixel data.
    /// </summary>
    public readonly ushort[] Pixels = pixels;
}

public class Spt(SptFrame[] frames)
{
    public readonly SptFrame[] Frames = frames;

    public static Spt Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        // Read header
        uint header = reader.ReadUInt32();
        uint frameCount = header & 0x3FFFFFFF;
        bool isRle = (header & 0x80000000) != 0;

        var framePointers = new uint[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            framePointers[i] = reader.ReadUInt32();
        }

        // Read frames
        var frames = new SptFrame[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            uint pointer = framePointers[i];
            ms.Position = pointer;

            uint width = reader.ReadUInt32();
            uint height = reader.ReadUInt32();

            var pixels = new ushort[width * height];

            if (isRle)
            {
                Rle.Decode16(bytes, (int)(pointer + 8), pixels);
            }
            else
            {
                for (int k = 0; k < pixels.Length; k++)
                    pixels[k] = reader.ReadUInt16();
            }

            frames[i] = new SptFrame(width, height, pixels);
        }

        return new Spt(frames);
    }
}
