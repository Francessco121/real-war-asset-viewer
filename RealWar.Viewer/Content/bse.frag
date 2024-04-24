#version 450 core

layout (location = 0) in vec3 FragPosition;
layout (location = 1) in vec3 FragNormal;
layout (location = 2) in vec2 FragUV;
layout (location = 3) in vec4 FragColor;

layout(location = 0) out vec4 OutColor;

uniform sampler2D Texture;

uniform vec3 LightPosition;
uniform bool Wireframe = false;
uniform bool Textured = true;

void main()
{
    if (Wireframe)
    {
        OutColor = vec4(1, 1, 1, 1);
    }
    else
    {
        // Calculate ambient light
        vec3 ambient = vec3(0.5, 0.5, 0.5);

        // Calculate diffuse light
        vec3 lightDir = normalize(LightPosition - FragPosition);
        float diff = clamp(dot(FragNormal, lightDir), 0.0, 1.0);
        vec3 diffuse = diff * vec3(1, 1, 1); // light color

        // Sample texture
        vec4 texColor;
        if (Textured)
            texColor = texture(Texture, FragUV);
        else
            texColor = vec4(1, 1, 1, 1);

        // Combine ambient, diffuse, and texture
        vec3 result = min(ambient + diffuse, vec3(1, 1, 1)) * FragColor.rgb * texColor.rgb;

        OutColor = vec4(result, FragColor.a * texColor.a);

        if (OutColor.a == 0.0)
            discard;
    }
}
