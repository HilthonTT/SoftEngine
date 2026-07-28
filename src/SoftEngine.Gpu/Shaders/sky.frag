// The skybox, as SkyRenderer draws it: the environment sampled along the ray through each
// pixel, with no geometry involved.
//
// A cube would work and is the usual trick, but the direction is computable from the pixel's
// own position, which is both simpler and exact — no seams where the cube's triangles meet,
// and nothing that can be clipped by the near plane. The CPU pass reaches the same conclusion
// for the same reason; this is the same arithmetic against gl_FragCoord.

out vec4 fragColor;

uniform samplerCube uEnvironment;
uniform mat3 uInverseViewRotation;
uniform vec2 uInverseProjectionScale;   // 1 / M11, 1 / M22
uniform vec2 uPixelToNdc;               // 2 / (width - 1), 2 / (height - 1)
uniform float uIntensity;
uniform bool uHighDynamicRange;

vec3 linearToSrgbLocal(vec3 c)
{
    c = clamp(c, 0.0, 1.0);
    bvec3 low = lessThanEqual(c, vec3(0.0031308));
    return mix(1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, 12.92 * c, vec3(low));
}

vec3 srgbToLinearLocal(vec3 c)
{
    bvec3 low = lessThanEqual(c, vec3(0.04045));
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(low));
}

void main()
{
    // The scene is rendered with Y flipped, so that reading the framebuffer back gives rows
    // in the order the software renderer stores them. That makes gl_FragCoord.y count from
    // the top of the image, which is the row index SkyRenderer works in.
    vec2 pixel = gl_FragCoord.xy - 0.5;

    float ndcX = pixel.x * uPixelToNdc.x - 1.0;
    float ndcY = 1.0 - pixel.y * uPixelToNdc.y;

    // The view looks down -Z: the ray through a pixel is the point one unit ahead whose
    // projection lands on it.
    vec3 viewDirection = vec3(ndcX * uInverseProjectionScale.x, ndcY * uInverseProjectionScale.y, -1.0);
    vec3 worldDirection = uInverseViewRotation * viewDirection;

    vec3 linear = srgbToLinearLocal(texture(uEnvironment, worldDirection).rgb) * uIntensity;

    fragColor = vec4(uHighDynamicRange ? linear : linearToSrgbLocal(linear), 1.0);
}
