using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Graph;

/// <summary>
/// A node in the typed property graph: the stable <see cref="Id"/> used by the
/// repository records (for example <c>aws:s3:prod-release-artifacts</c>), its
/// declared <see cref="NodeType"/>, and arbitrary JSON-valued properties.
/// Values are JSON nodes, the same value representation used by the IO and
/// state layers. Nodes are created through <see cref="TypedPropertyGraph.AddNode"/>,
/// which validates the type against the ontology slice. The node carries
/// provenance for itself and per property, so any fact in the graph can
/// answer where it came from.
/// </summary>
public sealed class GraphNode
{
    private readonly Dictionary<string, JsonNode?> _properties;
    private readonly Dictionary<string, FactProvenance> _propertyProvenance = new(StringComparer.Ordinal);
    private FactProvenance _provenance;

    /// <summary>
    /// Creates a node, optionally seeding its properties and its provenance. A
    /// repeated property name in a seed keeps the last value. A JSON null
    /// value carries no information and is treated as absent by readers.
    /// </summary>
    public GraphNode(
        string id,
        NodeType type,
        IEnumerable<KeyValuePair<string, JsonNode?>>? properties = null,
        FactProvenance? provenance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(type);

        Id = id;
        Type = type;
        _provenance = provenance ?? FactProvenance.None;
        _properties = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (properties is not null)
        {
            foreach (var (property, value) in properties)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(property);
                _properties[property] = value;
            }
        }
    }

    /// <summary>
    /// The stable identifier of the node, shared with the repository records it
    /// was derived from.
    /// </summary>
    public string Id { get; }

    /// <summary>The node type, canonicalized to the ontology slice's declaration.</summary>
    public NodeType Type { get; }

    /// <summary>The arbitrary properties, keyed by name.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Properties => _properties;

    /// <summary>
    /// The provenance of the node itself — where its record came from.
    /// <see cref="FactProvenance.None"/> for a node whose source is not
    /// known, which is marked unknown rather than defaulted to
    /// authoritative.
    /// </summary>
    public FactProvenance Provenance => _provenance;

    /// <summary>
    /// The per-property provenance, keyed by property name; a property that
    /// was never assigned provenance is absent from the dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, FactProvenance> PropertyProvenance => _propertyProvenance;

    /// <summary>
    /// Sets (replaces) the provenance of a property: the records saying where
    /// the property's value came from. The property itself may be seeded here
    /// or added later by the caller that owns the node's record.
    /// </summary>
    public void SetPropertyProvenance(string property, params ProvenanceRecord[] records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        ArgumentNullException.ThrowIfNull(records);
        _propertyProvenance[property] = new FactProvenance(records);
    }

    /// <summary>
    /// The provenance of a property, or <see cref="FactProvenance.None"/> when
    /// no source has been recorded for it — a property with no recorded
    /// source is marked unknown, never defaulted to authoritative.
    /// </summary>
    public FactProvenance GetPropertyProvenance(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return _propertyProvenance.TryGetValue(property, out var provenance) ? provenance : FactProvenance.None;
    }

    /// <summary>
    /// Appends provenance records to the node's own provenance, skipping
    /// records already present. Used when a later source restates the node's
    /// existence (for example the AWS observation of a bucket already known
    /// from Terraform state): every source is retained, none duplicated.
    /// </summary>
    internal void AppendProvenance(params ProvenanceRecord[] records) =>
        _provenance = new FactProvenance(_provenance.Sources.Concat(records).Distinct().ToArray());

    /// <summary>
    /// Sets a property value after the node has been created. Used when a
    /// later source states a property the node does not yet carry; the value
    /// is never overwritten once present — which source to consult for a
    /// disagreeing value is the authority table's answer, not this setter's.
    /// </summary>
    internal void SetProperty(string name, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _properties[name] = value;
    }
}
