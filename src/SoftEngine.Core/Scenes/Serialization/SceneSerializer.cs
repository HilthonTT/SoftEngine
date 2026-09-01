using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Math;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoftEngine.Core.Scenes.Serialization;

public static class SceneSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,

        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        Converters = { new Vector3JsonConverter(), new QuaternionJsonConverter() },
    };

    #region Files

    public static string ToJson(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document, nameof(document));

        return JsonSerializer.Serialize(document, Options);
    }

    public static SceneDocument FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json, nameof(json));

        return JsonSerializer.Deserialize<SceneDocument>(json, Options)
            ?? throw new JsonException("the document was empty");
    }

    public static void Save(string path, SceneDocument document) =>
        File.WriteAllText(path, ToJson(document));

    public static SceneDocument Load(string path) =>
        FromJson(File.ReadAllText(path));

    #endregion

    #region Capture

    public static SceneDocument Capture(Scene scene, RendererSettings? settings = null, PostProcessStack? post = null)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        return new SceneDocument
        {
            Camera = scene.Camera is { } camera ? new CameraState { Position = camera.Position } : null,
            Projection = CaptureProjection(scene.Projection),
            Meshes = CaptureMeshes(scene.World),
            Lights = CaptureLights(scene.World),
            Environment = new EnvironmentState
            {
                ShowSky = scene.ShowSky,
                SkyIntensity = scene.SkyIntensity,
                AmbientFromEnvironment = scene.AmbientFromEnvironment,
                AmbientIntensity = scene.AmbientIntensity,
            },
            Fog = CaptureFog(scene.Fog),
            Shadows = CaptureShadows(scene.Shadows),
            Rendering = CaptureRendering(scene, settings),
            Post = CapturePost(post),
        };
    }

    private static ProjectionState? CaptureProjection(IProjection? projection) => projection switch
    {
        PerspectiveProjection p => new ProjectionState
        {
            Kind = "perspective",
            FieldOfView = p.FieldOfView,
            Near = p.ZNear,
            Far = p.ZFar,
        },

        OrthographicProjection o => new ProjectionState
        {
            Kind = "orthographic",
            ViewHeight = o.ViewHeight,
            Near = o.ZNear,
            Far = o.ZFar,
        },

        _ => null,
    };

    private static List<MeshState>? CaptureMeshes(IWorld? world)
    {
        if (world?.Meshes is not { Count: > 0 } meshes)
        {
            return null;
        }

        var states = new List<MeshState>(meshes.Count);

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            states.Add(new MeshState
            {
                Index = i,
                Position = mesh.Position,
                Rotation = new Vector3(mesh.Rotation.XPitch, mesh.Rotation.YYaw, mesh.Rotation.ZRoll),
                Scale = mesh.Scale,
                Visible = mesh.Visible,
                Opacity = mesh.Opacity,
            });
        }

        return states;
    }

    private static List<LightState>? CaptureLights(IWorld? world)
    {
        if (world?.Lights is not { Count: > 0 } lights)
        {
            return null;
        }

        var states = new List<LightState>(lights.Count);

        foreach (var light in lights)
        {
            states.Add(light switch
            {
                DirectionalLight d => new LightState
                {
                    Kind = "directional",
                    Direction = d.Direction,
                    Intensity = d.Intensity,
                    Color = Pack(d.Color),
                },

                SpotLight s => new LightState
                {
                    Kind = "spot",
                    Position = s.Position,
                    Direction = s.Direction,
                    Intensity = s.Intensity,
                    Color = Pack(s.Color),
                    Range = Finite(s.Range),
                    InnerAngle = s.InnerAngle,
                    OuterAngle = s.OuterAngle,
                },

                PointLight p => new LightState
                {
                    Kind = "point",
                    Position = p.Position,
                    Intensity = p.Intensity,
                    Color = Pack(p.Color),
                    Range = Finite(p.Range),
                },

                _ => new LightState { Kind = "point" },
            });
        }

        return states;
    }

    private static FogState CaptureFog(FogSettings fog) => new()
    {
        Enabled = fog.Enabled,
        Mode = fog.Mode == FogMode.Exponential ? "exponential" : "linear",
        Color = Pack(fog.Color),
        Start = fog.Start,
        End = fog.End,
        Density = fog.Density,
    };

    private static ShadowState CaptureShadows(ShadowSettings shadows) => new()
    {
        Enabled = shadows.Enabled,
        Resolution = shadows.Resolution,
        DepthBias = shadows.DepthBias,
        SlopeBias = shadows.SlopeBias,
        SoftFilter = shadows.SoftFilter,
        CascadeCount = shadows.CascadeCount,
        SplitBlend = shadows.SplitBlend,
        MaxDistance = shadows.MaxDistance,
        Strength = shadows.Strength,
    };

    private static RenderState CaptureRendering(Scene scene, RendererSettings? settings) => new()
    {
        GammaCorrect = scene.GammaCorrect,
        HighDynamicRange = scene.HighDynamicRange,
        BackFaceCulling = settings?.BackFaceCulling ?? false,
        ShowTriangles = settings?.ShowTriangles ?? false,
        ShowXZGrid = settings?.ShowXZGrid ?? false,
        ShowAxes = settings?.ShowAxes ?? false,
        ShowSkeleton = settings?.ShowSkeleton ?? false,
        HierarchicalZ = settings?.HierarchicalZ ?? true,
        OcclusionCulling = settings?.OcclusionCulling ?? true,
        TemporalAntiAliasing = settings?.TemporalAntiAliasing ?? false,
        MotionBlur = settings?.MotionBlur ?? false,
        OrderIndependentTransparency = settings?.OrderIndependentTransparency ?? false,
        DebugView = (settings?.DebugView ?? DebugView.Off).ToString(),
    };

    private static PostState? CapturePost(PostProcessStack? post)
    {
        if (post is null)
        {
            return null;
        }

        var state = new PostState();

        if (post.Find<SsrEffect>() is { } ssr)
        {
            state.Ssr = new SsrState
            {
                Enabled = ssr.Enabled,
                Strength = ssr.Strength,
                MaxSteps = ssr.MaxSteps,
                MaxDistance = ssr.MaxDistance,
                Thickness = ssr.Thickness,
                MaxRoughness = ssr.MaxRoughness,
                BlurRadius = ssr.BlurRadius,
                EdgeFade = ssr.EdgeFade,
            };
        }

        if (post.Find<SsaoEffect>() is { } ssao)
        {
            state.Ssao = new SsaoState
            {
                Enabled = ssao.Enabled,
                Strength = ssao.Strength,
                Radius = ssao.Radius,
                RangeCutoff = ssao.RangeCutoff,
                Bias = ssao.Bias,
                BlurRadius = ssao.BlurRadius,
            };
        }

        if (post.Find<BloomEffect>() is { } bloom)
        {
            state.Bloom = new BloomState
            {
                Enabled = bloom.Enabled,
                Threshold = bloom.Threshold,
                Intensity = bloom.Intensity,
                Downsample = bloom.Downsample,
                Radius = bloom.Radius,
                Passes = bloom.Passes,
            };
        }

        if (post.Find<ToneMapEffect>() is { } tone)
        {
            state.ToneMap = new ToneMapState
            {
                Enabled = tone.Enabled,
                Exposure = tone.Exposure,
                Operator = tone.Operator.ToString(),
            };
        }

        if (post.Find<FxaaEffect>() is { } fxaa)
        {
            state.Fxaa = new FxaaState
            {
                Enabled = fxaa.Enabled,
                EdgeThreshold = fxaa.EdgeThreshold,
                EdgeThresholdMin = fxaa.EdgeThresholdMin,
                Strength = fxaa.Strength,
            };
        }

        if (post.Find<VignetteEffect>() is { } vignette)
        {
            state.Vignette = new VignetteState
            {
                Enabled = vignette.Enabled,
                Intensity = vignette.Intensity,
                Radius = vignette.Radius,
                Softness = vignette.Softness,
            };
        }

        return state;
    }

    #endregion

    #region Apply

    public static void Apply(
        SceneDocument document,
        Scene scene,
        RendererSettings? settings = null,
        PostProcessStack? post = null)
    {
        ArgumentNullException.ThrowIfNull(document, nameof(document));
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        if (document.Camera is { } camera && scene.Camera is { } target)
        {
            target.Position = camera.Position;
        }

        if (BuildProjection(document.Projection) is { } projection)
        {
            scene.Projection = projection;
        }

        ApplyMeshes(document.Meshes, scene.World);
        ApplyLights(document.Lights, scene.World);

        if (document.Environment is { } environment)
        {
            scene.ShowSky = environment.ShowSky;
            scene.SkyIntensity = environment.SkyIntensity;
            scene.AmbientFromEnvironment = environment.AmbientFromEnvironment;
            scene.AmbientIntensity = environment.AmbientIntensity;
        }

        ApplyFog(document.Fog, scene.Fog);
        ApplyShadows(document.Shadows, scene.Shadows);

        if (document.Rendering is { } rendering)
        {
            scene.GammaCorrect = rendering.GammaCorrect;
            scene.HighDynamicRange = rendering.HighDynamicRange;

            if (settings is not null)
            {
                settings.BackFaceCulling = rendering.BackFaceCulling;
                settings.ShowTriangles = rendering.ShowTriangles;
                settings.ShowXZGrid = rendering.ShowXZGrid;
                settings.ShowAxes = rendering.ShowAxes;
                settings.ShowSkeleton = rendering.ShowSkeleton;
                settings.HierarchicalZ = rendering.HierarchicalZ;
                settings.OcclusionCulling = rendering.OcclusionCulling;
                settings.TemporalAntiAliasing = rendering.TemporalAntiAliasing;
                settings.MotionBlur = rendering.MotionBlur;
                settings.OrderIndependentTransparency = rendering.OrderIndependentTransparency;
                settings.DebugView = ParseEnum(rendering.DebugView, DebugView.Off);
            }
        }

        ApplyPost(document.Post, post);
    }

    private static IProjection? BuildProjection(ProjectionState? state) => state?.Kind?.ToLowerInvariant() switch
    {
        "orthographic" => new OrthographicProjection(state.ViewHeight, state.Near, state.Far),
        "perspective" => new PerspectiveProjection(state.FieldOfView, state.Near, state.Far),
        _ => null,
    };

    private static void ApplyMeshes(List<MeshState>? states, IWorld? world)
    {
        if (states is null || world?.Meshes is not { } meshes)
        {
            return;
        }

        foreach (var state in states)
        {
            if ((uint)state.Index >= (uint)meshes.Count)
            {
                continue;
            }

            var mesh = meshes[state.Index];

            mesh.Position = state.Position;
            mesh.Scale = state.Scale;
            mesh.Rotation = new Rotation3D(state.Rotation.X, state.Rotation.Y, state.Rotation.Z);

            if (mesh is Mesh concrete)
            {
                concrete.Visible = state.Visible;
                concrete.Opacity = state.Opacity;
            }
        }
    }

    private static void ApplyLights(List<LightState>? states, IWorld? world)
    {
        if (states is null || world is null)
        {
            return;
        }

        var lights = new List<ILight>(states.Count);

        foreach (var state in states)
        {
            lights.Add(BuildLight(state));
        }

        world.Lights = lights;
    }

    private static ILight BuildLight(LightState state)
    {
        var color = Unpack(state.Color);

        return state.Kind?.ToLowerInvariant() switch
        {
            "directional" => new DirectionalLight
            {
                Direction = state.Direction,
                Intensity = state.Intensity,
                Color = color,
            },

            "spot" => new SpotLight
            {
                Position = state.Position,
                Direction = state.Direction,
                Intensity = state.Intensity,
                Color = color,

                OuterAngle = state.OuterAngle,
                InnerAngle = state.InnerAngle,
                Range = state.Range ?? float.PositiveInfinity,
            },

            _ => new PointLight
            {
                Position = state.Position,
                Intensity = state.Intensity,
                Color = color,
                Range = state.Range ?? float.PositiveInfinity,
            },
        };
    }

    private static void ApplyFog(FogState? state, FogSettings fog)
    {
        if (state is null)
        {
            return;
        }

        fog.Enabled = state.Enabled;
        fog.Mode = string.Equals(state.Mode, "exponential", StringComparison.OrdinalIgnoreCase)
            ? FogMode.Exponential
            : FogMode.Linear;
        fog.Color = Unpack(state.Color);
        fog.Start = state.Start;
        fog.End = state.End;
        fog.Density = state.Density;
    }

    private static void ApplyShadows(ShadowState? state, ShadowSettings shadows)
    {
        if (state is null)
        {
            return;
        }

        shadows.Enabled = state.Enabled;
        shadows.Resolution = state.Resolution;
        shadows.DepthBias = state.DepthBias;
        shadows.SlopeBias = state.SlopeBias;
        shadows.SoftFilter = state.SoftFilter;
        shadows.CascadeCount = state.CascadeCount;
        shadows.SplitBlend = state.SplitBlend;
        shadows.MaxDistance = state.MaxDistance;
        shadows.Strength = state.Strength;
    }

    private static void ApplyPost(PostState? state, PostProcessStack? post)
    {
        if (state is null || post is null)
        {
            return;
        }

        if (state.Ssr is { } r && post.Find<SsrEffect>() is { } ssr)
        {
            ssr.Enabled = r.Enabled;
            ssr.Strength = r.Strength;
            ssr.MaxSteps = r.MaxSteps;
            ssr.MaxDistance = r.MaxDistance;
            ssr.Thickness = r.Thickness;
            ssr.MaxRoughness = r.MaxRoughness;
            ssr.BlurRadius = r.BlurRadius;
            ssr.EdgeFade = r.EdgeFade;
        }

        if (state.Ssao is { } s && post.Find<SsaoEffect>() is { } ssao)
        {
            ssao.Enabled = s.Enabled;
            ssao.Strength = s.Strength;
            ssao.Radius = s.Radius;
            ssao.RangeCutoff = s.RangeCutoff;
            ssao.Bias = s.Bias;
            ssao.BlurRadius = s.BlurRadius;
        }

        if (state.Bloom is { } b && post.Find<BloomEffect>() is { } bloom)
        {
            bloom.Enabled = b.Enabled;
            bloom.Threshold = b.Threshold;
            bloom.Intensity = b.Intensity;
            bloom.Downsample = b.Downsample;
            bloom.Radius = b.Radius;
            bloom.Passes = b.Passes;
        }

        if (state.ToneMap is { } t && post.Find<ToneMapEffect>() is { } tone)
        {
            tone.Enabled = t.Enabled;
            tone.Exposure = t.Exposure;
            tone.Operator = ParseEnum(t.Operator, ToneMapOperator.Aces);
        }

        if (state.Fxaa is { } f && post.Find<FxaaEffect>() is { } fxaa)
        {
            fxaa.Enabled = f.Enabled;
            fxaa.EdgeThreshold = f.EdgeThreshold;
            fxaa.EdgeThresholdMin = f.EdgeThresholdMin;
            fxaa.Strength = f.Strength;
        }

        if (state.Vignette is { } v && post.Find<VignetteEffect>() is { } vignette)
        {
            vignette.Enabled = v.Enabled;
            vignette.Intensity = v.Intensity;
            vignette.Radius = v.Radius;
            vignette.Softness = v.Softness;
        }
    }

    #endregion

    #region Conversions

    private static int[] Pack(ColorRGB color) => [color.R, color.G, color.B];

    private static ColorRGB Unpack(int[]? channels)
    {
        if (channels is not { Length: >= 3 })
        {
            return ColorRGB.White;
        }

        return new ColorRGB(Byte(channels[0]), Byte(channels[1]), Byte(channels[2]));
    }

    private static byte Byte(int value) => (byte)System.Math.Clamp(value, 0, 255);

    private static float? Finite(float range) => float.IsFinite(range) ? range : null;

    private static T ParseEnum<T>(string? name, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(name, ignoreCase: true, out var value) ? value : fallback;

    #endregion
}
