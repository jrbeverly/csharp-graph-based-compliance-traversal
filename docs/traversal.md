# Typed Path Traversal

The query API that walks the typed property graph along named edges from any
starting node — forward and inverse names alike — and returns the ordered,
fully-trailed paths it followed. The code equivalent of this document is
`GraphTraversal`, `TraversalPath`, and `TraversalStep`
(`src/GraphBasedComplianceTraversal.Engine/Traversal/`), the Traversal
milestone built on the graph slice's storage-level `GetEdges` lookup
(`docs/graph.md`). The test suite pins this document's behavior against the
repository's own declared world.

## The query

A traversal is a start node plus an ordered chain of named edge steps:

```csharp
var paths = new GraphTraversal(graph, "aws:s3:prod-release-artifacts")
    .Step("used-by")      // the service that depends on the bucket
    .Step("implements")   // the capability the service realizes
    .Execute();
```

`Execute` returns one `TraversalPath` per route the steps follow; a query
whose steps match nothing returns an empty list. `Step` returns a new query,
so a prefix can be reused — the query is a value, not a cursor. The start node
must be stored in the graph, and the step names must be declared by the
ontology slice: both are rejected when the query is built, never silently
reported as an empty result.

## Names and directions

A step names an edge by its forward or its inverse name, exactly as the
ontology slice declares it. Names are directional: `depends-on` reads from a
service to its bucket, and `used-by` reads back. An unconstrained step
(`Step(name)`) follows every edge reading the name from the current node; the
trail records the direction each matched edge actually traversed in.

A step may constrain the direction: `Step(name, EdgeDirection.Forward)`
traverses the edge type(s) declaring `name` from their from-type endpoint to
their to-type endpoint, and `Step(name, EdgeDirection.Inverse)` traverses
them back. `Step("depends-on", EdgeDirection.Inverse)` from a bucket is
therefore the same traversal as `Step("used-by")`.

Because a single stored edge answers both of its names (see `docs/graph.md`),
business→infrastructure chains use the forward names and
infrastructure→business chains use the inverse names over the same edges — no
edge is ever defined twice:

```text
business-purpose:secure-endpoint-management
  → fulfilled-by → product:customer-agent
  → has-capability → capability:software-update-delivery
  → implemented-by → service:release-distribution
  → depends-on → aws:s3:prod-release-artifacts

aws:s3:prod-release-artifacts
  → used-by → service:release-distribution
  → implements → capability:software-update-delivery
  → belongs-to → product:customer-agent
  → fulfills → business-purpose:secure-endpoint-management
```

Every step of the trail records what happened: the reached node, the
traversed edge (and thus its edge type), the queried name, and the direction
the step moved in relative to the edge type. A path is inspectable as a
sequence of typed steps, not just its endpoints.

## Ordering and cycles

Execution is deterministic. At every step, routes fan out over the matching
edges in the order `GetEdges` returns them — the graph's insertion order — so
re-executing a query yields the same paths in the same order.

Execution is cycle-safe. A step onto a node already present on the current
path closes a loop: on the query's final step the path is reported with
`IsCycle` true and the repeated node as its `End`; on an earlier step the
route ends, because a path that revisits a node cannot complete the remaining
steps. A traversal over a cyclic graph therefore always terminates and
reports the repeated node rather than looping.

The declared world contains such a cycle:
`service:release-distribution` → `aws:cloudfront:customer-downloads`
(`depends-on`) → `aws:s3:prod-release-artifacts` (`origin`) → back to the
service (`used-by`).

## What is not traversed

- An edge whose reached endpoint has no stored node (an unresolved reference)
  is not followed: a path is a sequence of nodes, and a missing endpoint is
  not one. The edge itself stays queryable through the graph
  (`docs/graph.md`).
- Unmodeled references are never edges and cannot be traversed.

A declared step name that matches no edge from the reached nodes is an empty
result, not an error.

## Out of this slice

- **Path grammars** — constraining which edge sequences are valid
  conclusions: `docs/path-grammars.md` builds that on top of this query API,
  which itself executes any sequence of declared steps.
- **Rendering** — turning paths into prose, tables, or views is the final
  milestone; this slice produces the structured trails those renderers
  consume.

