using System;
using System.IO;

namespace RealWar.Viewer.Loaders;

public class S16Frame(uint width, uint height, ushort[] palette, byte[] colorIndices, byte[] alpha)
{
    public readonly uint Width = width;
    public readonly uint Height = height;
    /// <summary>
    /// ARGB1555 color palette.
    /// </summary>
    public readonly ushort[] Palette = palette;
    /// <summary>
    /// Indices into the color palette for each pixel.
    /// </summary>
    public readonly byte[] ColorIndices = colorIndices;
    /// <summary>
    /// Alpha values from 0-31 for each pixel.
    /// </summary>
    public readonly byte[] Alpha = alpha;
}

public class S16(S16Frame[] frames)
{
    public readonly S16Frame[] Frames = frames;

    public static S16 Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        // Read header
        uint header = reader.ReadUInt32();
        uint frameCount = header & 0x3FFFFFFF;

        var framePointers = new uint[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            framePointers[i] = reader.ReadUInt32();
        }

        // Read frames
        var frames = new S16Frame[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            // Jump to frame data
            uint pointer = framePointers[i];
            ms.Position = pointer;

            // Frame header
            uint width = reader.ReadUInt32();
            uint height = reader.ReadUInt32();

            // Frame color palette
            int paletteLength = reader.ReadUInt16();
            var palette = new ushort[paletteLength];

            for (int k = 0; k < paletteLength; k++)
                palette[k] = reader.ReadUInt16();

            // Color indices
            var colorIndices = new byte[width * height];

            for (int k = 0; k < colorIndices.Length; k++)
                colorIndices[k] = reader.ReadByte();

            // Alpha
            var alpha = new byte[width * height];

            for (int k = 0; k < alpha.Length; k++)
                alpha[k] = reader.ReadByte();

            frames[i] = new S16Frame(width, height, palette, colorIndices, alpha);
        }

        return new S16(frames);
    }
}
