namespace SoftEngine.Cli.Options;

internal static class UsageText
{
    public static void Print()
    {
        Console.WriteLine("""
            SoftEngine headless renderer — renders a model or a saved scene to a PNG.

              softengine <input> [options]

            The input is a model (.obj, .dae, .gltf, .glb) or a scene document (.json) written
            by the viewer's "Save scene as…".

            Output
              -o, --output <path>   PNG to write (default: the input's name with .png)
              -w, --width <px>      render width  (default 1920)
              -h, --height <px>     render height (default 1080)
                  --ss <n>          supersample n× and average down, 1-4 (default 1)
                  --oit             resolve transparency per pixel instead of by sorting the
                                    transparent triangles — correct where they intersect each
                                    other, and where a small one straddles a large one
                  --stats           print triangle, pixel and timing counts

            Where it renders
                  --backend <name>  auto, cpu, gpu or trace (default auto)
                  --gpu             shorthand for --backend gpu
                  --cpu             shorthand for --backend cpu
                  --adapter <which> which GPU to render on where there is more than one:
                                    "high" (discrete), "low" (integrated) or "auto"
                  --gpu-info        print the graphics adapter, if any, and exit

              auto uses a graphics adapter when one is there and the software rasterizer when
              it is not. gpu says so explicitly and falls back with a reason — an OpenGL served
              by a CPU implementation (llvmpipe, GDI Generic, SwiftShader) is reported as no
              adapter, since rendering through one is slower than rendering without it.

            Reference rendering
                  --trace           path-trace the frame instead of rasterizing it: real
                                    interreflection, real ambient occlusion, ray-traced
                                    shadows with no bias to tune — and minutes, not
                                    milliseconds
                  --samples <n>     paths per pixel (default 16); implies --trace
                  --bounces <n>     bounces of indirect light (default 3, 0 for direct
                                    lighting only); implies --trace
                  --physical        put direct and bounced light on the same scale, instead
                                    of matching the rasterizer's exposure for direct light

            Baked indirect light
                  --bake            measure the scene's bounce light into a grid of probes
                                    before rasterizing, instead of standing in for it with
                                    the environment's six directional averages
                  --bake-resolution <n>
                                    probes along the world's longest axis (default 12)
                  --bake-rays <n>   paths traced out of each probe (default 128)
                  --bake-bounces <n>
                                    bounces each of those paths may take (default 2)

            Shading
              -p, --painter <name>  none, classic, flat, gouraud, phong, textured, material, pbr
                                    (default gouraud)
                  --filter <mode>   texture filtering: nearest, bilinear (default),
                                    trilinear, which blends the two mip levels a surface
                                    falls between instead of stepping between them, or
                                    anisotropic, which measures the two axes of a pixel's
                                    texture footprint apart and spreads several taps
                                    across the longer one — a floor seen edge-on stays
                                    sharp into the distance instead of blurring
                  --fill <mode>     how triangles are filled: scanline (default), which
                                    walks the two edges of each and fills between them, or
                                    half-space, which classifies blocks of pixels against
                                    the three edge functions. Both draw the same pixels
                  --post <list>     comma-separated: ssr, ssao, bloom, tonemap, fxaa,
                                    vignette. ssr reflects the scene in the surfaces that
                                    reflect it, and needs the cpu backend to record what
                                    each one is made of
                  --shadows         render a shadow map from the scene's first light
                  --cascades <n>    shadows fitted to n slices of the view distance, 1-4
                  --no-sky          leave the background cleared instead of drawing a sky
                  --env <path>      light the scene with a panorama: .hdr keeps its full
                                    range, .png is projected as the 8-bit image it is
                  --environment-size <n>
                                    cube face resolution for --env (default: derived)
                  --hdr-sky         build the procedural sky in linear light, with a sun
                                    hundreds of times brighter than white instead of a
                                    white disc
                  --view <name>     present a buffer instead of the shaded image:
                                    depth, normals, overdraw, shadow, occlusion, mip

            Camera
                  --camera x,y,z    an explicit camera position
                  --yaw <deg>       bearing around the model  (default 0)
                  --pitch <deg>     elevation above it        (default 15)
                  --zoom <factor>   multiplies the framed distance; below 1 moves closer
              -t, --time <seconds>  how far into the model's animation to render

            Sequences
                  --frames <n>      render n frames into a numbered sequence
                                    (frame.0000.png, frame.0001.png, …)
                  --fps <rate>      frames per second the sequence represents (default 30),
                                    which is how far the animation advances between frames
                  --turntable <deg> degrees of yaw swept across the whole sequence; 360 is a
                                    full turn
                  --shutter <f>     motion-blur each frame by this fraction of its own motion
                                    (0.5 is a film shutter); needs --frames to have anything
                                    to measure

              A sequence is one PNG per frame. Turning it into a video is ffmpeg's job:
                ffmpeg -framerate 30 -i frame.%04d.png -pix_fmt yuv420p out.mp4

            Overlays
                  --wireframe       draw triangle edges over the shading
                  --grid            draw the ground grid
                  --axes            draw the world axes
                  --no-cull         draw back faces too

            A scene document may also be applied over a model with --scene <path>, which is how
            you render the same saved setup against a re-exported version of its model.

            Textures decode from PNG only: this front-end supplies the engine's own codec rather
            than a platform image library, so a model with JPEG maps renders untextured and says
            how many it skipped.
            """);
    }
}
