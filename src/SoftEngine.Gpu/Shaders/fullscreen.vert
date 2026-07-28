// A triangle that covers the viewport, generated from the vertex index alone — no buffer, no
// attributes, nothing to bind. Three vertices rather than a quad's four so there is no
// diagonal seam down the middle for a derivative to trip over.
//
// z = w puts it at the far plane, which is where the sky belongs: it is drawn with the depth
// test set to equality, so it lands on exactly the pixels the opaque pass left cleared.

void main()
{
    vec2 corner = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(corner * 2.0 - 1.0, 1.0, 1.0);
}
