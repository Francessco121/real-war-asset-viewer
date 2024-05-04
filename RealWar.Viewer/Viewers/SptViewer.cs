using System;
using System.Numerics;
using ImGuiNET;
using RealWar.Viewer.Loaders;
using RealWar.Viewer.PostProcessing;
using RealWar.Viewer.Utils;
using Silk.NET.OpenGL;

namespace RealWar.Viewer.Viewers;

class SptViewer : IViewer
{
    enum Mode
    {
        ColorOnly,
        FirstPixelAlpha,
        ColorAlpha,
        OneColorManyAlpha
    }

    public string Name { get; private set; }
    public bool Open => open;

    bool open = true;

    bool dragging = false;
    Vector2 offset = Vector2.Zero;
    float zoom = 1;

    Mode mode;

    bool showAdvanced = false;

    int frameCount;
    int frameIdx = 0;
    int frameStart;
    int frameEnd;
    float frameProgress = 0;
    int frameRate = 24;
    bool playing = false;
    bool loops = true;

    uint[]? frames;

    readonly Spt spt;
    readonly GL gl;

    public SptViewer(string name, Spt spt, GL gl)
    {
        Name = name;
        this.spt = spt;
        this.gl = gl;

        // Note: This is just a guess
        if (spt.Frames.Length % 2 == 0 && spt.Frames.Length >= 2 && IsGrayScale(spt.Frames[1].Pixels))
            mode = Mode.ColorAlpha;
        else
            mode = Mode.FirstPixelAlpha;

        GenerateTextures(mode);
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


                ImGui.PushItemWidth(140);
                if (ImGui.BeginCombo("Mode", ModeToString(mode)))
                {
                    foreach (Mode modeVal in Enum.GetValues<Mode>())
                    {
                        if (ImGui.Selectable(ModeToString(modeVal), mode == modeVal))
                        {
                            mode = modeVal;
                            GenerateTextures(modeVal);
                        }
                    }

                    ImGui.EndCombo();
                }
                ImGui.PopItemWidth();

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

            SptFrame frame = spt.Frames[frameIdx];
            uint? frameTexture = frames != null ? frames[frameIdx] : null;

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

            if (frameTexture != null)
            {
                ImGui.GetWindowDrawList().AddImage(
                    (nint)(frameTexture.Value),
                    new Vector2(drawPos.X, drawPos.Y),
                    new Vector2(drawPos.X + drawSize.X, drawPos.Y + drawSize.Y),
                    new Vector2(0, 0),
                    new Vector2(1, 1));
            }

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

    string ModeToString(Mode mode)
    {
        return (mode) switch
        {
            Mode.ColorOnly => "Color Only",
            Mode.FirstPixelAlpha => "First Pix. Alpha",
            Mode.ColorAlpha => "Color & Alpha",
            Mode.OneColorManyAlpha => "Animated Alpha",
            _ => throw new NotImplementedException(),
        };
    }

    void GenerateTextures(Mode mode)
    {
        if (frames != null)
            gl.DeleteTextures(frames);

        if (mode == Mode.ColorOnly || mode == Mode.FirstPixelAlpha)
        {
            frames = new uint[spt.Frames.Length];
            gl.CreateTextures(TextureTarget.Texture2D, n: (uint)frames.Length, frames);

            for (int i = 0; i < spt.Frames.Length; i++)
            {
                SptFrame frame = spt.Frames[i];
                uint texture = frames[i];

                ushort[] pixels;
                if (mode == Mode.ColorOnly)
                {
                    pixels = frame.Pixels;
                }
                else
                {
                    pixels = new ushort[frame.Pixels.Length];
                    System.Buffer.BlockCopy(frame.Pixels, 0, pixels, 0, frame.Pixels.Length * 2);

                    TexturePostProcessing.ApplyFirstPixelAlphaMask(pixels);
                }

                uint[] pixelData = ImageUtils.Argb1555ToRgba8888(pixels);

                InitTexture(texture, frame.Width, frame.Height, pixelData);
            }
        }
        else if (mode == Mode.ColorAlpha)
        {
            frames = new uint[spt.Frames.Length / 2];
            gl.CreateTextures(TextureTarget.Texture2D, n: (uint)frames.Length, frames);

            for (int i = 0, k = 0; i < spt.Frames.Length; i += 2, k++)
            {
                if (i + 1 >= spt.Frames.Length)
                    break;

                SptFrame colorFrame = spt.Frames[i];
                SptFrame alphaFrame = spt.Frames[i + 1];

                uint texture = frames[k];

                uint[] pixelData = TexturePostProcessing.ApplyAlphaMaskToRgba8888(colorFrame.Pixels, alphaFrame.Pixels);

                InitTexture(texture, colorFrame.Width, colorFrame.Height, pixelData);
            }
        }
        else if (mode == Mode.OneColorManyAlpha)
        {
            frames = new uint[spt.Frames.Length - 1];
            gl.CreateTextures(TextureTarget.Texture2D, n: (uint)frames.Length, frames);

            SptFrame colorFrame = spt.Frames[0];
            for (int i = 1, k = 0; i < spt.Frames.Length; i++, k++)
            {
                SptFrame alphaFrame = spt.Frames[i];

                uint texture = frames[k];

                uint[] pixelData = TexturePostProcessing.ApplyAlphaMaskToRgba8888(colorFrame.Pixels, alphaFrame.Pixels);

                InitTexture(texture, alphaFrame.Width, alphaFrame.Height, pixelData);
            }
        }
        else
        {
            throw new NotImplementedException();
        }

        frameCount = frames.Length;
        frameStart = 0;
        frameEnd = frameCount - 1;
        frameProgress = 0;
        frameIdx = 0;

        playing = false;
    }

    void InitTexture(uint texture, uint width, uint height, uint[] pixelData)
    {
        gl.TextureParameter(texture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TextureParameter(texture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, width, height);
        gl.TextureSubImage2D<uint>(texture, 0, 0, 0, width, height,
            PixelFormat.Rgba, PixelType.UnsignedInt8888, pixelData);
    }

    bool IsGrayScale(ushort[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            ushort pixel = pixels[i];

            int r = (pixel >> 10) & 0x1F;
            int g = (pixel >> 5) & 0x1F;
            int b = (pixel >> 0) & 0x1F;

            if (r != g || r != b || g != b)
                return false;
        };

        return true;
    }
}
