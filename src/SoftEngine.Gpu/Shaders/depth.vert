// The shadow pass: position only, straight into one cascade's clip space.

layout(location = 0) in vec3 aPosition;

uniform mat4 uLightViewProjection;   // model to cascade clip, device adjustment folded in

void main()
{
    gl_Position = uLightViewProjection * vec4(aPosition, 1.0);
}
