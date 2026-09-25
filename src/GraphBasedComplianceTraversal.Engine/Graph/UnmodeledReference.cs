using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Graph;

/// <summary>
/// An instance of a relationship field the ontology slice intentionally does
/// not model: a recorded reference from a stored node to an id through a field
/// declared by <c>EdgeTypeRegistry.UnmodeledRelationships</c> (for example the
/// fixtures' <c>generic_risk: ARR-RISK-044</c> or <c>owner: team:platform-engineering</c>).
/// Recorded through <see cref="TypedPropertyGraph.RecordUnmodeledReference"/>
/// so the reference is represented rather than silently dropped, but it is
/// never an edge: it has no edge type and cannot be traversed.
/// </summary>
public sealed class UnmodeledReference
{
    internal UnmodeledReference(string fromId, NodeType fromType, string fieldPath, string targetId, string reason)
    {
        FromId = fromId;
        FromType = fromType;
        FieldPath = fieldPath;
        TargetId = targetId;
        Reason = reason;
    }

    /// <summary>The id of the node the reference was recorded from.</summary>
    public string FromId { get; }

    /// <summary>The node type of the referencing node.</summary>
    public NodeType FromType { get; }

    /// <summary>The relationship field carrying the reference (fixture vocabulary).</summary>
    public string FieldPath { get; }

    /// <summary>The referenced id, which is outside the slice's declared node types.</summary>
    public string TargetId { get; }

    /// <summary>Why the field is unmodeled, from the ontology declaration.</summary>
    public string Reason { get; }
}
