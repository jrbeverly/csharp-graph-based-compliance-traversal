using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Reconciliation;

/// <summary>
/// Attaches the adapters' observed facts to the declared subjects they
/// describe and records the declared-versus-observed reconciliation of every
/// property both worlds state. A normalized observed fact is matched to its
/// declared subject by stable id — the same identifier the repository records
/// and the adapters emit. For each subject property both sides carry:
/// <list type="bullet">
/// <item>agreeing values are recorded <see cref="ValidationState.Pass"/> —
/// corroboration carrying both provenances, so the fact is queryable as
/// agreeing across independent sources;</item>
/// <item>disagreeing values are recorded
/// <see cref="ValidationState.Conflicting"/> carrying both values, both
/// provenances, and the source-authority precedence the claim type selects —
/// never overwritten, never silently reconciled to one value.</item>
/// </list>
/// Observed facts about a subject with no declared node, and observed
/// properties the declared record does not state, are attached to the graph
/// as observed facts without a reconciliation verdict. The graph must already
/// hold the declared facts when <see cref="ReconcileAll"/> runs: the declared
/// value entered the graph first, so applying an observed state never
/// replaces it.
/// </summary>
public static class DeclaredObservedReconciler
{
    /// <summary>
    /// Reconciles every observed fact in <paramref name="observedStates"/>
    /// against the declared facts already in <paramref name="graph"/>,
    /// records the per-property reconciliation on the graph, and then applies
    /// the observed states so the observed facts are attached with their
    /// provenance. Re-running over an already reconciled graph is a no-op:
    /// no value is overwritten and no reconciliation record is duplicated.
    /// </summary>
    public static void ReconcileAll(TypedPropertyGraph graph, IReadOnlyList<NormalizedState> observedStates)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(observedStates);

        foreach (var subject in GroupObservedFacts(observedStates))
        {
            if (!graph.TryGetNode(subject.SubjectId, out var node))
            {
                // An observed-only subject (the build run, the commit): the
                // observed facts are attached, with nothing declared to
                // reconcile them against.
                continue;
            }

            foreach (var property in subject.Properties)
            {
                ReconcileProperty(graph, node, property);
            }
        }

        foreach (var state in observedStates)
        {
            state.ApplyTo(graph);
        }
    }

    private static void ReconcileProperty(TypedPropertyGraph graph, GraphNode node, ObservedProperty observed)
    {
        // Reference fields the ontology maps to typed edges are relationship
        // facts, reconciled through their edges (whose merge retains every
        // source record) — not compared as property values: the declared
        // record references the target by node id while an adapter may state
        // the same relationship in the external system's own vocabulary, so a
        // value comparison would report a false conflict.
        if (graph.Registry.FieldMappings.Any(mapping =>
            mapping.SourceType.Name == node.Type.Name && mapping.FieldPath == observed.Name))
        {
            return;
        }

        // The declared side is the property value the node already carries
        // with Declaration provenance. A property without a declared value —
        // observed-only, or stated by another observed source before the
        // declared record — has nothing to reconcile; it is attached, not
        // judged.
        if (!node.Properties.TryGetValue(observed.Name, out var declaredValue) || declaredValue is null)
        {
            return;
        }

        var declaredProvenance = new FactProvenance(
            node.GetPropertyProvenance(observed.Name).Sources
                .Where(record => record.Kind == SourceKind.Declaration)
                .ToArray());
        if (!declaredProvenance.HasKnownSource)
        {
            return;
        }

        var claimType = ClaimTypeFor(node.Type);
        var registry = SourceAuthorityRegistry.Slice;

        foreach (var observedValue in observed.Values)
        {
            var state = JsonNode.DeepEquals(declaredValue, observedValue.Value)
                ? ValidationState.Pass
                : ValidationState.Conflicting;

            // The precedence the authority table declares for this claim
            // type, over the most authoritative record of each side. The
            // verdict is recorded next to both values — never applied: which
            // side "wins" is a human decision the model only represents.
            var declaredKind = registry.MostAuthoritative(claimType, declaredProvenance.Sources)?.Kind ?? SourceKind.Unknown;
            var observedKind = registry.MostAuthoritative(claimType, observedValue.Provenance.Sources)?.Kind ?? SourceKind.Unknown;

            graph.RecordReconciliation(new FactReconciliation(
                node.Id,
                observed.Name,
                state,
                declaredValue,
                declaredProvenance,
                observedValue.Value,
                observedValue.Provenance,
                claimType,
                registry.Compare(claimType, declaredKind, observedKind),
                registry.MostAuthoritative(claimType, declaredProvenance.Sources.Concat(observedValue.Provenance.Sources))));
        }
    }

    /// <summary>
    /// The claim type a subject's properties answer to: cloud resource
    /// properties are configuration claims, artifact and build properties
    /// are derivation claims, and everything else — repository metadata,
    /// ownership, and the organization's governance records — are
    /// organizational claims, which code-host metadata corroborates.
    /// </summary>
    private static ClaimType ClaimTypeFor(NodeType nodeType) => nodeType.Name switch
    {
        "S3Bucket" or "CloudFrontDistribution" => ClaimType.Configuration,
        "Artifact" or "BuildPipeline" or "BuildRun" or "Commit" => ClaimType.Derivation,
        _ => ClaimType.Organization,
    };

    /// <summary>
    /// Groups the observed facts by subject and property, merging statements
    /// of the same value into one observed value carrying every statement's
    /// provenance. Encounter order is preserved, so reconciliation records
    /// are created deterministically.
    /// </summary>
    private static ObservedSubject[] GroupObservedFacts(IReadOnlyList<NormalizedState> observedStates) =>
        observedStates
            .SelectMany(state => state.Nodes)
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Select(subject => new ObservedSubject(
                subject.Key,
                subject
                    .SelectMany(node => node.Properties)
                    .GroupBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new ObservedProperty(property.Key, DistinctValues(property)))
                    .ToArray()))
            .ToArray();

    private static List<ObservedValue> DistinctValues(IEnumerable<NormalizedProperty> properties)
    {
        var values = new List<ObservedValue>();
        foreach (var property in properties)
        {
            // A null value carries no information and is treated as absent.
            if (property.Value is null)
            {
                continue;
            }

            var existing = values.FindIndex(value => JsonNode.DeepEquals(value.Value, property.Value));
            if (existing < 0)
            {
                values.Add(new ObservedValue(property.Value, property.Provenance));
                continue;
            }

            var merged = Merge(values[existing].Provenance, property.Provenance);
            values[existing] = values[existing] with { Provenance = merged };
        }

        return values;
    }

    private static FactProvenance Merge(FactProvenance existing, FactProvenance incoming) =>
        new(existing.Sources.Concat(incoming.Sources).Distinct().ToArray());

    private sealed record ObservedSubject(string SubjectId, IReadOnlyList<ObservedProperty> Properties);

    private sealed record ObservedProperty(string Name, IReadOnlyList<ObservedValue> Values);

    private sealed record ObservedValue(JsonNode Value, FactProvenance Provenance);
}
