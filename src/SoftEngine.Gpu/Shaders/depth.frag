// Nothing to write: the depth attachment is the whole output of the shadow pass. A core
// profile still wants a fragment stage to link against, and this one has exactly one job
// beyond existing — letting a cutout caster punch its holes into the map.
//
// A leaf that is a hole in the picture has to be a hole in the shadow too, or the tree is
// lit through a canopy that shades the ground as a solid disc.

in vec2 vTexCoord;

uniform sampler2D uAlphaMask;
uniform float uAlphaCutoff;   // 0 is no cutout, which is every ordinary caster

void main()
{
    // Short-circuited, so an ordinary caster never samples the placeholder bound to the
    // unit — and the branch is uniform across the draw, not per fragment.
    if (uAlphaCutoff > 0.0 && texture(uAlphaMask, vTexCoord).a < uAlphaCutoff)
    {
        discard;
    }
}
