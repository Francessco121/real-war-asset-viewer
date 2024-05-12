using System;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace RealWar.Viewer;

unsafe class ImGuiController : IDisposable
{
    const string vertexShader = """
        #version 410
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0,1);
        }
        """;

    const string fragmentShader = """
        #version 410
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }
        """;

    uint shaderProgram;
    int uniLocationTex;
    int uniLocationProjMatrix;
    int attribLocationVtxPos;
    int attribLocationVtxUV;
    int attribLocationVtxColor;

    uint vbo;
    uint ebo;

    uint fontTexture;

    readonly GL gl;
    readonly Glfw glfw;
    readonly WindowHandle* window;
    readonly ImGuiIOPtr io;

    public ImGuiController(GL gl, Glfw glfw, WindowHandle* window)
    {
        this.gl = gl;
        this.glfw = glfw;
        this.window = window;

        ImGui.SetCurrentContext(ImGui.CreateContext());

        io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors | ImGuiBackendFlags.RendererHasVtxOffset;

        ImGui.StyleColorsDark();

        CreateDeviceObjects();
    }

    public void Dispose()
    {
        gl.DeleteBuffer(vbo);
        gl.DeleteBuffer(ebo);
        gl.DeleteProgram(shaderProgram);
        gl.DeleteTexture(fontTexture);

        io.Fonts.SetTexID(0);
    }

    public void OnWindowFocus(bool focused)
    {
        io.AddFocusEvent(focused);
    }

    public void OnCursorMoved(float x, float y)
    {
        io.AddMousePosEvent(x, y);
    }

    public void OnMouseButton(MouseButton button, InputAction action, KeyModifiers mods)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, mods == KeyModifiers.Control);
        io.AddKeyEvent(ImGuiKey.ModShift, mods == KeyModifiers.Shift);
        io.AddKeyEvent(ImGuiKey.ModAlt, mods == KeyModifiers.Alt);
        io.AddKeyEvent(ImGuiKey.ModSuper, mods == KeyModifiers.Super);

        io.AddMouseButtonEvent((int)button, action == InputAction.Press);
    }

    public void OnScroll(float xOffset, float yOffset)
    {
        io.AddMouseWheelEvent(xOffset, yOffset);
    }

    public void OnKey(Keys key, InputAction action, KeyModifiers mods)
    {
        if (action != InputAction.Press && action != InputAction.Release)
            return;

        io.AddKeyEvent(ImGuiKey.ModCtrl, mods == KeyModifiers.Control);
        io.AddKeyEvent(ImGuiKey.ModShift, mods == KeyModifiers.Shift);
        io.AddKeyEvent(ImGuiKey.ModAlt, mods == KeyModifiers.Alt);
        io.AddKeyEvent(ImGuiKey.ModSuper, mods == KeyModifiers.Super);

        if (TryMapKey(key, out ImGuiKey imGuiKey))
            io.AddKeyEvent(imGuiKey, action == InputAction.Press);
    }

    public void OnChar(uint c)
    {
        io.AddInputCharacter(c);
    }

    public void Begin(float deltaTime)
    {
        glfw.GetWindowSize(window, out int winWidth, out int winHeight);
        io.DisplaySize = new Vector2(winWidth, winHeight);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = deltaTime;

        if (glfw.GetWindowAttrib(window, WindowAttributeGetter.Focused))
        {
            if (io.WantSetMousePos)
                glfw.SetCursorPos(window, io.MousePos.X, io.MousePos.Y);
        }

        ImGui.NewFrame();
    }

    public void Draw()
    {
        // Let ImGUI render internally
        ImGui.Render();

        // Get rendered ImGUI data
        ImDrawDataPtr drawData = ImGui.GetDrawData();

        int frameBufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int frameBufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);

        if (frameBufferWidth <= 0 || frameBufferHeight <= 0)
            // Don't draw if minimized (width/height are usually zero in this case)
            return;

        // Save current OpenGL state
        int lastPolygonMode = gl.GetInteger(GetPName.PolygonMode);
        Span<int> lastViewport = stackalloc int[4];
        gl.GetInteger(GetPName.Viewport, lastViewport);
        Span<int> lastScissor = stackalloc int[4];
        gl.GetInteger(GetPName.ScissorBox, lastScissor);
        bool lastBlend = gl.IsEnabled(EnableCap.Blend);
        bool lastCullFace = gl.IsEnabled(EnableCap.CullFace);
        bool lastDepthTest = gl.IsEnabled(EnableCap.DepthTest);
        bool lastStencilTest = gl.IsEnabled(EnableCap.StencilTest);
        bool lastScissorTest = gl.IsEnabled(EnableCap.ScissorTest);
        bool lastPrimRestart = gl.IsEnabled(EnableCap.PrimitiveRestart);
        int lastBlendEquation = gl.GetInteger(GetPName.BlendEquation);
        int lastBlendSrcRgb = gl.GetInteger(GetPName.BlendSrcRgb);
        int lastBlendDstRgb = gl.GetInteger(GetPName.BlendDstRgb);
        int lastBlendSrcAlpha = gl.GetInteger(GetPName.BlendSrcAlpha);
        int lastBlendDstAlpha = gl.GetInteger(GetPName.BlendDstAlpha);

        // Init OpenGL state
        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.StencilTest);
        gl.Enable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.PrimitiveRestart);
        gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        gl.ActiveTexture(TextureUnit.Texture0);

        gl.Viewport(0, 0, (uint)frameBufferWidth, (uint)frameBufferHeight);

        float L = drawData.DisplayPos.X;
        float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float T = drawData.DisplayPos.Y;
        float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        if (gl.GetInteger(GLEnum.ClipOrigin) == (int)GLEnum.UpperLeft)
        {
            // Flip vertically if clip origin is top left
            float tmp = T;
            T = B;
            B = tmp;
        }

        ReadOnlySpan<float> orthoMatrix = stackalloc float[4 * 4]
        {
            2.0f / (R - L),     0.0f,               0.0f,   0.0f,
            0.0f,               2.0f / (T - B),     0.0f,   0.0f,
            0.0f,               0.0f,               -1.0f,  0.0f,
            (R + L) / (L - R),  (T + B) / (B - T),  0.0f,   1.0f
        };

        gl.UseProgram(shaderProgram);
        gl.Uniform1(uniLocationTex, 0);
        gl.UniformMatrix4(uniLocationProjMatrix, 1, false, orthoMatrix);

        gl.BindSampler(0, 0);

        uint vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        gl.EnableVertexAttribArray((uint)attribLocationVtxPos);
        gl.EnableVertexAttribArray((uint)attribLocationVtxUV);
        gl.EnableVertexAttribArray((uint)attribLocationVtxColor);
        gl.VertexAttribPointer((uint)attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), (void*)Marshal.OffsetOf<ImDrawVert>("pos"));
        gl.VertexAttribPointer((uint)attribLocationVtxUV, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), (void*)Marshal.OffsetOf<ImDrawVert>("uv"));
        gl.VertexAttribPointer((uint)attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)Marshal.OffsetOf<ImDrawVert>("col"));

        // Draw
        ref readonly Vector2 clipOff = ref drawData.DisplayPos;
        ref readonly Vector2 clipScale = ref drawData.FramebufferScale;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ref readonly ImDrawListPtr cmdList = ref drawData.CmdLists[n];

            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert)),
                (void*)cmdList.VtxBuffer.Data, BufferUsageARB.StreamDraw);
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort)),
                (void*)cmdList.IdxBuffer.Data, BufferUsageARB.StreamDraw);

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                ImDrawCmdPtr cmd = cmdList.CmdBuffer[i];

                if (cmd.UserCallback != nint.Zero)
                    throw new NotImplementedException("ImGUI user callbacks are not implemented.");

                float clipMinX = (cmd.ClipRect.X - clipOff.X) * clipScale.X;
                float clipMaxX = (cmd.ClipRect.Z - clipOff.X) * clipScale.X;
                float clipMinY = (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                float clipMaxY = (cmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                if (clipMaxX <= clipMinX || clipMaxY <= clipMinY)
                    continue;

                gl.Scissor((int)clipMinX, (int)(frameBufferHeight - clipMaxY), (uint)(clipMaxX - clipMinX), (uint)(clipMaxY - clipMinY));

                gl.BindTexture(TextureTarget.Texture2D, (uint)cmd.TextureId);

                gl.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    cmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (void*)(cmd.IdxOffset * sizeof(ushort)),
                    (int)cmd.VtxOffset);
            }
        }

        gl.DeleteVertexArray(vao);

        // Restore OpenGL state
        gl.UseProgram(0);
        gl.BindVertexArray(0);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        GlToggle(EnableCap.Blend, lastBlend);
        GlToggle(EnableCap.CullFace, lastCullFace);
        GlToggle(EnableCap.DepthTest, lastDepthTest);
        GlToggle(EnableCap.StencilTest, lastStencilTest);
        GlToggle(EnableCap.ScissorTest, lastScissorTest);
        GlToggle(EnableCap.PrimitiveRestart, lastPrimRestart);
        gl.BlendEquation((GLEnum)lastBlendEquation);
        gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb,
            (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);

        gl.PolygonMode(TriangleFace.FrontAndBack, (PolygonMode)lastPolygonMode);
        gl.Viewport(lastViewport[0], lastViewport[1], (uint)lastViewport[2], (uint)lastViewport[3]);
        gl.Scissor(lastScissor[0], lastScissor[1], (uint)lastScissor[2], (uint)lastScissor[3]);
    }

    void GlToggle(EnableCap cap, bool enabled)
    {
        if (enabled)
            gl.Enable(cap);
        else
            gl.Disable(cap);
    }

    void CreateDeviceObjects()
    {
        // Create shaders
        uint vertShaderId = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertShaderId, vertexShader);
        gl.CompileShader(vertShaderId);
        CheckShader(vertShaderId);

        uint fragShaderId = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragShaderId, fragmentShader);
        gl.CompileShader(fragShaderId);
        CheckShader(fragShaderId);

        // Link program
        shaderProgram = gl.CreateProgram();
        gl.AttachShader(shaderProgram, vertShaderId);
        gl.AttachShader(shaderProgram, fragShaderId);
        gl.LinkProgram(shaderProgram);

        if (gl.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus) != (int)GLEnum.True)
        {
            string log = gl.GetProgramInfoLog(shaderProgram);
            throw new Exception($"Failed to link ImGUI shader program: {log}");
        }

        gl.DetachShader(shaderProgram, vertShaderId);
        gl.DetachShader(shaderProgram, fragShaderId);
        gl.DeleteShader(vertShaderId);
        gl.DeleteShader(fragShaderId);

        uniLocationTex = gl.GetUniformLocation(shaderProgram, "Texture");
        uniLocationProjMatrix = gl.GetUniformLocation(shaderProgram, "ProjMtx");
        attribLocationVtxPos = gl.GetAttribLocation(shaderProgram, "Position");
        attribLocationVtxUV = gl.GetAttribLocation(shaderProgram, "UV");
        attribLocationVtxColor = gl.GetAttribLocation(shaderProgram, "Color");

        // Create buffers
        vbo = gl.GenBuffer();
        ebo = gl.GenBuffer();

        // Create fonts
        nint texData;
        int texWidth;
        int texHeight;
        io.Fonts.GetTexDataAsRGBA32(out texData, out texWidth, out texHeight);

        fontTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, fontTexture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)texWidth, (uint)texHeight, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, texData.ToPointer());

        io.Fonts.SetTexID((nint)fontTexture);

        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    void CheckShader(uint id)
    {
        if (gl.GetShader(id, ShaderParameterName.CompileStatus) != (int)GLEnum.True)
        {
            ShaderType type = (ShaderType)gl.GetShader(id, ShaderParameterName.ShaderType);
            string log = gl.GetShaderInfoLog(id);

            throw new Exception($"Failed to compile ImGUI {type}: {log}");
        }
    }

    bool TryMapKey(Keys key, out ImGuiKey result)
    {
        ImGuiKey KeyToImGuiKeyShortcut(Keys keyToConvert, Keys startKey1, ImGuiKey startKey2)
        {
            int changeFromStart1 = (int)keyToConvert - (int)startKey1;
            return startKey2 + changeFromStart1;
        }

        result = key switch
        {
            >= Keys.F1 and <= Keys.F24 => KeyToImGuiKeyShortcut(key, Keys.F1, ImGuiKey.F1),
            >= Keys.Keypad0 and <= Keys.Keypad9 => KeyToImGuiKeyShortcut(key, Keys.Keypad0, ImGuiKey.Keypad0),
            >= Keys.A and <= Keys.Z => KeyToImGuiKeyShortcut(key, Keys.A, ImGuiKey.A),
            >= Keys.Number0 and <= Keys.Number9 => KeyToImGuiKeyShortcut(key, Keys.Number0, ImGuiKey._0),
            Keys.ShiftLeft or Keys.ShiftRight => ImGuiKey.ModShift,
            Keys.ControlLeft or Keys.ControlRight => ImGuiKey.ModCtrl,
            Keys.AltLeft or Keys.AltRight => ImGuiKey.ModAlt,
            Keys.SuperLeft or Keys.SuperRight => ImGuiKey.ModSuper,
            Keys.Menu => ImGuiKey.Menu,
            Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow,
            Keys.Left => ImGuiKey.LeftArrow,
            Keys.Right => ImGuiKey.RightArrow,
            Keys.Enter => ImGuiKey.Enter,
            Keys.Escape => ImGuiKey.Escape,
            Keys.Space => ImGuiKey.Space,
            Keys.Tab => ImGuiKey.Tab,
            Keys.Backspace => ImGuiKey.Backspace,
            Keys.Insert => ImGuiKey.Insert,
            Keys.Delete => ImGuiKey.Delete,
            Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown,
            Keys.Home => ImGuiKey.Home,
            Keys.End => ImGuiKey.End,
            Keys.CapsLock => ImGuiKey.CapsLock,
            Keys.ScrollLock => ImGuiKey.ScrollLock,
            Keys.PrintScreen => ImGuiKey.PrintScreen,
            Keys.Pause => ImGuiKey.Pause,
            Keys.NumLock => ImGuiKey.NumLock,
            Keys.KeypadDivide => ImGuiKey.KeypadDivide,
            Keys.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Keys.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Keys.KeypadAdd => ImGuiKey.KeypadAdd,
            Keys.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Keys.KeypadEnter => ImGuiKey.KeypadEnter,
            Keys.GraveAccent => ImGuiKey.GraveAccent,
            Keys.Minus => ImGuiKey.Minus,
            Keys.Equal => ImGuiKey.Equal,
            Keys.LeftBracket => ImGuiKey.LeftBracket,
            Keys.RightBracket => ImGuiKey.RightBracket,
            Keys.Semicolon => ImGuiKey.Semicolon,
            Keys.Apostrophe => ImGuiKey.Apostrophe,
            Keys.Comma => ImGuiKey.Comma,
            Keys.Period => ImGuiKey.Period,
            Keys.Slash => ImGuiKey.Slash,
            Keys.BackSlash => ImGuiKey.Backslash,
            _ => ImGuiKey.None
        };

        return result != ImGuiKey.None;
    }
}
