using System;
using System.IO;
using Silk.NET.OpenGL;

namespace RealWar.Viewer.Utils;

public static class ShaderUtils
{
    public static uint LoadShader(string vertexPath, string fragmentPath, GL gl)
    {
        // Read shaders
        string vertexShader = File.ReadAllText(vertexPath);
        string fragmentShader = File.ReadAllText(fragmentPath);

        // Create shaders
        uint vertShaderId = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertShaderId, vertexShader);
        gl.CompileShader(vertShaderId);
        CheckShader(vertexPath, vertShaderId, gl);

        uint fragShaderId = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragShaderId, fragmentShader);
        gl.CompileShader(fragShaderId);
        CheckShader(fragmentPath, fragShaderId, gl);

        // Link program
        uint shaderProgram = gl.CreateProgram();
        gl.AttachShader(shaderProgram, vertShaderId);
        gl.AttachShader(shaderProgram, fragShaderId);
        gl.LinkProgram(shaderProgram);

        if (gl.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus) != (int)GLEnum.True)
        {
            string log = gl.GetProgramInfoLog(shaderProgram);
            string programName = Path.Join(
                Path.GetDirectoryName(vertexPath),
                Path.GetFileNameWithoutExtension(vertexPath));
            throw new Exception($"Failed to link shader program {programName}: {log}");
        }

        // Detach and delete now-unneeded shaders
        gl.DetachShader(shaderProgram, vertShaderId);
        gl.DetachShader(shaderProgram, fragShaderId);
        gl.DeleteShader(vertShaderId);
        gl.DeleteShader(fragShaderId);

        return shaderProgram;
    }

    static void CheckShader(string name, uint id, GL gl)
    {
        if (gl.GetShader(id, ShaderParameterName.CompileStatus) != (int)GLEnum.True)
        {
            ShaderType type = (ShaderType)gl.GetShader(id, ShaderParameterName.ShaderType);
            string log = gl.GetShaderInfoLog(id);

            throw new Exception($"Failed to compile {type} {name}: {log}");
        }
    }
}
