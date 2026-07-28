# SoftEngine

A **software 3D rasterizer** written in C#. The entire pipeline — model transforms, projection, culling, clipping, scanline rasterization, z-buffering and shading — runs on the CPU with no GPU or graphics-API dependency. A WinForms front-end renders live into a bitmap so you can orbit models, switch shading modes, and watch per-frame render statistics.

It can also be told to fill the frame on a **graphics adapter** instead, through OpenGL, and draw
the same scene the same way — see [Rendering on the GPU](#rendering-on-the-gpu). The software path
remains the point of the project and the default everywhere; the GPU one is there to be switched to,
and to be compared against.

![Skull model (31k triangles) rendered with Gouraud shading](docs/screenshots/skull.png)

| ![Elephant model (26k triangles, 5 meshes) with Gouraud shading](docs/screenshots/elephant.png) | ![Parrot model (7k triangles) with Gouraud shading](docs/screenshots/parrot.png) |
| :--: | :--: |
| Elephant — 26k triangles across 5 meshes | Parrot — 7k triangles |

## What it does

- Loads and renders 3D models (Wavefront `.obj`, Collada `.dae`, **glTF 2.0** `.gltf`/`.glb`) and procedural primitives in real time.
- Rasterizes triangles with a generic scanline filler and a depth (z) buffer.
- Skips **meshes hidden behind other meshes** before transforming a single one of their vertices, by rasterizing the frame's largest occluders into a small depth pyramid first.
- Supports several shading modes — wireframe, solid, flat, Gouraud, Phong, textured, full material and **physically-based** — selectable at runtime.
- Lights a scene with **any number of coloured lights** — directional, point with distance falloff, and spot.
- Rasterizes into an **HDR linear float target**, so highlights brighter than white survive to the post-process stack.
- Surrounds the scene with an **environment cube map**, drawn as a skybox and reduced to the ambient light the painters use.
- Casts **shadows** with a shadow-map pass rendered from the light's point of view, split into **cascades** fitted to slices of the camera's own view distance, and **screen-space ambient occlusion** for the contact detail a shadow map cannot resolve.
- Shades **materials**: albedo, tangent-space normal and specular maps, loaded from a model's `.mtl`.
- Deforms **skinned meshes** over a **scene graph** of transforms, played from keyframed animation clips — all imported from Collada.
- Runs a **post-process stack** over the finished frame — bloom, tone mapping, FXAA, vignette.
- Anti-aliases by **supersampling**: render at a multiple of the display resolution and average down.
- Shades **metallic-roughness materials** with a Cook-Torrance microfacet model, lit by the scene's lights and by a split-sum approximation of its environment.
- Answers clicks by **ray-casting the world** rather than reading the frame, outlines what it hits, and lets it be **moved, turned and stretched by dragging** the transform handles — **snapped to a grid** and **undoable**.
- Saves and reopens a **scene as a JSON file** you can read and edit: camera, lights, per-mesh transforms, shading and post-processing.
- Renders **headlessly from the command line** to a PNG, at any resolution, with no window and no GPU.
- Presents the frame's own **intermediate buffers** — depth, normals, overdraw, the shadow map, the occlusion pyramid — in place of the shaded image.
- Provides an interactive arc-ball camera, WASD fly controls, gizmos (world axes, ground grid), and a live stats overlay.
- Ships a **graphics debugger** — event list, object table and per-pixel history — built on the renderer's own instrumentation, and able to **step back through earlier frames**.

| ![Shadow mapping](docs/screenshots/shadows.png) | ![Normal mapping](docs/screenshots/normal-mapping.png) |
| :--: | :--: |
| Shadow mapping — one depth pass from the light | Normal mapping — the same two cubes, 12 triangles each; only the left one has a normal map |

## Shading modes

| Mode | Painter | Description |
| --- | --- | --- |
| **None** | — | Geometry only (combine with the wireframe overlay to see edges). |
| **Classic** | `ClassicPainter` | Flat per-triangle base color, no lighting. |
| **Flat** | `FlatPainter` | One Lambert (N·L) light per triangle from its centroid normal. |
| **Gouraud** | `GouraudPainter` | Per-vertex Lambert light interpolated across the triangle. |
| **Phong** | `PhongPainter` | Per-pixel Blinn-Phong from an interpolated world position and normal. |
| **Textured** | `TexturedPainter` | Perspective-correct texturing with Gouraud lighting, bilinear filtering and mip-maps. |
| **Material** | `MaterialPainter` | Per-pixel albedo, normal and specular maps over Blinn-Phong — the full material path. |
| **Physically based** | `PbrPainter` | Cook-Torrance metallic-roughness, lit by the scene's lights and by its environment. |

A `WireFramePainter` overlay (Liang–Barsky homogeneous line clipping) can be drawn on top of any mode.

## Lighting

A scene's lights are a list, and every lit painter sums over all of them. Each carries a
colour as well as an intensity, so a warm key and a cool fill produce a surface whose lit
side and shadowed side differ in hue rather than only in brightness — and the sum is never
clamped, because two lights on one surface really do deliver twice the light.

| Light | Falls off | Notes |
| --- | --- | --- |
| [`DirectionalLight`](src/SoftEngine.Core/Scenes/Lights/DirectionalLight.cs) | Never | Parallel rays; the sun. |
| [`PointLight`](src/SoftEngine.Core/Scenes/Lights/PointLight.cs) | With distance, if given a `Range` | Windowed inverse-square, reaching exactly zero at the range rather than trailing off forever. `Range` is infinite by default, which is the no-falloff behaviour the engine had before. |
| [`SpotLight`](src/SoftEngine.Core/Scenes/Lights/SpotLight.cs) | With distance and angle | Two cone angles, so the beam's edge ramps instead of stepping — a single angle aliases in the lighting, where no amount of supersampling can reach it. |

The `ILight` interface is not what a shader talks to. Every light is flattened once per
frame into a [`ShaderLight`](src/SoftEngine.Core/Shading/ShaderLight.cs) — a struct of plain
floats — and the frame's set into a [`LightSet`](src/SoftEngine.Core/Shading/LightSet.cs),
so the per-pixel loop is a branch on a field rather than a virtual call that also forfeits
inlining. The array is reused across frames.

Only the first light casts a shadow: the shadow map is one depth buffer taken from one point
of view, so a second shadowed light would need a second pass and a second buffer.

## High dynamic range

An 8-bit render target cannot hold a value above white. A specular glint five times paper
white and one exactly at it are the same pixel by the time anything downstream sees them —
which means bloom deciding what is "bright" and tone mapping "compressing the range" are both
working on an image whose brights were flattened before they arrived.

`Scene.HighDynamicRange` rasterizes into a linear float buffer instead
([`FrameBuffer.SetHighDynamicRange`](src/SoftEngine.Core/Buffers/FrameBuffer.cs)). A shader
returns a [`LinearColor`](src/SoftEngine.Core/Shading/LinearColor.cs) — linear light with no
ceiling — and the value is clamped once, at the very end of the frame, after the effects have
had it. It pairs with `GammaCorrect`, which is the path where the shaders produce light rather
than pre-encoded bytes and so the only one with a range above white to keep.

Two things moved into linear light along with it, because that is where mixing light is
defined: **alpha blending** and **fog**. Half of a full-intensity channel encodes to about
188, not to 128 — the latter is a good deal darker than half the light.

## Environment and ambient

`Scene.Environment` is a [`CubeMap`](src/SoftEngine.Core/Geometry/CubeMap.cs), and it does two
jobs.

[`SkyRenderer`](src/SoftEngine.Core/Pipeline/SkyRenderer.cs) draws it behind the scene. There
is no cube to rasterize: a skybox drawn as geometry is really just a way of getting a
direction interpolated per pixel, and here the direction comes straight from the pixel's
position through the inverse projection — no seams where the cube's own triangles meet, and
nothing that can be clipped by the near plane. It runs *between* the opaque and transparent
passes, filling only pixels whose depth is still the cleared value: after the opaque fill so
it shades nothing that was covered, and before the transparent one because that blends
without writing depth, so a sky drawn last would paint over the glass rather than behind it.

The same map also becomes the ambient term, as an
[`AmbientCube`](src/SoftEngine.Core/Shading/AmbientCube.cs) — six directional averages instead
of one constant. A flat ambient says a ceiling and a floor in the same room receive the same
light from their surroundings, which is never true: one faces the sky and the other faces the
ground. Averaging each face and blending the three a normal points toward is the cheapest
correction that says otherwise. The per-pixel painters evaluate it with the *shading* normal,
so a normal map shapes the ambient the same way it shapes the lights.

[`SkyBox.Gradient`](src/SoftEngine.Core/Geometry/SkyBox.cs) generates one procedurally — zenith,
horizon, ground and a sun disc — so a scene can have a sky, and therefore directional ambient,
with no asset to load.

## Shadow mapping

Shadows come from a second, depth-only render of the world from where the light stands
([`Pipeline/Shadows/ShadowMapRenderer.cs`](src/SoftEngine.Core/Pipeline/Shadows/ShadowMapRenderer.cs)).
Shading a point then means projecting it with the same matrix and comparing: if something
nearer to the light already occupies that direction, the point is in shadow.

- The light gets an **orthographic** projection ([`OrthographicProjection`](src/SoftEngine.Core/Scenes/Projections/OrthographicProjection.cs)) sized to a sphere around what it covers, so the map works from any angle. Point lights are approximated as directional — accurate while the light sits outside the scene.
- The pass is the main pipeline with everything that doesn't affect occlusion removed: no colour, no lighting, no varyings. It uses a **bounding-box edge-function rasterizer**, split into contiguous bands of rows so it parallelizes without locking the depth buffer.
- **Phong and Material** sample the map per pixel; **Flat, Gouraud and Textured** fold the visibility into their per-vertex intensity. Ambient light is never shadowed, so a shadowed surface darkens rather than going black.
- **Bias is measured in shadow-map texels of depth**, not raw normalized depth — one texel of error means the same thing in a 2-unit skull and a 1500-unit elephant, so it does not need retuning per scene or per resolution. A slope term scales it with the light's incidence angle.
- Transparent and hidden meshes are excluded: something you can see through should not block the light, and a mesh dropped from the frame should not leave its shadow behind.

`Scene.Shadows` ([`ShadowSettings`](src/SoftEngine.Core/Scenes/ShadowSettings.cs)) controls resolution, bias, 3×3 PCF filtering, shadow strength, and the cascades below.

### Cascades

One map over a whole scene spends its resolution uniformly, which puts the texels where they
do least good. Perspective makes a shadow ten units from the eye cover a hundred times the
pixels of one five hundred units away, and a single map gives them the same number of texels:
the near shadow comes out as a staircase while the far one is finer than anything can see.

`ShadowSettings.CascadeCount` splits the pass into up to four depth buffers, each fitted to a
slice of the camera's own view distance. Four decisions make the difference between cascades
that help and cascades that flicker:

- **The slices are divided by a blend of two schemes.** Evenly by distance gives the near
  slice — where nearly all the pixels are — the same span as the far one. Evenly by *ratio*,
  each slice a fixed multiple of the last, puts the first boundary a few units in front of the
  eye. `SplitBlend` interpolates between them, weighted toward the ratio.
- **Each cascade is fitted to a sphere, not a box.** A box fitted to a slice's eight corners
  changes size as the camera turns, and every shadow edge in the frame breathes with it. A
  sphere around the same corners depends only on the slice's dimensions, which rotating does
  not change.
- **The fit is snapped to whole texels.** Otherwise the light-space grid slides continuously as
  the camera moves, each frame re-dicing every shadow edge into different texels — which reads
  as crawling, and is far more visible in motion than the aliasing it comes from.
- **A cascade only rasterizes the casters that can reach it.** Under a parallel projection
  that is a question about perpendicular distance to the light axis and nothing else. It is
  where the extra passes pay for themselves: the near cascade covers a few units and rejects
  nearly everything, so three cascades over a long scene cost well under three times one map.

Which cascade shades a point is decided by **containment**, not by its view depth: the cascades
are nested, so the first one that covers the point is also the sharpest one that does. That
keeps `ShadowMap.Visibility` a function of world position alone, which is what lets the same
call work from a vertex-lit painter and a per-pixel one without either knowing cascades exist.
Every cascade but the last hands a point over a filter's width early, so the seam between two
of them is not a bright line of taps that fell off the edge of a buffer.

Two honest limits. Cascades are slices of a view frustum, so the standalone shadow-map API
produces a single map when it is called without a camera rather than guessing at one — and a
**parallel projection** stays on the single-map path, because its shadow map already covers the
view uniformly, which is the very thing cascades exist to fix.

The **Shadow map** buffer view shows every cascade side by side, nearest on the left, each
tinted so the sequence is readable at a glance.

## Materials and normal mapping

Every mesh carries a [`Material`](src/SoftEngine.Core/Geometry/Material.cs): a diffuse
colour, an albedo map, a tangent-space normal map, a specular mask, and the two numbers
that shape its highlight. `Mesh.Texture` is the albedo map under its older name, so nothing
that predates materials had to change.

Normal mapping adds surface detail finer than a vertex without adding a single triangle. It
needs a per-vertex UV frame, which [`TangentBuilder`](src/SoftEngine.Core/Geometry/TangentBuilder.cs)
derives by solving each triangle's edges against its UV deltas and accumulating the result
per vertex — including the ±1 handedness that mirrored UV islands flip. Frames are built
in the painter's `Prepare`, before the parallel paint phase, exactly as mip chains are.

The OBJ importer reads `Kd`, `Ks`, `Ns`, `d`/`Tr`, `map_Kd`, `map_Ks` and the normal map
under all of its spellings (`map_Bump`, `bump`, `norm`).
[`NormalMapBuilder`](src/SoftEngine.Core/Geometry/NormalMapBuilder.cs) converts a height map
into a normal map, since `map_Bump` was specified as one and is used as the other.

## Physically-based shading

![Eighteen spheres: roughness left to right, metalness bottom to top](docs/screenshots/pbr-spheres.png)

*One albedo and one lighting setup. Roughness runs left to right, metalness bottom to top —
and the top row reads as a different **material** rather than as the bottom row at another
brightness, which is the whole point of the model.*

[`PbrPainter`](src/SoftEngine.Core/Rasterization/Painters/PbrPainter.cs) and
[`PbrShader`](src/SoftEngine.Core/Rasterization/PbrShader.cs) shade a metallic-roughness
material with the Cook-Torrance microfacet model. What separates it from the Blinn-Phong path
is not the number of terms but what they are answerable to: specular strength and shininess
are two dials with no units, tuned under one light and wrong again when it moves, where
roughness and metalness describe the *surface* and hold under any lighting.

Three functions do the work, and everything else is one of them evaluated somewhere
([`Ggx`](src/SoftEngine.Core/Shading/Ggx.cs)):

| Term | What it says |
| --- | --- |
| **D** — Trowbridge-Reitz distribution | What fraction of the microfacets face a given direction. Its long tail is why a real highlight has a bright core fading into a wide haze rather than an edge. |
| **V** — height-correlated Smith visibility | How much the surface shadows and masks itself at grazing angles, carried with the specular denominator already folded in. Height-correlated because masking and shadowing happen on the same surface. |
| **F** — Schlick Fresnel | How reflectivity climbs toward 1 edge-on. A dielectric reflects the same 4% whatever colour it is; a metal has no diffuse at all and tints its reflection with the albedo. That one interpolation is the entire difference between the rows above. |

Roughness is squared into the model's α before use — the Disney mapping, which makes the
visible change per unit of roughness roughly even.

**The environment is half the lighting**, and it arrives through the split-sum approximation.
[`PrefilteredEnvironment`](src/SoftEngine.Core/Shading/PrefilteredEnvironment.cs) convolves the
cube map with the GGX lobe once per roughness, so a surface looks up what it reflects with a
single sample instead of integrating a hemisphere per pixel; level 0 is the source map itself,
because a mirror wants the sharpest image available rather than a blurred copy of it.
[`BrdfLut`](src/SoftEngine.Core/Shading/BrdfLut.cs) is the other half — the BRDF integrated
against a *white* environment, which depends on nothing but `n·v` and roughness because F0
factors out of it into a scale and a bias. Two numbers per texel, one 32×32 table, every
material in every scene.

**One deliberate deviation.** The physical BRDF divides diffuse by π, and every other painter
here multiplies albedo by `n·l` with no such divisor — so an identical scene would render
about three times darker the moment you clicked a different radio button. The whole BRDF is
therefore scaled by π, which is the same as saying the engine's lights carry irradiance with
the 1/π already folded in. It changes the exposure, never the ratio of diffuse to specular,
which is the part that has to be right.

Maps degrade one at a time, exactly as `MaterialPainter`'s do: metallic from a map's blue
channel and roughness from its green (the channels glTF packs them into), falling back to the
material's scalars, falling back to a mid-grey dielectric lit from the triangle colour. So the
mode can be switched on over any scene in the viewer, not only ones authored for it.

## glTF 2.0

[`GltfImporter`](src/SoftEngine.Core/Geometry/Gltf/GltfImporter.cs) reads both forms of the
format: the JSON one (`.gltf`, with buffers and images beside it or inline as data URIs) and
the binary container (`.glb`).

glTF is the format this engine already had the shading model for. Collada carries geometry,
skins and clips, but describes surfaces with the specular-and-shininess vocabulary of a decade
earlier — so the physically-based painter could only ever be pointed at procedural demos or
hand-set scalars. A glTF material is metallic-roughness, and its packed texture puts roughness
in green and metalness in blue, which is exactly what
[`Material.MetallicMap`](src/SoftEngine.Core/Geometry/Material.cs) and `RoughnessMap` already
read: those channels were chosen for this format before there was a reader for it. One packed
map is assigned to both properties rather than decoded twice.

| Read | Not read |
| --- | --- |
| The default scene's node hierarchy, with instancing | Morph targets (`weights` animation channels) |
| Every mesh primitive as its own mesh, one per material | Cameras and `KHR_lights_punctual` |
| Triangles, strips and fans; indexed or not | `KHR_texture_transform` |
| Metallic-roughness materials, base-colour / normal / metallic-roughness / emissive maps, `KHR_materials_emissive_strength` | A second UV set (`TEXCOORD_1`) |
| Skins with their inverse bind matrices | Draco and meshopt compression — **refused by name**, see below |
| Animation samplers in all three interpolation modes | |

Four details worth naming:

- **The matrix convention is the opposite of Collada's, and needs no work.** glTF stores a
  matrix column-major for the column-vector convention — element (row *r*, column *c*) at index
  *c*·4 + *r*. This engine composes row-vector matrices, which are the transpose, and
  transposing a column-major array *is* reading it row-major. So the sixteen floats go straight
  into `Matrix4x4`'s constructor untouched, where Collada's have to be transposed. The two
  files disagree, not the two engines.
- **Sparse accessors are decoded.** An accessor that stores only the elements differing from a
  base is rare outside morph targets, and ignoring it renders the base — which for positions is
  the wrong *shape*, not a missing detail. It fails silently, which is why it is implemented.
- **All three interpolation modes are honoured**, which is why
  [`TrackInterpolation`](src/SoftEngine.Core/Animation/TrackInterpolation.cs) exists. `STEP` is
  how a blinking light or a swapped-out prop is authored, and blending it produces a value the
  animator never wrote. `CUBICSPLINE` stores three values per key — the tangent in, the value,
  the tangent out — so reading it as a plain value array both misreads the values *and* triples
  the apparent key count; it is sampled as the cubic Hermite it is.
- **A file requiring compressed geometry is refused with the extension's name.** A Draco
  primitive's accessors describe a compressed stream, and reading them as vertices produces a
  mesh made of noise — which looks like a bug in the renderer rather than an unread file.

Decoding images stays out of the Core, as it does for OBJ: the importer resolves all three
places an image can live — a file beside the model, a data URI, a stretch of the GLB's binary
chunk — down to bytes, and the front-end supplies one decoder for them.

A glTF mesh instanced by several nodes becomes several engine meshes **sharing one vertex
array**. Nothing in the pipeline writes to a vertex, so a second instance of a dense model
costs one small object rather than the whole model again. The triangle colours are not shared,
because recolouring one instance and finding its twin recoloured is a trap the engine's own
primitives already lay once.

## Picking

![The picked sphere outlined in amber over the shaded frame](docs/screenshots/picking.png)

[`ScenePicker`](src/SoftEngine.Core/Picking/ScenePicker.cs) answers *what did I just click on*
by intersecting the world with a ray, not by reading anything the frame drew.

The alternative — rendering an identifier per pixel and looking one up — is what a GPU
renderer usually does, and it answers a subtly different question: what was *drawn* there, at
the resolution it was drawn at, after culling and the depth test. A ray answers what is
*there*. It costs nothing per frame, works on geometry the frame never rasterized, reports the
exact triangle and the point on it rather than a pixel's worth of it, and — being pure
geometry with no framebuffer in it — can be tested without rendering anything at all.

- The ray is the pipeline run backwards, and its screen mapping matches
  [`FrameBuffer.ToScreen3`](src/SoftEngine.Core/Buffers/FrameBuffer.cs) exactly. It goes
  through the pixel's **centre**, which is where the rasterizer decided coverage; aiming at the
  corner would put the two answers half a pixel apart along every silhouette in the frame —
  exactly where a person is most likely to click.
- Whole meshes are rejected against their bounding spheres first, so a click on a scene of
  forty thousand cubes tests a handful of them. The sphere follows the whole scene-graph chain,
  not just the mesh's own scale.
- **Möller-Trumbore, both faces.** A click is a question about geometry, not about winding: a
  single-sided test would make an inward-facing wall unclickable for no reason the user can see.
- Hidden and fully-faded meshes are skipped; transparent ones are not. Something you can see
  through is still something you can point at.

`Settings.HighlightedMesh` outlines the hit in amber, and it walks the frame's own draw lists
rather than the mesh — so a mesh culled out of the frame highlights nothing, which is the
honest answer.

### Dragging what you picked

[`TransformGizmo`](src/SoftEngine.Core/Gizmos/TransformGizmo.cs) puts handles on the picked
mesh: three arrows to move it, three rings to turn it, three arms with a box on the end to
stretch it.

It is built out of the two things picking already provides. A handle is just more geometry to
test the click's ray against — an axis is a line segment, a ring is a circle in a plane. And
once a handle is grabbed, the same ray answers the question the drag is actually asking: how
far *along* this axis, or how far *around* it, is the cursor now? So the gizmo reads nothing
from the frame, works on geometry the frame never rasterized, and can be driven — and tested —
with no rendering at all.

- **The handles are sized in screen terms**, a fixed fraction of the viewport's height
  converted back to world units at the gizmo's own distance. A gizmo measured in world units is
  unusable at both ends of the range this renderer covers: a speck on a 1500-unit elephant, and
  swallowing a 2-unit skull.
- **The grab frame is frozen when the drag starts.** The gizmo is drawn at the mesh's own
  origin, so translating the mesh moves it — and measuring each step against the moved frame
  feeds the mesh's motion back into the number that caused it, running it away from the cursor.
  The line being dragged along stands still; only the drawing follows.
- **Every step is measured from where the drag began**, not from the step before it, so a
  cursor that wanders off the handle and comes back leaves the mesh where the pointer is rather
  than where the accumulated error put it.
- **The handles are drawn without a depth test**, alone among the gizmos. A grid or a skeleton
  is describing where things are, so hiding behind them is right; a manipulator is not — you
  grab it with a ray that knows nothing about depth, so a handle buried inside the mesh it is
  attached to would be a control you can use and cannot see.
- **A parented mesh's drag is carried back through its parent.** `Position` on a mesh hanging
  off a node is an offset in that node's space, so a world-space drag on a mesh under a node
  scaled ×8 would otherwise run eight times as far as the cursor.

One deliberate limitation, and it is the format's rather than the gizmo's: **rotation drives
the mesh's own Euler angles**, because that is what `IMesh` stores — the Y ring is yaw, the X
ring pitch, the Z ring roll. With two of the three at zero that is exactly a rotation about the
world axis drawn; with all three set it is not, because composed Euler angles cannot express
one. Turning `Mesh.Rotation` into the quaternion `SceneNode` already uses is what would fix it,
and this is one more reason to.

### Snapping, and taking it back

A gizmo on its own is a control you can only commit with. Dragging is an *estimating* gesture —
you push a mesh, look at it, push it back — and the two things that make trying cheap are an
increment worth landing on and a way to undo it.

[`GizmoSnap`](src/SoftEngine.Core/Gizmos/GizmoSnap.cs) quantizes a drag, and **snaps the
resulting transform rather than the distance the cursor travelled.** That distinction is the
whole feature. Rounding the travel preserves whatever offset the mesh started at, so two meshes
"snapped" to the same gridline end up a fraction apart — precisely what a person turns snapping
on to prevent. Rounding the result means a step of 1 puts every mesh dragged along X on an
integer, and 15° means 15° from zero rather than from wherever the drag began. Translation is
snapped in **world** space, before the offset is carried into a parented mesh's own space, because
the grid the viewport draws is a world grid and a node's local axes are not it.

[`EditHistory`](src/SoftEngine.Core/Editing/EditHistory.cs) is the undo stack, and it stores whole
transforms rather than the deltas that produced them, for two reasons. Undo has to be *exact*, and
a chain of accumulated floating-point deltas does not return to where it started. And a delta that
knows only about its own axis would be wrong the moment two edits interleave.

Three details that are the difference between a history you can trust and one you fight:

- **A drag that moved nothing records nothing.** A handle grabbed and released in place is a drag
  as far as the gizmo is concerned, and an entry that undoes nothing makes the first Ctrl+Z after
  a misclick appear dead — so the user presses it again and loses real work.
- **The gizmo produces the command; it does not push it.** It has no opinion about whether the
  application keeps a history. What it has, and nothing downstream does, is the transform from
  before the drag: by the time a caller sees the mouse-up, the mesh has already moved a hundred
  times.
- **Loading a world clears the stack.** The commands point at meshes that are no longer in the
  scene, and undoing one would silently transform an object nothing draws.

The menu names what it would reverse — *Undo Move Cube* — because that is what tells you whether
the next Ctrl+Z is the one you meant before you press it.

## Scene files

`File ▸ Save scene as…` writes what you set up as JSON
([`SceneSerializer`](src/SoftEngine.Core/Scenes/Serialization/SceneSerializer.cs)): the camera and
its orientation, the projection, every light, every mesh's transform, the shading mode, the fog,
the shadows and the whole post-process stack.

**What it deliberately does not contain is vertices.** A scene document names the model it was
built on and records what was done to it. Inlining the geometry would turn a file a person can
read into a several-megabyte copy of a model that already exists on disk, and one that goes stale
the moment that model is re-exported.

```json
{
  "version": 1,
  "world": { "demo": "cascades" },
  "camera": { "position": [0, 12, -48], "orientation": [0, 0.38, 0, 0.92] },
  "lights": [
    { "kind": "directional", "direction": [-0.4, -0.6, -1], "intensity": 1.1, "color": [255, 240, 214] }
  ],
  "rendering": { "painter": "Pbr", "showXZGrid": true, "debugView": "Off" }
}
```

Four decisions worth naming, all of them about being a file rather than a memory dump:

- **Every section is optional, and a missing one means "leave this alone".** That is what makes
  the format writable by hand: a file containing nothing but a camera position is a valid scene
  document, and applying it moves the camera and changes nothing else. It is also what makes
  applying a document to the wrong world merely partly wrong rather than destructive.
- **Vectors are written as one-line arrays** — `[0, 12, -48]` rather than an object with three
  named members, and on a single line, because an indented JSON writer puts every array element on
  a line of its own and three numbers would become five lines.
- **A light with no falloff writes no range at all.** The engine's default really is infinity and
  JSON has no way to spell it; recording it as a very large number would turn "no falloff" into
  "an enormous falloff", which is a different thing that happens to look the same nearby.
- **Meshes are addressed by index, and an index past the end is skipped rather than thrown on.** A
  document written against a model that has since been re-exported with fewer meshes is a scene
  that has partly gone stale, not a file to refuse.

The engine never interprets `world` — resolving a demo name or a path is a question about the
machine the file is opened on, which belongs above a rendering library rather than inside one.

## Headless rendering

```bash
dotnet run -c Release --project src/SoftEngine.Cli -- model.gltf -o frame.png -w 1920 -h 1080 -p pbr --ss 2
```

[`SoftEngine.Cli`](src/SoftEngine.Cli) renders a model or a saved scene straight to a PNG. The
engine draws into a plain `int[]`, so this needs no window, no GPU and no platform beyond the
runtime — which is what makes it usable from a build, a script or a batch of a hundred models.

| | |
| --- | --- |
| `-w`, `-h`, `--ss` | resolution, and supersampling to average down from |
| `-p`, `--post`, `--view` | painter, post-process effects, and which buffer to present instead of the shaded image |
| `--yaw`, `--pitch`, `--zoom`, `--camera` | where to stand; `-t` says how far into the model's animation |
| `--scene` | apply a saved scene document over the model named on the command line |
| `--stats` | triangle, pixel and timing counts |

Three things it does differently from the viewer, because one shot is all it gets:

- **The framing is solved rather than guessed.** The distance at which a sphere of radius *r*
  exactly fills the frame is `r / sin(fov/2)`. A multiplier that frames one model crops the next,
  and there is no orbiting your way out of it afterwards.
- **The camera is a bearing, not a gesture.** An arc-ball accumulates the orientation a sequence of
  drags left behind; three numbers on a command line have to produce the same frame on every
  machine and every run, which is what makes the output something a script can compare against.
- **Textures decode from PNG only.** The Core deliberately does not decode images for import — a
  texture arrives in whatever format an artist saved it in — so a host supplies the decoder. The
  viewer answers that with GDI+, which reads everything and runs on Windows; this answers it with
  the engine's own codec, which reads one format and runs anywhere. A model with JPEG maps renders
  untextured, and the program says how many it skipped rather than hiding it.

## Scene graph

A mesh with nothing but its own position can only ever be placed absolutely. There is no way
to say *the hand goes wherever the arm puts it* — and that sentence is most of what a model
that moves consists of.

[`SceneNode`](src/SoftEngine.Core/Scenes/Graph/SceneNode.cs) is one transform in a hierarchy:
a local translation, rotation and scale, a parent, and the world matrix that composing the
chain produces. `IMesh.Parent` hangs a mesh off one, and the mesh's own transform becomes an
offset from the node rather than from the origin. Nothing that predates the graph changed —
a mesh with no parent composes exactly the matrix it always did.

Two decisions worth naming:

- **World matrices are cached, not walked on every read.** A skinned mesh reads its joints'
  world matrices once per vertex influence; re-composing the chain at each read would make a
  deep skeleton quadratic in its own depth.
- **A node's rotation is a quaternion**, where `Mesh` still carries Euler angles. Animation
  has to interpolate between two rotations, and Euler angles interpolate through gimbal lock.

Nodes also carry a [`SceneNodeKind`](src/SoftEngine.Core/Scenes/Graph/SceneNodeKind.cs). A
scene file's node tree is not all rig: exported alongside the bones are the nodes holding the
artist's lights and cameras, which sit metres away and dwarf the model. Only the skeleton view
reads the kind, and it is what makes that view legible on a real file.

## Skeletal animation

![A generated tube bent by a chain of seven joints, wireframe on](docs/screenshots/skinning.png)

*A tube rigged to seven joints, mid-bend. The wireframe is the point: the rings crowd on the
inside of each curve and spread on the outside, which is a mesh being deformed rather than a
rigid one being rotated.*

### Skinning

[`SkinnedMesh`](src/SoftEngine.Core/Geometry/Skinning/SkinnedMesh.cs) implements linear blend
skinning. Each vertex is transformed by every joint that claims it and the results mixed by
weight — equivalently, and this is how it is computed, the joint matrices are mixed first and
the vertex transformed once.

The deformed positions are written back into the arrays [`Mesh`](src/SoftEngine.Core/Geometry/Mesh.cs)
already exposes, so **the renderer needs no knowledge of skinning at all**: it transforms
`Mesh.Vertices` exactly as it always has, and they simply happen to have moved since the last
frame. The bind pose is kept privately, because deforming the deformed output would compound
the pose frame after frame.

- A [`Skeleton`](src/SoftEngine.Core/Geometry/Skinning/Skeleton.cs) holds the joint nodes and
  their **inverse bind matrices**. A joint's inverse bind undoes the transform it had when the
  mesh was modelled; its current world matrix puts the vertex back down wherever the joint has
  moved to. The product is the only thing skinning needs per joint, so it is built once per
  pose rather than once per vertex.
- [`SkinWeights`](src/SoftEngine.Core/Geometry/Skinning/SkinWeights.cs) stores a fixed four
  influences per vertex, flat rather than as an array of small arrays. Four is a budget, not a
  format limit — riggers paint six or eight, and the fifth is worth a fraction of a percent —
  so the builder keeps the four heaviest and renormalizes.
- **Normals are deformed too**, by the same blended matrix and then renormalized. Strictly a
  normal wants the inverse transpose, which differs once a joint scales non-uniformly; joints
  rotate and translate, and renormalizing covers the rest. Tangents follow the same path when
  a skinned mesh has them, so normal mapping survives a pose.
- **The bounding sphere is remeasured with every pose.** The renderer culls whole meshes
  against it, and a raised arm reaches outside the sphere the bind pose fitted in.

One deliberate leniency: a skin covering fewer vertices than its mesh leaves the rest at bind
pose instead of throwing. Malformed weight tables are common in exported files, and a model
that renders with a stiff patch beats one that will not load.

### Clips and playback

An [`AnimationClip`](src/SoftEngine.Core/Animation/AnimationClip.cs) is a set of per-node
curves — translation and scale as `Vector3Track`s, rotation as a `QuaternionTrack` that slerps
along the shorter of the two arcs between neighbours. Clips are pure data and hold no playhead,
so two things can play one clip at different times; [`AnimationPlayer`](src/SoftEngine.Core/Animation/AnimationPlayer.cs)
owns the time, the speed and the looping.

Channels address nodes **by name**, which keeps a clip independent of any one skeleton. Names
are resolved once, at construction, into an array parallel to the channel list — the parrot
would otherwise spend thousands of string comparisons per frame to move a wing.

Outside a clip's own span, values are *held* rather than extrapolated. A clip says nothing
about what happens before its first key, and guessing produces motion no animator authored.

Files bake a joint's whole transform into one `float4x4` curve far more often than they key
the components separately, so a matrix track is decomposed into translation, rotation and
scale **once, at load**. Blending the matrices themselves component by component shears a
rotating joint, because the halfway point between two rotation matrices is not a rotation
matrix.

`IWorld.Update(deltaSeconds)` runs the whole chain: play the clips, refresh the hierarchy,
re-deform anything skinned. The renderer never calls it — rendering a frame twice, which the
graphics debugger does every time a probed pixel is re-recorded, must not advance time.

### What the importer reads

`MeshFactory.ImportColladaScene` returns an [`ImportedScene`](src/SoftEngine.Core/Geometry/ImportedScene.cs):
the meshes, the visual scene's node tree, the skin controllers bound to it, and the animation
channels that pose it. It is the same type the glTF reader returns, because a scene is a scene —
the two readers disagree about matrix conventions, chunk layout and where a material's roughness
lives, and about nothing downstream of that. `HackyImportCollada` is untouched and still returns
bare meshes, which is all a static model needs.

Collada writes matrices for the **column-vector** convention — a point is transformed as
`M·v`, and a node's translation sits in the fourth column. This engine composes row-vector
matrices, `v·M`, with translation in the fourth row. The two are transposes of each other, so
every matrix is transposed on the way in and nothing downstream knows the file's convention.
The bind shape matrix is folded into the bind pose at load, so it costs nothing per frame.

### Seeing the rig

![The parrot's sixty-node rig, a cube on every joint](docs/screenshots/rig.png)

A skeleton is invisible in a rendered image by construction: it is the thing that moves the
vertices, never a thing that gets drawn. Which makes a rig that is subtly wrong indistinguishable
from a mesh that is subtly wrong. `Settings.ShowSkeleton` draws the hierarchy as bones — a line
to each node's parent, and a three-axis tick at each joint so a node with no children is still
visible.

Three bundled demos cover the two halves of the problem and the case where both are present:

| Demo | What it shows |
| --- | --- |
| **Bone chain (skinned)** | Geometry, rig and clip all generated — the smallest thing that exercises the whole skinning path, with a correct answer that is obvious by eye. |
| **Juliet (skinned)** | A real 55,000-vertex skin from a real file: 205 joints and weights painted by whoever rigged her. The file carries no animation, so the sway is authored against the joint names her rig actually uses — with every key composed *on top of* the joint's rest orientation, since a clip written against someone else's rig must not discard the pose it was modelled in. |
| **Parrot rig (animated)** | A twelve-second clip over a sixty-node hierarchy, and no skin binding the mesh to any of it — so a cube on each joint makes the hierarchy the model. Move a wing joint and the four cubes below it go with it. |

## Post-processing

![Post-processing](docs/screenshots/post-processing.png)

[`PostProcessStack`](src/SoftEngine.Core/Pipeline/PostProcess/PostProcessStack.cs) runs
full-screen effects over the finished render target. It owns the conversion at both ends, so
effects only ever see linear float RGB — the space blurring and adding light are actually
defined in. Against an HDR surface the image arrives already linear and unbounded and no
decode happens at all; against an 8-bit one the stack decodes sRGB on the way in, and nothing
downstream can recover highlights the rasterizer already clipped.

| Effect | What it does |
| --- | --- |
| **SSAO** | Reconstructs a position and a normal per pixel from the depth buffer, samples a hemisphere around each, and darkens by the fraction of samples something else is in front of. |
| **Bloom** | Bright-passes and blurs at a quarter resolution, then adds the result back — a wide blur is what sells the effect, and it costs a sixteenth of the samples downsampled. |
| **Tone map** | Exposure plus a Reinhard or ACES filmic curve, so values above white roll off instead of flattening into one clipped patch. |
| **FXAA** | Detects luminance edges, works out which way each runs, and blurs across it — never along it. Fixed screen-sized cost rather than a multiple of the whole render. |
| **Vignette** | Darkens the frame toward its corners, normalized so it behaves the same at any aspect ratio. |

### Ambient occlusion

[`SsaoEffect`](src/SoftEngine.Core/Pipeline/PostProcess/SsaoEffect.cs) finds the creases and
contact points a shadow map at any sane resolution cannot resolve, from nothing but the depth
buffer the frame already has. The depth buffer is a partial model of the scene: one point in
space per pixel, and — by differencing its neighbours — the surface's orientation there. A
point is occluded to the extent that other points sit above its own tangent plane nearby.

Effects that want this declare `NeedsDepth`, and the stack then reads the z-buffer back as
*view-space distance* ([`FrameBuffer.ReadViewDepth`](src/SoftEngine.Core/Buffers/FrameBuffer.cs))
rather than as the quantized device depth the depth test uses — a screen-space effect's radius
is measured in world units, so it needs the distance, not something merely monotonic in it.
The read costs a full-screen pass, so it only happens when something enabled asked for it, and
it is only possible under a perspective projection.

Two honest limitations. It only knows what is on screen, so geometry outside the frame or
hidden behind something nearer occludes nothing — inherent to the technique. And it multiplies
the finished image, which darkens direct light along with ambient; separating the two in a
forward renderer means carrying the ambient in a buffer of its own through the whole frame.
`Radius` is a world-space distance and is the one number that must be scaled to the scene.

## Rendering pipeline

```
model ──worldMatrix──▶ world ──viewMatrix──▶ view ──projectionMatrix──▶ clip ──/w──▶ NDC ──▶ screen
```

Per frame, the `Renderer` ([`Pipeline/Renderer.cs`](src/SoftEngine.Core/Pipeline/Renderer.cs)):

1. Clears the color and z-buffers.
2. Renders the shadow map from the light, if the scene casts shadows — before any painter prepares, since every shade that follows reads it.
3. Rasterizes the largest opaque meshes into an occlusion buffer, so the meshes hidden behind them can be rejected whole.
4. Transforms each surviving mesh's vertices into view space (pooled `VertexBuffer` per mesh).
5. Rejects triangles behind the far plane, back-facing triangles (optional culling), and triangles outside the view frustum.
6. Projects survivors into clip space, maps to screen space, and bins them into the screen tiles they cover.
7. Fills the tiles in parallel through the active painter.
8. Draws the sky into whatever pixels the opaque pass left untouched.
9. Blends the transparent triangles over the result, farthest first.
10. Draws optional gizmos (XZ grid, world axes, skeleton) and outlines the picked mesh.
11. Runs the post-process stack over the finished image, and encodes it for presentation.
12. Swaps the image for one of the buffers that produced it, if a buffer view is selected.

The rasterizer ([`Rasterization/ScanlineRasterizer.cs`](src/SoftEngine.Core/Rasterization/ScanlineRasterizer.cs)) sorts a triangle's vertices by Y, splits it at the middle vertex, and walks two half-triangles, interpolating depth plus an arbitrary *varying* payload. Painters only supply a **varying** type and a **shader** — both are `struct` generics, so the JIT devirtualizes and inlines the per-pixel shade call with no allocation on the hot path.

## Tiled rasterization

The fill phase is split by **screen tile**, not by triangle: [`TileBinner`](src/SoftEngine.Core/Rasterization/TileBinner.cs)
sorts each frame's triangles into the 32×32-pixel tiles their bounding boxes touch, and one
worker owns each tile ([`ScreenTile`](src/SoftEngine.Core/Rasterization/ScreenTile.cs)). Because
tiles never overlap, the z-buffer needs no locking — and because a triangle only reaches the
tiles it covers, its per-triangle setup is paid once per tile instead of once per core.

Three things fall out of owning a rectangle of pixels:

- **Coarse depth rejection.** Before drawing a triangle into a tile, its nearest depth is
  compared against the farthest depth currently stored anywhere in that tile. If it is behind,
  the triangle is dropped whole — no rows walked, no pixels tested. The bound is re-read every
  few triangles, and a scan that buys no rejection doubles the interval to the next one, so a
  scene with no depth complexity stops paying for it. `Settings.HierarchicalZ` turns it off.
- **Vectorized depth tests.** Runs of `Vector<int>.Count` pixels are depth-tested at once, and
  a run entirely behind the z-buffer is skipped without interpolating or shading any of it.
  Depth is evaluated as an affine function of x rather than a running sum, so the vector test
  and the scalar loop agree exactly.
- **Contiguous memory.** A tile walks the framebuffer the way it is laid out, rather than
  every n-th row.

Measured at 1280×720 on eight hardware threads, the three together render a dense model about
**1.5–1.8×** faster than the row-interleaved fill they replace, and a scene with heavy overdraw
about **2×** faster.

## Occlusion culling

Frustum culling answers *is it on screen*. In a scene built the way real scenes are — a room, a
street, a hillside — most of what is on screen is standing behind something else that is also on
screen, and every bit of it is transformed, clipped, projected and binned before the depth test
gets to say so.

[`OcclusionCuller`](src/SoftEngine.Core/Pipeline/Culling/OcclusionCuller.cs) asks the other
question first. It picks the few largest opaque meshes in the frame, rasterizes them depth-only
into an [`OcclusionBuffer`](src/SoftEngine.Core/Pipeline/Culling/OcclusionBuffer.cs) at half the
frame's resolution, folds that into a pyramid, and then tests every other mesh's bounding volume
against it. A mesh that fails is dropped before its first vertex is touched.

It is the tile rasterizer's coarse depth bound moved to the other end of the pipeline, and the
difference is what each one can still save. `HierarchicalZ` rejects a triangle that has already
been transformed, projected and binned, so it saves the pixels; this rejects a mesh before any of
that happens, so it saves all of it.

**The rule is that it may only ever be wrong in the direction of drawing too much.** A mesh it
fails to reject costs time. A mesh it rejects wrongly is a hole in the picture, and one that
reads as a bug in the rasterizer rather than as a bug in a culling pass. So the depth written to
a texel is the occluder's *farthest* point anywhere inside it, folding the pyramid takes the
*farthest* of each group of four, an unwritten texel sits at the far plane where it can hide
nothing, and a bounding sphere is tested through the projected corners of its box — a shape that
contains it — rather than through anything tighter.

Three decisions are the difference between a pass that helps and one that costs:

- **Coverage is measured a level above the one that is rasterized.** The obvious rule is to write
  a texel only where a single triangle covers all of it, and it is a trap: two triangles sharing
  an edge — which is what every quad in every scene is — leave a seam along it that neither fills
  alone, so a wall built the only way walls are built acquires a diagonal crack through the
  middle and stops occluding anything that crosses it. Level 0 is centre-sampled instead, which
  is watertight across a shared edge, and a level-1 texel carries a real depth only where all
  four of its children were sampled inside the geometry. That is coverage, measured on a grid
  twice as fine as the answer is given on.
- **A big mesh is not automatically a good occluder.** Rasterizing one is a fixed cost paid up
  front and repaid one rejected mesh at a time, so `MinimumTestableMeshes` declines the whole
  pass on a world without enough meshes to repay it. Without that floor the worst case is
  brutal and easy to hit: a handful of nested spheres are each enormous on screen, all of them
  get chosen, drawing them costs more than the entire rest of the frame, and there is nothing
  behind them to find. `MinimumOccluderExtent` and a triangle budget make the same judgement
  within a scene.
- **A wall's bounding sphere reaches the camera, and that must not disqualify it.** A wall is a
  flat thing with a sphere as wide as its diagonal, so one filling the view from a few units
  away has a sphere that swallows the eye while every triangle in it sits comfortably in front.
  Rejecting those — which looks like ordinary near-plane hygiene — throws away the best occluder
  in most scenes. Triangles that really do straddle the near plane are dropped one at a time by
  the rasterizer, which is where a question about a triangle belongs.

Measured at 1280×720 on eight hardware threads, on 512 dense meshes standing behind a wall that
covers the frame, it renders about **1.6×** faster and rejects 362 of the 512 outright. On the
other six benchmark scenes it is within noise of 1.00×, which is the other half of what it has to
do: `many-meshes` has four thousand meshes and nothing large enough to occlude with, and pays
about 4% to find that out every frame.

Two honest limits. **An occluder is never tested against the buffer it helped write**, so a large
mesh completely hidden behind another large mesh survives — testing them properly would mean
building the pyramid incrementally, front to back, and folding it once per occluder. And because
level 0 is centre-sampled, a mesh visible only through a gap about a pixel wide at an occluder's
silhouette can be culled; everything wider than that is safe.

`Settings.OcclusionCulling` turns it off. A probed frame turns it off too, exactly as the tile's
coarse depth bound is turned off and for the same reason: the pixel history has to show the
writes the depth test rejects, and a mesh dropped here never attempts them.

The **occlusion buffer** view presents the pyramid itself, which is the quickest way to see why a
scene culls less than it should — see [Buffer views](#buffer-views).

## Supersampling

[`SuperSampler`](src/SoftEngine.Core/Pipeline/SuperSampler.cs) resolves a render target drawn
at an integer multiple of the display resolution back down to it. It is the one kind of
anti-aliasing that asks nothing of the rasterizer — the whole pipeline just runs larger — so
unlike FXAA it smooths specular glints and texture shimmer as well as silhouettes. Colours are
averaged in linear light, and alpha along with them, which leaves the edge pixels of a shape
over the cleared background correctly premultiplied for presentation. A 2× frame fills four
times the pixels, which is exactly what it costs.

## Interactive app

The WinForms app ([`SoftEngine.WinForms`](src/SoftEngine.WinForms)) renders the scene into a 32-bpp bitmap that is blitted to a `Panel3D`.

| Control | Action |
| --- | --- |
| **Left-drag** | Orbit the arc-ball camera |
| **Right-drag** | Pan; **left+right-drag** dollies |
| **Mouse wheel** | Move the camera in/out — the status bar's zoom percentage follows it (100% is the framing a world loads with) |
| **W / A / S / D** | Fly the camera forward / left / back / right (**Q**/**E** for down/up). Hold **Shift** to move faster, **Ctrl** for fine steps; the step scales with the camera's distance, so it works on a 2-unit skull and a 1500-unit elephant alike |
| **Left-click the viewport** | Probe that pixel *and* pick what is under it — the write history appears in the Pixel History panel, the hit mesh is outlined in amber and selected in the object table, and the status bar names it (**Esc** clears both) |
| **Drag a gizmo handle** | Move, turn or stretch the picked mesh, once a mode is chosen. The handle under the cursor highlights, grabbing one suspends the camera for the drag, and the status bar reports the mesh's new position, rotation or scale |
| **Ctrl+Z / Ctrl+Y** | Undo or redo a gizmo drag; the menu names the edit it would reverse |
| **Ctrl+G** | Snap drags to a grid — whole units of position, 15° of rotation, tenths of scale. The grid step is scaled to the world that is loaded |
| **Ctrl+S** | Save the whole scene as JSON; **File ▸ Open scene…** brings it back |
| **Ctrl+← / Ctrl+→** | Step the debugger panels back and forth through kept frames |
| **F12** | Save the current view as a PNG |
| **Load model…** | Pick a bundled world (skull, parrot, elephant, teapot, cubes, spheres, towns, shadows, cascaded shadows, normal mapping, PBR spheres, the three animated ones…) or open an OBJ, Collada or glTF file from disk |
| **Shading radios** | Switch between None / Classic / Flat / Gouraud / Phong / Textured / Material / Physically based |
| **Buffer view** | Present the shaded image, or the depth, normals, overdraw, shadow-map or occlusion buffer that produced it |
| **Shadow cascades** | One map over the world, or two to four fitted to slices of the view distance |
| **Transform gizmo** | Off / Move / Rotate / Scale — the handles drawn on the picked mesh |
| **Display checkboxes** | Toggle wireframe triangles, back-face culling, XZ grid, world axes, skeleton, animation, fog, shadows, sky, gamma-correct light, HDR target, texture filtering, 2× supersampling |
| **Post-processing checkboxes** | Toggle ambient occlusion, bloom, tone mapping, FXAA and vignette independently |

A stats overlay reports triangle counts (total / back-facing / out-of-view / behind), pixel counts (drawn / z-rejected), and calculation vs. paint timing per frame.

## Graphics debugger

The front-end doubles as a small graphics debugger, modelled on [Rasterizr Studio](https://github.com/tgjones/rasterizr). Because the whole pipeline runs on the CPU, the panels show what the renderer actually did rather than what a driver reported.

| Panel | Shows |
| --- | --- |
| **Graphics Event List** | Every step of the frame in pipeline order — viewport and depth-range setup, buffer clears, the shadow-map pass, the view and projection matrices, then per mesh: vertex transform, cull results and the draw call, then the post-process pass and the present. |
| **Graphics Object Table** | Every object the frame touched — render target, depth buffer, camera, projection, painter, shadow map, post-process stack, lights, meshes and textures — with its size, vertex/triangle counts and dimensions. Meshes carry an **active** checkbox that drops them from the frame. |
| **Pixel History** | For the selected pixel: the clear, then each triangle that tried to write it — including the ones the depth test rejected — with the input-assembler and transformed vertex data, the depth comparison, and the previous → resulting colour, ending with the post-process pass's before → after. |

### Frame history

The panels normally read the renderer's live log, which is one buffer reused every frame — so the
moment you see something worth looking at, the frame that produced it has already been overwritten
by the next one. That is fine while the camera is still and unbearable while anything moves, which
is exactly when the interesting frames happen.

`View ▸ Frame history ▸ Keep recent frames` files each finished frame into a
[`FrameCapture`](src/SoftEngine.Core/Diagnostics/FrameCapture.cs), and **Ctrl+←** / **Ctrl+→** step
the panels back and forth through them. Three decisions:

- **It is off by default and separate from event recording.** Capturing events is a write into a
  buffer reused for ever; *keeping* a frame means copying that buffer, and a busy scene emits
  thousands of events per frame. It is the one piece of instrumentation here that genuinely
  allocates, so the cost is opt-in and bounded by a number the caller chose.
- **The pin is a frame number, not a position in the list.** The viewport goes on rendering while a
  frame is pinned, and every new capture drops the oldest — an index would quietly come to mean a
  different frame each time that happened, so the panels would creep forward through history while
  claiming to stand still. If a pinned frame does age out of the window, the oldest one still kept
  is shown and the status bar names it, so the slip is visible rather than silent.
- **The image is not kept.** A capture holds the event list, the probed pixel's history and the
  counts — everything the three panels draw — and none of the pixels. A frame at 1920×1080 is eight
  megabytes of colour and as much again of depth, and keeping a dozen of those to answer "what did
  the renderer do" would spend a hundred and sixty megabytes on the one question the panels never
  ask. Stepping back changes what the panels show, not what the viewport shows.

Identifiers are shared: `obj:7` in the event list is `obj:7` in the object table, and clicking an entry in the pixel history selects both. Clicking the viewport asks the same pixel two questions at once: the probe records what the renderer *did* there, and the ray says which mesh is *under* it — and the second selects the matching row in the object table.

### Buffer views

[`BufferVisualizer`](src/SoftEngine.Core/Pipeline/Debugging/BufferVisualizer.cs) presents one
of the frame's intermediate buffers in place of the shaded image. Everything it draws already
exists by the time the frame ends; the work is choosing a mapping to colour a person can read,
which is most of what makes a buffer view useful rather than merely available. The pass runs
last, over the finished image, so nothing upstream has to know it exists — and the buffer
being shown is the one the frame really used.

| ![The shaded frame](docs/screenshots/buffer-shaded.png) | ![Depth, auto-ranged over the geometry on screen](docs/screenshots/buffer-depth.png) |
| :--: | :--: |
| **Shaded** — the frame the other four came from | **Depth** — auto-ranged over the geometry actually on screen, because a perspective depth buffer presented literally is a white screen |
| ![Normals reconstructed from the depth buffer](docs/screenshots/buffer-normals.png) | ![Overdraw as a heat map](docs/screenshots/buffer-overdraw.png) |
| **Normals** — differenced out of the depth buffer, since a forward renderer has no normal buffer to show | **Overdraw** — writes per pixel, blue through red. Shown here with back-face culling off, which is the frame paying for the far side of every surface |
| ![The shadow map as the light sees it](docs/screenshots/buffer-shadowmap.png) | |
| **Shadow map** — the depth the light recorded, fitted into the viewport with its aspect preserved | |

The **occlusion buffer** view is the one pass whose working nothing else in the debugger can show.
A mesh that should have been culled and was not is a question about coverage in a buffer you have
never seen, and coverage is invisible in the finished frame by construction: the pass only ever
decides what *not* to draw, so when it under-performs the picture is exactly right and merely
slower. Two decisions make it worth looking at rather than merely available:

- **The level shown is the finest one a query may read, not the level that was rasterized.** Level
  0 is centre-sampled, so a texel there is written wherever a triangle reached its middle — which
  is not the same as covering it. Coverage only appears one level up, where a texel carries a real
  depth exactly where all four of its children were sampled inside geometry. Showing level 0 would
  paint a confident picture of occlusion the culler cannot actually use, and the gap between the
  two is exactly what you opened the view to find.
- **Texels nothing covered are a cold blue-grey rather than black**, because "nothing here" and
  "something at the far plane" are different answers and a greyscale ramp gives them the same
  colour — which would make an empty buffer look like a fully occluding one. The filled texels are
  auto-ranged over the depths actually present, for the same reason the depth view is.

A frame the pass declined — switched off, probed, or a world with too few meshes to repay it —
leaves the shaded image alone and says so in the event list, rather than presenting the previous
frame's pyramid, which would look current.

Two details that are the difference between a view you can trust and one you cannot:

- **Depth is fitted to the frame, not to the frustum.** The same view stays legible on a
  2-unit skull and a 1500-unit elephant.
- **Overdraw counts writes the rasterizer attempted**, not triangles that geometrically cover
  the pixel. A triangle the tile's coarse depth bound dropped whole never reaches a pixel and
  never shows up. That is the intended reading — the view answers "what did this frame pay
  for", which is the question overdraw is asked for.

A view the frame carries nothing for — normals under a parallel projection, the shadow map of
a scene that casts none — leaves the image alone, and says so in the event list rather than
logging a pass that never ran.

Recording is driven from `RenderDiagnostics` on the renderer ([`Diagnostics/`](src/SoftEngine.Core/Diagnostics)):

- Events are stored as a `readonly record struct` with a numeric payload in a reused buffer, and formatted only for the rows the list actually draws — a busy scene emits thousands of events per frame and capturing them allocates nothing.
- The pixel probe is a single int compare inside `FrameBuffer.PutPixel`, off (`-1`) unless a pixel is selected. The "what is drawing" context is thread-static: each paint worker owns a disjoint set of screen rows, so the one worker that owns the probed pixel is also the one that tags its writes, and they stay in draw order.
- Triangle vertices are snapshotted only when a write actually lands on the probed pixel, never per triangle.

Both can be switched off from the **View** menu, along with each panel.

## Rendering on the GPU

The frame can be filled by a graphics adapter rather than by the scanline rasterizer. It is the
same scene, the same `IPainter` choosing the shading model, the same settings, and a finished frame
in the same `FrameBuffer` — what changes is where the triangles are rasterized, and therefore how
the cost of a frame scales.

```bash
# what adapter is here, if any
dotnet run -c Release --project src/SoftEngine.Cli -- --gpu-info

# render on it
dotnet run -c Release --project src/SoftEngine.Cli -- model.gltf --gpu -o frame.png --stats
```

In the viewer it is **View → Rendered by → CPU / GPU**, and the status bar names the device the
frame is being drawn by.

### "GPU" means a graphics adapter

An OpenGL context is perfectly happy to be served by a CPU implementation — Mesa's `llvmpipe`,
Windows' `GDI Generic` fallback, SwiftShader — and one of those would run this engine's own job on
the CPU anyway, only through a driver, and slower. So the backend reads the driver's account of
itself and refuses to call that hardware: an explicit `--gpu` falls back to the software renderer
with the reason, and `--backend auto` quietly does the right thing. Discrete and integrated are
both accepted and both reported, because integrated is still a graphics processor and still several
times faster here than the software path. A device the classifier does not recognise is treated as
hardware, which is the safe direction — new graphics cards appear constantly and new CPU
rasterizers essentially never.

### What runs where

Everything that scales with triangles times pixels runs on the adapter: the shadow cascades, the
opaque fill, the sky, the transparent blend, the wireframe overlay. Everything that runs once over
the finished image runs where it already did — the post-process stack, the debug views, the gizmos
and the grid — over a frame read back into the engine's own buffers.

That read-back is the deliberate trade. Every one of those passes already exists, already works, and
reads a `FrameBuffer`; reproducing them in GLSL would be a second implementation of each, free to
disagree with the first. Handing the pixels back instead costs one transfer of the finished image
and buys all of them unchanged. Depth comes back only when something is going to read it.

It is also the ceiling on what the backend is worth. The transfer is linear in pixels, so the
advantage is largest where the fill is dense relative to the frame and narrows as the viewport
grows — and on a **discrete** card, where the read-back crosses PCIe rather than staying in shared
memory, it narrows faster. Supersampling multiplies the transferred area by the square of the
factor, which is why 2× supersampling costs a GPU frame far more than four times the fill.

### Agreement with the software renderer

The two backends are held to the same picture, not merely to plausible ones. Both are built from the
same matrices, the shading maths in [`common.glsl`](src/SoftEngine.Gpu/Shaders/common.glsl) is a
port of the CPU shaders function for function, and where the cascades go is
[`ShadowCascadePlanner`](src/SoftEngine.Core/Pipeline/Shadows/ShadowCascadePlanner.cs) — one object,
shared, so the two cannot drift.

A sphere over a ground plane, rendered at 480×360 by both backends across the features the backend
touches. Mean absolute difference per channel, out of 255:

| | mean | >8/255 | | | mean | >8/255 |
| --- | --- | --- | --- | --- | --- | --- |
| Phong | 0.59 | 0.3% | | Orthographic | 0.13 | 0.2% |
| Gouraud | 0.58 | 0.3% | | No gamma correction | 0.64 | 0.3% |
| Flat | 0.69 | 0.4% | | Fog, linear / exponential | 0.63 / 0.57 | 0.3% |
| Classic | 0.50 | 0.3% | | Four lights, mixed types | 0.65 | 0.3% |
| Material | 0.59 | 0.3% | | Spot / ranged point | 0.75 / 0.72 | 0.3% |
| Physically based | 1.02 | 0.3% | | Transparency | 0.60 | 0.4% |
| Shadows, soft / hard | 0.70 | 0.7% / 0.4% | | Sky off | 0.38 | 0.3% |
| Shadows, 3 cascades | 0.70 | 0.6% | | No back-face culling | 0.59 | 0.3% |
| Depth view | 0.79 | 0.1% | | SSAO | 0.60 | 0.3% |
| Normals view | 0.64 | 0.3% | | Tone map / FXAA / vignette | 0.59 | 0.3% |
| Overdraw view | 0.29 | 0.6% | | Shadow-map view | 0.00 | 0.0% |

Most of what is left is silhouette coverage — the two rasterizers disagree about which pixels a
triangle's edge owns, which shows up as a one-pixel outline and nothing else.

Two things deviate further, both knowingly. The **physically-based** path uses Karis' analytic fit
for the environment BRDF rather than the CPU's tabulated integral, and takes its reflections from
the sky's mip chain rather than a per-roughness convolution. The **wireframe overlay and the picked
mesh's outline** differ by about 5 and 4 — the software renderer draws them as Bresenham lines whose
depth is interpolated along the line, which disagrees slightly with the depth its own triangle fill
wrote at the same pixel, so parts of every line lose the depth test and the outline comes out
dotted. OpenGL's line-mode polygons inherit the polygon's own depth exactly and stay continuous. The
GPU's outline is the more complete of the two.

### What it does not do

The graphics debugger's **per-pixel history** is a log of every write the software rasterizer
attempted, including the ones the depth test rejected. A GPU discards those inside the hardware and
has nowhere to write them down, so a probed pixel reports nothing under the GPU backend. The event
list, the object table and the frame statistics are recorded as usual.

The **occlusion pre-pass** is also absent, and deliberately: it exists to spare the software
rasterizer the fill of geometry it cannot see, and the hardware's own early-depth rejection does
that job without a pass over the frame first. Frustum culling stays, because it removes draw calls.
The occlusion buffer view therefore reports having nothing to show, rather than presenting a
pyramid no pass built.

Two views cost extra and so are computed only while they are open. **Overdraw** needs a second pass
over the frame's geometry, additively blending one per fragment, because a GPU rejects a fragment
inside the hardware and leaves no counter to read. The **shadow map** lives in a texture that
shading samples directly, and is copied back only when the view that displays it is showing.

There is a ceiling of **16 lights** in the fragment shader, where the software path has none.

### Presenting a frame at 2× supersampling is the expensive case

Supersampling renders at a multiple of the display resolution, so the read-back area — and the
CPU-side resolve that averages it down — both grow with the *square* of the factor. Rendering four
times the pixels is nothing to a graphics card; transferring four times the pixels back across
PCIe and averaging them on the CPU is most of the frame. Downsampling on the adapter and reading
back only the display-resolution image would fix it, and is not done: the viewport sizes the render
target itself and resolves afterwards, so the renderer is never told what the factor is.

## Project layout

```
src/
├── SoftEngine.Core/        # engine, no UI dependency (net10.0 class library)
│   ├── Animation/          # keyframe tracks, interpolation modes, node channels, clips, playback
│   ├── Buffers/            # FrameBuffer (color + z-buffer + pixel probe), pooled Vertex/World buffers
│   ├── Diagnostics/        # render stats, graphics event log, pixel history, frame captures
│   ├── Editing/            # undoable edits and the history the viewport records drags into
│   ├── Geometry/           # IMesh/Mesh, Material, Triangle, tangents, primitives, OBJ/Collada importers
│   │   ├── Gltf/           # glTF 2.0 / GLB reader: schema, accessor decoding, scene building
│   │   └── Skinning/       # skeleton, skin weights, linear blend skinning, generated bone chain
│   ├── Gizmos/             # grid, world axes, skeleton, the draggable transform handles, snapping
│   ├── Imaging/            # PNG codec for the engine's own frames
│   ├── Picking/            # ray, ray-triangle intersection, scene picker
│   ├── Pipeline/           # Renderer, settings, homogeneous clipping, sky pass
│   │   ├── Culling/        # frustum planes, occluder selection, occlusion depth pyramid
│   │   ├── Debugging/      # buffer views: depth, normals, overdraw, cascades, occlusion
│   │   ├── PostProcess/    # effect stack: SSAO, bloom, tone map, FXAA, vignette
│   │   └── Shadows/        # depth-only shadow-map pass, cascade fitting
│   ├── Rasterization/      # scanline filler, painters, shaders, varyings, texture sampling
│   ├── Scenes/             # world, camera, projections, lights, fog and shadow settings
│   │   ├── Graph/          # SceneNode transform hierarchy
│   │   └── Serialization/  # the JSON scene document, and moving it on and off a live Scene
│   └── Shading/            # linear colour, light sets, ambient cube, sRGB conversion, shadow map,
│                           #   GGX, BRDF table, prefiltered environment
├── SoftEngine.Gpu/         # OpenGL backend via Silk.NET: same IRenderer, fill on the adapter
│   └── Shaders/            # GLSL — the CPU shaders ported, plus the depth, sky and overlay passes
├── SoftEngine.Cli/         # headless renderer: model or scene in, PNG out (net10.0 console)
└── SoftEngine.WinForms/    # interactive front-end (net10.0-windows)
    ├── Debugging/          # event list, object table and pixel history panels
    └── Dialogs/            # model picker

bench/SoftEngine.Benchmarks/   # headless frame-time harness (net10.0 console)
tests/SoftEngine.Core.Tests/   # xUnit suite over the Core
└── Golden/                    # golden-image harness, scenes and committed baselines
```

`SoftEngine.Cli` and the golden-image harness share the Core's own
[`PngCodec`](src/SoftEngine.Core/Imaging/PngCodec.cs), as the viewer's screenshot key does. That is
not a retreat from the line the importers hold — an OBJ or glTF reader still resolves an image to
bytes and hands the *decoding* to whoever hosts it, because a texture arrives in whatever format an
artist saved it in. Writing out the frame the renderer just produced is a different question, about
the engine's own buffer in its own layout, and three copies of a PNG encoder is not a line being
held but a line being paid for repeatedly.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows (the interactive app uses WinForms; `SoftEngine.Core` itself is platform-neutral).
- Nothing else to render on the CPU. The GPU backend wants an OpenGL 3.3 driver, and says so
  and falls back when there isn't one — `SoftEngine.Core` still has no graphics-API dependency,
  and neither does anything that only references it.

## Build & run

```bash
# build everything
dotnet build SoftEngine.slnx

# run the interactive app
dotnet run --project src/SoftEngine.WinForms

# render a model to a PNG with no window
dotnet run -c Release --project src/SoftEngine.Cli -- model.gltf -o frame.png -w 1920 -h 1080

# the same, filled by a graphics adapter (see "Rendering on the GPU")
dotnet run -c Release --project src/SoftEngine.Cli -- model.gltf --gpu -o frame.png --stats

# run the tests
dotnet test tests/SoftEngine.Core.Tests

# measure the renderer (Release, or the numbers measure the debugger)
dotnet run -c Release --project bench/SoftEngine.Benchmarks
```

## Performance notes

The renderer avoids managed-heap traffic on the pixel hot path:

- **`ColorRGB` is a `readonly struct`** — shaders can produce a color per pixel without allocating.
- **Struct-based varyings and shaders** let the JIT inline the shade call instead of dispatching through an interface.
- **`ArrayPool`-backed vertex buffers** are rented per frame rather than allocated.
- **Tile bins are flat arrays filled by a counting sort**, reused across frames, so binning tens of thousands of triangles allocates nothing either.
- **The frame's lights are flattened into structs once**, so the per-pixel loop over them branches on a field instead of dispatching through `ILight`.

One cost was accepted rather than avoided: shaders now return `LinearColor`, so a shader with
no HDR range to give converts a `ColorRGB` in and the framebuffer converts it back out —
six lookup-table reads per pixel that the old packed-byte path did not pay. Both tables are
small enough to stay in L1, and the alternative was two parallel shading paths.

### Work the frame no longer does

Three things the renderer used to pay for and now does not. Figures are the benchmark harness at
1280×720 on twenty hardware threads, best of thirty frames.

**The parallel fill was chosen by triangle count.** Below thirty-two triangles the frame was filled
on the calling thread, on the grounds that scheduling would cost more than it saved. That is true
while triangles are small and false in the way that matters: sixteen triangles that each cover the
viewport are fourteen million pixels of fill, and sixteen is fewer than thirty-two, so they were
being drawn on one core. The decision is now made on tile coverage — the unit the fill is actually
divided into — after binning rather than before it. *`big-triangles` 22.3 ms → 3.4 ms.*

**The clear was one thread, and cleared a buffer nothing was going to read.** At 1080p the depth
buffer alone is 8 MB, and one thread sweeping it cannot saturate the memory controller; it is now
split into bands of rows. On an HDR target the shaded pixels live in the float buffer and `Screen`
holds nothing until the frame resolves — which rewrites every pixel of it — so clearing it first
was a sweep of the whole image thrown away later in the same frame. *`overdraw` 7.8 ms → 6.0 ms,
`shadows` 7.2 ms → 5.4 ms.*

**Model-to-view was one thread.** Transforming a mesh's vertices is a pure map — every vertex reads
its own slot and writes its own — so a dense model's, which is tens of thousands of them, is now
split across the cores. The cull phase around it stays sequential: it appends to the frame's draw
list, whose order the transparent sort and the pixel probe both depend on.
*`dense-model` 9.2 ms → 7.9 ms.*

## Testing

`dotnet test tests/SoftEngine.Core.Tests` runs the suite over the Core. Most of it is ordinary
unit tests — this triangle is back-facing, that matrix round-trips, the near plane splits a
straddling triangle into two — and there is a whole class of regression none of them can reach.

A renderer can satisfy every property a test names and still produce a picture that is visibly
wrong. Nothing in four hundred passing tests notices that the specular term came out a tenth
dimmer, that a normal map is being sampled with its green channel flipped, or that the tone-map
curve shifted. Each is a change to a number no test mentions, and all three are obvious the
moment you look at the frame.

### Golden images

So the frame itself is an assertion. Fifteen scenes in
[`tests/SoftEngine.Core.Tests/Golden`](tests/SoftEngine.Core.Tests/Golden) are rendered headless
at 320×180 and compared against PNGs committed beside them — a reviewer can open the baseline,
and a change to it shows up in the diff as a picture. Between them they cover every painter, the
shadow pass and its cascades, materials and normal mapping, the physically-based path with its
environment, transparency, fog, the post-process effects, skinning, supersampling and the
overlays: each a body of arithmetic that is one edit away from being quietly wrong.

Scenes are generated rather than loaded. A baseline that depends on a model in the front-end's
assets folder breaks when the model is re-exported, which teaches everyone to re-record on
failure without looking — the exact habit the harness exists to prevent. For the same reason
`SOFTENGINE_UPDATE_GOLDEN=1` is the only way to rewrite one: a suite that quietly re-records
whatever the renderer just did will agree with every regression it ever meets.

Comparison is three numbers rather than one, because the failures worth catching have different
shapes. A shading term that moves by a percent moves nearly every lit pixel a little, which a
mean absolute error sees and a per-pixel count would let through; a geometry or culling bug
moves a few pixels a great deal, which a count sees and a mean averages away. Exact equality is
tempting — the same scene rendered twice on this machine really is identical, since a fill worker
owns a screen tile and no two of them touch a pixel — but whether the JIT contracts a multiply
and an add into one FMA is a property of the host, and an ulp of drift lands on a channel
boundary often enough that a zero-tolerance baseline would fail somewhere other than where it was
recorded. A failing run writes the actual frame and a diff image next to the baseline and names
all three in the message.

The occlusion pass gets a stronger test than a baseline, because it makes a stronger claim: every
golden scene is rendered twice, with the pass on and off, and the two frames are compared at
**zero** tolerance. An optimization that decides what not to draw is only correct if what is
drawn does not change, and no count of rejected meshes says that — a pass that culled the wall
itself would report splendid numbers.

### Measuring it

Work is measured rather than assumed. [`bench/SoftEngine.Benchmarks`](bench/SoftEngine.Benchmarks)
is a headless harness that renders fixed scenes and reports the median frame time:

```bash
dotnet run -c Release --project bench/SoftEngine.Benchmarks
dotnet run -c Release --project bench/SoftEngine.Benchmarks -- --scene overdraw --compare
```

The engine renders into a plain `int[]`, so measuring it needs no window, no GPU and no
platform beyond the runtime — which is what makes the numbers reproducible on any machine that
can build the solution. Seven scenes cover the shapes the renderer is built around: a dense
model where the cost is per-triangle setup, heavy overdraw where it is the depth test, a
handful of screen-filling triangles, thousands of small meshes, geometry hidden behind a wall,
the shadow pass, and the physically-based shader.

The **median** rather than the mean, because a frame time distribution on a desktop OS has a
long right tail belonging to the scheduler rather than to the renderer — one preempted frame
moves a mean and cannot move a median. Warm-up frames are discarded for the same reason in the
other direction: the first frame through a scene pays for JIT and for every buffer, tile bin,
mip chain and prefiltered environment being allocated.

`--compare` re-runs each scene with one optimization switched off and reports the ratio.
`--compare` alone measures hierarchical-Z; `--compare occlusion` measures the culling pass.

```bash
dotnet run -c Release --project bench/SoftEngine.Benchmarks -- --compare occlusion
```

On eight hardware threads at 1280×720, hierarchical-Z is worth **≈4×** on the overdraw scene and
about 1× everywhere else, which is what it should be: the tile's coarse depth bound cannot reject
anything in a scene one layer deep, and it costs a periodic scan to find that out. Occlusion
culling is worth **≈1.6×** on the scene built around it and about 1× on the rest.

Both tables also report how many meshes were rasterized as occluders and how many were rejected
because of them, in every scene rather than only the compared one. A pass that rejects nothing
and a pass that rejects everything both show up as a speedup of about one, and they call for
opposite responses.

## Roadmap

- Replace `Mesh`'s `Rotation3D` (Euler angles) with the quaternion rotation `SceneNode` already uses — which is also what would let the transform gizmo's rings turn a mesh about a world axis rather than about its own.
- Animation blending — crossfading two clips, and layering one over another — which the player is one weight away from.
- A mip-level buffer view, the one view of the frame the visualizer still has nothing to draw from.
- More than one shadow-casting light, which needs a depth buffer and a pass per light.
- Morph targets, the one part of glTF's animation the importer reads past.
- A deferred or visibility-buffer path, which would let SSAO darken the ambient term alone instead of the finished image — the limitation the post-processing section admits to.
- Testing occluders against each other, by building the occlusion pyramid front to back rather than all at once — the first of the two limits that section admits to.
- Alpha-tested (`MASK`) materials: the glTF importer reads `alphaMode` but only acts on `BLEND`, so foliage and fences render as opaque quads — and cast opaque shadows.
- A JPEG decoder for the headless renderer, which reads PNG only and says so.

## Credits

Inspired by David Rousset's tutorial series
[*Learning how to write a 3D soft engine from scratch in C#, TypeScript or JavaScript*](https://www.davrous.com/2013/06/13/tutorial-series-learning-how-to-write-a-3d-soft-engine-from-scratch-in-c-typescript-or-javascript/),
which this project started from before growing its own pipeline, rasterizer, and shading system.

## License

[MIT](LICENSE) © Hilthon
