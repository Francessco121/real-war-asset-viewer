using System;
using System.IO;

namespace RealWar.Viewer.Loaders;

public class Tgc(int Width, int Height, ushort[] Pixels, uint Trailer)
{
    public readonly int Width = Width;
    public readonly int Height = Height;
    /// <summary>
    /// ARGB1555 pixel data.
    /// </summary>
    public readonly ushort[] Pixels = Pixels;
    /// <summary>
    /// An unknown 32-bit trailer.
    /// </summary>
    public readonly uint Trailer = Trailer;

    /// <summary>
    /// Reads a TGC file and decodes its pixel data.
    /// </summary>
    public static Tgc Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        int width = reader.ReadUInt16();
        int height = reader.ReadUInt16();

        var pixels = new ushort[width * height];
        Rle.Decode16(bytes, (int)ms.Position, pixels);
        reader.ReadInt32(); // Skip RLE end marker

        uint trailer = reader.ReadUInt32();

        return new Tgc(width, height, pixels, trailer);
    }
}
