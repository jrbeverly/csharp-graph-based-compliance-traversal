namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>Which declared name of an <see cref="EdgeType"/> a usage matches.</summary>
public enum EdgeDirection
{
    /// <summary>
    /// The usage matches <see cref="EdgeType.ForwardName"/>: from
    /// <see cref="EdgeType.FromType"/> to <see cref="EdgeType.ToType"/>.
    /// </summary>
    Forward,

    /// <summary>
    /// The usage matches <see cref="EdgeType.InverseName"/>: from
    /// <see cref="EdgeType.ToType"/> to <see cref="EdgeType.FromType"/>.
    /// </summary>
    Inverse,
}
