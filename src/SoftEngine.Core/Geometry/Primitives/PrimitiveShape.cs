namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// The generated shapes a front-end can offer to add to a world, named rather than typed so a
/// menu, a scene document or a command line can carry the choice around before anything is built.
/// </summary>
public enum PrimitiveShape
{
    /// <summary>A flat sheet in the XZ plane facing +Y. See <see cref="PlaneMesh"/>.</summary>
    Plane,

    /// <summary>A cube. See <see cref="Primitives.Box"/>.</summary>
    Box,

    /// <summary>A sphere of latitude rings, textured. See <see cref="Primitives.UvSphere"/>.</summary>
    UvSphere,

    /// <summary>A subdivided icosahedron: even triangles, no UVs. See <see cref="Primitives.IcoSphere"/>.</summary>
    IcoSphere,

    /// <summary>A capped cylinder about Y. See <see cref="Primitives.Cylinder"/>.</summary>
    Cylinder,

    /// <summary>A capped cone about Y. See <see cref="Primitives.Cone"/>.</summary>
    Cone,

    /// <summary>A ring lying in the XZ plane. See <see cref="Primitives.Torus"/>.</summary>
    Torus,
}
