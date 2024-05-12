using System;
using System.IO;

namespace RealWar.Viewer.Loaders;

public class Kvag(uint sampleRate, bool isStereo, short[] pcm)
{
    public readonly uint SampleRate = sampleRate;
    public readonly bool IsStereo = isStereo;
    public readonly short[] Pcm = pcm;

    static readonly int[] indexTable = new int[]
    {
        -1,-1,-1,-1, 2, 4, 6, 8,
        -1,-1,-1,-1, 2, 4, 6, 8,
    };
    static readonly int[] stepSizeTable = new int[]
    {
        7, 8, 9, 10, 11, 12, 13,
        14, 16, 17, 19, 21, 23, 25, 28,
        31, 34, 37, 41, 45, 50, 55, 60,
        66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230,
        253, 279, 307, 337, 371, 408, 449,
        494, 544, 598, 658, 724, 796, 876,
        963, 1060, 1166, 1282, 1411, 1552,
        1707, 1878, 2066, 2272, 2499, 2749,
        3024, 3327, 3660, 4026, 4428, 4871,
        5358, 5894, 6484, 7132, 7845, 8630,
        9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385,
        24623, 27086, 29794, 32767,
    };

    public static Kvag Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        uint sampleRate;
        bool isStereo;
        short[] pcm;

        var magic = new string(reader.ReadChars(4));
        if (magic == "KVAG")
        {
            uint dataSize = reader.ReadUInt32();
            sampleRate = reader.ReadUInt32();
            isStereo = reader.ReadUInt16() == 1;

            pcm = isStereo
                ? AdpcmDecompressStereo(reader, dataSize)
                : AdpcmDecompressMono(reader, dataSize);
        }
        else
        {
            ms.Position = 0;

            sampleRate = 22050 / 2; // TODO: idk if this is /1 or /2 :(
            isStereo = false;

            pcm = AdpcmDecompressMono(reader, (uint)bytes.Length);
        }

        return new Kvag(sampleRate, isStereo, pcm);
    }

    static short[] AdpcmDecompressMono(BinaryReader reader, uint dataSize)
    {
        short[] output = new short[dataSize * 2];

        int index = 0;
        int predictor = 0;
        int step = stepSizeTable[index];
        int nibbleIdx = 0;
        int i = 0;
        int curByte = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int nibble;
            if (nibbleIdx == 1)
            {
                nibble = curByte;
            }
            else
            {
                curByte = reader.ReadByte();
                nibble = curByte >> 4;
            }

            nibble = nibble & 0xF;
            nibbleIdx = nibbleIdx == 0 ? 1 : 0;

            index += indexTable[nibble];

            index = Math.Clamp(index, 0, 88);

            int signBit = nibble & 8;
            nibble = nibble & 7;

            int diff = step >> 3;
            if ((nibble & 4) != 0) diff += step;
            if ((nibble & 2) != 0) diff += (step >> 1);
            if ((nibble & 1) != 0) diff += (step >> 2);

            if (signBit != 0)
                predictor -= diff;
            else
                predictor += diff;

            predictor = Math.Clamp(predictor, -32768, 32767);

            step = stepSizeTable[index];
            output[i++] = (short)predictor;
        }

        return output;
    }

    static short[] AdpcmDecompressStereo(BinaryReader reader, uint dataSize)
    {
        short[] output = new short[dataSize * 2];

        int index1 = 0;
        int predictor1 = 0;
        int step1 = stepSizeTable[index1];

        int index2 = 0;
        int predictor2 = 0;
        int step2 = stepSizeTable[index2];

        int i = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int curByte = reader.ReadByte();
            int nibble1 = curByte & 0xF; // left channel
            int nibble2 = (curByte >> 4) & 0xF; // right channel

            index1 += indexTable[nibble1];
            index2 += indexTable[nibble2];

            index1 = Math.Clamp(index1, 0, 88);
            index2 = Math.Clamp(index2, 0, 88);

            int signBit1 = nibble1 & 8;
            int signBit2 = nibble2 & 8;
            nibble1 = nibble1 & 7;
            nibble2 = nibble2 & 7;

            int diff1 = step1 >> 3;
            if ((nibble1 & 4) != 0) diff1 += step1;
            if ((nibble1 & 2) != 0) diff1 += (step1 >> 1);
            if ((nibble1 & 1) != 0) diff1 += (step1 >> 2);

            int diff2 = step2 >> 3;
            if ((nibble2 & 4) != 0) diff2 += step2;
            if ((nibble2 & 2) != 0) diff2 += (step2 >> 1);
            if ((nibble2 & 1) != 0) diff2 += (step2 >> 2);

            if (signBit1 != 0)
                predictor1 -= diff1;
            else
                predictor1 += diff1;

            if (signBit2 != 0)
                predictor2 -= diff2;
            else
                predictor2 += diff2;

            predictor1 = Math.Clamp(predictor1, -32768, 32767);
            predictor2 = Math.Clamp(predictor2, -32768, 32767);

            step1 = stepSizeTable[index1];
            step2 = stepSizeTable[index2];

            output[i++] = (short)predictor1;
            output[i++] = (short)predictor2;
        }

        return output;
    }
}
