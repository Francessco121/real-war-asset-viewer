using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Silk.NET.GLFW;
using Silk.NET.OpenAL;
using Silk.NET.OpenGL;

namespace RealWar.Viewer;

static unsafe class Program
{
    static void Main(string[] args)
    {
        using Glfw glfw = Glfw.GetApi();

        if (!glfw.Init())
            throw new Exception("Failed to init GLFW.");

        glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGL);
        glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        glfw.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        glfw.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
        glfw.WindowHint(WindowHintBool.OpenGLDebugContext, true);

        glfw.SetErrorCallback(OnGlfwError);

        WindowHandle* window = glfw.CreateWindow(1600, 900, "Real War Viewer", null, null);
        if (window == null)
            throw new Exception("Failed to initialize GLFW window!");

        CenterWindow(glfw, window);

        glfw.MakeContextCurrent(window);

        using GL gl = GL.GetApi(glfw.GetProcAddress);

        gl.Enable(EnableCap.DebugOutput);
        gl.Enable(EnableCap.DebugOutputSynchronous);
        gl.DebugMessageCallback(OnOpenGLDebugMessage, null);

        using ALContext alc = ALContext.GetApi(soft: true);
        using AL al = AL.GetApi(soft: true);

        {
            Device* device = alc.OpenDevice("");
            if (device == null)
                throw new Exception("Failed to open OpenAL device.");

            alc.MakeContextCurrent(alc.CreateContext(device, null));

            AudioError error = al.GetError();
            if (error != AudioError.NoError)
                throw new Exception($"OpenAL error: {error}");
        }

        glfw.GetFramebufferSize(window, out int framebufferWidth, out int framebufferHeight);
        gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);

        double lastTime = glfw.GetTime();
        double maxDeltaTime = 1.0 / GetRefreshRate(glfw);

        using (Application app = new Application(glfw, gl, al, window))
        {
            while (!glfw.WindowShouldClose(window))
            {
                double now = glfw.GetTime();
                float deltaTime = (float)(now - lastTime);
                lastTime = now;

                glfw.PollEvents();

                app.Update(deltaTime);
                app.Draw(deltaTime);

                glfw.SwapBuffers(window);

                double startSleepNow = glfw.GetTime();
                double timeToWait = maxDeltaTime - (startSleepNow - now);

                while (timeToWait > 0)
                {
                    Thread.Sleep(0);

                    double sleepNow = glfw.GetTime();
                    timeToWait -= (sleepNow - startSleepNow);
                    startSleepNow = sleepNow;
                }
            }
        }

        glfw.Terminate();
    }

    static int GetRefreshRate(Glfw glfw)
    {
        int bestRefreshRate = 60;

        int numMonitors;
        Silk.NET.GLFW.Monitor** monitors = glfw.GetMonitors(out numMonitors);

        for (int i = 0; i < numMonitors; i++)
        {
            VideoMode* vidMode = glfw.GetVideoMode(monitors[i]);
            bestRefreshRate = Math.Max(bestRefreshRate, vidMode->RefreshRate);
        }

        return bestRefreshRate;
    }

    static void CenterWindow(Glfw glfw, WindowHandle* window)
    {
        int winX, winY;
        glfw.GetWindowPos(window, out winX, out winY);
        int winWidth, winHeight;
        glfw.GetWindowSize(window, out winWidth, out winHeight);

        int numMonitors;
        Silk.NET.GLFW.Monitor** monitors = glfw.GetMonitors(out numMonitors);

        for (int i = 0; i < numMonitors; i++)
        {
            Silk.NET.GLFW.Monitor* monitor = monitors[i];

            int monX, monY;
            glfw.GetMonitorPos(monitor, out monX, out monY);

            VideoMode* vidMode = glfw.GetVideoMode(monitor);
            int monRight = monX + vidMode->Width;
            int monBottom = monY + vidMode->Height;

            if (winX >= monX && winX <= monRight && winY >= monY && winY <= monBottom)
            {
                glfw.SetWindowPos(window,
                    monX + (vidMode->Width / 2) - (winWidth / 2),
                    monY + (vidMode->Height / 2) - (winHeight / 2));
                break;
            }
        }
    }

    static void OnGlfwError(Silk.NET.GLFW.ErrorCode error, string description)
    {
        if (error != Silk.NET.GLFW.ErrorCode.NoError)
            throw new Exception($"GLFW Error ({error}): {description}");
    }

    static void OnOpenGLDebugMessage(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        string msg = Encoding.UTF8.GetString((byte*)message, length);
        Console.WriteLine(msg);
        Console.WriteLine(new StackTrace(true));
    }
}
