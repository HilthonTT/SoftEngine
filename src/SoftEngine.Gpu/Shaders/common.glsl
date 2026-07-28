// Shared declarations and shading maths for the scene shaders.
//
// Every function here is a port of one in SoftEngine.Core, and the pairing is deliberate:
// the two backends have to agree on what a scene looks like, not merely both look plausible.
// Where the CPU uses a lookup table (ColorSpace) this evaluates the curve the table was built
// from, which agrees to well under an 8-bit step. Where it uses a precomputed table that
// cannot be evaluated cheaply (BrdfLut) the deviation is called out at the function.

#define MAX_LIGHTS 16
#define MAX_CASCADES 4

// Shading modes, matching SoftEngine.Gpu.GpuShadingMode.
#define MODE_CLASSIC  1
#define MODE_FLAT     2
#define MODE_GOURAUD  3
#define MODE_PHONG    4
#define MODE_TEXTURED 5
#define MODE_MATERIAL 6
#define MODE_PBR      7

#define FOG_NONE   0
#define FOG_LINEAR 1
#define FOG_EXP    2

const float PI = 3.14159265358979323846;
const float DIELECTRIC_F0 = 0.04;

// ---------------------------------------------------------------------------------------
// Colour space
// ---------------------------------------------------------------------------------------

// The sRGB transfer function, in both directions. ColorSpace tabulates exactly these.
vec3 srgbToLinear(vec3 c)
{
    bvec3 low = lessThanEqual(c, vec3(0.04045));
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(low));
}

vec3 linearToSrgb(vec3 c)
{
    c = clamp(c, 0.0, 1.0);
    bvec3 low = lessThanEqual(c, vec3(0.0031308));
    return mix(1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, 12.92 * c, vec3(low));
}

// ---------------------------------------------------------------------------------------
// Lights
// ---------------------------------------------------------------------------------------

uniform int  uLightCount;
uniform vec3 uLightVector[MAX_LIGHTS];   // direction toward a directional light, else its position
uniform vec3 uLightAxis[MAX_LIGHTS];     // the beam direction of a spot
uniform vec3 uLightColor[MAX_LIGHTS];    // colour times intensity, linear
uniform vec4 uLightParams[MAX_LIGHTS];   // x: 1/range^2, y: cos(outer), z: 1/cone falloff, w: directional?
uniform int  uShadowLight;               // which light the shadow map was rendered from, or -1

// PointLight.Attenuation, term for term.
//
// A light with no range does not fall off at all — it reaches everything at full strength,
// which is how this engine's point lights behaved before they could be given one and what
// keeps a scene lit without tuning a number against the model's scale first. Plain
// inverse-square here instead would darken every scene whose light happens to sit further
// from the geometry than one unit, which is nearly all of them.
//
// Given a range, the falloff is inverse-square windowed so it reaches exactly zero at the
// range rather than trailing off forever.
float attenuate(float distanceSquared, float inverseRangeSquared)
{
    if (inverseRangeSquared <= 0.0)
    {
        return 1.0;
    }

    float t = distanceSquared * inverseRangeSquared;

    if (t >= 1.0)
    {
        return 0.0;
    }

    float window = 1.0 - t * t;

    return window * window / (t + 1.0);
}

// SpotLight.Cone: a ramp between the inner and outer half-angles, squared so it leaves the
// inner edge smoothly rather than with a visible crease.
float coneFalloff(float cosAngle, float cosOuter, float inverseConeFalloff)
{
    float t = clamp((cosAngle - cosOuter) * inverseConeFalloff, 0.0, 1.0);
    return t * t;
}

// ShaderLight.Sample: the unit vector toward the light and how much of it arrives.
// Returns false when none of it does, so the caller can skip the rest.
bool sampleLight(int i, vec3 world, out vec3 toLight, out float attenuation)
{
    vec4 params = uLightParams[i];

    if (params.w > 0.5)
    {
        toLight = uLightVector[i];
        attenuation = 1.0;
        return true;
    }

    vec3 delta = uLightVector[i] - world;
    float distanceSquared = dot(delta, delta);

    if (distanceSquared < 1e-12)
    {
        toLight = vec3(0.0, 1.0, 0.0);
        attenuation = 1.0;
        return true;
    }

    toLight = delta * inversesqrt(distanceSquared);
    attenuation = attenuate(distanceSquared, params.x);

    if (attenuation <= 0.0)
    {
        return false;
    }

    // -2 is ShaderLight's "not a spot" sentinel.
    if (params.y > -2.0)
    {
        attenuation *= coneFalloff(dot(uLightAxis[i], -toLight), params.y, params.z);
    }

    return attenuation > 0.0;
}

// ---------------------------------------------------------------------------------------
// Ambient
// ---------------------------------------------------------------------------------------

uniform vec3 uAmbient[6];   // +X -X +Y -Y +Z -Z

// AmbientCube.Evaluate: the squared components of the normal, which sum to 1, weighting the
// three faces it points toward.
vec3 ambientAt(vec3 n)
{
    vec3 w = n * n;

    vec3 alongX = n.x >= 0.0 ? uAmbient[0] : uAmbient[1];
    vec3 alongY = n.y >= 0.0 ? uAmbient[2] : uAmbient[3];
    vec3 alongZ = n.z >= 0.0 ? uAmbient[4] : uAmbient[5];

    return w.x * alongX + w.y * alongY + w.z * alongZ;
}

// ---------------------------------------------------------------------------------------
// Shadows
// ---------------------------------------------------------------------------------------

uniform sampler2DArray uShadowMap;
uniform mat4  uShadowMatrix[MAX_CASCADES];
uniform vec2  uShadowBias[MAX_CASCADES];   // x: constant, y: slope-scaled
uniform int   uShadowCascades;             // 0 when the scene casts none
uniform float uShadowStrength;
uniform float uShadowResolution;
uniform bool  uShadowSoft;

bool shadowOccluded(int cascade, vec2 uv, float depth)
{
    // Off-map texels have never been drawn into, so nothing there can cast a shadow.
    if (uv.x < 0.0 || uv.x >= 1.0 || uv.y < 0.0 || uv.y >= 1.0)
    {
        return false;
    }

    return depth > texture(uShadowMap, vec3(uv, float(cascade))).r;
}

// ShadowMap.Visibility: 1 is fully lit, 0 fully shadowed. Points outside every cascade are
// treated as lit — the map only covers the range the cascades were fitted to.
float shadowVisibility(vec3 world, float nDotL)
{
    if (uShadowCascades <= 0 || uShadowStrength <= 0.0)
    {
        return 1.0;
    }

    for (int cascade = 0; cascade < uShadowCascades; cascade++)
    {
        // Parallel projection: w is 1, so the transform needs no divide.
        vec4 light = uShadowMatrix[cascade] * vec4(world, 1.0);

        vec2 uv = light.xy * 0.5 + 0.5;
        float depth = light.z;

        if (depth < 0.0 || depth > 1.0)
        {
            continue;
        }

        // A point right at a cascade's edge has its filter taps hanging off the buffer,
        // which read as lit and leave a bright seam along the boundary. Every cascade but
        // the last therefore hands the point to the next one out a margin early.
        float margin = cascade + 1 < uShadowCascades
            ? (uShadowSoft ? 2.0 : 1.0) / uShadowResolution
            : 0.0;

        if (uv.x < margin || uv.x >= 1.0 - margin || uv.y < margin || uv.y >= 1.0 - margin)
        {
            continue;
        }

        // sqrt(1 - cos^2)/cos is the tangent of the incidence angle — how much depth one
        // texel of surface spans. Clamped, or a surface edge-on to the light asks for
        // unbounded bias and its shadow detaches completely.
        float cosine = clamp(abs(nDotL), 0.05, 1.0);
        float bias = uShadowBias[cascade].x
            + uShadowBias[cascade].y * min(sqrt(1.0 - cosine * cosine) / cosine, 4.0);

        float compare = depth - bias;
        float occlusion;

        if (uShadowSoft)
        {
            float texel = 1.0 / uShadowResolution;
            float occluded = 0.0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    occluded += shadowOccluded(cascade, uv + vec2(dx, dy) * texel, compare) ? 1.0 : 0.0;
                }
            }

            occlusion = occluded * (1.0 / 9.0);
        }
        else
        {
            occlusion = shadowOccluded(cascade, uv, compare) ? 1.0 : 0.0;
        }

        return 1.0 - occlusion * uShadowStrength;
    }

    return 1.0;
}

// How much of light i reaches a point, shadowing included.
float lightVisibility(int i, vec3 world, float nDotL)
{
    return i == uShadowLight ? shadowVisibility(world, nDotL) : 1.0;
}

// ---------------------------------------------------------------------------------------
// Lambert accumulation — LitPainter.LitColor, the term the vertex-lit painters interpolate
// ---------------------------------------------------------------------------------------

vec3 lambertLight(vec3 world, vec3 normal)
{
    vec3 n = dot(normal, normal) > 1e-12 ? normalize(normal) : vec3(0.0, 1.0, 0.0);
    vec3 total = ambientAt(n);

    for (int i = 0; i < uLightCount; i++)
    {
        vec3 toLight;
        float attenuation;

        if (!sampleLight(i, world, toLight, attenuation))
        {
            continue;
        }

        float nDotL = dot(n, toLight);
        if (nDotL <= 0.0)
        {
            continue;
        }

        attenuation *= lightVisibility(i, world, nDotL);
        if (attenuation <= 0.0)
        {
            continue;
        }

        total += nDotL * attenuation * uLightColor[i];
    }

    return total;
}

// ---------------------------------------------------------------------------------------
// GGX — the microfacet model the physically-based path is built on
// ---------------------------------------------------------------------------------------

float ggxAlpha(float roughness)
{
    float clamped = clamp(roughness, 0.03, 1.0);
    return clamped * clamped;
}

float ggxDistribution(float nDotH, float alpha)
{
    float a2 = alpha * alpha;
    float d = nDotH * nDotH * (a2 - 1.0) + 1.0;

    return a2 / max(PI * d * d, 1e-9);
}

// Height-correlated Smith visibility, with the specular denominator already folded in.
float ggxVisibility(float nDotV, float nDotL, float alpha)
{
    float a2 = alpha * alpha;

    float lambdaV = nDotL * sqrt(nDotV * nDotV * (1.0 - a2) + a2);
    float lambdaL = nDotV * sqrt(nDotL * nDotL * (1.0 - a2) + a2);

    return 0.5 / max(lambdaV + lambdaL, 1e-9);
}

float fresnelWeight(float cosine)
{
    float f = clamp(1.0 - cosine, 0.0, 1.0);
    float f2 = f * f;

    return f2 * f2 * f;
}

vec3 fresnelSchlick(vec3 f0, float cosine)
{
    return f0 + (vec3(1.0) - f0) * fresnelWeight(cosine);
}

// The split-sum environment BRDF.
//
// This is the one place the GPU path does not mirror the CPU's: BrdfLut precomputes the
// integral by importance sampling and reads it back from a table, which would mean shipping
// the table to the GPU every frame or duplicating the sampler in GLSL. Karis' analytic fit
// is used instead. It tracks the tabulated version to within about a percent over the whole
// (n·v, roughness) domain, which is below what the 8-bit output can show.
vec2 environmentBrdf(float nDotV, float roughness)
{
    const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
    const vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);

    vec4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * nDotV)) * r.x + r.y;

    return vec2(-1.04, 1.04) * a004 + r.zw;
}

// ---------------------------------------------------------------------------------------
// Fog — RasterState.ApplyFog, blended in linear light after shading
// ---------------------------------------------------------------------------------------

uniform int   uFogMode;
uniform float uFogA;     // linear: End / (End - Start); exponential: density
uniform float uFogB;     // linear: -1 / (End - Start)
uniform vec3  uFogColor;

vec3 applyFog(vec3 color, float viewDepth)
{
    if (uFogMode == FOG_NONE)
    {
        return color;
    }

    float visibility = uFogMode == FOG_LINEAR
        ? clamp(uFogA + uFogB * viewDepth, 0.0, 1.0)
        : exp(-uFogA * viewDepth);

    return mix(uFogColor, color, visibility);
}
