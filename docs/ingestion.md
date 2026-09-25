# Loading Organizational Records as Declared Facts

The loader that turns every organizational record under `data/**` into the
declared graph: typed nodes carrying declaration provenance, reference fields
converted into typed edges through the edge registry, and the observations
history ingested as observation nodes linked to their assertion. The code
equivalent of this document is `DeclaredRecordsLoader` and
`DeclaredRecordException`
(`src/GraphBasedComplianceTraversal.Engine/Ingestion/`), the Ingestion
milestone the ontology, graph, provenance, and adapter slices pointed at. The
test suite pins this document's behavior against the repository's own
`data/**` world.

`DeclaredRecordsLoader.ApplyAll(graph, records)` loads every record into a
typed property graph in document order. The graph is expected to hold only
declared facts — merging them with the observed facts the external-source
adapters emit is the reconciliation milestone's concern, not this loader's.

## Record shapes

The loader accepts two shapes; anything else is rejected:

- A **typed record** — a YAML object with string `id` and `type` fields. Every
  one of the 20 typed records under `data/**` has this shape.
- The **observations history** — the document under
  `data/security/observations/**` that carries an `observations` list rather
  than a single `id`/`type` pair. Each entry becomes one `Observation` node.

A malformed record fails loudly with a `DeclaredRecordException` naming its
repository-relative path: a record that is neither shape, a `type` the
ontology slice does not declare, an `id` that does not carry its type's id
prefix, or a reference field whose values are not strings. Nothing is skipped.

## Nodes

A typed record's `id` and `type` fields map to the node's stable id and its
ontology type — the id must carry the type's declared prefix
(`type: S3Bucket` with id `aws:s3:prod-release-artifacts`). Every remaining
field becomes a node property, so the node mirrors its record: reference
lists included, because an edge or unmodeled reference is a typed *projection*
of the same statement, not a replacement for it. Observations entries map
the same way with `Observation` as their type, properties being everything
except the entry's `id`.

The node itself and every property carry `SourceKind.Declaration` provenance
naming the record's file, so any declared fact can answer where it came from:

```text
node: aws:s3:prod-release-artifacts
type: S3Bucket
provenance:
  kind: declaration
  locator: data/cloud/aws/resources/s3-prod-release-artifacts.yaml
```

## Reference fields become typed edges

A field on the node's type that the registry's field-name mapping declares
(`EdgeTypeRegistry.FieldMappings`) is converted into typed edges in the
mapping's canonical direction — a field that reads along an inverse name
stores the edge under that inverse name, and the forward query answers it
from the other endpoint. Field paths may be dotted: `control.specification`
addresses a nested member, and a list along the path fans the remainder out
over its elements (`mechanisms.covers`), while a list at the leaf holds
several references to the same field (`capabilities`, `scope`).

When a field maps to several target types (`depends_on`, `scope`, ...), a
value names its target through the target type's id prefix. A bare name —
`control.specification: ARR-3-11` — resolves against the field's single
declared target and is qualified to its node id (`control-def:ARR-3-11`);
`control.version` stays a property.

Edges are added with explicit endpoint types, so a reference whose target has
no record is retained as an unresolved edge rather than dropped. In the
declared world that is the product's two capabilities without records
(`capability:endpoint-telemetry-collection`, `capability:policy-enforcement`)
and the four observation subjects naming artifact versions without records.

## Unmodeled references stay visible

A field the registry declares unmodeled (`EdgeTypeRegistry.UnmodeledRelationships`)
is recorded through `RecordUnmodeledReference` for every string value it
carries — owners and approvers naming `team:`/`role:` ids, Terraform modules,
fixture locators, `ARR-RISK-044`, control-definition clauses, and incidents.
The reference is queryable but never an edge: it has no edge type and cannot
be traversed. Structured or free-text values (`external_assurance` documents,
`compensating_controls` prose) remain node properties, since they are not
references.

## One relationship, one edge

The field vocabulary carries reciprocal fields (`stored_in`/`contains`,
`used_by`/`depends_on`, ...), so the same relationship is often stated once
per side. It stays one edge: the first statement fixes the stored direction,
and every later statement appends its provenance record — the declared-world
analog of `NormalizedState.ApplyTo`'s merge. An edge stated by both records
carries both locators; traversal answers it from either endpoint.

## The observations history

Each entry of `data/security/observations/provenance-observations.yaml`
becomes one `Observation` node with declaration provenance naming that file.
The entry's `assertion` field maps to the `produces` edge type stored under
its inverse name, so the assertion's `produces` query answers all five
observations; the nested `subject.artifact` field maps to `observes` edges,
and `related_incident` is recorded as an unmodeled reference. The entries are
ingested as declared history — evaluating them against their assertion is a
later milestone. The observation-store milestone
(`docs/observation-store.md`) ingests the same entries, with their
timestamps and validator versions, into the queryable `ObservationStore`,
where outdated-validator staleness is derived.

## The resulting declared graph

Loading the repository's `data/**` yields 25 nodes (20 typed records plus 5
observations), 42 typed edges of which 6 are retained unresolved references,
and 29 recorded unmodeled references. The three canonical paths are connected
as typed edges:

```text
business-purpose:secure-endpoint-management
  → product:customer-agent → capability:software-update-delivery
  → service:release-distribution → aws:cloudfront:customer-downloads
  → aws:s3:prod-release-artifacts → artifact:customer-agent-2.8.4
  → build-pipeline:customer-agent-release → repository:customer-agent

risk:artifact-tampering → impact:artifact-compromise
  → control-adoption:ca-artifact-supply-chain
  → implementation:signed-release-pipeline → assertion:prod-artifact-provenance
  → observation:obs-2026-08-08-001 ... observation:obs-2026-07-15-001

framework:nis2 → requirement:nis2-article-21-supply-chain
  → control-adoption:ca-artifact-supply-chain
```

## Out of this slice

- **Normalizing `fixtures/**`** — the external-source adapters
  (`docs/adapters.md`). This loader reads only `data/**` records.
- **Reconciling declared facts against observed facts** — the reconciliation
  milestone (`docs/reconciliation.md`), which attaches the adapters' observed
  facts to declared subjects and records corroboration and `CONFLICTING`
  verdicts on the graph. Resolving a conflict into one value is still
  separate, human work.
- **Evaluating assertions or linting the organization** — later milestones.
  The failed, stale, and expiring observations and exceptions are ingested
  exactly as declared; no conclusion is drawn from them here.

