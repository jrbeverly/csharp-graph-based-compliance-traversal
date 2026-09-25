using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// The authoritative definition of the assertion slice this repository
/// compiles against: the three assertions the fixtures imply.
/// <list type="bullet">
/// <item><c>assertion:prod-artifact-provenance</c> — the compiled form of the
/// declared <c>Assertion</c> record under <c>data/security/assertions/</c>,
/// whose <c>subject.class</c> and <c>predicate</c> fields this selector and
/// predicate realize.</item>
/// <item><c>assertion:production-resource-owner</c> — implied by the world:
/// the production cloud resources, none of which declares an owner.</item>
/// <item><c>assertion:adopted-control-implementation</c> — implied by the
/// world: the one adopted control and the implementation it declares.</item>
/// </list>
/// This class is the contract code compiles against; the test suite pins the
/// slice's evaluations to the fixtures in <c>data/**</c>.
/// </summary>
internal static class AssertionDefinition
{
    private const string CloudCategory = "Cloud";

    public static AssertionRegistry Create()
    {
        Assertion[] assertions =
        [
            new(
                "assertion:prod-artifact-provenance",
                "Every production artifact has valid provenance.",
                new NodeTypeSubjectSelector(Node("Artifact"), IsProduction),
                new ValidProvenancePredicate()),

            new(
                "assertion:production-resource-owner",
                "Every production resource has an owner.",
                new QuerySubjectSelector(graph => graph.Nodes
                    .Where(node => node.Type.Category == CloudCategory && IsProduction(node))
                    .ToArray()),
                new OwnerPredicate()),

            new(
                "assertion:adopted-control-implementation",
                "Every adopted control has an implementation.",
                new NodeTypeSubjectSelector(Node("ControlAdoption")),
                new ImplementedPredicate()),
        ];

        return new AssertionRegistry(assertions);
    }

    /// <summary>
    /// The fixture world's production classifier: a subject whose
    /// <c>classification</c> property is a string containing "production"
    /// (the fixtures use "production" and "production-release"). A subject
    /// without a classification is not known to be production and is out of
    /// the production assertions' scope.
    /// </summary>
    private static bool IsProduction(GraphNode node) =>
        node.Properties.TryGetValue("classification", out var value)
        && value is JsonValue jsonValue
        && jsonValue.TryGetValue<string>(out var classification)
        && classification.Contains("production", StringComparison.OrdinalIgnoreCase);

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    /// <summary>
    /// "Has valid provenance" over the artifact's signing facts: the
    /// <c>signing</c> property must be a structured statement carrying a
    /// non-empty <c>method</c>, <c>identity</c>, and
    /// <c>transparency_log</c> — the verifiable signature, trusted build
    /// identity, and traceable provenance the declared assertion statement
    /// names. A complete statement passes and an incomplete or malformed one
    /// fails; an artifact carrying no signing facts is unchecked and resolves
    /// Unknown, never an implied pass.
    /// </summary>
    private sealed class ValidProvenancePredicate : IAssertionPredicate
    {
        private static readonly string[] RequiredFields = ["method", "identity", "transparency_log"];

        public AssertionEvaluation Evaluate(TypedPropertyGraph graph, GraphNode subject)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(subject);

            if (!subject.Properties.TryGetValue("signing", out var signing) || signing is null)
            {
                // No signing fact: the provenance is unvalidated. The subject's
                // declared fact set was consulted (its provenance is the
                // declaring record) and none of it supports the property.
                return new AssertionEvaluation(
                    ValidationState.Unknown,
                    subject.Provenance,
                    "The artifact declares no signing facts, so its provenance is unvalidated.");
            }

            var provenance = subject.GetPropertyProvenance("signing");
            if (signing is not JsonObject statement)
            {
                return new AssertionEvaluation(
                    ValidationState.Fail,
                    provenance,
                    "The artifact's signing fact is not a structured signing statement.");
            }

            var missing = RequiredFields.Where(field => !HasNonEmptyString(statement, field)).ToArray();
            return missing.Length == 0
                ? new AssertionEvaluation(
                    ValidationState.Pass,
                    provenance,
                    "The artifact declares a complete signing statement: method, build identity, and transparency log.")
                : new AssertionEvaluation(
                    ValidationState.Fail,
                    provenance,
                    $"The artifact's signing statement is incomplete: missing {JoinFields(missing)}.");
        }

        private static bool HasNonEmptyString(JsonObject statement, string field) =>
            statement.TryGetPropertyValue(field, out var value)
            && value is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var text)
            && text.Length > 0;

        private static string JoinFields(IEnumerable<string> fields) =>
            string.Join(", ", fields.Select(field => $"'{field}'"));
    }

    /// <summary>
    /// "Has an owner" over the resource's owner facts: a non-empty
    /// <c>owner</c> or <c>owned_by</c> property is the supporting fact and
    /// passes the check; an owner fact that is declared but empty fails it.
    /// A resource whose declared facts carry no owner field at all is
    /// unchecked — Unknown, never a pass by omission.
    /// </summary>
    private sealed class OwnerPredicate : IAssertionPredicate
    {
        private static readonly string[] OwnerFields = ["owner", "owned_by"];

        public AssertionEvaluation Evaluate(TypedPropertyGraph graph, GraphNode subject)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(subject);

            foreach (var field in OwnerFields)
            {
                if (!subject.Properties.TryGetValue(field, out var value) || value is null)
                {
                    continue;
                }

                var provenance = subject.GetPropertyProvenance(field);
                if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var owner) && owner.Length > 0)
                {
                    return new AssertionEvaluation(
                        ValidationState.Pass,
                        provenance,
                        $"The resource declares {field} '{owner}'.");
                }

                return new AssertionEvaluation(
                    ValidationState.Fail,
                    provenance,
                    $"The resource declares {field}, but the value is empty.");
            }

            // No owner fact anywhere in the subject's declared facts: the fact
            // set was consulted (its provenance is the declaring record) and
            // found to carry no owner, so the property is unchecked.
            return new AssertionEvaluation(
                ValidationState.Unknown,
                subject.Provenance,
                "The resource declares no owner fact, so its ownership is unchecked.");
        }
    }

    /// <summary>
    /// "Has an implementation" over the adoption's <c>implemented-by</c>
    /// edges: an edge whose implementation endpoint has a record passes the
    /// check, and an edge naming an implementation with no record fails it
    /// (the adoption declares an implementation that does not exist). An
    /// adoption declaring no implementation is unchecked — Unknown, never a
    /// pass by omission.
    /// </summary>
    private sealed class ImplementedPredicate : IAssertionPredicate
    {
        public AssertionEvaluation Evaluate(TypedPropertyGraph graph, GraphNode subject)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(subject);

            var edges = graph.GetEdges(subject.Id, "implemented-by");
            if (edges.Count == 0)
            {
                return new AssertionEvaluation(
                    ValidationState.Unknown,
                    subject.Provenance,
                    "The adoption declares no implementation, so the control is unimplemented and unchecked.");
            }

            var provenance = new FactProvenance(
                edges.SelectMany(edge => edge.Provenance.Sources).Distinct().ToArray());
            var unresolved = edges.Where(edge => !edge.IsResolved).ToArray();
            return unresolved.Length == 0
                ? new AssertionEvaluation(
                    ValidationState.Pass,
                    provenance,
                    $"The adoption declares {Describe(edges, subject.Id)}, which has a record.")
                : new AssertionEvaluation(
                    ValidationState.Fail,
                    provenance,
                    $"The adoption declares {Describe(unresolved, subject.Id)}, which has no record.");
        }

        private static string Describe(IReadOnlyList<GraphEdge> edges, string subjectId) =>
            string.Join(" and ", edges.Select(edge => $"'{OtherEndpoint(edge, subjectId)}'"));

        private static string OtherEndpoint(GraphEdge edge, string subjectId) =>
            edge.ToId == subjectId ? edge.FromId : edge.ToId;
    }
}
