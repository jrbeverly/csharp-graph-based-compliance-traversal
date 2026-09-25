using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// The contract that normalizes an external system's response into the graph
/// model: a parsed response goes in, normalized nodes/edges/properties tagged
/// with that source's provenance kind come out. This is the middle of the
/// eventual <c>API → Adapter → Normalized State → Graph</c> boundary — a
/// future live connector replaces only the fixture reader, feeding parsed API
/// responses through the same adapters, so downstream code is indifferent to
/// whether a fact came from a fixture file or a live API.
/// <para>
/// An adapter never performs network access and never requires credentials:
/// it reads only the parsed content it is handed. The input carries the
/// response's locator (its repository-relative fixture path in this world)
/// so every emitted fact can point back at its source.
/// </para>
/// </summary>
public interface IExternalSourceAdapter
{
    /// <summary>The external operation the adapter normalizes, for example <c>aws.s3.get-bucket-encryption</c>.</summary>
    string Name { get; }

    /// <summary>The provenance kind stamped on every fact the adapter emits.</summary>
    SourceKind SourceKind { get; }

    /// <summary>True when the parsed response has the shape this adapter normalizes.</summary>
    bool CanNormalize(RepositoryDocument document);

    /// <summary>
    /// Normalizes one parsed response into normalized facts, each tagged with
    /// provenance carrying the response's locator. <paramref name="availableDocuments"/>
    /// is the rest of the loaded world, letting an adapter resolve references
    /// its response makes to other source documents — the attestation adapter
    /// resolves the workflow run its invocation id names. A live connector
    /// passes whatever companion responses it holds; adapters that make no
    /// cross-document references ignore the parameter.
    /// </summary>
    NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments);
}
