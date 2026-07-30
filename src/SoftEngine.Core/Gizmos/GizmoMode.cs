namespace SoftEngine.Core.Gizmos;

/// <summary>What a transform gizmo edits.</summary>
public enum GizmoMode
{
    /// <summary>No gizmo: clicks orbit and pick as they always did.</summary>
    Off,

    /// <summary>Three arrows; dragging one slides the mesh along that axis.</summary>
    Translate,

    /// <summary>Three rings; dragging one turns the mesh about that axis.</summary>
    Rotate,

    /// <summary>Three arms with a box on the end; dragging one stretches the mesh along that axis.</summary>
    Scale,
}
