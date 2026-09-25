using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>A normalized property: a name, its JSON value, and the provenance of the value.</summary>
public sealed record NormalizedProperty(string Name, JsonNode? Value, FactProvenance Provenance);

/// <summary>
/// A normalized node: a stable id, its declared node type, its properties, and
/// the provenance of the node itself. The id is the same identifier the
/// repository records use, so facts observed by an adapter land on the subject
/// the declared world names.
/// </summary>
public sealed record NormalizedNode(
    string Id,
    NodeType Type,
    IReadOnlyList<NormalizedProperty> Properties,
    FactProvenance Provenance);

/// <summary>
/// A normalized typed edge. Endpoint types are explicit so an edge may
/// reference a subject whose node no adapter has emitted yet — such an edge
/// is applied as an unresolved reference rather than dropped.
/// </summary>
public sealed record NormalizedEdge(
    string Name,
    string FromId,
    NodeType FromType,
    string ToId,
    NodeType ToType,
    FactProvenance Provenance);

/// <summary>
/// The output of one adapter: the normalized nodes and edges a parsed external
/// response states, each fact tagged with provenance. This is the normalized
/// state the eventual <c>API → Adapter → Normalized State → Graph</c> boundary
/// carries; <see cref="ApplyTo"/> projects it into a typed property graph.
/// <para>
/// <see cref="ApplyTo"/> merges: facts about the same subject from several
/// adapters (the bucket's Terraform state and its two AWS observations)
/// accumulate on one node, and the same edge stated by two adapters stays one
/// edge carrying every source record. A property stated more than once keeps
/// the first value, while every statement's provenance is retained — which
/// source to consult for a disagreeing value is the authority table's answer,
/// and representing the disagreement is the reconciliation milestone's work.
/// </para>
/// </summary>
public sealed class NormalizedState
{
    private readonly List<NormalizedNode> _nodes = [];
    private readonly List<NormalizedEdge> _edges = [];

    /// <summary>The normalized nodes, in emission order.</summary>
    public IReadOnlyList<NormalizedNode> Nodes => _nodes;

    /// <summary>The normalized edges, in emission order.</summary>
    public IReadOnlyList<NormalizedEdge> Edges => _edges;

    /// <summary>Adds a normalized node to the state.</summary>
    public NormalizedState AddNode(NormalizedNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
        return this;
    }

    /// <summary>Adds a normalized edge to the state.</summary>
    public NormalizedState AddEdge(NormalizedEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _edges.Add(edge);
        return this;
    }

    /// <summary>
    /// Projects the normalized facts into the graph: nodes are added or merged
    /// into existing ones, and edges are added or merged into an existing edge
    /// of the same type and endpoints. Merging retains every source record
    /// without duplicating it, so the graph can answer where each fact came
    /// from even when several adapters stated it.
    /// </summary>
    public void ApplyTo(TypedPropertyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        foreach (var node in _nodes)
        {
            ApplyNode(graph, node);
        }

        foreach (var edge in _edges)
        {
            ApplyEdge(graph, edge);
        }
    }

    private static void ApplyNode(TypedPropertyGraph graph, NormalizedNode normalized)
    {
        if (graph.TryGetNode(normalized.Id, out var existing))
        {
            if (existing.Type.Name != normalized.Type.Name)
            {
                throw new ArgumentException(
                    $"Normalized node '{normalized.Id}' declares type '{normalized.Type.Name}', but the graph already holds it as '{existing.Type.Name}'.",
                    nameof(normalized));
            }

            existing.AppendProvenance(normalized.Provenance.Sources.ToArray());
            foreach (var property in normalized.Properties)
            {
                if (!existing.Properties.ContainsKey(property.Name))
                {
                    existing.SetProperty(property.Name, property.Value);
                }

                existing.SetPropertyProvenance(
                    property.Name,
                    Merge(existing.GetPropertyProvenance(property.Name), property.Provenance).Sources.ToArray());
            }

            return;
        }

        var node = graph.AddNode(
            normalized.Id,
            normalized.Type,
            normalized.Properties.Select(property => new KeyValuePair<string, JsonNode?>(property.Name, property.Value)),
            normalized.Provenance);
        foreach (var property in normalized.Properties)
        {
            node.SetPropertyProvenance(property.Name, property.Provenance.Sources.ToArray());
        }
    }

    private static void ApplyEdge(TypedPropertyGraph graph, NormalizedEdge normalized)
    {
        // Resolving up front validates the edge against the graph's ontology,
        // so an invalid normalized edge fails loudly instead of being skipped.
        var resolved = graph.Registry.Resolve(normalized.Name, normalized.FromType, normalized.ToType);

        var existing = graph.Edges.FirstOrDefault(edge =>
            edge.EdgeType == resolved.EdgeType && edge.FromId == normalized.FromId && edge.ToId == normalized.ToId);
        if (existing is not null)
        {
            existing.AppendProvenance(normalized.Provenance.Sources.ToArray());
            return;
        }

        graph.AddEdge(normalized.Name, normalized.FromId, normalized.FromType, normalized.ToId, normalized.ToType, normalized.Provenance);
    }

    private static FactProvenance Merge(FactProvenance existing, FactProvenance incoming) =>
        new(existing.Sources.Concat(incoming.Sources).Distinct().ToArray());
}
