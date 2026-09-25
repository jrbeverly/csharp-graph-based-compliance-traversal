using System.Globalization;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// The deterministic default rendering of a <see cref="CompiledNarrative"/>:
/// a templated, AI-free assembly of the document from the compiled structure
/// — the title line, the compiled sentences in order, and the supporting
/// facts with their provenance, so the narrative reads with its traceability
/// attached. The rendering introduces nothing: no model, no randomness, no
/// clock; the same compiled narrative always renders the same text, which
/// the test suite pins golden-file by golden-file. A narrative with the
/// <see cref="NarrativeVerdict.Gap"/> verdict renders its gap sentences and
/// no conclusion.
/// </summary>
public static class NarrativeRendering
{
    /// <summary>Renders the compiled narrative deterministically.</summary>
    public static string Render(CompiledNarrative narrative)
    {
        ArgumentNullException.ThrowIfNull(narrative);

        var sections = new List<string> { narrative.Title };
        if (narrative.Statements.Count > 0)
        {
            sections.Add(string.Join('\n', narrative.Statements.Select(statement => statement.Text)));
        }

        if (narrative.Facts.Count > 0)
        {
            sections.Add(
                "Supporting facts:" +
                string.Concat(narrative.Facts.Select(fact => $"\n{RenderFact(fact)}")));
        }

        return string.Join("\n\n", sections);
    }

    private static string RenderFact(NarrativeFact fact)
    {
        var lines = new List<string> { FactLine(fact) };
        if (fact.Provenance.Sources.Count == 0)
        {
            // A fact with no recorded source is stated as unknown, never
            // omitted and never dressed up as authoritative.
            lines.Add("    source: unknown");
        }

        foreach (var record in fact.Provenance.Sources)
        {
            var source = $"    source: {SourceKindName(record.Kind)}";
            if (record.HasKnownSource)
            {
                source += $" {record.Locator}";
            }

            if (record.Timestamp is not null)
            {
                source += $" (at {record.Timestamp.Value.ToString("O", CultureInfo.InvariantCulture)})";
            }

            if (record.Version is not null)
            {
                source += $" (version {record.Version})";
            }

            lines.Add(source);
        }

        return string.Join('\n', lines);
    }

    private static string FactLine(NarrativeFact fact) => fact.Kind switch
    {
        NarrativeFactKind.Node => $"  [node] {fact.NodeId} is {Article(fact.Name)} {fact.Name} node",
        NarrativeFactKind.Edge => $"  [edge] {fact.Name}: {fact.NodeId} → {fact.TargetNodeId}",
        NarrativeFactKind.Property => $"  [property] {fact.NodeId}/{fact.Name} = \"{fact.Value}\"",
        NarrativeFactKind.State => $"  [state] {fact.NodeId}: the store surfaces {fact.Value}",
        _ => throw new ArgumentOutOfRangeException(nameof(fact)),
    };

    private static string Article(string name) =>
        name.Length > 0 && "aeiouAEIOU".Contains(name[0]) ? "an" : "a";

    private static string SourceKindName(SourceKind kind) => kind switch
    {
        SourceKind.Unknown => "unknown",
        SourceKind.Declaration => "declaration",
        SourceKind.Terraform => "terraform",
        SourceKind.AwsObserved => "aws-observed",
        SourceKind.Github => "github",
        SourceKind.Ci => "ci",
        SourceKind.ProvenanceAttestation => "provenance-attestation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
