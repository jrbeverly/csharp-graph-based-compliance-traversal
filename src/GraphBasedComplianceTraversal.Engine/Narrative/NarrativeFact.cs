using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>The kind of supporting fact a narrative statement is derived from.</summary>
public enum NarrativeFactKind
{
    /// <summary>The node exists with its declared type.</summary>
    Node,

    /// <summary>A stored typed edge connects the step's two nodes.</summary>
    Edge,

    /// <summary>A named property of a node the narrative reads.</summary>
    Property,

    /// <summary>The observation store's current surface for an observation node.</summary>
    State,
}

/// <summary>
/// One supporting fact of a compiled narrative: an established graph fact a
/// narrative statement rests on, carrying the provenance of that fact —
/// <see cref="NarrativeFactKind.Node"/> for a node's existence,
/// <see cref="NarrativeFactKind.Edge"/> for the typed edge a step traversed,
/// <see cref="NarrativeFactKind.Property"/> for a node property the narrative
/// reads, and <see cref="NarrativeFactKind.State"/> for the observation
/// store's current surface of an observation. Every fact is an immutable
/// snapshot of what the graph (or, for state facts, the observation store)
/// established: a renderer — deterministic or AI — receives these records,
/// so the narrative rests on exactly the facts it names and on nothing a
/// renderer could invent downstream.
/// </summary>
public sealed record NarrativeFact
{
    /// <summary>
    /// Creates a supporting fact. <see cref="NodeId"/> names the fact's
    /// subject node — the step's from-node for an edge fact, the node itself
    /// for the other kinds — and <see cref="Name"/> names what the fact is
    /// about: the node's declared type, the queried edge name, the property
    /// name, or <c>state</c>. An edge fact also carries the reached
    /// <see cref="TargetNodeId"/>; a property or state fact carries the
    /// value text in <see cref="Value"/>.
    /// </summary>
    public NarrativeFact(
        NarrativeFactKind kind,
        string nodeId,
        string name,
        string? targetNodeId,
        string? value,
        FactProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(provenance);

        if (kind == NarrativeFactKind.Edge)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        }

        if (kind is NarrativeFactKind.Property or NarrativeFactKind.State)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        }

        Kind = kind;
        NodeId = nodeId;
        Name = name;
        TargetNodeId = targetNodeId;
        Value = value;
        Provenance = provenance;
    }

    /// <summary>What the fact is: a node, an edge, a property, or a store surface.</summary>
    public NarrativeFactKind Kind { get; }

    /// <summary>The fact's subject node id.</summary>
    public string NodeId { get; }

    /// <summary>The declared type name, queried edge name, property name, or <c>state</c>.</summary>
    public string Name { get; }

    /// <summary>The reached node id for an edge fact; <c>null</c> for the other kinds.</summary>
    public string? TargetNodeId { get; }

    /// <summary>The value text for a property or state fact; <c>null</c> for the other kinds.</summary>
    public string? Value { get; }

    /// <summary>Where the fact came from, record by record.</summary>
    public FactProvenance Provenance { get; }
}
