using System;
using System.Numerics;
using ImGuiNET;
using RealWar.Viewer.Loaders;
using RealWar.Viewer.Utils;
using Silk.NET.OpenGL;

namespace RealWar.Viewer.Viewers;

class TgcViewer : IViewer
{
    public string Name { get; private set; }
    public bool Open => open;

    bool open = true;

    bool dragging = false;
    Vector2 offset = Vector2.Zero;
    float zoom = 1;

    readonly uint texture;

    readonly Tgc tgc;
    readonly GL gl;

    public TgcViewer(string name, Tgc tgc, GL gl)
    {
        Name = name;
        this.tgc = tgc;
        this.gl = gl;

        uint[] pixelData = ImageUtils.Argb1555ToRgba8888(tgc.Pixels);

        texture = gl.CreateTexture(TextureTarget.Texture2D);
        gl.TextureParameter(texture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TextureParameter(texture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, (uint)tgc.Width, (uint)tgc.Height);
        gl.TextureSubImage2D<uint>(texture, 0, 0, 0, (uint)tgc.Width, (uint)tgc.Height,
            PixelFormat.Rgba, PixelType.UnsignedInt8888, pixelData);
    }

    public void Dispose()
    {
        gl.DeleteTexture(texture);
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

                ImGui.EndMenuBar();
            }

            ImGui.Text($"Dimensions: {tgc.Width}x{tgc.Height}");
            ImGui.Text($"Trailer: 0x{tgc.Trailer.ToString("X")}");

            ImGui.SetWindowSize(
                Vector2.Max(
                    new Vector2(tgc.Width * 2, tgc.Height * 2 + ImGui.GetContentRegionMax().Y),
                    new Vector2(128, 128)),
                ImGuiCond.FirstUseEver);

            Vector2 pos = ImGui.GetCursorScreenPos();
            Vector2 sizeAvail = ImGui.GetContentRegionAvail();
            Vector2 size;

            if ((sizeAvail.Y / tgc.Height) > (sizeAvail.X / tgc.Width))
            {
                size = new Vector2(
                    sizeAvail.X,
                    ((float)tgc.Height / tgc.Width) * sizeAvail.X);
            }
            else
            {
                size = new Vector2(
                    ((float)tgc.Width / tgc.Height) * sizeAvail.Y,
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
                (nint)texture,
                new Vector2(drawPos.X, drawPos.Y),
                new Vector2(drawPos.X + drawSize.X, drawPos.Y + drawSize.Y),
                new Vector2(0, 0),
                new Vector2(1, 1));

            ImGui.PopClipRect();
        }
    }

    public void Draw(float deltaTime) { }
}
