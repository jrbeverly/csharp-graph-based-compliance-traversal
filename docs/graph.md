# In-Memory Typed Property Graph with Inverse Edges

The graph that holds nodes and typed edges by stable identity and validates
them against the ontology. The code equivalent of this document is
`TypedPropertyGraph`, `GraphNode`, `GraphEdge`, and `UnmodeledReference`
(`src/GraphBasedComplianceTraversal.Engine/Graph/`), the contract the
Ingestion and Traversal milestones compile against. The test suite pins this
document's behavior. The graph is constructed programmatically and lives only
in memory — persistence is out of scope, and reading `data/**` into it is the
loader's separate concern.

## Nodes

A node is its stable `id` — the same identifier the repository records use
(for example `aws:s3:prod-release-artifacts`, `risk:artifact-tampering`) — a
node type declared by `EdgeTypeRegistry.Slice`, and arbitrary JSON-valued
properties:

```text
node: aws:s3:prod-release-artifacts
type: S3Bucket
properties:
  encryption: aws:kms
  region: us-east-1
```

`AddNode(id, type, properties)` rejects a node type the ontology slice does
not declare and rejects a repeated id: stable identity means one node per id.
`GetNode` / `TryGetNode` look nodes up by id, `GetNodes(type)` by type.

## Typed edges

An edge is a registry-validated `EdgeType` connecting two node ids. `AddEdge`
resolves the given name against the endpoint node types through
`EdgeTypeRegistry.Resolve` and stores the edge in the direction it was added.
Every edge is therefore queryable by both of its names:

```text
AddEdge("implemented-by", capability, service)
   → GetEdges(capability, "implemented-by") answers the edge (forward)
   → GetEdges(service, "implements")     answers the same edge (inverse)
```

`GetEdges(fromId, name)` returns the edges leaving `fromId` under `name`:
edges stored from `fromId` under the name itself, plus edges stored *to*
`fromId` under the edge type's other name (an edge added under the inverse
name is answered from its target by the forward name, and vice versa).
Traversal can move in either direction through one stored edge, and a name
only ever matches across the endpoints the registry declared it for.
`GetEdges(fromId)` returns every edge touching a node, and
`GetEdges(edgeType)` every edge of a type.

## Validation semantics

`AddEdge` rejects, with a clear error, any edge the registry rejects —
including an edge whose name does not resolve across the given endpoint types
(`EdgeTypeViolationException`) and an endpoint type the slice does not
declare. Edge names are directional, so using a forward name across swapped
endpoints is a rejection even when each endpoint type is valid.

Adding an edge by bare ids (`AddEdge(name, fromId, toId)`) requires both
endpoints to be stored nodes, since their types come from node storage.
Adding a node whose type is not declared is rejected, and an explicit
endpoint type that contradicts the stored node's type is rejected.

## Unresolved references: nothing is silently dropped

An edge may reference a node id that is not (yet) stored. The edge is
validated like any other, retained, and reported as unresolved rather than
dropped or auto-created as a real node:

```text
AddEdge("observes", observation:obs-2026-08-07-001, Observation,
                    artifact:customer-agent-2.8.3,   Artifact)

GetEdges("observation:obs-2026-08-07-001", "observes") → the edge
GetEdges("artifact:customer-agent-2.8.3", "observed-by") → the same edge

edge.IsResolved        → false
edge.MissingEndpointIds → [artifact:customer-agent-2.8.3]
```

`GetUnresolvedEdges()` lists every edge with a missing endpoint, computed
against current node storage: adding the missing node later resolves the
edge. This is what surfaces incomplete worlds — the fixtures' observations
reference artifact versions without records, and `data/**` references entities
that are not part of the slice at all.

References the ontology *declares unmodeled* (relationship fields whose values
point outside the declared node types — `generic_risk: ARR-RISK-044`, owners
pointing at `team:` / `role:` ids, and the other fields in
`docs/ontology.md`'s "Intentionally unmodeled" table) are recorded through
`RecordUnmodeledReference(fromId, fieldPath, targetId)`. The field must be
declared unmodeled for the referencing node's type; a field mapped to an edge
type belongs in `AddEdge` instead, and an undeclared field is rejected. Such a
reference is represented and queryable (`UnmodeledReferences`,
`GetUnmodeledReferences(fromId)`) but is never an edge: it has no edge type
and cannot be traversed.

## Provenance

Every node, edge, and property can answer "where did this come from?".
`AddNode` and `AddEdge` accept optional provenance, and each `GraphNode`
carries its own provenance plus per-property provenance
(`SetPropertyProvenance` / `GetPropertyProvenance`). Provenance is recorded,
never resolved: attaching a record says which source stated the fact, and a
fact with no recorded source reads as `Unknown` — never as a fabricated
authoritative one. Which source to consult when statements disagree is the
per-claim-type authority table's answer. See `docs/provenance.md` for the
record shape, the unknown-source semantics, and the precedence table; the
external-source adapters populate provenance from `fixtures/**`
(`docs/adapters.md`), and the Ingestion milestone's loader populates it from
`data/**` (`docs/ingestion.md`).

## Declared-versus-observed reconciliations

The graph also stores the reconciliation of the two worlds that feed it:
`FactReconciliation` records, added through `RecordReconciliation` and
queryable per subject through `GetReconciliations` (or all of them through
`Reconciliations`). Each record is the verdict for one property of one
subject: `PASS` when the observed value corroborates the declared one —
carrying both values and both provenances — and `CONFLICTING` when they
disagree, additionally carrying the claim type, the source-authority
precedence, and the most authoritative source record. Recording a verdict
never touches the node's property value, and a verdict identical to one
already recorded is not recorded twice. The records are produced by
`DeclaredObservedReconciler` — see `docs/reconciliation.md`.

## Out of this slice

- **The loaders feeding the graph** — `data/**` is loaded by the Ingestion
  milestone's declared-record loader (`docs/ingestion.md`), and the
  `fixtures/**` side is normalized by the external-source adapters (see
  `docs/adapters.md`), which project normalized state into the graph through
  `NormalizedState.ApplyTo`; this slice defines the container and its
  validation, not the loaders.
- **The path-query API** — the Traversal milestone's `GraphTraversal`
  (`src/GraphBasedComplianceTraversal.Engine/Traversal/`) builds typed,
  directional walks on top of `GetEdges`; see `docs/traversal.md`.
  Constraining which edge sequences are valid conclusions (path grammars)
  and rendering paths as prose or views remain later milestones.
- **Persistence** to disk or a database.

