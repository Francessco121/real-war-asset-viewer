using System;
using System.IO;
using System.Linq;

namespace RealWar.Viewer.Loaders;

public readonly struct BseVert(float x, float y, float z)
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float Z = z;
}

public readonly struct BseTri(uint v1, uint v2, uint v3, uint unk4, uint bseIndex, uint polyIdx, uint unk7, uint unk8)
{
    public readonly uint V1 = v1;
    public readonly uint V2 = v2;
    public readonly uint V3 = v3;
    /// <summary>
    /// Object ID? These roughly identify "logical" chunks of the model.
    /// </summary>
    public readonly uint Unk4 = unk4;
    public readonly uint BseIndex = bseIndex;
    public readonly uint PolyIdx = polyIdx;
    /// <summary>
    /// Almost always 0xFFFFFFFF.
    /// </summary>
    public readonly uint Unk7 = unk7;
    /// <summary>
    /// Almost always 0.
    /// </summary>
    public readonly uint Unk8 = unk8;
}

public readonly struct BseUv(float u, float v)
{
    public readonly float U = u;
    public readonly float V = v;
}

public readonly struct BseTriUv(BseUv v1, BseUv v2, BseUv v3)
{
    public readonly BseUv V1 = v1;
    public readonly BseUv V2 = v2;
    public readonly BseUv V3 = v3;
}

public readonly struct BseRgb(int r, int g, int b)
{
    public readonly int R = r;
    public readonly int G = g;
    public readonly int B = b;
}

public readonly struct BseTriColor(BseRgb v1, BseRgb v2, BseRgb v3)
{
    public readonly BseRgb V1 = v1;
    public readonly BseRgb V2 = v2;
    public readonly BseRgb V3 = v3;
}

public class BseFrame(BseVert[] verts)
{
    public readonly BseVert[] Verts = verts;
}

public class BseUvFrame(BseTriUv[] uvs)
{
    public readonly BseTriUv[] Uvs = uvs;
}

public class Bse(
    uint numPoly,
    uint numVerts,
    uint numFrames,
    BseVert[] vertices,
    BseTri[] polys,
    BseTriColor[] colors,
    BseTriUv[] uvs,
    uint[] flags,
    BseFrame[]? frames,
    float? scale,
    BseUvFrame[]? uvFrames)
{
    public readonly uint NumPoly = numPoly;
    public readonly uint NumVerts = numVerts;
    public readonly uint NumFrames = numFrames;
    public readonly BseVert[] Vertices = vertices;
    public readonly BseTri[] Polys = polys;
    public readonly BseTriColor[] Colors = colors;
    public readonly BseTriUv[] Uvs = uvs;
    /// <summary>
    /// Flags:
    /// <list type="bullet">
    /// <item>0 - No Texture?</item>
    /// <item>1 - Textured?</item>
    /// <item>2 - Partially transparent?</item>
    /// <item>4 - Unknown</item>
    /// <item>8 - Unknown</item>
    /// <item>16 - No linear filtering?</item>
    /// </list>
    /// </summary>
    public readonly uint[] Flags = flags;
    public readonly BseFrame[]? Frames = frames;
    public readonly float? Scale = scale;
    public readonly BseUvFrame[]? UvFrames = uvFrames;

    public static Bse Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        AssertMagic(reader, "BSE1");

        uint numPoly = reader.ReadUInt32();
        uint numVert = reader.ReadUInt32();
        uint numFrames = reader.ReadUInt32();

        AssertMagic(reader, "VERT");

        var verts = new BseVert[numVert];
        for (int i = 0; i < numVert; i++)
        {
            verts[i] = new BseVert(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        AssertMagic(reader, "POLY");

        var polys = new BseTri[numPoly];
        for (int i = 0; i < numPoly; i++)
        {
            polys[i] = new BseTri(
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32());
        }

        AssertMagic(reader, "COLR");

        var colors = new BseTriColor[numPoly];
        for (int i = 0; i < numPoly; i++)
        {
            colors[i] = new BseTriColor(
                new BseRgb(
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte()),
                new BseRgb(
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte()),
                new BseRgb(
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte()));
        }

        AssertMagic(reader, "UVS0");

        var uvs = new BseTriUv[numPoly];
        for (int i = 0; i < numPoly; i++)
        {
            uvs[i] = new BseTriUv(
                new BseUv(
                    reader.ReadSingle(),
                    reader.ReadSingle()),
                new BseUv(
                    reader.ReadSingle(),
                    reader.ReadSingle()),
                new BseUv(
                    reader.ReadSingle(),
                    reader.ReadSingle()));
        }

        AssertMagic(reader, "FLAG");

        var flags = new uint[numPoly];
        for (int i = 0; i < numPoly; i++)
        {
            flags[i] = reader.ReadUInt32();
        }

        BseFrame[]? frames = null;
        if (numFrames != 0)
        {
            AssertMagic(reader, "FRMS");

            frames = new BseFrame[numFrames];
            for (int i = 0; i < numFrames; i++)
            {
                var frameVerts = new BseVert[numVert];
                for (int j = 0; j < numVert; j++)
                {
                    frameVerts[j] = new BseVert(
                        reader.ReadSingle(),
                        reader.ReadSingle(),
                        reader.ReadSingle());
                }

                frames[i] = new BseFrame(frameVerts);
            }
        }

        float? scale = null;
        if (reader.BaseStream.Position < (reader.BaseStream.Length - 4) && ReadMagic(reader, 4) == "SCAL")
        {
            scale = reader.ReadSingle();
        }

        BseUvFrame[]? uvFrames = null;
        if (reader.BaseStream.Position < (reader.BaseStream.Length - 4) && ReadMagic(reader, 4) == "AUVS")
        {
            uvFrames = new BseUvFrame[numFrames];
            for (int i = 0; i < numFrames; i++)
            {
                var frameUvs = new BseTriUv[numPoly];
                for (int j = 0; j < numPoly; j++)
                {
                    frameUvs[j] = new BseTriUv(
                        new BseUv(
                            reader.ReadSingle(),
                            reader.ReadSingle()),
                        new BseUv(
                            reader.ReadSingle(),
                            reader.ReadSingle()),
                        new BseUv(
                            reader.ReadSingle(),
                            reader.ReadSingle()));
                }

                uvFrames[i] = new BseUvFrame(frameUvs);
            }
        }

        return new Bse(
            numPoly,
            numVert,
            numFrames,
            verts,
            polys,
            colors,
            uvs,
            flags,
            frames,
            scale,
            uvFrames);
    }

    static string ReadMagic(BinaryReader reader, int length)
    {
        return new string(reader.ReadChars(length));
    }

    static void AssertMagic(BinaryReader reader, string magic)
    {
        char[] readMagic = reader.ReadChars(magic.Length);
        if (!readMagic.SequenceEqual(magic))
            throw new Exception($"Expected magic {magic} but got {new string(readMagic)} at file offset 0x{(reader.BaseStream.Position - magic.Length):X}");
    }
}
