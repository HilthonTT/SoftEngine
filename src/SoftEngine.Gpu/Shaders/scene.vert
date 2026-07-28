// The scene's vertex stage: model to clip, plus the per-vertex lighting the Gouraud and
// textured modes interpolate.

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec4 aTangent;

uniform mat4 uModel;            // model to world
uniform mat4 uViewProjection;   // world to clip, device adjustment already folded in
uniform mat4 uModelView;        // model to view, for the view depth fog needs

out vec3 vWorld;
out vec3 vNormal;
out vec2 vTexCoord;
out vec4 vTangent;
out vec3 vLit;        // LitPainter.LitColor at this vertex
out float vViewDepth; // clip-space w before the device adjustment: the view distance

// The same lighting, but taken from the provoking vertex and held constant over the
// triangle — which is what flat shading is. FlatPainter computes one colour per face from
// its centroid and the average of its three normals; this is one colour per face from one
// of its corners. The two differ by however much the light varies across a single triangle,
// which for anything but a coarse mesh under a very near point light is not visible, and
// both are constant per face — the property that makes the mode look the way it does.
flat out vec3 vFlatLit;

void main()
{
    vec4 world = uModel * vec4(aPosition, 1.0);

    vWorld = world.xyz;

    // The plain upper-left 3x3, not the inverse transpose — which is what
    // Vector3.TransformNormal does on the CPU. Matching it matters more than being right
    // about non-uniform scale, since a normal that differed between backends would show up
    // as a lighting difference nobody could attribute to the scene.
    vNormal = mat3(uModel) * aNormal;

    vTexCoord = aTexCoord;
    vTangent = vec4(mat3(uModel) * aTangent.xyz, aTangent.w);

    vLit = lambertLight(vWorld, vNormal);
    vFlatLit = vLit;

    // The view-space distance the perspective divide would produce. Taken from the model-view
    // rather than from gl_Position.w, which the device adjustment leaves alone but which an
    // orthographic projection pins to 1.
    vViewDepth = -(uModelView * vec4(aPosition, 1.0)).z;

    gl_Position = uViewProjection * world;
}
