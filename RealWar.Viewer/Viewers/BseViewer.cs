using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using NativeFileDialogs.Net;
using RealWar.Viewer.Loaders;
using RealWar.Viewer.PostProcessing;
using RealWar.Viewer.Utils;
using Silk.NET.OpenGL;

namespace RealWar.Viewer.Viewers;

unsafe class BseViewer : IViewer
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    readonly struct BseVertex(
        Vector3 position,
        Vector3 normal,
        Vector2 uv,
        Vector3 color)
    {
        public readonly Vector3 Position = position;
        public readonly Vector3 Normal = normal;
        public readonly Vector2 UV = uv;
        public readonly Vector3 Color = color;
    }

    class DrawCmd(int offset, int count, uint flag)
    {
        public readonly int Offset = offset;
        public readonly int Count = count;
        public readonly uint Flag = flag;
    }

    public string Name { get; private set; }
    public bool Open => open;

    bool open = true;

    uint lastFramebufferWidth = 0;
    uint lastFramebufferHeight = 0;

    readonly ArcBallCamera camera = new();

    bool dragging = false;

    bool wireframe = false;
    bool textured = true;

    bool animationEnabled = false;
    bool animationAdvEnabled = false;
    int animationFrame = 0;
    float animationFrameProgress = 0;
    int animationFrameRate = 24;
    bool animationPlaying = false;
    bool animationLoops = true;
    int animationFrameStart = 0;
    int animationFrameEnd = 0;
    bool animationInterpolates = true;
    bool animationInterpolateUvs = false;

    readonly float maxVertComponent;

    readonly List<DrawCmd> opaqueDrawList = new();
    readonly List<DrawCmd> transparentDrawList = new();

    readonly uint shaderProgram;
    readonly uint attribPosition;
    readonly uint attribNormal;
    readonly uint attribUv;
    readonly uint attribColor;
    readonly int uniformProjection;
    readonly int uniformView;
    readonly int uniformWorld;
    readonly int uniformLightPosition;
    readonly int uniformTexture;
    readonly int uniformWireframe;
    readonly int uniformTextured;
    readonly int uniformUseAnimation;
    readonly int uniformAnimationFrame;
    readonly int uniformAnimationFrameProgress;
    readonly int uniformAnimationFrameSize;
    readonly int uniformAnimationFrameCount;
    readonly int uniformAnimationInterpolateUVs;
    readonly int uniformTransparent;

    readonly uint vao;
    readonly uint vbo;
    readonly uint texture;

    readonly uint framebuffer;
    uint colorBuffer;
    readonly uint depthBuffer;
    readonly uint animationPositionBuffer;
    readonly uint animationNormalBuffer;
    readonly uint animationUvBuffer;

    readonly Bse bse;
    readonly Tgc? colorTgc;
    readonly Tgc? alphaTgc;
    readonly GL gl;

    public BseViewer(string name, Bse bse, Tgc? colorTgc, Tgc? alphaTgc, GL gl)
    {
        Name = name;
        this.bse = bse;
        this.colorTgc = colorTgc;
        this.alphaTgc = alphaTgc;
        this.gl = gl;

        // Set up framebuffer
        framebuffer = gl.CreateFramebuffer();

        colorBuffer = gl.CreateTexture(TextureTarget.Texture2D);
        gl.TextureParameter(colorBuffer, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TextureParameter(colorBuffer, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TextureStorage2D(colorBuffer, 1, SizedInternalFormat.Rgb8, 1, 1);
        gl.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, colorBuffer, 0);

        depthBuffer = gl.CreateRenderbuffer();
        gl.NamedRenderbufferStorage(depthBuffer, InternalFormat.Depth24Stencil8, 1, 1);
        gl.NamedFramebufferRenderbuffer(framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, depthBuffer);

        var framebufferStatus = (FramebufferStatus)gl.CheckNamedFramebufferStatus(framebuffer, FramebufferTarget.Framebuffer);
        if (framebufferStatus != FramebufferStatus.Complete)
            throw new Exception($"Framebuffer is not complete: {framebufferStatus}");

        // Set up shader
        shaderProgram = ShaderUtils.LoadShader(
            "Content/bse.vert",
            "Content/bse.frag",
            gl);

        attribPosition = (uint)gl.GetAttribLocation(shaderProgram, "VertPosition");
        attribNormal = (uint)gl.GetAttribLocation(shaderProgram, "VertNormal");
        attribUv = (uint)gl.GetAttribLocation(shaderProgram, "VertUV");
        attribColor = (uint)gl.GetAttribLocation(shaderProgram, "VertColor");
        uniformProjection = gl.GetUniformLocation(shaderProgram, "Projection");
        uniformView = gl.GetUniformLocation(shaderProgram, "View");
        uniformWorld = gl.GetUniformLocation(shaderProgram, "World");
        uniformLightPosition = gl.GetUniformLocation(shaderProgram, "LightPosition");
        uniformTexture = gl.GetUniformLocation(shaderProgram, "Texture");
        uniformWireframe = gl.GetUniformLocation(shaderProgram, "Wireframe");
        uniformTextured = gl.GetUniformLocation(shaderProgram, "Textured");
        uniformUseAnimation = gl.GetUniformLocation(shaderProgram, "UseAnimation");
        uniformAnimationFrame = gl.GetUniformLocation(shaderProgram, "AnimationFrame");
        uniformAnimationFrameProgress = gl.GetUniformLocation(shaderProgram, "AnimationFrameProgress");
        uniformAnimationFrameSize = gl.GetUniformLocation(shaderProgram, "AnimationFrameSize");
        uniformAnimationFrameCount = gl.GetUniformLocation(shaderProgram, "AnimationFrameCount");
        uniformAnimationInterpolateUVs = gl.GetUniformLocation(shaderProgram, "AnimationInterpolateUVs");
        uniformTransparent = gl.GetUniformLocation(shaderProgram, "Transparent");

        // Set up vertex array
        vao = gl.CreateVertexArray();
        vbo = gl.CreateBuffer();

        gl.EnableVertexArrayAttrib(vao, attribPosition);
        gl.EnableVertexArrayAttrib(vao, attribNormal);
        gl.EnableVertexArrayAttrib(vao, attribUv);
        gl.EnableVertexArrayAttrib(vao, attribColor);

        gl.VertexArrayVertexBuffer(vao, 0, vbo, 0, (uint)sizeof(BseVertex));

        gl.VertexArrayAttribFormat(vao, attribPosition, 3, VertexAttribType.Float, false, (uint)Marshal.OffsetOf<BseVertex>("Position"));
        gl.VertexArrayAttribFormat(vao, attribNormal, 3, VertexAttribType.Float, false, (uint)Marshal.OffsetOf<BseVertex>("Normal"));
        gl.VertexArrayAttribFormat(vao, attribUv, 2, VertexAttribType.Float, false, (uint)Marshal.OffsetOf<BseVertex>("UV"));
        gl.VertexArrayAttribFormat(vao, attribColor, 3, VertexAttribType.Float, false, (uint)Marshal.OffsetOf<BseVertex>("Color"));

        gl.VertexArrayAttribBinding(vao, attribPosition, 0);
        gl.VertexArrayAttribBinding(vao, attribNormal, 0);
        gl.VertexArrayAttribBinding(vao, attribUv, 0);
        gl.VertexArrayAttribBinding(vao, attribColor, 0);

        var vertices = new BseVertex[bse.NumPoly * 3];
        maxVertComponent = 0;

        int drawCmdStart = 0;
        uint drawCmdFlag = 0;

        void FlushDrawCmd(int end)
        {
            int count = end - drawCmdStart;
            if (count > 0)
            {
                var cmd = new DrawCmd(drawCmdStart, count, drawCmdFlag);
                if ((drawCmdFlag & 2) != 0)
                    transparentDrawList.Add(cmd);
                else
                    opaqueDrawList.Add(cmd);
            }

            drawCmdStart = end;
        }

        for (int i = 0; i < bse.NumPoly; i++)
        {
            BseTri tri = bse.Polys[i];
            BseTriUv triUv = bse.Uvs[i];
            BseTriColor triColor = bse.Colors[i];
            uint triFlag = bse.Flags[i];

            BseVert v1 = bse.Vertices[tri.V1 / 3];
            BseVert v2 = bse.Vertices[tri.V2 / 3];
            BseVert v3 = bse.Vertices[tri.V3 / 3];

            BseUv uv1 = triUv.V1;
            BseUv uv2 = triUv.V2;
            BseUv uv3 = triUv.V3;

            BseRgb color1 = triColor.V1;
            BseRgb color2 = triColor.V2;
            BseRgb color3 = triColor.V3;

            Vector3 p1 = new Vector3(v1.X, -v1.Z, v1.Y);
            Vector3 p2 = new Vector3(v2.X, -v2.Z, v2.Y);
            Vector3 p3 = new Vector3(v3.X, -v3.Z, v3.Y);

            Vector3 norm = TriUtils.CalculateSurfaceNormal(p1, p2, p3);

            vertices[(i * 3) + 0] = new BseVertex(
                position: p1,
                normal: norm,
                uv: new Vector2(uv1.U, uv1.V),
                color: new Vector3(color1.R / 255.0f, color1.G / 255.0f, color1.B / 255.0f));

            vertices[(i * 3) + 1] = new BseVertex(
                position: p2,
                normal: norm,
                uv: new Vector2(uv2.U, uv2.V),
                color: new Vector3(color2.R / 255.0f, color2.G / 255.0f, color2.B / 255.0f));

            vertices[(i * 3) + 2] = new BseVertex(
                position: p3,
                normal: norm,
                uv: new Vector2(uv3.U, uv3.V),
                color: new Vector3(color3.R / 255.0f, color3.G / 255.0f, color3.B / 255.0f));

            Vector3 maxP = Vector3.Max(Vector3.Abs(p1), Vector3.Max(Vector3.Abs(p2), Vector3.Abs(p3)));
            maxVertComponent = Math.Max(maxVertComponent, maxP.X);
            maxVertComponent = Math.Max(maxVertComponent, maxP.Y);
            maxVertComponent = Math.Max(maxVertComponent, maxP.Z);

            if (triFlag != drawCmdFlag)
            {
                FlushDrawCmd(i * 3);
                drawCmdFlag = triFlag;
            }
        }

        FlushDrawCmd((int)(bse.NumPoly * 3));

        gl.NamedBufferData<BseVertex>(vbo, (nuint)(vertices.Length * sizeof(BseVertex)), vertices,
            VertexBufferObjectUsage.StaticDraw);

        // Set up frames
        if (bse.Frames != null)
        {
            var framePositions = new Vector4[bse.NumPoly * 3 * bse.NumFrames];
            var frameNormals = new Vector4[bse.NumPoly * 3 * bse.NumFrames];
            var frameUvs = new Vector2[bse.NumPoly * 3 * bse.NumFrames];

            for (int i = 0; i < bse.NumFrames; i++)
            {
                BseFrame frame = bse.Frames[i];

                for (int j = 0; j < bse.NumPoly; j++)
                {
                    BseTri tri = bse.Polys[j];

                    BseVert v1 = frame.Verts[tri.V1 / 3];
                    BseVert v2 = frame.Verts[tri.V2 / 3];
                    BseVert v3 = frame.Verts[tri.V3 / 3];

                    BseUv uv1;
                    BseUv uv2;
                    BseUv uv3;

                    if (bse.UvFrames != null)
                    {
                        BseUvFrame uvFrame = bse.UvFrames[i];
                        BseTriUv triUv = uvFrame.Uvs[j];

                        uv1 = triUv.V1;
                        uv2 = triUv.V2;
                        uv3 = triUv.V3;
                    }
                    else
                    {
                        BseTriUv triUv = bse.Uvs[j];

                        uv1 = triUv.V1;
                        uv2 = triUv.V2;
                        uv3 = triUv.V3;
                    }

                    Vector3 p1 = new Vector3(v1.X, -v1.Z, v1.Y);
                    Vector3 p2 = new Vector3(v2.X, -v2.Z, v2.Y);
                    Vector3 p3 = new Vector3(v3.X, -v3.Z, v3.Y);

                    Vector3 norm = TriUtils.CalculateSurfaceNormal(p1, p2, p3);

                    framePositions[(i * bse.NumPoly * 3) + (j * 3) + 0] = new Vector4(p1, 0);
                    framePositions[(i * bse.NumPoly * 3) + (j * 3) + 1] = new Vector4(p2, 0);
                    framePositions[(i * bse.NumPoly * 3) + (j * 3) + 2] = new Vector4(p3, 0);

                    frameNormals[(i * bse.NumPoly * 3) + (j * 3) + 0] = new Vector4(norm, 0);
                    frameNormals[(i * bse.NumPoly * 3) + (j * 3) + 1] = new Vector4(norm, 0);
                    frameNormals[(i * bse.NumPoly * 3) + (j * 3) + 2] = new Vector4(norm, 0);

                    frameUvs[(i * bse.NumPoly * 3) + (j * 3) + 0] = new Vector2(uv1.U, uv1.V);
                    frameUvs[(i * bse.NumPoly * 3) + (j * 3) + 1] = new Vector2(uv2.U, uv2.V);
                    frameUvs[(i * bse.NumPoly * 3) + (j * 3) + 2] = new Vector2(uv3.U, uv3.V);
                }
            }

            animationPositionBuffer = gl.CreateBuffer();
            animationNormalBuffer = gl.CreateBuffer();
            animationUvBuffer = gl.CreateBuffer();

            gl.NamedBufferStorage<Vector4>(animationPositionBuffer, framePositions, (uint)0);
            gl.NamedBufferStorage<Vector4>(animationNormalBuffer, frameNormals, (uint)0);
            gl.NamedBufferStorage<Vector2>(animationUvBuffer, frameUvs, (uint)0);

            gl.ProgramUniform1(shaderProgram, uniformAnimationFrameSize, v0: (int)(bse.NumPoly * 3));
            gl.ProgramUniform1(shaderProgram, uniformAnimationFrameCount, v0: (int)bse.NumFrames);
            gl.ProgramUniform1(shaderProgram, uniformAnimationInterpolateUVs, animationInterpolateUvs ? 1 : 0);

            animationEnabled = true;
            animationFrameStart = 0;
            animationFrameEnd = (int)(bse.NumFrames - 1);
        }

        // Set up texture
        if (colorTgc != null)
        {
            texture = gl.CreateTexture(TextureTarget.Texture2D);
            gl.TextureParameter(texture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TextureParameter(texture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, (uint)colorTgc.Width, (uint)colorTgc.Height);

            LoadTexture(colorTgc, alphaTgc);
        }
        else
        {
            textured = false;
        }

        gl.ProgramUniform1(shaderProgram, uniformTexture, 0);

        // Set up camera
        ResetCamera();
        UpdateCameraMatrices();

        var worldMatrix = Matrix4x4.CreateTranslation(0, 0, 0) * Matrix4x4.CreateScale(bse.Scale ?? 1);
        gl.ProgramUniformMatrix4(shaderProgram, uniformWorld, 1, false, (float*)&worldMatrix);
    }

    public void Dispose()
    {
        gl.DeleteVertexArray(vao);
        gl.DeleteBuffer(vbo);
        gl.DeleteProgram(shaderProgram);
        gl.DeleteFramebuffer(framebuffer);
        gl.DeleteTexture(colorBuffer);
        gl.DeleteRenderbuffer(depthBuffer);
        if (bse.Frames != null)
        {
            gl.DeleteBuffer(animationPositionBuffer);
            gl.DeleteBuffer(animationNormalBuffer);
            gl.DeleteBuffer(animationUvBuffer);
        }
    }

    public void Update(float deltaTime)
    {
        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin(Name, ref open,
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.MenuBar))
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.Button("Reset View"))
                {
                    ResetCamera();
                    UpdateCameraMatrices();
                }

                if (colorTgc != null)
                {
                    if (ImGui.Button("Apply Cammo..."))
                    {
                        LoadCammo();
                    }
                }

                ImGui.Checkbox("Wireframe", ref wireframe);
                if (colorTgc != null)
                    ImGui.Checkbox("Textured", ref textured);

                if (bse.Frames != null)
                {
                    if (ImGui.Checkbox("Animation", ref animationEnabled))
                    {
                        if (!animationEnabled)
                        {
                            animationAdvEnabled = false;
                            animationPlaying = false;
                            animationFrame = 0;
                            animationFrameProgress = 0;
                        }
                    }

                    if (animationEnabled)
                    {
                        if (ImGui.Checkbox("Animation Adv.", ref animationAdvEnabled))
                        {
                            if (!animationAdvEnabled)
                            {
                                animationFrameStart = 0;
                                animationFrameEnd = (int)(bse.NumFrames - 1);
                                animationInterpolates = true;
                                animationInterpolateUvs = false;
                            }
                        }
                    }
                }

                ImGui.EndMenuBar();
            }

            ImGui.Text($"Verts: {bse.NumVerts}");
            ImGui.SameLine();
            ImGui.Text($"| Polys: {bse.NumPoly}");
            ImGui.SameLine();
            ImGui.Text($"| Frames: {bse.NumFrames}");
            if (bse.NumFrames != 0)
            {
                ImGui.SameLine();
                ImGui.Text($"| UV Frames? {(bse.UvFrames == null ? "No" : "Yes")}");
            }
            ImGui.SameLine();
            ImGui.Text($"| Scale: {(bse.Scale == null ? "None" : bse.Scale)}");
            ImGui.SameLine();
            ImGui.Text($"| Texture? {(colorTgc == null ? "No" : "Yes")}");
            if (colorTgc != null)
            {
                ImGui.SameLine();
                ImGui.Text($"| Alpha Texture? {(alphaTgc == null ? "No" : "Yes")}");
            }
            ImGui.SameLine();
            ImGui.Text($"| Transparency? {(transparentDrawList.Count == 0 ? "No" : "Yes")}");

            if (animationEnabled)
            {
                if (ImGui.Button(animationPlaying ? "Stop" : "Play"))
                    animationPlaying = !animationPlaying;

                Vector2 framerateTextSize = ImGui.GetFont().CalcTextSizeA(ImGui.GetFontSize(), float.MaxValue, float.MaxValue, "Framerate |");
                float windowPadding = ImGui.GetStyle().WindowPadding.X;

                ImGui.SameLine();
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetCursorPosX() - 150 - framerateTextSize.X - windowPadding);
                int frame = animationFrame + 1;
                if (ImGui.SliderInt("Frame |", ref frame, 1, (int)bse.NumFrames))
                {
                    animationFrame = frame - 1;
                    animationFrameProgress = 0;
                    animationPlaying = false;
                }
                ImGui.PopItemWidth();

                ImGui.SameLine();
                ImGui.PushItemWidth(80);
                ImGui.InputInt("Framerate |", ref animationFrameRate);
                ImGui.PopItemWidth();

                ImGui.SameLine();
                ImGui.Checkbox("Loop", ref animationLoops);
            }

            if (animationAdvEnabled)
            {
                int frameStart = animationFrameStart + 1;
                ImGui.PushItemWidth(80);
                ImGui.InputInt("Frame Start |", ref frameStart);

                int frameEnd = animationFrameEnd + 1;
                ImGui.SameLine();
                ImGui.InputInt("Frame End |", ref frameEnd);
                ImGui.PopItemWidth();

                animationFrameStart = Math.Clamp(frameStart - 1, 0, Math.Min(animationFrameEnd, (int)(bse.NumFrames - 1)));
                animationFrameEnd = Math.Clamp(Math.Max(animationFrameStart, frameEnd - 1), 0, (int)(bse.NumFrames - 1));

                ImGui.SameLine();
                ImGui.Checkbox("Interpolation", ref animationInterpolates);

                if (animationInterpolates)
                {
                    ImGui.SameLine();
                    ImGui.Checkbox("Interpolate UVs", ref animationInterpolateUvs);
                }
            }

            // Get space for framebuffer
            Vector2 pos = ImGui.GetCursorScreenPos();
            Vector2 size = ImGui.GetContentRegionAvail();

            // Add framebuffer image to draw list
            ImGui.GetWindowDrawList().AddImage(
                (nint)colorBuffer,
                pos,
                new Vector2(pos.X + size.X, pos.Y + size.Y),
                new Vector2(0, 1),
                new Vector2(1, 0));

            // Update framebuffer if necessary
            uint framebufferWidth = (uint)size.X;
            uint framebufferHeight = (uint)size.Y;
            if (framebufferWidth != lastFramebufferWidth || framebufferHeight != lastFramebufferHeight)
            {
                lastFramebufferWidth = framebufferWidth;
                lastFramebufferHeight = framebufferHeight;
                ResizeFramebuffer(framebufferWidth, framebufferHeight);

                camera.AspectRatio = (float)framebufferWidth / (float)framebufferHeight;
                UpdateCameraMatrices();
            }

            // Handle mouse/keyboard inputs
            if (ImGui.IsWindowFocused() && (dragging || ImGui.IsMouseHoveringRect(pos, pos + size)) && ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                dragging = true;

                // Rotation
                Vector2 mouseDrag = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right, 0);
                ImGui.ResetMouseDragDelta(ImGuiMouseButton.Right);

                camera.Yaw += MathUtils.ToRadians(mouseDrag.X * 0.5f);
                camera.Pitch += MathUtils.ToRadians(mouseDrag.Y * 0.5f);

                camera.Pitch = Math.Clamp(camera.Pitch, MathUtils.ToRadians(-89.9f), MathUtils.ToRadians(89.9f));

                // Zoom
                camera.Radius = Math.Max(camera.Radius - ImGui.GetIO().MouseWheel * 20, 0.1f);

                // Translation
                float xMove = 0;
                float yMove = 0;
                float zMove = 0;

                const float cameraSpeed = 120;

                if (ImGui.IsKeyDown(ImGuiKey.W)) zMove += 1;
                if (ImGui.IsKeyDown(ImGuiKey.S)) zMove -= 1;
                if (ImGui.IsKeyDown(ImGuiKey.A)) xMove -= 1;
                if (ImGui.IsKeyDown(ImGuiKey.D)) xMove += 1;
                if (ImGui.IsKeyDown(ImGuiKey.E)) yMove += 1;
                if (ImGui.IsKeyDown(ImGuiKey.Q)) yMove -= 1;

                var moveVec = new Vector3(xMove, yMove, zMove);
                if (moveVec != Vector3.Zero)
                {
                    moveVec = camera.TransformXYRotation(moveVec);
                    moveVec = Vector3.Normalize(moveVec);

                    camera.Target += moveVec * cameraSpeed * deltaTime;
                }

                UpdateCameraMatrices();
            }
            else if (dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                dragging = false;
            }

            // Play animation
            if (animationEnabled && animationPlaying)
            {
                animationFrameProgress += (animationFrameRate * deltaTime);

                if (!animationLoops && animationFrame >= animationFrameEnd)
                {
                    animationFrame = animationFrameStart;
                    animationFrameProgress = 0;
                }

                while (animationFrameProgress > 1)
                {
                    animationFrame++;
                    animationFrameProgress -= 1;

                    if (animationFrame >= animationFrameEnd + (animationLoops && !animationInterpolates ? 1 : 0))
                    {
                        if (animationLoops)
                        {
                            animationFrame = animationFrameStart;
                            animationFrameProgress = 0;
                        }
                        else
                        {
                            animationFrameProgress = 0;
                            animationPlaying = false;
                        }
                    }
                }
            }
        }
    }

    public void Draw(float deltaTime)
    {
        gl.ProgramUniform1(shaderProgram, uniformWireframe, wireframe ? 1 : 0);

        gl.ProgramUniform1(shaderProgram, uniformUseAnimation, animationEnabled && bse.Frames != null ? 1 : 0);
        if (animationEnabled)
        {
            gl.ProgramUniform1(shaderProgram, uniformAnimationFrame, animationFrame);
            gl.ProgramUniform1(shaderProgram, uniformAnimationFrameProgress, animationInterpolates ? animationFrameProgress : 0);
            gl.ProgramUniform1(shaderProgram, uniformAnimationInterpolateUVs, animationInterpolateUvs ? 1 : 0);
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        gl.Viewport(0, 0, lastFramebufferWidth, lastFramebufferHeight);

        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        if (wireframe)
        {
            gl.Disable(EnableCap.CullFace);
            gl.Disable(EnableCap.DepthTest);
        }
        else
        {
            gl.Enable(EnableCap.CullFace);
            gl.Enable(EnableCap.DepthTest);
        }
        gl.Disable(EnableCap.StencilTest);
        gl.Disable(EnableCap.ScissorTest);
        gl.CullFace(TriangleFace.Back);

        gl.PolygonMode(TriangleFace.FrontAndBack, wireframe ? PolygonMode.Line : PolygonMode.Fill);

        if (bse.Frames != null)
        {
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, animationPositionBuffer);
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, animationNormalBuffer);
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 2, animationUvBuffer);
        }

        gl.UseProgram(shaderProgram);
        gl.BindVertexArray(vao);

        gl.BindSampler(0, 0);
        gl.BindTextureUnit(0, texture);

        gl.ProgramUniform1(shaderProgram, uniformTransparent, 0);

        foreach (DrawCmd cmd in opaqueDrawList)
        {
            gl.ProgramUniform1(shaderProgram, uniformTextured, textured && ((cmd.Flag & 0x1) != 0) ? 1 : 0);

            gl.DrawArrays(PrimitiveType.Triangles, cmd.Offset, (uint)cmd.Count);
        }

        if (transparentDrawList.Count > 0)
        {
            gl.ProgramUniform1(shaderProgram, uniformTransparent, 1);

            foreach (DrawCmd cmd in transparentDrawList)
            {
                gl.ProgramUniform1(shaderProgram, uniformTextured, textured && ((cmd.Flag & 0x1) != 0) ? 1 : 0);

                gl.DrawArrays(PrimitiveType.Triangles, cmd.Offset, (uint)cmd.Count);
            }
        }

        gl.UseProgram(0);
        gl.BindVertexArray(0);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        if (bse.Frames != null)
        {
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, 0);
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, 0);
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 2, 0);
        }
    }

    void LoadCammo()
    {
        Dictionary<string, string> filePickerFilter = new()
        {
            { "Real War Textures", "tgc" }
        };

        if (Nfd.OpenDialog(out string? path, filePickerFilter) == NfdStatus.Ok && path != null)
        {
            string colorTexPath = path;
            string alphaTexPath = Path.Join(Path.GetDirectoryName(path), $"{Path.GetFileNameWithoutExtension(path)}A.tgc");

            Tgc cammoColorTgc;
            Tgc? cammoAlphaTgc = null;

            cammoColorTgc = Tgc.Read(File.ReadAllBytes(colorTexPath));
            if (File.Exists(alphaTexPath))
                cammoAlphaTgc = Tgc.Read(File.ReadAllBytes(alphaTexPath));

            LoadTexture(
                colorTgc!,
                alphaTgc,
                cammoColorTgc,
                cammoAlphaTgc);
        }
    }

    void LoadTexture(Tgc colorTgc, Tgc? alphaTgc, Tgc? cammoColorTgc = null, Tgc? cammoAlphaTgc = null)
    {
        // Clone TGC pixels
        var pixels = new ushort[colorTgc.Pixels.Length];
        System.Buffer.BlockCopy(colorTgc.Pixels, 0, pixels, 0, colorTgc.Pixels.Length * 2);

        if (cammoColorTgc != null)
        {
            TexturePostProcessing.ApplyCammo(
                pixels,
                alphaTgc?.Pixels,
                colorTgc.Width,
                colorTgc.Height,
                cammoColorTgc.Pixels,
                cammoAlphaTgc?.Pixels,
                cammoColorTgc.Width,
                cammoColorTgc.Height);
        }

        // Note: This step is not in the base game's code (at least for when hardware acceleration is used) but
        // it seems to be necessary given that other parts of the game do this and transparency is incorrect
        // when hardware acceleration is enabled (for models) on modern hardware...
        TexturePostProcessing.MaskOutBlackPixels(pixels);

        // Upload pixels
        uint[] rgba8888 = ImageUtils.Argb1555ToRgba8888(pixels);

        gl.TextureSubImage2D<uint>(texture, 0, 0, 0, (uint)colorTgc.Width, (uint)colorTgc.Height,
            PixelFormat.Rgba, PixelType.UnsignedInt8888, rgba8888);
    }

    void UpdateCameraMatrices()
    {
        camera.Update();

        ref readonly Matrix4x4 projMatrix = ref camera.ProjectionMatrix;
        ref readonly Matrix4x4 viewMatrix = ref camera.ViewMatrix;
        ref readonly Vector3 cameraPos = ref camera.Position;

        fixed (Matrix4x4* projMatrixPtr = &projMatrix)
            gl.ProgramUniformMatrix4(shaderProgram, uniformProjection, 1, false, (float*)projMatrixPtr);
        fixed (Matrix4x4* viewMatrixPtr = &viewMatrix)
            gl.ProgramUniformMatrix4(shaderProgram, uniformView, 1, false, (float*)viewMatrixPtr);

        fixed (Vector3* cameraPosPtr = &cameraPos)
            gl.ProgramUniform3(shaderProgram, uniformLightPosition, 1, (float*)cameraPosPtr);
    }

    void ResizeFramebuffer(uint width, uint height)
    {
        gl.DeleteTexture(colorBuffer);
        colorBuffer = gl.CreateTexture(TextureTarget.Texture2D);
        gl.TextureParameter(colorBuffer, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TextureParameter(colorBuffer, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TextureStorage2D(colorBuffer, 1, SizedInternalFormat.Rgb8, width, height);
        gl.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, colorBuffer, 0);

        gl.NamedRenderbufferStorage(depthBuffer, InternalFormat.Depth24Stencil8, width, height);
    }

    void ResetCamera()
    {
        camera.Yaw = MathUtils.ToRadians(-45);
        camera.Pitch = MathUtils.ToRadians(25);
        camera.Radius = maxVertComponent * 2f * (bse.Scale ?? 1);
        camera.Target = Vector3.Zero;
    }
}
