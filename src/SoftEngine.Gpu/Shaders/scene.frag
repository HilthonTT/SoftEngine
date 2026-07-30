// The scene's fragment stage: one shading mode per painter, selected by uniform.
//
// The modes are the CPU painters — Classic, Flat, Gouraud, Phong, Textured, Material,
// Physically-based — and each branch is a port of the matching IPixelShader. A single
// program rather than seven is worth it here because the difference between them is a
// handful of terms, and one program means one place where the light loop, the shadow
// lookup, the fog and the output encoding are written.

in vec3 vWorld;
in vec3 vNormal;
in vec2 vTexCoord;
in vec4 vTangent;
in vec3 vLit;
in float vViewDepth;
flat in vec3 vFlatLit;

out vec4 fragColor;

uniform int  uMode;
uniform vec3 uEye;

// Whether the frame shades in linear light. When it does not, the lit modes accumulate in
// sRGB-encoded units exactly as the CPU shaders do when Scene.GammaCorrect is off — the
// light scales the encoded colour directly — and the result is decoded once at the end.
uniform bool uGammaCorrect;

// Whether the render target holds unbounded linear floats. When it does the shader writes
// linear light; when it does not it writes sRGB, which is what FrameBuffer.StoreAt does on
// an 8-bit target.
uniform bool uHighDynamicRange;

// The mesh's base colour as sRGB in [0, 1] — the material's diffuse, or the triangle colour
// where the mesh has no material.
uniform vec3 uBaseColor;

// Per-triangle colours, as a buffer indexed by primitive. The demo worlds colour every face
// of a cube differently, which is per-triangle data with nowhere to live on a vertex that
// three triangles share.
uniform samplerBuffer uTriangleColors;
uniform bool uHasTriangleColors;

uniform sampler2D uAlbedoMap;    uniform bool uHasAlbedoMap;
uniform sampler2D uNormalMap;    uniform bool uHasNormalMap;
uniform sampler2D uSpecularMap;  uniform bool uHasSpecularMap;
uniform sampler2D uMetallicMap;  uniform bool uHasMetallicMap;
uniform sampler2D uRoughnessMap; uniform bool uHasRoughnessMap;
uniform sampler2D uEmissiveMap;  uniform bool uHasEmissiveMap;

uniform float uSpecularStrength;
uniform float uShininess;
uniform float uNormalStrength;
uniform float uMetallic;
uniform float uRoughness;
uniform vec3  uEmissive;         // linear, already scaled by EmissiveStrength

uniform samplerCube uEnvironment;
uniform bool  uHasEnvironment;
uniform float uEnvironmentMaxLod;

// Scene.AmbientIntensity. The CPU bakes it into PrefilteredEnvironment when it builds one,
// so the reflection it samples already carries it; here the cube map is the raw sky and the
// scaling has to happen at the sample.
uniform float uAmbientIntensity;

uniform float uOpacity;

// Material.AlphaCutoff: alpha below which the fragment is not drawn at all. 0 is no cutout,
// which is every material that does not ask for one. Distinct from uOpacity — that is a
// mesh-wide blend the transparent pass sorts, this is a per-texel statement about whether
// the surface is there.
uniform float uAlphaCutoff;

// The mesh's base colour, in sRGB units, for this fragment.
vec3 baseColorSrgb()
{
    if (uHasTriangleColors)
    {
        return texelFetch(uTriangleColors, gl_PrimitiveID).rgb;
    }

    return uBaseColor;
}


// MaterialShader.ShadingNormal / PbrShader.ShadingNormal: the interpolated vertex normal,
// tilted by the normal map when there is one and the mesh brought a usable tangent.
vec3 shadingNormal()
{
    vec3 n = dot(vNormal, vNormal) > 1e-12 ? normalize(vNormal) : vec3(0.0, 1.0, 0.0);

    if (!uHasNormalMap || dot(vTangent.xyz, vTangent.xyz) < 1e-12)
    {
        return n;
    }

    // Re-orthogonalize: interpolating a frame across a triangle does not preserve the right
    // angle between the tangent and the normal.
    vec3 tangent = vTangent.xyz - n * dot(n, vTangent.xyz);

    if (dot(tangent, tangent) < 1e-12)
    {
        return n;
    }

    tangent = normalize(tangent);

    vec3 bitangent = cross(n, tangent) * (vTangent.w < 0.0 ? -1.0 : 1.0);

    // No gamma decode: this is geometry, not colour.
    vec3 texel = texture(uNormalMap, vTexCoord).rgb;

    float x = (texel.r * 2.0 - 1.0) * uNormalStrength;
    float y = (texel.g * 2.0 - 1.0) * uNormalStrength;
    float z = texel.b * 2.0 - 1.0;

    vec3 perturbed = tangent * x + bitangent * y + n * z;

    return dot(perturbed, perturbed) > 1e-12 ? normalize(perturbed) : n;
}

// BlinnPhongShader / MaterialShader: ambient plus, per light, a Lambert diffuse term and a
// specular highlight from the half-vector. Returns the two accumulations separately, since
// the way they are folded onto the base colour differs between the linear and encoded paths.
void blinnPhong(vec3 n, float specularStrength, out vec3 diffuse, out vec3 specular)
{
    vec3 view = normalize(uEye - vWorld);

    diffuse = ambientAt(n);
    specular = vec3(0.0);

    for (int i = 0; i < uLightCount; i++)
    {
        vec3 toLight;
        float attenuation;

        if (!sampleLight(i, vWorld, toLight, attenuation))
        {
            continue;
        }

        float nDotL = dot(n, toLight);
        if (nDotL <= 0.0)
        {
            continue;
        }

        // Shadowing scales the light's own contribution; ambient stands in for everything
        // that reaches the surface by other paths, so it survives.
        attenuation *= lightVisibility(i, vWorld, nDotL);
        if (attenuation <= 0.0)
        {
            continue;
        }

        diffuse += nDotL * attenuation * uLightColor[i];

        if (specularStrength > 0.0)
        {
            vec3 h = normalize(toLight + view);
            float nDotH = max(dot(n, h), 0.0);

            // The highlight takes the light's colour, not the surface's: it is the light
            // reflecting off the surface rather than being absorbed by it.
            specular += pow(nDotH, uShininess) * specularStrength * attenuation * uLightColor[i];
        }
    }
}

// Folds accumulated light onto a base colour, in whichever space the frame is shading in.
// The encoded path is the one Scene.GammaCorrect turns off: the light scales the sRGB bytes
// directly and saturates at white, which is what the engine did before linear shading and
// what makes the difference between the two visible side by side.
vec3 combine(vec3 baseSrgb, vec3 diffuse, vec3 specular)
{
    if (uGammaCorrect)
    {
        // Unclamped: a highlight above white is a real measurement, and on an HDR target it
        // survives to the tone map instead of being flattened here.
        return srgbToLinear(baseSrgb) * diffuse + specular;
    }

    return srgbToLinear(clamp(baseSrgb * diffuse + specular, 0.0, 1.0));
}

// PbrShader: Cook-Torrance over a metallic-roughness material, lit by the scene's lights and
// by its environment. Always linear — this model is defined in linear light, and has no
// encoded-byte path of the kind GammaCorrect selects for the older shaders.
vec3 physicallyBased()
{
    vec3 albedo = uHasAlbedoMap
        ? srgbToLinear(texture(uAlbedoMap, vTexCoord).rgb)
        : srgbToLinear(baseColorSrgb());

    // Metallic from blue, roughness from green: the channels glTF packs them into, and the
    // same value in every channel of the greyscale maps an OBJ brings.
    float metallic = uMetallic;
    if (uHasMetallicMap)
    {
        metallic *= texture(uMetallicMap, vTexCoord).b;
    }

    float roughness = uRoughness;
    if (uHasRoughnessMap)
    {
        roughness *= texture(uRoughnessMap, vTexCoord).g;
    }

    metallic = clamp(metallic, 0.0, 1.0);
    roughness = clamp(roughness, 0.0, 1.0);

    float alpha = ggxAlpha(roughness);

    vec3 n = shadingNormal();
    vec3 view = normalize(uEye - vWorld);

    // Clamped away from zero: at exactly grazing incidence every term below divides by it,
    // and a surface seen edge-on is one pixel wide, not a stripe of infinities.
    float nDotV = max(dot(n, view), 1e-4);

    // A dielectric reflects the same few percent whatever colour it is and keeps its albedo
    // for the diffuse it scatters. A metal has no diffuse at all, and tints its reflection
    // with the albedo instead. This one interpolation is the whole difference.
    vec3 f0 = mix(vec3(DIELECTRIC_F0), albedo, metallic);
    vec3 diffuseColor = albedo * (1.0 - metallic);

    vec3 result = vec3(0.0);

    for (int i = 0; i < uLightCount; i++)
    {
        vec3 toLight;
        float attenuation;

        if (!sampleLight(i, vWorld, toLight, attenuation))
        {
            continue;
        }

        float nDotL = dot(n, toLight);
        if (nDotL <= 0.0)
        {
            continue;
        }

        attenuation *= lightVisibility(i, vWorld, nDotL);
        if (attenuation <= 0.0)
        {
            continue;
        }

        vec3 h = normalize(toLight + view);

        float nDotH = max(dot(n, h), 0.0);
        float vDotH = max(dot(view, h), 0.0);

        vec3 fresnel = fresnelSchlick(f0, vDotH);

        // D * V * F is the specular BRDF; the pi is the exposure correction PbrShader
        // documents, applied to both terms so their ratio is untouched.
        float specularWeight = PI * ggxDistribution(nDotH, alpha) * ggxVisibility(nDotV, nDotL, alpha);

        // Whatever Fresnel reflects cannot also be transmitted and scattered back out, so
        // the diffuse term gets what the specular left.
        vec3 brdf = diffuseColor * (vec3(1.0) - fresnel) + specularWeight * fresnel;

        result += (nDotL * attenuation) * (brdf * uLightColor[i]);
    }

    // The environment, as both halves of what it contributes: the light arriving from
    // everywhere at once, and the image of itself the surface reflects.
    //
    // Fresnel with roughness folded in. The plain form assumes a perfect mirror and sends
    // every grazing pixel of a rough surface to white; this keeps the edge brightening a
    // smooth surface deserves and a rough one does not.
    float weight = fresnelWeight(nDotV);
    float ceiling = max(1.0 - roughness, DIELECTRIC_F0);
    vec3 ambientFresnel = f0 + (max(vec3(ceiling), f0) - f0) * weight;

    vec3 irradiance = ambientAt(n);
    result += diffuseColor * irradiance * (vec3(1.0) - ambientFresnel);

    vec3 reflection = 2.0 * dot(n, view) * n - view;

    // The prefiltered environment, as the mip chain of the sky cube map: roughness picks a
    // level, and a rougher surface reflects a blurrier image. It stands in for the CPU's
    // PrefilteredEnvironment, which convolves the same cube map per roughness properly —
    // a box filter down the chain is a coarser convolution of the same thing.
    // Decoded and scaled, because the cube map is the sky as authored: sRGB bytes at full
    // brightness. PrefilteredEnvironment does both when it convolves the same faces on the
    // CPU — sums them in linear light and multiplies by the ambient intensity — so a sample
    // taken raw here would be reflecting the wrong colour at the wrong strength. The
    // AmbientCube fallback needs neither; it was reduced from the same environment and
    // already carries both.
    vec3 incoming = uHasEnvironment
        ? srgbToLinear(textureLod(uEnvironment, reflection, roughness * uEnvironmentMaxLod).rgb) * uAmbientIntensity
        : ambientAt(reflection);

    vec2 response = environmentBrdf(nDotV, roughness);
    result += incoming * (f0 * response.x + vec3(response.y));

    if (uHasEmissiveMap)
    {
        result += uEmissive * srgbToLinear(texture(uEmissiveMap, vTexCoord).rgb);
    }
    else
    {
        result += uEmissive;
    }

    return result;
}

void main()
{
    // Before anything is shaded, and before the depth write: a cut-out texel must leave the
    // depth buffer alone, or it occludes whatever is behind the hole it made.
    if (uAlphaCutoff > 0.0 && texture(uAlbedoMap, vTexCoord).a < uAlphaCutoff)
    {
        discard;
    }

    vec3 linear;

    if (uMode == MODE_PBR)
    {
        linear = physicallyBased();
    }
    else if (uMode == MODE_CLASSIC)
    {
        // Flat per-triangle base colour, no lighting at all.
        linear = srgbToLinear(baseColorSrgb());
    }
    else if (uMode == MODE_FLAT)
    {
        // One colour for the whole triangle. Lighting it per pixel here — which is what
        // deriving a geometric normal from the fragment's derivatives would do — puts a
        // gradient across every face and turns flat shading into a worse Gouraud.
        linear = combine(baseColorSrgb(), vFlatLit, vec3(0.0));
    }
    else if (uMode == MODE_GOURAUD)
    {
        linear = combine(baseColorSrgb(), vLit, vec3(0.0));
    }
    else if (uMode == MODE_TEXTURED)
    {
        vec3 texel = uHasAlbedoMap ? texture(uAlbedoMap, vTexCoord).rgb : baseColorSrgb();
        linear = combine(texel, vLit, vec3(0.0));
    }
    else if (uMode == MODE_MATERIAL)
    {
        vec3 n = shadingNormal();

        float specularStrength = uSpecularStrength;
        if (uHasSpecularMap)
        {
            // A specular map is a mask, not a colour: the red channel is the convention.
            specularStrength *= texture(uSpecularMap, vTexCoord).r;
        }

        vec3 diffuse;
        vec3 specular;
        blinnPhong(n, specularStrength, diffuse, specular);

        vec3 albedo = uHasAlbedoMap ? texture(uAlbedoMap, vTexCoord).rgb : baseColorSrgb();
        linear = combine(albedo, diffuse, specular);
    }
    else
    {
        // MODE_PHONG: per-pixel Blinn-Phong from the interpolated normal and world position.
        vec3 n = dot(vNormal, vNormal) > 1e-12 ? normalize(vNormal) : vec3(0.0, 1.0, 0.0);

        vec3 diffuse;
        vec3 specular;
        blinnPhong(n, uSpecularStrength, diffuse, specular);

        linear = combine(baseColorSrgb(), diffuse, specular);
    }

    linear = applyFog(linear, vViewDepth);

    fragColor = vec4(uHighDynamicRange ? linear : linearToSrgb(linear), uOpacity);
}
