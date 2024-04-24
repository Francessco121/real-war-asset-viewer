using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using NativeFileDialogs.Net;
using RealWar.Viewer.Loaders;
using RealWar.Viewer.Viewers;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace RealWar.Viewer;

unsafe class Application : IDisposable
{
    static readonly Dictionary<string, string> filePickerFilter = new()
    {
        { "Real War Assets", "bse,tgc" }
    };

    readonly List<IViewer> viewers = new();

    readonly Glfw glfw;
    readonly GL gl;
    readonly WindowHandle* window;
    readonly ImGuiController imGui;

    public Application(Glfw glfw, GL gl, WindowHandle* window)
    {
        this.glfw = glfw;
        this.gl = gl;
        this.window = window;

        imGui = new ImGuiController(gl, glfw, window);
        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        glfw.SetFramebufferSizeCallback(window, OnWindowFramebufferSizeChanged);
        glfw.SetWindowFocusCallback(window, OnWindowFocusChanged);
        glfw.SetCursorPosCallback(window, OnCursorPosChanged);
        glfw.SetKeyCallback(window, OnKeyChanged);
        glfw.SetCharCallback(window, OnChar);
        glfw.SetMouseButtonCallback(window, OnMouseButtonChanged);
        glfw.SetScrollCallback(window, OnScrollChanged);

        gl.ClearColor(0, 0, 0, 1);
    }

    public void Dispose()
    {
        foreach (IViewer viewer in viewers)
            viewer.Dispose();

        imGui.Dispose();
    }

    public void Update(float deltaTime)
    {
        imGui.Begin(deltaTime);

        ImGui.DockSpaceOverViewport();

        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Open Files..."))
                    OpenFile();

                ImGui.EndMenu();
            }

            if (ImGui.Button("Close All"))
            {
                foreach (IViewer viewer in viewers)
                    viewer.Dispose();
                viewers.Clear();
            }

            ImGui.EndMainMenuBar();
        }

        for (int i = viewers.Count - 1; i >= 0; i--)
        {
            if (!viewers[i].Open)
            {
                viewers[i].Dispose();
                viewers.RemoveAt(i);
            }
        }

        foreach (IViewer viewer in viewers)
        {
            viewer.Update(deltaTime);
        }
    }

    public void Draw(float deltaTime)
    {
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        foreach (IViewer viewer in viewers)
            viewer.Draw(deltaTime);

        imGui.Draw();
    }

    void OpenFile()
    {
        if (Nfd.OpenDialogMultiple(out string[]? paths, filePickerFilter) == NfdStatus.Ok && paths != null)
        {
            foreach (string path in paths)
            {
                switch (Path.GetExtension(path).ToLower())
                {
                    case ".tgc":
                        OpenTgc(path);
                        break;
                    case ".bse":
                        OpenBse(path);
                        break;
                }
            }
        }
    }

    void OpenTgc(string path)
    {
        var tgc = Tgc.Read(File.ReadAllBytes(path));
        viewers.Add(new TgcViewer(GetUniqueViewerName(Path.GetFileName(path)), tgc, gl));
    }

    void OpenBse(string path)
    {
        var bse = Bse.Read(File.ReadAllBytes(path));

        string colorTexPath = Path.ChangeExtension(path, ".tgc");
        string alphaTexPath = Path.Join(Path.GetDirectoryName(path), $"{Path.GetFileNameWithoutExtension(path)}A.tgc");

        Tgc? colorTgc = null;
        Tgc? alphaTgc = null;

        if (File.Exists(colorTexPath))
            colorTgc = Tgc.Read(File.ReadAllBytes(colorTexPath));
        if (File.Exists(alphaTexPath))
            alphaTgc = Tgc.Read(File.ReadAllBytes(alphaTexPath));

        viewers.Add(new BseViewer(GetUniqueViewerName(Path.GetFileName(path)), bse, colorTgc, alphaTgc, gl));
    }

    string GetUniqueViewerName(string name)
    {
        if (!viewers.Any(v => v.Name == name))
            return name;

        int i = 2;
        string newName;
        do
        {
            newName = $"{name} ({i++})";
        } while (viewers.Any(v => v.Name == newName));

        return newName;
    }

    void OnWindowFramebufferSizeChanged(WindowHandle* window, int width, int height)
    {
        gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    void OnWindowFocusChanged(WindowHandle* window, bool focused)
    {
        imGui.OnWindowFocus(focused);
    }

    void OnCursorPosChanged(WindowHandle* window, double x, double y)
    {
        imGui.OnCursorMoved((float)x, (float)y);
    }

    void OnKeyChanged(WindowHandle* window, Keys key, int scanCode, InputAction action, KeyModifiers mods)
    {
        imGui.OnKey(key, action, mods);
    }

    void OnChar(WindowHandle* window, uint codepoint)
    {
        imGui.OnChar(codepoint);
    }

    void OnMouseButtonChanged(WindowHandle* window, MouseButton button, InputAction action, KeyModifiers mods)
    {
        imGui.OnMouseButton(button, action, mods);
    }

    void OnScrollChanged(WindowHandle* window, double offsetX, double offsetY)
    {
        imGui.OnScroll((float)offsetX, (float)offsetY);
    }
}
