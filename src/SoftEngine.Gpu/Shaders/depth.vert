// The shadow pass: position into one cascade's clip space, plus the UV a cutout caster's
// mask is read at. The texture coordinate costs nothing when no cutout is bound — the
// fragment stage's discard is behind a uniform branch that is false for every ordinary mesh.

layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uLightViewProjection;   // model to cascade clip, device adjustment folded in

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = uLightViewProjection * vec4(aPosition, 1.0);
}
