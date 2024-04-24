using System;
using System.Buffers.Binary;
using System.IO;

namespace RealWar.Viewer.Loaders;

public static class Rle
{
    /// <summary>
    /// Decode 16-bit run-length encoded data.
    /// </summary>
    public static void Decode16(byte[] inBytes, int inOffset, ushort[] outWords)
    {
        using var ms = new MemoryStream(inBytes, inOffset, inBytes.Length - inOffset);
        using var reader = new BinaryReader(ms);

        int i = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            // Next control word
            ushort ctrl = reader.ReadUInt16();

            // 0xFFFF marks end of string
            if (ctrl == 0xFFFF)
                break;

            int sequenceLen = ctrl & 0x7FFF;

            // Check MSB
            if ((ctrl & 0x8000) != 0)
            {
                // Copy next n words as is
                for (int k = 0; k < sequenceLen; k++)
                    outWords[i++] = reader.ReadUInt16();
            }
            else
            {
                // Repeat next word n number of times
                ushort word = reader.ReadUInt16();

                for (int k = 0; k < sequenceLen; k++)
                    outWords[i++] = word;
            }
        }
    }
}
