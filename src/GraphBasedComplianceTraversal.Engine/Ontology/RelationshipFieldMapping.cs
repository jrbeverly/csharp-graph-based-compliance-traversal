namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>
/// Reconciles one relationship field occurrence from the repository fixtures
/// onto a canonical edge type: a field named <see cref="FieldPath"/> on records
/// of <see cref="SourceType"/> whose values reference <see cref="TargetType"/>
/// corresponds to <see cref="Edge"/> read in <see cref="Direction"/>. Dotted
/// paths address nested members (for example <c>subject.artifact</c> or
/// <c>control.specification</c>). A single fixture field may carry several
/// mappings when its targets span multiple node types (for example
/// <c>depends_on</c> or <c>scope</c>); each (source type, field, target type)
/// triple maps to exactly one edge type.
/// </summary>
public sealed record RelationshipFieldMapping(
    NodeType SourceType,
    string FieldPath,
    NodeType TargetType,
    EdgeType Edge,
    EdgeDirection Direction,
    string? Note = null)
{
    /// <summary>The canonical edge name this field occurrence maps onto, in the direction it is used.</summary>
    public string CanonicalName => Direction == EdgeDirection.Forward ? Edge.ForwardName : Edge.InverseName;
}
