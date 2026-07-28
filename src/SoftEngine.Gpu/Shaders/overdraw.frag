// One per fragment, additively blended, into a single-channel float target. The sum at a
// pixel is how many times the frame tried to write it — which is what the overdraw view
// asks, and what the software rasterizer counts inside PutPixel.

out float fragCount;

void main()
{
    fragCount = 1.0;
}
