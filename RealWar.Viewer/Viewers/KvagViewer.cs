using System;
using System.Numerics;
using ImGuiNET;
using RealWar.Viewer.Loaders;
using RealWar.Viewer.Utils;
using Silk.NET.OpenAL;

namespace RealWar.Viewer.Viewers;

class KvagViewer : IViewer
{
    public string Name { get; private set; }
    public bool Open => open;

    bool open = true;

    uint buffer;
    uint source;
    TimeSpan sourceLength;

    readonly Kvag kvag;
    readonly AL al;

    public KvagViewer(string name, Kvag kvag, AL al)
    {
        Name = name;
        this.kvag = kvag;
        this.al = al;

        buffer = al.GenBuffer();
        AssertALError();

        al.BufferData<short>(buffer,
            kvag.IsStereo ? BufferFormat.Stereo16 : BufferFormat.Mono16,
            kvag.Pcm,
            (int)kvag.SampleRate);
        AssertALError();

        source = al.GenSource();
        AssertALError();

        al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
        AssertALError();

        sourceLength = TimeSpan.FromSeconds((float)kvag.Pcm.Length / kvag.SampleRate);
        if (kvag.IsStereo)
            sourceLength /= 2;
    }

    public void Dispose()
    {
        al.DeleteSource(source);
        al.DeleteBuffer(buffer);
    }

    public void Update(float deltaTime)
    {
        if (ImGui.Begin(Name, ref open,
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Samples: {kvag.Pcm.Length}");
            ImGui.SameLine();
            ImGui.Text($"| Sample Rate: {kvag.SampleRate}");
            ImGui.SameLine();
            ImGui.Text($"| Length: {sourceLength:mm\\:ss\\.ff}");
            ImGui.SameLine();
            ImGui.Text($"| {(kvag.IsStereo ? "Stereo" : "Mono")}");

            int state;
            al.GetSourceProperty(source, GetSourceInteger.SourceState, out state);

            bool playing = state == (int)SourceState.Playing;

            if (ImGui.Button(playing ? "Stop" : "Play"))
            {
                if (playing)
                    al.SourcePause(source);
                else
                    al.SourcePlay(source);
                AssertALError();
            }

            bool looping;
            al.GetSourceProperty(source, SourceBoolean.Looping, out looping);

            ImGui.SameLine();
            if (ImGui.Checkbox("Loop", ref looping))
            {
                al.SetSourceProperty(source, SourceBoolean.Looping, looping);
                AssertALError();
            }

            float position;
            al.GetSourceProperty(source, SourceFloat.SecOffset, out position);

            ImGui.PushItemWidth(400);
            if (ImGui.SliderFloat("Position", ref position, 0, (float)sourceLength.TotalSeconds - 0.001f,
                TimeSpan.FromSeconds(position).ToString("mm\\:ss\\.ff")))
            {
                if (state != (int)SourceState.Paused && !playing)
                {
                    al.SourcePlay(source);
                    AssertALError();
                    al.SourcePause(source);
                    AssertALError();
                }

                al.SetSourceProperty(source, SourceFloat.SecOffset, position);
                AssertALError();
            }
            ImGui.PopItemWidth();
        }
    }

    public void Draw(float deltaTime) { }

    void AssertALError()
    {
        AudioError error = al.GetError();
        if (error != AudioError.NoError)
            throw new Exception($"OpenAL error: {error}");
    }
}
