using System;
using Silk.NET.OpenGL;

namespace RealWar.Viewer;

static class GLExtensions
{
    public static void CheckError(this GL gl)
    {
        GLEnum error = gl.GetError();
        if (error != GLEnum.NoError)
            throw new Exception(error.ToString());
    }
}
