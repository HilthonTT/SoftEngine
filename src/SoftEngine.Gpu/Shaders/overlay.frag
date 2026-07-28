out vec4 fragColor;

uniform vec3 uColor;            // sRGB in [0, 1], as the overlay colours are authored
uniform bool uHighDynamicRange;

void main()
{
    vec3 c = uColor;

    if (uHighDynamicRange)
    {
        bvec3 low = lessThanEqual(c, vec3(0.04045));
        c = mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(low));
    }

    fragColor = vec4(c, 1.0);
}
