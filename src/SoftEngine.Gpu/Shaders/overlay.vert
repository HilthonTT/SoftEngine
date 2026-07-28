// Overlays — the wireframe pass and the picked-mesh outline. Geometry only: they are drawn
// in one flat colour, so nothing but the position has to reach the fragment stage.

layout(location = 0) in vec3 aPosition;

uniform mat4 uModelViewProjection;

void main()
{
    gl_Position = uModelViewProjection * vec4(aPosition, 1.0);
}
