#version 450 core

layout (location = 0) in vec3 VertPosition;
layout (location = 1) in vec3 VertNormal;
layout (location = 2) in vec2 VertUV;
layout (location = 3) in vec3 VertColor;

layout (location = 0) out vec3 FragPosition;
layout (location = 1) out vec3 FragNormal;
layout (location = 2) out vec2 FragUV;
layout (location = 3) out vec4 FragColor;

uniform mat4 Projection;
uniform mat4 View;
uniform mat4 World;

uniform bool UseAnimation = false;
uniform int AnimationFrame;
uniform float AnimationFrameProgress;
uniform int AnimationFrameSize;
uniform int AnimationFrameCount;
uniform int AnimationFrameStart;
uniform bool AnimationInterpolateUVs;

uniform bool Transparent;

struct AnimationFrameVertex 
{
    vec4 Position;
    vec4 UV;
};

layout (binding = 0, std430) readonly buffer animationPositions
{
    vec4 framePositions[];
};
layout (binding = 1, std430) readonly buffer animationNormals
{
    vec4 frameNormals[];
};
layout (binding = 2, std430) readonly buffer animationUvs
{
    vec2 frameUvs[];
};

void main()
{
    vec3 vertPosition;
    vec3 vertNormal;
    vec2 vertUv;

    if (UseAnimation)
    {
        int frameVertIdx = (AnimationFrame * AnimationFrameSize) + gl_VertexID;
        int nextFrame = ((AnimationFrame + 1) - AnimationFrameStart) % (AnimationFrameCount - AnimationFrameStart) + AnimationFrameStart;
        int nextFrameVertIdx = (nextFrame * AnimationFrameSize) + gl_VertexID;

        vertPosition = mix(vec3(framePositions[frameVertIdx]), vec3(framePositions[nextFrameVertIdx]), AnimationFrameProgress);
        vertNormal = mix(vec3(frameNormals[frameVertIdx]), vec3(frameNormals[nextFrameVertIdx]), AnimationFrameProgress);

        if (AnimationInterpolateUVs)
            vertUv = mix(vec2(frameUvs[frameVertIdx]), vec2(frameUvs[nextFrameVertIdx]), AnimationFrameProgress);
        else
            vertUv = vec2(frameUvs[frameVertIdx]);
    }
    else
    {
        vertPosition = VertPosition;
        vertNormal = VertNormal;
        vertUv = VertUV;
    }

    gl_Position = Projection * View * World * vec4(vertPosition, 1.0);

    FragPosition = vec3(World * vec4(vertPosition, 1.0));
    FragNormal = mat3(transpose(inverse(World))) * vertNormal;
    FragUV = vertUv;
    FragColor = vec4(VertColor, 1.0);

    if (Transparent)
    {
        // TODO: not sure what value this is supposed to be
        FragColor.a = 0.5;
    }
}
