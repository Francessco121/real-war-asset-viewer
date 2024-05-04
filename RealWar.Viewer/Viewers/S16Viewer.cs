using System;
using System.Numerics;
using ImGuiNET;
using RealWar.Viewer.Loaders;
using Silk.NET.OpenGL;

namespace RealWar.Viewer.Viewers;

class S16Viewer : IViewer
{
    public string Name { get; private set; }
    public bool Open => open;

    bool open = true;

    bool dragging = false;
    Vector2 offset = Vector2.Zero;
    float zoom = 1;

    bool showAdvanced = false;

    int frameCount;
    int frameIdx = 0;
    int frameStart;
    int frameEnd;
    float frameProgress = 0;
    int frameRate = 24;
    bool playing = false;
    bool loops = true;

    readonly uint[] frames;

    readonly S16 s16;
    readonly GL gl;

    public S16Viewer(string name, S16 s16, GL gl)
    {
        Name = name;
        this.s16 = s16;
        this.gl = gl;

        frames = new uint[s16.Frames.Length];
        gl.CreateTextures(TextureTarget.Texture2D, n: (uint)frames.Length, frames);

        for (int i = 0; i < s16.Frames.Length; i++)
        {
            S16Frame frame = s16.Frames[i];
            var pixelData = new uint[frame.Width * frame.Height];

            for (int k = 0; k < pixelData.Length; k++)
            {
                byte colorIdx = frame.ColorIndices[k];
                byte alpha = frame.Alpha[k];

                int color = frame.Palette[colorIdx];

                int a = ((alpha & 0x1F) * 255) / 31;
                int r = (((color >> 10) & 0x1F) * 255) / 31;
                int g = (((color >> 5) & 0x1F) * 255) / 31;
                int b = (((color >> 0) & 0x1F) * 255) / 31;

                pixelData[k] = (uint)((r << 24) | (g << 16) | (b << 8) | (a));
            }

            uint texture = frames[i];

            gl.TextureParameter(texture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TextureParameter(texture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, frame.Width, frame.Height);
            gl.TextureSubImage2D<uint>(texture, 0, 0, 0, frame.Width, frame.Height,
                PixelFormat.Rgba, PixelType.UnsignedInt8888, pixelData);
        }

        frameCount = frames.Length;
        frameStart = 0;
        frameEnd = frameCount - 1;
    }

    public void Dispose()
    {
        if (frames != null)
            gl.DeleteTextures(frames);
    }

    public void Update(float deltaTime)
    {
        if (ImGui.Begin(Name, ref open,
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.MenuBar))
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.Button("Reset View"))
                {
                    offset = Vector2.Zero;
                    zoom = 1;
                }

                if (ImGui.Checkbox("Show Adv.", ref showAdvanced))
                {
                    if (!showAdvanced)
                    {
                        frameStart = 0;
                        frameEnd = frameCount - 1;
                        frameIdx = 0;
                    }
                }

                ImGui.EndMenuBar();
            }

            {
                if (ImGui.Button(playing ? "Stop" : "Play"))
                {
                    playing = !playing;

                    if (playing && !loops && frameIdx >= frameEnd)
                    {
                        frameIdx = frameStart;
                        frameProgress = 0;
                    }
                }

                Vector2 framerateTextSize = ImGui.GetFont().CalcTextSizeA(ImGui.GetFontSize(), float.MaxValue, float.MaxValue, "Framerate |");
                float windowPadding = ImGui.GetStyle().WindowPadding.X;

                ImGui.SameLine();
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetCursorPosX() - 150 - framerateTextSize.X - windowPadding);
                int frameNum = frameIdx + 1;
                if (ImGui.SliderInt("Frame |", ref frameNum, 1, frameCount))
                {
                    frameIdx = frameNum - 1;
                    frameProgress = 0;
                    playing = false;
                }
                ImGui.PopItemWidth();

                ImGui.SameLine();
                ImGui.PushItemWidth(80);
                ImGui.InputInt("Framerate |", ref frameRate);
                ImGui.PopItemWidth();

                ImGui.SameLine();
                ImGui.Checkbox("Loop", ref loops);
            }

            if (showAdvanced)
            {
                int frameStartNum = frameStart + 1;
                ImGui.PushItemWidth(80);
                ImGui.InputInt("Frame Start |", ref frameStartNum);

                int frameEndNum = frameEnd + 1;
                ImGui.SameLine();
                ImGui.InputInt("Frame End |", ref frameEndNum);
                ImGui.PopItemWidth();

                frameStart = Math.Clamp(frameStartNum - 1, 0, Math.Min(frameEnd, frameCount - 1));
                frameEnd = Math.Clamp(Math.Max(frameStart, frameEndNum - 1), 0, frameCount - 1);
            }

            S16Frame frame = s16.Frames[frameIdx];
            uint frameTexture = frames[frameIdx];

            ImGui.Text($"Dimensions: {frame.Width}x{frame.Height}");
            ImGui.SameLine();
            ImGui.Text($"| Frames: {frameCount}");

            ImGui.SetWindowSize(
                Vector2.Max(
                    new Vector2(frame.Width * 2, frame.Height * 2 + ImGui.GetContentRegionMax().Y),
                    new Vector2(500, 500)),
                ImGuiCond.FirstUseEver);

            Vector2 pos = ImGui.GetCursorScreenPos();
            Vector2 sizeAvail = ImGui.GetContentRegionAvail();
            Vector2 size;

            if ((sizeAvail.Y / frame.Height) > (sizeAvail.X / frame.Width))
            {
                size = new Vector2(
                    sizeAvail.X,
                    ((float)frame.Height / frame.Width) * sizeAvail.X);
            }
            else
            {
                size = new Vector2(
                    ((float)frame.Width / frame.Height) * sizeAvail.Y,
                    sizeAvail.Y);
            }

            ImGui.PushClipRect(pos, pos + sizeAvail, false);

            if (ImGui.IsWindowFocused() && (dragging || ImGui.IsMouseHoveringRect(pos, pos + sizeAvail)) && ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                Vector2 mouseDrag = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right, 0);
                ImGui.ResetMouseDragDelta(ImGuiMouseButton.Right);

                offset += mouseDrag;

                zoom = Math.Max(zoom + ImGui.GetIO().MouseWheel * 0.1f, 0.05f);
            }

            pos += offset;

            Vector2 drawSize = size * zoom;
            Vector2 drawPos = pos - (drawSize - size) / 2f;

            ImGui.GetWindowDrawList().AddImage(
                (nint)frameTexture,
                new Vector2(drawPos.X, drawPos.Y),
                new Vector2(drawPos.X + drawSize.X, drawPos.Y + drawSize.Y),
                new Vector2(0, 0),
                new Vector2(1, 1));

            ImGui.PopClipRect();

            // Play animation
            if (playing)
            {
                frameProgress += (frameRate * deltaTime);

                while (frameProgress > 1)
                {
                    frameIdx++;
                    frameProgress -= 1;

                    if (frameIdx >= frameEnd + 1)
                    {
                        if (loops)
                        {
                            frameIdx = frameStart;
                            frameProgress = 0;
                        }
                        else
                        {
                            frameIdx = frameEnd;
                            frameProgress = 0;
                            playing = false;
                        }
                    }
                }
            }
        }
    }

    public void Draw(float deltaTime) { }
}
